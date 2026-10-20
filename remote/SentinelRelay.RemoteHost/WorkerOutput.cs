using System.Diagnostics;
using System.Text.Json;
using SentinelRelay.Remote;
using SentinelRelay.RemoteInput;

namespace SentinelRelay.RemoteHost;

public sealed class WorkerOutput : IManualOutput
{
    private readonly Process process;
    private long sequence;
    private bool sent, disposed;
    private WorkerOutput(Process process) { this.process = process; }
    public static async Task<IManualOutput> Start(string backend)
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "worker", "SentinelRelay.RemoteInput.exe");
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        info.ArgumentList.Add("--worker"); info.ArgumentList.Add(backend);
        var process = Process.Start(info) ?? throw new IOException("input_worker_unavailable");
        try
        {
            _ = process.StandardError.ReadToEndAsync();
            var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            if (line is null || line.Length > 1024) throw new IOException();
            using var ready = JsonDocument.Parse(line);
            if (!ready.RootElement.TryGetProperty("ready", out var value) || value.ValueKind != JsonValueKind.True)
                throw new IOException("input_backend_unavailable");
            _ = process.StandardOutput.ReadToEndAsync();
            return new WorkerOutput(process);
        }
        catch
        {
            try { process.StandardInput.Close(); process.Kill(); } catch { }
            process.Dispose(); throw;
        }
    }
    public void Send(ControllerFrame frame)
    {
        if (disposed || process.HasExited) throw new IOException("input_worker_stopped");
        sent = true;
        Write(frame);
    }
    private void Write(ControllerFrame frame)
    {
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            WorkerWire.Write(process.StandardInput.BaseStream, WorkerPacket.Create(++sequence, frame), deadline.Token)
                .WaitAsync(TimeSpan.FromMilliseconds(120)).GetAwaiter().GetResult();
        }
        catch { Dispose(); throw new IOException("input_worker_stopped"); }
    }
    public void Neutralize()
    {
        if (disposed) return;
        // Acquire requires an initial neutral without detaching the newly-created device.
        if (!sent) { Write(ControllerFrame.Neutral); return; }
        Dispose(); // Device removal releases the entire state, including independent triggers.
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        // EOF and force-kill are both field-proven cleanup paths. No send lock is needed to abort.
        try { process.StandardInput.Close(); } catch { }
        try { if (!process.HasExited) process.Kill(); } catch { }
        process.Dispose();
    }
}
