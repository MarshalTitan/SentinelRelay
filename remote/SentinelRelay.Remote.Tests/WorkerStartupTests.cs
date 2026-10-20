using System.Diagnostics;
using System.Text.Json;
using SentinelRelay.Remote;
using SentinelRelay.RemoteHost;
using SentinelRelay.RemoteInput;

static class WorkerStartupTests
{
    // A real child process and anonymous stdin/stdout, without native USB/controller attachment.
    // This verifies the host launch/ready/packet path separately from the standalone native proof.
    public static async Task<int> Child(string mode)
    {
        if (mode == "fail") { Console.WriteLine("{\"ready\":false,\"code\":\"backend_attach_failed\"}"); return 2; }
        if (mode == "malformed") { Console.WriteLine("not json"); return 2; }
        Console.WriteLine("{\"ready\":true,\"code\":\"worker_ready\"}"); Console.Out.Flush();
        using var life = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var stream = Console.OpenStandardInput();
        var first = await WorkerWire.Read(stream, life.Token);
        if (first.ToFrame() != ControllerFrame.Neutral || !WorkerWire.Accept(first, 0, Stopwatch.GetTimestamp())) return 3;
        var second = await WorkerWire.Read(stream, life.Token);
        if (second.Sequence != first.Sequence + 1 || second.Buttons != 0x1000 ||
            second.LeftX != .5 || !WorkerWire.Accept(second, first.Sequence, Stopwatch.GetTimestamp())) return 4;
        if (mode == "success") return 0;
        await Task.Delay(Timeout.Infinite, life.Token);
        return 0;
    }
    private static ProcessStartInfo Info(string mode)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException();
        var info = new ProcessStartInfo(executable);
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(typeof(WorkerStartupTests).Assembly.Location);
        info.ArgumentList.Add("--test-input-child"); info.ArgumentList.Add(mode);
        return info;
    }
    public static async Task Run(Action<bool, string> check)
    {
        using (var output = await WorkerOutput.StartProcess(Info("success")))
        {
            output.Send(new(.5, 0, 0, 0, 0, 0, 0x1000));
            check(await output.CompletionForTest().WaitAsync(TimeSpan.FromSeconds(3)) == 0,
                "host worker startup sends fresh neutral then complete rising-sequence input through real pipe");
        }
        using (var output = await WorkerOutput.StartProcess(Info("hold")))
        {
            output.Send(new(.5, 0, 0, 0, 0, 0, 0x1000));
            var pid = output.ProcessId;
            output.Dispose();
            var deadline = Stopwatch.GetTimestamp();
            bool Alive()
            {
                try { using var p = Process.GetProcessById(pid); return !p.HasExited; }
                catch (ArgumentException) { return false; }
            }
            while (Alive() && Stopwatch.GetElapsedTime(deadline) < TimeSpan.FromSeconds(2)) await Task.Delay(10);
            check(!Alive(), "host output dispose terminates its child process");
        }
        try { using var failed = await WorkerOutput.StartProcess(Info("fail")); check(false, "backend error propagation"); }
        catch (InputBackendException error) { check(error.Code == "backend_attach_failed", "host preserves known worker startup failure code"); }
        try { using var failed = await WorkerOutput.StartProcess(Info("malformed")); check(false, "malformed worker response"); }
        catch (InputBackendException error) { check(error.Code == "input_worker_reply_invalid", "host rejects malformed worker ready reply without raw text"); }
    }
}
