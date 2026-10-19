using System.Security.Principal;

namespace SentinelRelay.RemoteDesktop;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args is ["--smoke-test", var output])
            return DesktopSmoke.Run(output);
        if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
        {
            MessageBox.Show("Open Sentinel Remote normally, without Run as administrator, using the Windows account that runs FFXIV.", "Sentinel Remote");
            return 1;
        }
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
