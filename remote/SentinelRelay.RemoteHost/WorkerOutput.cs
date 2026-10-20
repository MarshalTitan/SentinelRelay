using System.Diagnostics;
using System.Text.Json;
using SentinelRelay.Remote;
using SentinelRelay.RemoteInput;

namespace SentinelRelay.RemoteHost;

public sealed class WorkerOutput : IManualOutput
{
    private readonly Process process;
    internal int ProcessId => process.Id;
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
        return await StartProcess(info);
    }
    internal static async Task<WorkerOutput> StartProcess(ProcessStartInfo info)
    {
        info.UseShellExecute = false; info.CreateNoWindow = true;
        info.RedirectStandardInput = info.RedirectStandardOutput = info.RedirectStandardError = true;
        Process process;
        try { process = Process.Start(info) ?? throw new InputBackendException("input_worker_unavailable"); }
        catch { throw new InputBackendException("input_worker_unavailable"); }
        try
        {
            _ = process.StandardError.ReadToEndAsync();
            var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(8));
            if (line is null || line.Length > 1024) throw new InputBackendException("input_worker_reply_invalid");
            using var ready = JsonDocument.Parse(line);
            if (!ready.RootElement.TryGetProperty("ready", out var value) || value.ValueKind != JsonValueKind.True)
            {
                var code = ready.RootElement.TryGetProperty("code", out var reason) && reason.ValueKind == JsonValueKind.String
                    ? reason.GetString() : null;
                throw new InputBackendException(code ?? "input_backend_unavailable");
            }
            _ = process.StandardOutput.ReadToEndAsync();
            var output = new WorkerOutput(process);
            output.Write(ControllerFrame.Neutral); // Start with a fresh neutral packet, as in the native proof.
            return output;
        }
        catch (Exception error)
        {
            try { process.StandardInput.Close(); process.Kill(); } catch { }
            process.Dispose();
            if (error is TimeoutException) throw new InputBackendException("input_worker_timeout");
            if (error is InputBackendException) throw;
            throw new InputBackendException("input_worker_reply_invalid");
        }
    }
    public void Send(ControllerFrame frame)
    {
        if (disposed || process.HasExited) throw new InputBackendException("input_worker_stopped");
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
        catch { Dispose(); throw new InputBackendException("input_worker_stopped"); }
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
