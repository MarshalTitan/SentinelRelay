using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;
using SentinelRelay.Remote;

namespace SentinelRelay.RemoteHost;

public sealed record DesktopRequest(int Version, long Sequence, string Kind, string? FrameId = null,
    double? X = null, double? Y = null, string? Key = null, string? Text = null);
public sealed record DesktopImage(string FrameId, int Width, int Height, string JpegBase64, DateTimeOffset CapturedUtc);
public interface IDesktopAdapter
{
    DesktopImage Capture();
    bool Apply(DesktopRequest request);
}
public sealed class DesktopAccessException(string code) : Exception { public string Code { get; } = code; }
public sealed record DesktopDiagnostic(string Code, long Frames, long Inputs);
public sealed class DesktopStream(RemoteAuthority authority, IDesktopAdapter adapter) : IDisposable
{
    private readonly object gate = new();
    private readonly HashSet<string> permitted = [];
    private readonly SemaphoreSlim exclusive = new(1);
    private CancellationTokenSource? connection;
    private string? activeDevice;
    private bool disposed;
    private DesktopDiagnostic diagnostic = new("not_started", 0, 0);
    public DesktopDiagnostic Diagnostic { get { lock (gate) return diagnostic; } }
    private void Report(string code, bool frame = false, bool input = false)
    { lock (gate) diagnostic = new(code, diagnostic.Frames + (frame ? 1 : 0), diagnostic.Inputs + (input ? 1 : 0)); }
    public bool Active { get { lock (gate) return activeDevice is not null; } }
    public void Permit(string device, bool enabled)
    {
        CancellationTokenSource? stop = null;
        lock (gate)
        {
            if (enabled && !disposed) permitted.Add(device);
            else { permitted.Remove(device); if (activeDevice == device) stop = connection; }
        }
        try { stop?.Cancel(); } catch (ObjectDisposedException) { }
    }
    private bool Permitted(string device) { lock (gate) return !disposed && permitted.Contains(device); }
    public void Stop()
    {
        CancellationTokenSource? stop;
        lock (gate) { permitted.Clear(); stop = connection; }
        try { stop?.Cancel(); } catch (ObjectDisposedException) { }
    }
    public static bool Valid(DesktopRequest request)
    {
        if (request.Version != 1 || request.Sequence <= 0) return false;
        if (request.Kind == "frame") return request.FrameId is null && request.X is null && request.Y is null && request.Key is null && request.Text is null;
        if (!Protocol.IsSecret(request.FrameId)) return false;
        if (request.Kind is "tap" or "doubleTap" or "rightClick")
            return request.X is >= 0 and <= 1 && request.Y is >= 0 and <= 1 && request.Key is null && request.Text is null;
        if (request.X is not null || request.Y is not null) return false;
        if (request.Kind == "key") return request.Text is null && request.Key is
            "Enter" or "Escape" or "Tab" or "Backspace" or "Up" or "Down" or "Left" or "Right" or "Start" or "ScrollUp" or "ScrollDown";
        return request.Kind == "text" && request.Key is null && request.Text is { Length: > 0 and <= 256 } text && !text.Any(char.IsControl);
    }
    public async Task Run(WebSocket socket, string? token, CancellationToken cancellation)
    {
        if (!await exclusive.WaitAsync(0, cancellation)) { socket.Abort(); return; }
        using var life = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        Task? watch = null;
        try
        {
            lock (gate) diagnostic = new("checking_permission", 0, 0);
            var owner = authority.DesktopSession(token);
            if (owner is null) { Report("unauthorized"); await Send(socket, new(false, "unauthorized"), life.Token); return; }
            if (!Permitted(owner.DeviceId)) { Report("desktop_not_permitted"); await Send(socket, new(false, "desktop_not_permitted"), life.Token); return; }
            lock (gate) { connection = life; activeDevice = owner.DeviceId; }
            watch = Task.Run(async () =>
            {
                try
                {
                    while (!life.IsCancellationRequested)
                    {
                        await Task.Delay(250, life.Token);
                        if (!Permitted(owner.DeviceId) || !authority.DesktopAllowed(owner)) { life.Cancel(); return; }
                    }
                }
                catch (OperationCanceledException) { }
            });
            Report("desktop_ready");
            await Send(socket, new(true, "desktop_ready"), life.Token);
            long sequence = 0, lastFrame = 0;
            var frames = new Queue<(string Id, long Stamp)>();
            var rate = new Queue<long>();
            while (!life.IsCancellationRequested)
            {
                var request = await Read(socket, life.Token);
                var now = Stopwatch.GetTimestamp();
                while (rate.TryPeek(out var stamp) && Stopwatch.GetElapsedTime(stamp, now) >= TimeSpan.FromSeconds(1)) rate.Dequeue();
                if (rate.Count >= 12 || !Valid(request) || request.Sequence != ++sequence)
                { await Send(socket, new(false, "desktop_request_rejected"), life.Token); return; }
                rate.Enqueue(now);
                if (!Permitted(owner.DeviceId) || !authority.DesktopAllowed(owner)) return;
                Reply reply;
                if (request.Kind == "frame")
                {
                    if (lastFrame != 0 && Stopwatch.GetElapsedTime(lastFrame) < TimeSpan.FromMilliseconds(333))
                    { await Send(socket, new(false, "desktop_rate_limited"), life.Token); return; }
                    var image = await Task.Run(adapter.Capture, life.Token);
                    if (image.Width is < 1 or > 1280 || image.Height is < 1 or > 720 ||
                        image.JpegBase64.Length is < 1 or > 440000 || !Protocol.IsSecret(image.FrameId))
                        throw new InvalidDataException();
                    lastFrame = now;
                    frames.Enqueue((image.FrameId, now));
                    while (frames.Count > 3) frames.Dequeue();
                    Report("desktop_viewing", frame: true);
                    reply = new(true, "desktop_frame", image);
                }
                else
                {
                    if (!frames.Any(f => f.Id == request.FrameId && Stopwatch.GetElapsedTime(f.Stamp) < TimeSpan.FromSeconds(2)))
                    { await Send(socket, new(false, "desktop_stale_frame"), life.Token); return; }
                    reply = await Task.Run(() => adapter.Apply(request) ? new Reply(true, "desktop_applied") : new(false, "desktop_input_unavailable"), life.Token);
                }
                if (!Permitted(owner.DeviceId) || !authority.DesktopAllowed(owner)) return;
                if (request.Kind != "frame") Report(reply.Code, input: reply.Success);
                await Send(socket, reply, life.Token);
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            var code = e is DesktopAccessException failure && failure.Code is "desktop_session_unavailable" or "desktop_display_unavailable" or "desktop_capture_failed"
                ? failure.Code : "desktop_unavailable";
            Report(life.IsCancellationRequested ? "desktop_stopped" : code);
            if (!life.IsCancellationRequested)
                try { await Send(socket, new(false, code), life.Token); } catch { }
        }
        finally
        {
            life.Cancel();
            if (watch is not null) try { await watch; } catch { }
            lock (gate) { connection = null; activeDevice = null; }
            exclusive.Release();
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var close = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
                try { await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", close.Token); } catch { }
            }
            socket.Abort();
        }
    }
    private static async Task Send(WebSocket socket, Reply reply, CancellationToken cancel)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(reply, Protocol.Json), WebSocketMessageType.Text, true, deadline.Token);
    }
    private static async Task<DesktopRequest> Read(WebSocket socket, CancellationToken cancel)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        var bytes = new byte[4097]; var count = 0;
        while (true)
        {
            var part = await socket.ReceiveAsync(bytes.AsMemory(count), deadline.Token);
            if (part.MessageType != WebSocketMessageType.Text || (count += part.Count) > 4096) throw new InvalidDataException();
            if (part.EndOfMessage) return JsonSerializer.Deserialize<DesktopRequest>(bytes.AsSpan(0, count), Protocol.Json) ?? throw new JsonException();
        }
    }
    public void Dispose() { Stop(); lock (gate) disposed = true; }
}
