using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using SentinelRelay.Remote;

namespace SentinelRelay.RemoteInput;

public enum BackendStage { File, Workspace, Start, Job, WaitReady, CreateBus, AddDevice, Connect }

/// <summary>
/// Experimental, worker-owned backend. Never connects to an already running VIIPER.
/// The host must authorize the target before launching a worker; this class has no network-facing API.
/// </summary>
public sealed class ViiperBackend : IControllerOutput, IDisposable
{
    public const string Version = "0.9.0";
    public const string WindowsSha256 = "142c144f6abee8b8a5f6a1abbc7018683444de71d2e45102cde6aaab9b2223e6";
    public const string LinuxSha256 = "9221f840da46029641712ee7c51d4e33d1f60444c6814fa013b859b4b91c5f07";
    private readonly Process process;
    private readonly WindowsChildJob? job;
    private readonly string directory;
    private TcpClient? controller;
    private int disposed;
    private int finalized;
    private readonly object sendGate = new();
    public int ApiPort { get; }
    public int UsbPort { get; }
    public int ProcessId => process.Id;
    private ViiperBackend(Process process, WindowsChildJob? job, string directory, int apiPort, int usbPort)
    {
        this.process = process; this.job = job; this.directory = directory; ApiPort = apiPort; UsbPort = usbPort;
    }

    public static async Task<ViiperBackend> Start(string executable, bool attach, CancellationToken cancellation = default, Action<BackendStage>? stageChanged = null)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        stageChanged?.Invoke(BackendStage.File);
        executable = Path.GetFullPath(executable);
        if (!File.Exists(executable) || (File.GetAttributes(executable) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("backend_file");
        using (var file = File.OpenRead(executable))
        {
            var expected = OperatingSystem.IsWindows() ? WindowsSha256 : LinuxSha256;
            if (!Convert.ToHexString(SHA256.HashData(file)).Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("backend_hash_mismatch");
        }
        stageChanged?.Invoke(BackendStage.Workspace);
        var directory = Path.Combine(Path.GetTempPath(), "SentinelInput-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        if (OperatingSystem.IsWindows())
        {
            var acl = new DirectorySecurity();
            acl.SetAccessRuleProtection(true, false);
            acl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(directory).SetAccessControl(acl);
        }
        else File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var apiPort = FreePort(); var usbPort = FreePort();
        while (usbPort == apiPort) usbPort = FreePort();
        var config = Path.Combine(directory, "isolated.json");
        File.WriteAllText(config, "{}");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = directory,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("VIIPER_", StringComparison.OrdinalIgnoreCase)).ToArray())
            start.Environment.Remove(key);
        start.Environment["AppData"] = directory;
        start.Environment["XDG_CONFIG_HOME"] = directory;
        // Explicit flags outrank all config sources. No auto-update, tray, raw logs, shell, or remote listener.
        foreach (var arg in new[] { "server", "--config=" + config, "--update-notify=none", "--log.level=error",
            "--no-tray", "--api.addr=127.0.0.1:" + apiPort, "--usb.addr=127.0.0.1:" + usbPort,
            "--api.auto-attach-local-client=" + (attach ? "true" : "false"),
            "--api.device-handler-connect-timeout=5s", "--connection-timeout=2s" })
            start.ArgumentList.Add(arg);
        Process? process = null; WindowsChildJob? job = null; ViiperBackend? backend = null;
        try
        {
            stageChanged?.Invoke(BackendStage.Job);
            if (OperatingSystem.IsWindows()) job = new WindowsChildJob();
            stageChanged?.Invoke(BackendStage.Start);
            process = Process.Start(start) ?? throw new InputBackendException("backend_start");
            stageChanged?.Invoke(BackendStage.Job);
            if (job is not null) job.Add(process); // Before creating a device. Worker death closes this handle.
            _ = Drain(process.StandardOutput);
            _ = Drain(process.StandardError); // Never forward passwords, arbitrary logs, or input values.
            backend = new(process, job, directory, apiPort, usbPort);
            stageChanged?.Invoke(BackendStage.WaitReady);
            await backend.WaitReady(cancellation);
            stageChanged?.Invoke(BackendStage.CreateBus);
            using var busReply = await ViiperWire.Request(apiPort, "bus/create", cancellation, backend.OwnsEndpoints);
            var bus = busReply.RootElement.GetProperty("busId").GetUInt32();
            if (bus == 0) throw new InvalidDataException("backend_bus");
            stageChanged?.Invoke(BackendStage.AddDevice);
            using var added = await ViiperWire.Request(apiPort, "bus/" + bus + "/add {\"type\":\"xbox360\"}", cancellation, backend.OwnsEndpoints);
            var id = added.RootElement.GetProperty("devId").GetString();
            if (id is null || id.Length is < 1 or > 10 || !uint.TryParse(id, out var device) || device == 0 ||
                added.RootElement.GetProperty("busId").GetUInt32() != bus ||
                added.RootElement.GetProperty("type").GetString() != "xbox360")
                throw new InvalidDataException("backend_device");
            stageChanged?.Invoke(BackendStage.Connect);
            var tcp = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true, SendTimeout = 100, ReceiveBufferSize = 1024 };
            backend.controller = tcp;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            await tcp.ConnectAsync(IPAddress.Loopback, apiPort, timeout.Token);
            if (!backend.OwnsEndpoints()) throw new InputBackendException("backend_endpoint_owner_changed");
            await tcp.GetStream().WriteAsync(System.Text.Encoding.ASCII.GetBytes("bus/" + bus + "/" + id + "\0"), timeout.Token);
            backend.Neutralize();
            _ = DrainFeedback(tcp.GetStream());
            return backend;
        }
        catch
        {
            if (backend is not null) backend.Dispose();
            else
            {
                job?.Dispose();
                if (process is not null)
                {
                    try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
                    process.Dispose();
                }
                Directory.Delete(directory, true);
            }
            throw;
        }
    }

    private bool OwnsEndpoints() => !process.HasExited && BackendEndpointOwnership.Matches(process.Id, ApiPort, UsbPort);

    private async Task WaitReady(CancellationToken cancellation)
    {
        var started = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(5))
        {
            if (process.HasExited) throw new InputBackendException("backend_exited_before_ready");
            try
            {
                if (!OwnsEndpoints()) { await Task.Delay(50, cancellation); continue; }
                using var ping = await ViiperWire.Request(ApiPort, "ping", cancellation, OwnsEndpoints);
                var version = ping.RootElement.GetProperty("version").GetString();
                if (ping.RootElement.GetProperty("server").GetString() != "VIIPER" || version is not ("0.9.0" or "v0.9.0"))
                    throw new InvalidDataException("backend_version");
                return;
            }
            catch (SocketException) { await Task.Delay(50, cancellation); } // Readiness only; never retry a device/input mutation.
        }
        throw new InputBackendException("backend_readiness");
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }
    private static async Task Drain(StreamReader reader)
    {
        try { var buffer = new char[512]; while (await reader.ReadAsync(buffer) != 0) { } }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }
    private static async Task DrainFeedback(Stream stream)
    {
        try { var buffer = new byte[64]; while (await stream.ReadAsync(buffer) != 0) { } }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }
    public void Send(ControllerFrame frame)
    {
        var bytes = ViiperWire.Encode(frame);
        lock (sendGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            (controller ?? throw new InputBackendException("backend_stream_missing")).GetStream().Write(bytes);
        }
    }
    public void Neutralize() => Send(ControllerFrame.Neutral);

    // Does not wait for sendGate: the independent watchdog can stop a blocked sender.
    public void Abort()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        try { controller?.Dispose(); }
        finally
        {
            if (job is not null) job.Dispose(); // Windows kills the owned backend without waiting for managed callbacks.
            else
            {
                try { if (!process.HasExited) process.Kill(true); }
                catch (InvalidOperationException) { }
            }
        }
    }
    public void Dispose()
    {
        Abort();
        if (Interlocked.Exchange(ref finalized, 1) != 0) return;
        if (!process.WaitForExit(1500)) throw new InputBackendException("backend_did_not_exit");
        process.Dispose();
        try { Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
