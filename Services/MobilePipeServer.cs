using System.Collections.Concurrent;
using System.IO.Pipes;
using SentinelRelay.Bridge;

namespace SentinelRelay.Services;

/// <summary>Private same-Windows-user IPC only. All game access is pumped on the framework thread.</summary>
internal sealed class MobilePipeServer : IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly ConcurrentQueue<Job> jobs = new();
    private readonly Task worker;
    public string CharacterKey { get; }
    public string GameEpoch { get; } = Guid.NewGuid().ToString("N");
    internal int PendingCount => jobs.Count;
    private sealed record Job(BridgeRequest Request, TaskCompletionSource<BridgeReply> Completion, CancellationToken Token, bool ValidateOnly);
    public MobilePipeServer(string characterKey) { CharacterKey = characterKey; worker = Task.Run(Run); }
    public void Pump(Func<BridgeRequest, bool, BridgeReply> execute)
    {
        if (jobs.TryDequeue(out var job) && !job.Token.IsCancellationRequested)
        {
            try { job.Completion.TrySetResult(execute(job.Request, job.ValidateOnly)); }
            catch { job.Completion.TrySetResult(new(false, "operation_failed")); }
        }
    }
    private async Task<BridgeReply> Submit(BridgeRequest request, CancellationToken token, bool validateOnly = false)
    {
        var completion = new TaskCompletionSource<BridgeReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (jobs.Count >= 8) return new(false, "busy");
        jobs.Enqueue(new(request, completion, token, validateOnly));
        return await completion.Task.WaitAsync(token);
    }
    private async Task Run()
    {
        while (!lifetime.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(BridgeWire.PipeName(Environment.ProcessId), PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(lifetime.Token);
                using var requestLife = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                requestLife.CancelAfter(TimeSpan.FromSeconds(4));
                var request = await BridgeWire.Read<BridgeRequest>(pipe, 4096, requestLife.Token);
                if (!BridgeWire.Allowed(request.Operation)) continue;
                // A closed local connection cancels work still waiting for the next game tick.
                var disconnected = ObserveDisconnect(pipe, requestLife);
                var reply = await Submit(request, requestLife.Token);
                if (reply.CaptureWindow != 0)
                {
                    var capture = GameWindowCapture.CapturePng(reply.CaptureWindow);
                    requestLife.Token.ThrowIfCancellationRequested();
                    var validation = await Submit(request, requestLife.Token, validateOnly: true);
                    reply = !validation.Success ? validation : capture.Success && capture.PngBytes is not null
                        ? new(true, "screenshot", validation.Snapshot, Convert.ToBase64String(capture.PngBytes))
                        : new(false, "capture_failed");
                }
                await BridgeWire.Write(pipe, reply, 10_100_000, requestLife.Token);
                requestLife.Cancel();
                try { await disconnected; } catch { /* closed/cancelled by design */ }
            }
            catch (OperationCanceledException) { }
            catch { try { await Task.Delay(100, lifetime.Token); } catch (OperationCanceledException) { } }
        }
    }
    private static async Task ObserveDisconnect(Stream pipe, CancellationTokenSource cancellation)
    {
        try { await pipe.ReadAsync(new byte[1], cancellation.Token); }
        catch { }
        finally { cancellation.Cancel(); }
    }
    public void Dispose()
    {
        lifetime.Cancel();
        while (jobs.TryDequeue(out var job)) job.Completion.TrySetCanceled();
        // Do not block FFXIV waiting for a background capture/pipe shutdown.
        _ = worker.ContinueWith(_ => lifetime.Dispose(), TaskScheduler.Default);
    }
}
