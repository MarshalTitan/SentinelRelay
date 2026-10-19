using System.Diagnostics;
using System.IO.Pipes;
using SentinelRelay.Bridge;
using SentinelRelay.Services;

static class PipeServerTests
{
    public static async Task Run(Action<bool, string> check)
    {
        using var server = new MobilePipeServer("cid:A");
        async Task WaitForJob()
        {
            var timer = Stopwatch.StartNew();
            while (server.PendingCount == 0 && timer.Elapsed < TimeSpan.FromSeconds(3)) await Task.Delay(10);
            check(server.PendingCount > 0, "local pipe queued one bounded framework job");
        }
        async Task<NamedPipeClientStream> Connect()
        {
            var client = new NamedPipeClientStream(".", BridgeWire.PipeName(Environment.ProcessId), PipeDirection.InOut, PipeOptions.Asynchronous);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await client.ConnectAsync(timeout.Token);
            await BridgeWire.Write(client, new BridgeRequest(1, "srank.skip", "cid:A", server.GameEpoch), 4096, timeout.Token);
            return client;
        }
        var executions = 0;
        var client = await Connect(); await WaitForJob();
        client.Dispose(); await Task.Delay(150);
        server.Pump((_, _) => { executions++; return new(true, "executed"); });
        check(executions == 0, "local disconnect cancels work before framework dispatch");
        client = await Connect(); await WaitForJob();
        server.Pump((_, _) => { executions++; return new(true, "executed"); });
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
        {
            var reply = await BridgeWire.Read<BridgeReply>(client, 8192, timeout.Token);
            check(reply.Success && executions == 1, "live local pipe executes one framework callback and replies");
        }
        client.Dispose();
        client = await Connect(); await WaitForJob();
        await Task.Delay(4200);
        server.Pump((_, _) => { executions++; return new(true, "executed"); });
        check(executions == 1, "expired local job cannot execute on a delayed game tick");
        client.Dispose();
    }
}

namespace SentinelRelay.Services
{
    // Capture is never invoked by these IPC lifecycle tests. Native capture remains an in-game gate.
    internal static class GameWindowCapture
    {
        internal sealed record Capture(bool Success, byte[]? PngBytes);
        public static Capture CapturePng(nint _) => new(false, null);
    }
}
