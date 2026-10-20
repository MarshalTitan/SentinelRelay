using System.Diagnostics;
using System.Security.Cryptography;
using SentinelRelay.RemoteInput;
using System.Runtime.InteropServices;
using System.Security.Principal;
using SentinelRelay.Remote;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (!GetTokenInformation(identity.Token, 18, out var elevationType, sizeof(int), out _) ||
                DesktopLaunchPolicy.RequiresStandardToken(elevationType, identity.IsSystem ||
                    identity.User?.IsWellKnown(WellKnownSidType.LocalServiceSid) == true ||
                    identity.User?.IsWellKnown(WellKnownSidType.NetworkServiceSid) == true, false))
            {
                MessageBox.Show("Open Gamepad Check normally from Start so it can test your usual Windows session.",
                    "Sentinel Remote", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 1;
            }
            if (args.Length == 2 && args[0] == "--verify-backend")
            {
                using var stream = File.OpenRead(args[1]);
                return Convert.ToHexString(SHA256.HashData(stream)).Equals(ViiperBackend.WindowsSha256, StringComparison.OrdinalIgnoreCase) &&
                    SignatureCheck.Valid(args[1]) ? 0 : 1;
            }
            using var form = new CheckWindow(elevationType);
            if (args.Length == 2 && args[0] == "--smoke-test")
            {
                Directory.CreateDirectory(args[1]);
                form.Show(); Application.DoEvents();
                foreach (var state in new[] { "initial", "passed", "failed" })
                {
                    form.Preview(state); Application.DoEvents();
                    using var bitmap = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                    bitmap.Save(Path.Combine(args[1], "gamepad-" + state + ".png"));
                }
                return 0;
            }
            if (args.Length != 0) return 1;
            Application.Run(form);
            return 0;
        }
        catch
        {
            MessageBox.Show("Gamepad Check could not start. Open it normally from Start and copy the result if the check fails.",
                "Sentinel Remote", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 1;
        }
    }
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(nint token, int informationClass, out int information, int size, out int returned);
}
