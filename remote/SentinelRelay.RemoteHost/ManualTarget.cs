using System.Diagnostics;
using System.Runtime.InteropServices;
using SentinelRelay.Remote;

namespace SentinelRelay.RemoteHost;

// Cache only a recent authenticated bridge snapshot; never wait on the game inside a lease lock.
public sealed class ManualTarget : IManualTarget
{
    private readonly GameBinding binding;
    private readonly CancellationTokenSource cancel = new();
    private volatile string? epoch;
    private long observed;
    public string? GameEpoch => epoch;
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
            if (!SingleForeground(binding)) { epoch = null; return; }
            using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(180));
            var snapshot = new PipeGameBridge().ReadBounded(binding, deadline.Token);
            if (snapshot is not { ControlAllowed: true } || !SingleForeground(binding)) { epoch = null; return; }
            epoch = snapshot.GameEpoch;
            Interlocked.Exchange(ref observed, Stopwatch.GetTimestamp());
        }
        catch { epoch = null; }
    }
    public bool Allowed(ControllerOwner owner) => !cancel.IsCancellationRequested && owner.Binding == binding &&
        owner.GameEpoch == epoch && Stopwatch.GetElapsedTime(Interlocked.Read(ref observed)) < TimeSpan.FromMilliseconds(300) &&
        SingleForeground(binding);
    private static bool SingleForeground(GameBinding binding)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            var games = Process.GetProcessesByName("ffxiv_dx11").Concat(Process.GetProcessesByName("ffxiv")).ToArray();
            try
            {
                if (games.Length != 1 || games[0].Id != binding.ProcessId ||
                    games[0].StartTime.ToUniversalTime().Ticks != binding.StartedUtcTicks ||
                    !string.Equals(games[0].MainModule?.FileName, binding.Executable, StringComparison.OrdinalIgnoreCase)) return false;
                GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
                return pid == binding.ProcessId; // Also releases on screen lock/focus loss. This is additional to the one-client veto.
            }
            finally { foreach (var game in games) game.Dispose(); }
        }
        catch { return false; }
    }
    public void Dispose() { cancel.Cancel(); epoch = null; /* monitor owns its bounded pending read */ }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
}
