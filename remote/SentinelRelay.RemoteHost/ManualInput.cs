using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;
using SentinelRelay.Remote;
using SentinelRelay.RemoteInput;

namespace SentinelRelay.RemoteHost;

public sealed record InputHello(int Version, string InstanceId);
public sealed record InputPoll(int Version, long Sequence, string Challenge);
public sealed record InputFrame(int Version, long Sequence, string Challenge,
    double LeftX, double LeftY, double RightX, double RightY, double LeftTrigger, double RightTrigger, ushort Buttons)
{
    public ControllerFrame ToFrame() => new(LeftX, LeftY, RightX, RightY, LeftTrigger, RightTrigger, Buttons);
}
public interface IManualTarget : IDisposable
{
    string? GameEpoch { get; }
    string StatusCode => "target_unavailable";
    bool Allowed(ControllerOwner owner);
}
public interface IManualOutput : IControllerOutput, IDisposable { }
public sealed record InputDiagnostic(string Phase, string Code, long AcceptedFrames);

/// <summary>One socket and output per host. Permission is explicit, local, per-phone and memory-only.</summary>
public sealed class ManualInput : IDisposable
{
    private readonly RemoteAuthority authority;
    private readonly Func<GameBinding, Task<IManualTarget>> targetFactory;
    private readonly Func<Task<IManualOutput>> outputFactory;
    private readonly object gate = new();
    private readonly HashSet<string> permitted = [];
    private readonly SemaphoreSlim exclusive = new(1);
    private ControllerLease? current;
    private CancellationTokenSource? connection;
    private string? activeDevice;
    private bool disposed;
    private InputDiagnostic diagnostic = new("idle", "not_started", 0);
    public InputDiagnostic Diagnostic { get { lock (gate) return diagnostic; } }
    public string Status
    {
        get
        {
            lock (gate)
            {
                if (diagnostic.Phase == "active") return "PHONE GAMEPAD ACTIVE";
                return "Gamepad: " + Description(diagnostic.Code);
            }
        }
    }
    public bool Active { get { lock (gate) return activeDevice is not null; } }
    public ManualInput(RemoteAuthority authority, Func<GameBinding, Task<IManualTarget>> targetFactory, Func<Task<IManualOutput>> outputFactory)
    { this.authority = authority; this.targetFactory = targetFactory; this.outputFactory = outputFactory; }
    private void Report(string phase, string code, long? frames = null)
    { lock (gate) diagnostic = new(phase, code, frames ?? diagnostic.AcceptedFrames); }

    public static string Description(string code) => code switch
    {
        "not_started" => "ready for local setup",
        "permission_allowed" => "phone allowed locally",
        "waiting_hello" => "waiting for phone",
        "checking_permission" => "checking phone permission",
        "checking_game" => "checking game",
        "starting_worker" => "starting controller",
        "waiting_first_frame" => "waiting for first phone frame",
        "active" => "active",
        "manual_not_permitted" => "allow this phone locally",
        "session_unavailable" => "refresh the phone session",
        "target_no_game" => "FFXIV is not running",
        "target_extra_game" => "more than one FFXIV is running",
        "target_foreground" => "keep FFXIV in the foreground",
        "target_process_changed" => "refresh the approved character",
        "target_visibility_unknown" => "game identity could not be checked",
        "target_bridge_unavailable" => "Relay bridge did not respond",
        "target_control_disabled" => "enable Relay mobile control",
        "target_epoch_changed" => "character or Relay session changed",
        "target_snapshot_stale" => "game status became stale",
        "local_stop" => "stopped locally; allow phone again",
        "permission_removed" => "phone gamepad permission removed",
        "phone_disconnected" => "phone connection closed",
        "phone_frame_timeout" => "phone frame did not arrive in time",
        "input_frame_rejected" => "phone frame was stale or invalid",
        "input_lease_ended" => "input lease ended",
        "input_worker_timeout" => "controller startup timed out",
        _ => "stopped; copy gamepad result",
    };
    public void Permit(string deviceId, bool enabled)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (enabled)
            {
                permitted.Add(deviceId);
                if (activeDevice is null) diagnostic = new("idle", "permission_allowed", 0);
            }
            else
            {
                permitted.Remove(deviceId);
                if (activeDevice == deviceId)
                {
                    diagnostic = new("stopped", "permission_removed", diagnostic.AcceptedFrames);
                    connection?.Cancel();
                }
            }
        }
    }
    public void Stop()
    {
        CancellationTokenSource? cancel;
        lock (gate)
        {
            permitted.Clear(); cancel = connection;
            diagnostic = new("stopped", "local_stop", diagnostic.AcceptedFrames);
        }
        try { cancel?.Cancel(); } catch (ObjectDisposedException) { }
    }
    private bool Permitted(string id) { lock (gate) return !disposed && permitted.Contains(id); }

    public static bool Accept(InputFrame frame, InputPoll poll, TimeSpan age) =>
        frame.Version == 1 && frame.Sequence == poll.Sequence && frame.Sequence > 0 &&
        frame.Challenge == poll.Challenge && age < TimeSpan.FromMilliseconds(250) && frame.ToFrame().IsValid;

    public async Task Run(WebSocket socket, string? session, CancellationToken cancellation)
    {
        if (!await exclusive.WaitAsync(0, cancellation)) { socket.Abort(); return; }
        using var life = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        IManualTarget? target = null; IManualOutput? output = null; ControllerLease? lease = null;
        var outcome = "input_connection_ended";
        var veto = "";
        long frames = 0;
        try
        {
            lock (gate) { if (disposed) return; connection = life; }
            Report("starting", "waiting_hello", 0);
            var hello = await Read<InputHello>(socket, TimeSpan.FromSeconds(3), life.Token);
            if (hello.Version != 1 || hello.InstanceId.Length > 64) { outcome = "input_hello_rejected"; return; }
            Report("starting", "checking_permission");
            var owner = authority.InputOwner(session, hello.InstanceId);
            if (owner is null) { outcome = "session_unavailable"; return; }
            if (!Permitted(owner.DeviceId)) { outcome = "manual_not_permitted"; return; }
            Report("starting", "checking_game");
            target = await targetFactory(owner.Binding);
            if (target.GameEpoch is not { } epoch) { outcome = target.StatusCode; return; }
            owner = owner with { GameEpoch = epoch };
            bool Allowed(ControllerOwner o)
            {
                var code = life.IsCancellationRequested ? "local_stop" :
                    !Permitted(o.DeviceId) ? "permission_removed" :
                    !authority.InputAllowed(o) ? "session_unavailable" :
                    !target.Allowed(o) ? target.StatusCode : "";
                if (code.Length != 0) Volatile.Write(ref veto, code);
                return code.Length == 0;
            }
            if (!Allowed(owner)) { outcome = Volatile.Read(ref veto); return; }
            lock (gate) activeDevice = owner.DeviceId;
            Report("starting", "starting_worker");
            output = await outputFactory();
            lease = new ControllerLease(output, Allowed);
            lock (gate) current = lease;
            var acquired = lease.Acquire(owner);
            if (acquired.Result != ControllerResult.Acquired || acquired.LeaseId is not { } id)
            { outcome = Volatile.Read(ref veto) is { Length: > 0 } why ? why : "input_lease_ended"; return; }
            Report("starting", "waiting_first_frame");
            for (long sequence = 1; !life.IsCancellationRequested; sequence++)
            {
                var poll = new InputPoll(1, sequence, Protocol.Secret());
                var started = Stopwatch.GetTimestamp();
                using var sendLimit = CancellationTokenSource.CreateLinkedTokenSource(life.Token);
                sendLimit.CancelAfter(TimeSpan.FromMilliseconds(250));
                await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(poll, Protocol.Json), WebSocketMessageType.Text, true, sendLimit.Token);
                var frame = await Read<InputFrame>(socket, TimeSpan.FromMilliseconds(250), life.Token);
                if (!Accept(frame, poll, Stopwatch.GetElapsedTime(started))) { outcome = "input_frame_rejected"; return; }
                var submitted = lease.Submit(id, owner, sequence, frame.ToFrame());
                if (submitted != ControllerResult.Applied)
                { outcome = Volatile.Read(ref veto) is { Length: > 0 } why ? why : "input_lease_ended"; return; }
                if (!authority.InputAllowed(owner, touch: true)) { outcome = "session_unavailable"; return; }
                Report("active", "active", ++frames);
                await Task.Delay(50, life.Token);
            }
            outcome = "local_stop";
        }
        catch (InputBackendException error) { outcome = SafeBackendCode(error.Code); }
        catch (TimeoutException) { outcome = "input_worker_timeout"; }
        catch (OperationCanceledException) { outcome = life.IsCancellationRequested ? "local_stop" : "phone_frame_timeout"; }
        catch (WebSocketException) { outcome = "phone_disconnected"; }
        catch (InvalidDataException) { outcome = "input_frame_rejected"; }
        catch (JsonException) { outcome = "input_frame_rejected"; }
        catch (Exception) { outcome = "input_start_failed"; }
        finally
        {
            if (Volatile.Read(ref veto) is { Length: > 0 } why) outcome = why;
            // Release native output BEFORE publishing the diagnostic or closing the socket.
            lease?.Dispose();
            output?.Dispose();
            target?.Dispose();
            lock (gate)
            {
                current = null; connection = null; activeDevice = null;
                // Keep the specific local action when cancellation raced a read/write.
                if (diagnostic.Code is not ("local_stop" or "permission_removed"))
                    diagnostic = new("stopped", outcome, frames);
            }
            socket.Abort();
            exclusive.Release();
        }
    }
    private static string SafeBackendCode(string code) => code is
        "backend_attach_failed" or "backend_request_rejected" or "backend_readiness" or "backend_version" or
        "backend_exited_before_ready" or "backend_hash_mismatch" or "backend_endpoint_owner_changed" or
        "backend_did_not_exit" or "worker_stopped" or "input_worker_unavailable" or "input_worker_timeout" or
        "input_worker_reply_invalid" or "input_worker_stopped" ? code : "input_backend_unavailable";

    private static async Task<T> Read<T>(WebSocket socket, TimeSpan limit, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(limit);
        var buffer = new byte[513]; var total = 0;
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer.AsMemory(total), timeout.Token);
            if (result.MessageType != WebSocketMessageType.Text) throw new WebSocketException();
            total += result.Count;
            if (total > 512) throw new InvalidDataException();
            if (result.EndOfMessage) break;
            if (total == 512) throw new InvalidDataException();
        }
        return JsonSerializer.Deserialize<T>(buffer.AsSpan(0, total), Protocol.Json) ?? throw new InvalidDataException();
    }
    public void Dispose()
    {
        Stop();
        ControllerLease? lease;
        lock (gate) { disposed = true; lease = current; }
        lease?.EmergencyStop();
    }
}
