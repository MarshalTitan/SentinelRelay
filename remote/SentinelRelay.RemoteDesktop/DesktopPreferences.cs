using System.Text.Json;
using Microsoft.Win32;

namespace SentinelRelay.RemoteDesktop;

public sealed record DesktopPreferences(string? Host = null, string? Login = null, bool ResumeOnLaunch = false)
{
    public static DesktopPreferences Load(string directory)
    {
        var path = Path.Combine(directory, "desktop.json");
        if (!File.Exists(path)) return new();
        RejectLink(path);
        return JsonSerializer.Deserialize<DesktopPreferences>(File.ReadAllText(path)) ?? throw new InvalidDataException();
    }
    public void Save(string directory)
    {
        var path = Path.Combine(directory, "desktop.json");
        RejectLink(path);
        var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void RejectLink(string path)
    {
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException();
    }
    public static bool StartsWithWindows()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue("SentinelRemote") is string;
    }
    public static void SetStartsWithWindows(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        if (enabled) key.SetValue("SentinelRemote", "\"" + Application.ExecutablePath + "\" --minimized");
        else key.DeleteValue("SentinelRemote", false);
    }
}
