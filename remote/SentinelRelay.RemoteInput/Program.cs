using System.Diagnostics;
using System.Text.Json;
using SentinelRelay.Remote;
using SentinelRelay.RemoteInput;

if (args.Length is < 2 or > 3 || args[0] != "--worker" || (args.Length == 3 && args[2] != "--protocol-only"))
{
    Console.WriteLine("{\"ready\":false,\"code\":\"local_worker_only\"}");
    return 1;
}
ViiperBackend? backend = null;
try
{
    backend = await ViiperBackend.Start(args[1], args.Length != 3);
    using var stop = new ManualResetEventSlim();
    long lastFrame = Stopwatch.GetTimestamp();
    var watcher = new Thread(() =>
    {
        while (!stop.Wait(25))
        {
            if (Stopwatch.GetElapsedTime(Interlocked.Read(ref lastFrame)) < ControllerLease.FrameTimeout) continue;
            backend.Abort();
            Environment.Exit(2); // Console pipe reads are not reliably cancellable on every platform.
        }
    }) { IsBackground = true, Name = "Sentinel input fail-safe" };
    // Start the independent watchdog before announcing readiness.
    watcher.Start();
    try
    {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            ready = true, code = "worker_ready", backendPid = backend.ProcessId, apiPort = backend.ApiPort, usbPort = backend.UsbPort
        }));
        Console.Out.Flush();
        var input = Console.OpenStandardInput();
        long sequence = 0;
        while (true)
        {
            var packet = await WorkerWire.Read(input, CancellationToken.None);
            var now = Stopwatch.GetTimestamp();
            if (!WorkerWire.Accept(packet, sequence, now)) throw new InvalidDataException("worker_frame_rejected");
            sequence = packet.Sequence;
            Interlocked.Exchange(ref lastFrame, now);
            backend.Send(packet.ToFrame());
        }
    }
    finally { stop.Set(); watcher.Join(1000); }
}
catch (Exception error)
{
    // Stable outcome only. Never forward backend logs, command text, paths, frames, or exceptions.
    Console.WriteLine(JsonSerializer.Serialize(new { ready = false,
        code = error is InputBackendException failure ? failure.Code : "worker_stopped", kind = error.GetType().Name }));
    return 2;
}
finally { backend?.Dispose(); }
