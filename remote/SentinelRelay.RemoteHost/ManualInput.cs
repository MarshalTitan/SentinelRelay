using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;
using SentinelRelay.Remote;

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
    bool Allowed(ControllerOwner owner);
}
public interface IManualOutput : IControllerOutput, IDisposable { }

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
    public string Status { get { lock (gate) return activeDevice is null ? "Phone gamepad: idle" : "PHONE GAMEPAD ACTIVE"; } }
    public bool Active { get { lock (gate) return activeDevice is not null; } }
    public ManualInput(RemoteAuthority authority, Func<GameBinding, Task<IManualTarget>> targetFactory, Func<Task<IManualOutput>> outputFactory)
    { this.authority = authority; this.targetFactory = targetFactory; this.outputFactory = outputFactory; }
    public void Permit(string deviceId, bool enabled)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (enabled) permitted.Add(deviceId);
            else { permitted.Remove(deviceId); if (activeDevice == deviceId) connection?.Cancel(); }
        }
    }
    public void Stop()
    {
        // Do not acquire the lease lock under this lock; its watchdog checks local permission.
        CancellationTokenSource? cancel;
        lock (gate) { permitted.Clear(); cancel = connection; }
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
        try
        {
            lock (gate) { if (disposed) return; connection = life; }
            var hello = await Read<InputHello>(socket, TimeSpan.FromSeconds(3), life.Token);
            if (hello.Version != 1 || hello.InstanceId.Length > 64) return;
            var owner = authority.InputOwner(session, hello.InstanceId);
            if (owner is null || !Permitted(owner.DeviceId)) return;
            target = await targetFactory(owner.Binding);
            if (target.GameEpoch is not { } epoch) return;
            owner = owner with { GameEpoch = epoch };
            bool Allowed(ControllerOwner o) => !life.IsCancellationRequested && Permitted(o.DeviceId) &&
                authority.InputAllowed(o) && target.Allowed(o);
            if (!Allowed(owner)) return;
            // Visible locally before any device is created.
            lock (gate) activeDevice = owner.DeviceId;
            output = await outputFactory();
            lease = new ControllerLease(output, Allowed);
            lock (gate) current = lease;
            var acquired = lease.Acquire(owner);
            if (acquired.Result != ControllerResult.Acquired || acquired.LeaseId is not { } id) return;
            for (long sequence = 1; !life.IsCancellationRequested; sequence++)
            {
                var poll = new InputPoll(1, sequence, Protocol.Secret());
                var started = Stopwatch.GetTimestamp();
                using var sendLimit = CancellationTokenSource.CreateLinkedTokenSource(life.Token);
                sendLimit.CancelAfter(TimeSpan.FromMilliseconds(250));
                await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(poll, Protocol.Json), WebSocketMessageType.Text, true, sendLimit.Token);
                var frame = await Read<InputFrame>(socket, TimeSpan.FromMilliseconds(250), life.Token);
                if (!Accept(frame, poll, Stopwatch.GetElapsedTime(started)) ||
                    lease.Submit(id, owner, sequence, frame.ToFrame()) != ControllerResult.Applied ||
                    !authority.InputAllowed(owner, touch: true)) return;
                await Task.Delay(50, life.Token); // At most 20 full states/sec; no push queue or retry.
            }
        }
        catch (Exception) { /* Stable closure only; no raw frames, credentials or exception text. */ }
        finally
        {
            lease?.Dispose();
            output?.Dispose();
            target?.Dispose();
            lock (gate) { current = null; connection = null; activeDevice = null; }
            socket.Abort(); // Cancelled/invalid/uncertain connections require a fresh explicit user action.
            exclusive.Release();
        }
    }
    private static async Task<T> Read<T>(WebSocket socket, TimeSpan limit, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(limit);
        var buffer = new byte[513]; var total = 0;
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer.AsMemory(total), timeout.Token);
            if (result.MessageType != WebSocketMessageType.Text) throw new InvalidDataException();
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
