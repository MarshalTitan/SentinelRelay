using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using SentinelRelay.Remote;

namespace SentinelRelay.RemoteHost;

public sealed record LauncherSelection(string Path, string Hash);

/// <summary>Locally configured launch/restore. Independent from a logged-in bridge or gamepad lease.</summary>
public sealed class RecoveryActions : IRecoveryActions
{
    private readonly object gate = new();
    private readonly HashSet<string> permitted = [];
    private LauncherSelection? launcher;
    private readonly Func<LauncherSelection, Reply> open;
    private readonly Func<GameBinding, Reply> restore;
    public RecoveryActions() : this(Open, Restore) { }
    internal RecoveryActions(Func<LauncherSelection, Reply> open, Func<GameBinding, Reply> restore)
    { this.open = open; this.restore = restore; }
    public void Configure(LauncherSelection? selection) { lock (gate) launcher = selection; }
    public void Permit(string device, bool enabled)
    { lock (gate) { if (enabled) permitted.Add(device); else permitted.Remove(device); } }
    public void Clear() { lock (gate) permitted.Clear(); }
    public Reply Execute(string deviceId, string operation, GameBinding? binding)
    {
        lock (gate)
        {
            if (!permitted.Contains(deviceId)) return new(false, "recovery_not_permitted");
            try
            {
                return operation switch
                {
                    "launcher.open" => launcher is null ? new(false, "launcher_not_configured") : open(launcher),
                    "game.restore" => binding is null ? new(false, "process_exited") : restore(binding),
                    _ => new(false, "operation_not_allowed"),
                };
            }
            catch { return new(false, "recovery_failed"); }
        }
    }
    public static LauncherSelection SelectLauncher(string path)
    {
        if (!OperatingSystem.IsWindows() || !System.IO.Path.IsPathFullyQualified(path) ||
            path.StartsWith(@"\\") || !string.Equals(System.IO.Path.GetFileName(path), "XIVLauncher.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("launcher_path_invalid");
        var full = System.IO.Path.GetFullPath(path);
        for (var current = new FileInfo(full) as FileSystemInfo; current is not null;
            current = current is FileInfo file ? file.Directory : ((DirectoryInfo)current).Parent)
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("launcher_link_invalid");
        using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is < 64 or > 104857600 || stream.ReadByte() != 'M' || stream.ReadByte() != 'Z')
            throw new InvalidDataException("launcher_file_invalid");
        stream.Position = 0;
        return new(full, Convert.ToHexString(SHA256.HashData(stream)));
    }
    private static Reply Open(LauncherSelection selection)
    {
        using var host = Process.GetCurrentProcess();
        if (!InteractiveDesktop.Available(host.SessionId)) return new(false, "recovery_desktop_unavailable");
        // Hold a non-writable/non-deletable file handle through verification and CreateProcess.
        using var pinned = new FileStream(selection.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (SelectLauncher(selection.Path) != selection) return new(false, "launcher_changed");
        var games = Process.GetProcessesByName("ffxiv_dx11").Concat(Process.GetProcessesByName("ffxiv")).ToArray();
        try { if (games.Length != 0) return new(false, "game_already_running"); }
        finally { foreach (var game in games) game.Dispose(); }
        var existing = Process.GetProcessesByName("XIVLauncher");
        try
        {
            if (existing.Any(p => p.SessionId == host.SessionId))
                return new(true, "launcher_already_running");
        }
        finally { foreach (var p in existing) p.Dispose(); }
        using var started = Process.Start(new ProcessStartInfo(selection.Path)
        {
            UseShellExecute = false,
            WorkingDirectory = System.IO.Path.GetDirectoryName(selection.Path)!,
        });
        return started is null ? new(false, "recovery_failed") : new(true, "launcher_opened");
    }
    private static Reply Restore(GameBinding binding)
    {
        if (!OperatingSystem.IsWindows()) return new(false, "recovery_unavailable");
        using var game = Process.GetProcessById(binding.ProcessId);
        if (!InteractiveDesktop.Available(game.SessionId)) return new(false, "recovery_desktop_unavailable");
        if (game.StartTime.ToUniversalTime().Ticks != binding.StartedUtcTicks ||
            !string.Equals(game.MainModule?.FileName, binding.Executable, StringComparison.OrdinalIgnoreCase) ||
            game.ProcessName is not ("ffxiv_dx11" or "ffxiv")) return new(false, "character_changed");
        var window = game.MainWindowHandle;
        if (window == 0) return new(false, "game_window_unavailable");
        GetWindowThreadProcessId(window, out var pid);
        if (pid != binding.ProcessId) return new(false, "character_changed");
        // Restore only. Never steal foreground focus or synthesize desktop input.
        if (!ShowWindowAsync(window, 9 /* SW_RESTORE */)) return new(false, "game_window_unavailable");
        return new(true, "game_restore_requested");
    }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(nint window, int command);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
}
