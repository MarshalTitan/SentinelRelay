using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using SentinelRelay.Bridge;
using SentinelRelay.Remote;
namespace SentinelRelay.RemoteHost;

public sealed class PipeGameBridge : IGameBridge
{
    private readonly Dictionary<string, BridgeSnapshot> lastKnown = [];
    public static GameBinding Enroll(string instanceId, int processId)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var process = Process.GetProcessById(processId);
        if (process.ProcessName != "ffxiv_dx11") throw new InvalidOperationException("not_ffxiv");
        var executable = process.MainModule?.FileName ?? throw new InvalidOperationException("executable_unavailable");
        var provisional = new GameBinding(instanceId, processId, process.StartTime.ToUniversalTime().Ticks, executable, "");
        var reply = Call(provisional, new(1, "hello"), CancellationToken.None);
        if (!reply.Success || reply.Snapshot is not { } snapshot || string.IsNullOrWhiteSpace(snapshot.CharacterKey))
            throw new InvalidOperationException("plugin_unavailable");
        return provisional with { CharacterKey = snapshot.CharacterKey };
    }
    private static bool Alive(GameBinding binding)
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return false;
            using var process = Process.GetProcessById(binding.ProcessId);
            return process.ProcessName == "ffxiv_dx11" && process.StartTime.ToUniversalTime().Ticks == binding.StartedUtcTicks &&
                string.Equals(process.MainModule?.FileName, binding.Executable, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
    private static string ProcessState(GameBinding binding)
    {
        if (!OperatingSystem.IsWindows()) return "unknown";
        try
        {
            using var process = Process.GetProcessById(binding.ProcessId);
            if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != binding.StartedUtcTicks) return "exited";
            return Alive(binding) ? "running" : "unknown";
        }
        catch (ArgumentException) { return "exited"; }
        catch { return "unknown"; }
    }
    private static BridgeReply Call(GameBinding binding, BridgeRequest request, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(3.5));
        return Run().GetAwaiter().GetResult();
        async Task<BridgeReply> Run()
        {
            if (!Alive(binding)) throw new InvalidOperationException("process_exited");
            using var pipe = new NamedPipeClientStream(".", BridgeWire.PipeName(binding.ProcessId), PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(timeout.Token);
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var pid) || pid != binding.ProcessId || !Alive(binding))
                throw new InvalidOperationException("wrong_pipe_owner");
            await BridgeWire.Write(pipe, request, 4096, timeout.Token);
            var reply = await BridgeWire.Read<BridgeReply>(pipe, 10_100_000, timeout.Token);
            if (!Alive(binding)) throw new InvalidOperationException("process_changed");
            return reply;
        }
    }
    public BridgeSnapshot? Read(GameBinding binding)
    {
        try
        {
            var reply = Call(binding, new(1, "hello"), CancellationToken.None);
            var snapshot = reply.Success ? reply.Snapshot : null;
            if (snapshot is null || snapshot.CharacterKey != binding.CharacterKey) return null;
            lastKnown[binding.InstanceId] = snapshot;
            return snapshot;
        }
        catch { return null; }
    }
    private static RemoteGameStatus Project(BridgeSnapshot? s, bool processAlive, bool connected) => new(
        processAlive ? "running" : "exited", connected ? "connected" : "unavailable", s?.Character, s?.World,
        s?.Territory, s?.Map, s?.HeartbeatUtc, s?.SRank, Vnavmesh: connected ? s?.Vnavmesh : null, ControlAllowed: connected ? s?.ControlAllowed : null,
        ScreenshotAllowed: connected ? s?.ScreenshotAllowed : null);
    public Reply Status(GameBinding binding)
    {
        var snapshot = Read(binding);
        lastKnown.TryGetValue(binding.InstanceId, out var previous);
        if (previous?.CharacterKey != binding.CharacterKey) previous = null;
        var status = Project(snapshot ?? previous, snapshot is not null, snapshot is not null) with { ProcessStatus = ProcessState(binding) };
        return new(true, "status", status);
    }
    public Reply Execute(GameBinding binding, string operation, string? expectedGameEpoch, Func<bool> authorized, CancellationToken cancellation)
    {
        if (operation is not ("srank.skip" or "srank.reset.confirm" or "screenshot")) return new(false, "operation_not_allowed");
        var snapshot = Read(binding);
        if (snapshot is null) return new(false, ProcessState(binding) == "exited" ? "process_exited" : "bridge_unavailable");
        if (expectedGameEpoch is not null && expectedGameEpoch != snapshot.GameEpoch) return new(false, "character_changed");
        using var requestLife = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var watch = Task.Run(async () =>
        {
            try { while (!requestLife.IsCancellationRequested) { if (!authorized()) { requestLife.Cancel(); break; } await Task.Delay(50, requestLife.Token); } }
            catch { requestLife.Cancel(); }
        });
        try
        {
            if (!authorized()) return new(false, "unauthorized");
            var reply = Call(binding, new(1, operation, binding.CharacterKey, snapshot.GameEpoch), requestLife.Token);
            if (!authorized()) return new(false, "unauthorized");
            return reply.Success ? new(true, reply.Code, operation == "screenshot" ? new { reply.PngBase64 } : Project(reply.Snapshot, true, true))
                : new(false, reply.Code);
        }
        catch { return new(false, "operation_failed"); } // Outcome may be uncertain; never retry.
        finally { requestLife.Cancel(); try { watch.GetAwaiter().GetResult(); } catch { } }
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
}
