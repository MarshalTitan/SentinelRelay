using System.Security.Principal;

namespace SentinelRelay.RemoteDesktop;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var smokeIndex = Array.IndexOf(args, "--smoke-test");
        try
        {
            if (WindowsLaunch.RedirectIfNeeded(args)) return 0;
        }
        catch
        {
            if (smokeIndex < 0)
                MessageBox.Show("Sentinel Remote could not open in your Windows user session. Please report startup_launch_failed.", "Sentinel Remote");
            return 1;
        }
        if (smokeIndex >= 0 && smokeIndex + 1 < args.Length)
            return DesktopSmoke.Run(args[smokeIndex + 1]);
        using var mutex = new Mutex(true, "Local\\SentinelRemoteDesktop-" + WindowsIdentity.GetCurrent().User!.Value, out var first);
        if (!first)
        {
            MessageBox.Show("Sentinel Remote is already open. Use its icon beside the Windows clock to open the setup window.", "Sentinel Remote");
            return 0;
        }
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sentinel", "Remote");
            Application.Run(new MainForm(directory, args.Contains("--minimized")));
            return 0;
        }
        catch
        {
            MessageBox.Show("Sentinel Remote could not open its local settings. Close any other host window and try again.", "Sentinel Remote");
            return 1;
        }
    }
}
