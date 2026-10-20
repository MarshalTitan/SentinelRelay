using System.Diagnostics;
using System.Runtime.InteropServices;
using SentinelRelay.Remote;

namespace SentinelRelay.RemoteHost;

// Cache a recent authenticated bridge snapshot; never wait on the game inside a lease lock.
public sealed class ManualTarget : IManualTarget
{
    private readonly GameBinding binding;
    private readonly CancellationTokenSource cancel = new();
    private volatile string? epoch;
    private volatile string status = "target_unavailable";
    private long observed;
    public string? GameEpoch => epoch;
    public string StatusCode => status;
    private ManualTarget(GameBinding binding) { this.binding = binding; }
    public static async Task<IManualTarget> Create(GameBinding binding)
    {
        var result = new ManualTarget(binding);
        await Task.Run(result.Observe);
        _ = Task.Run(result.Monitor);
        return result;
    }
    private async Task Monitor()
    {
        try
        {
            while (!cancel.IsCancellationRequested)
            { await Task.Delay(75, cancel.Token); Observe(); }
        }
        catch { epoch = null; }
    }
    private void Observe()
    {
        try
        {
            var reason = Inspect(binding);
            if (reason.Length != 0) { status = reason; epoch = null; return; }
            using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(180));
            var snapshot = new PipeGameBridge().ReadBounded(binding, deadline.Token);
            if (snapshot is null) { status = "target_bridge_unavailable"; epoch = null; return; }
            if (!snapshot.ControlAllowed) { status = "target_control_disabled"; epoch = null; return; }
            reason = Inspect(binding);
            if (reason.Length != 0) { status = reason; epoch = null; return; }
            status = "target_ready";
            Interlocked.Exchange(ref observed, Stopwatch.GetTimestamp());
            epoch = snapshot.GameEpoch;
        }
        catch { status = "target_visibility_unknown"; epoch = null; }
    }
    public bool Allowed(ControllerOwner owner)
    {
        if (cancel.IsCancellationRequested) return false;
        if (owner.Binding != binding) { status = "target_process_changed"; return false; }
        var reason = Inspect(binding);
        if (reason.Length != 0) { status = reason; return false; }
        if (epoch is null) return false; // Preserve the observation failure instead of hiding it as an epoch change.
        if (owner.GameEpoch != epoch) { status = "target_epoch_changed"; return false; }
        if (Stopwatch.GetElapsedTime(Interlocked.Read(ref observed)) >= TimeSpan.FromMilliseconds(300))
        { status = "target_snapshot_stale"; return false; }
        return true;
    }
    public sealed record ObservedProcess(int Id, long StartedUtcTicks, string? Executable);
    public static bool MatchesOnlyProcess(GameBinding binding, ObservedProcess[]? allGames, int foregroundPid) =>
        ExplainProcess(binding, allGames, foregroundPid).Length == 0;
    public static string ExplainProcess(GameBinding binding, ObservedProcess[]? allGames, int foregroundPid)
    {
        if (allGames is null) return "target_visibility_unknown";
        if (allGames.Length == 0) return "target_no_game";
        if (allGames.Length != 1) return "target_extra_game";
        var game = allGames[0];
        if (game.Id != binding.ProcessId || game.StartedUtcTicks != binding.StartedUtcTicks ||
            string.IsNullOrWhiteSpace(game.Executable) ||
            !string.Equals(game.Executable, binding.Executable, StringComparison.OrdinalIgnoreCase)) return "target_process_changed";
        return foregroundPid == binding.ProcessId ? "" : "target_foreground";
    }
    private static string Inspect(GameBinding binding)
    {
        if (!OperatingSystem.IsWindows()) return "target_visibility_unknown";
        try
        {
            var games = Process.GetProcessesByName("ffxiv_dx11").Concat(Process.GetProcessesByName("ffxiv")).ToArray();
            try
            {
                if (games.Length == 0) return "target_no_game";
                if (games.Length != 1) return "target_extra_game"; // Also unbound and other-session games.
                GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
                return ExplainProcess(binding,
                    [new(games[0].Id, games[0].StartTime.ToUniversalTime().Ticks, games[0].MainModule?.FileName)], checked((int)pid));
            }
            finally { foreach (var game in games) game.Dispose(); }
        }
        catch { return "target_visibility_unknown"; }
    }
    public void Dispose() { cancel.Cancel(); epoch = null; }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
}
