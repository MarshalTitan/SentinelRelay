using System.Drawing.Imaging;
using SentinelRelay.Remote;

namespace SentinelRelay.RemoteDesktop;

internal static class DesktopSmoke
{
    // CI renders the real form with isolated state; no Tailscale, game, registry, or user secrets.
    public static int Run(string output)
    {
        var directory = Path.Combine(Path.GetTempPath(), "sentinel-desktop-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            CheckRememberedApproval(directory);
            using var form = new MainForm(directory, smoke: true);
            form.Show();
            Application.DoEvents();
            Directory.CreateDirectory(output);
            Capture(form, Path.Combine(output, "desktop-wide.png"), new Size(860, 700));
            Capture(form, Path.Combine(output, "desktop-narrow.png"), new Size(720, 640));
            form.ScrollToGamepadPreview();
            Capture(form, Path.Combine(output, "desktop-gamepad-narrow.png"), new Size(720, 640));
            form.ScrollToGamepadPreview();
            Capture(form, Path.Combine(output, "desktop-gamepad-wide.png"), new Size(860, 700));
            using var pair = new PairingForm("https://example.test.ts.net", new string('A', 64));
            pair.Show();
            Application.DoEvents();
            Capture(pair, Path.Combine(output, "pairing.png"), new Size(650, 330));
            DesktopNativeSmoke.Run(output);
            return 0;
        }
        finally
        {
            // This path is created above solely for CI state, never a user's actual settings.
            Directory.Delete(directory, true);
        }
    }
    private static void CheckRememberedApproval(string directory)
    {
        var state = new FileStateStore(directory);
        var admin = new LocalAdministration(state);
        admin.SetEnabled(true);
        var slot = admin.AddInstance("Approved character (test)");
        var credential = (DeviceCredential)new RemoteAuthority(state).Enroll(
            new(1, admin.Invite("My phone (test)", [slot.InstanceId], true))).Data!;
        using var form = new MainForm(directory, smoke: true);
        form.Show(); Application.DoEvents();
        static IEnumerable<Control> Descendants(Control root) =>
            root.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Descendants(child)));
        var buttons = Descendants(form).OfType<Button>().ToArray();
        buttons.Single(b => b.Text == "Trust selected phone").PerformClick();
        Application.DoEvents();
        if (!new LocalAdministration(new FileStateStore(directory)).Snapshot().Devices.Single().Trusted)
            throw new InvalidOperationException("remembered_approval_failed");
        buttons.Single(b => b.Text == "Pause desktop + gamepad").PerformClick();
        Application.DoEvents();
        if (new LocalAdministration(new FileStateStore(directory)).Snapshot().Devices.Any(d => d.Trusted))
            throw new InvalidOperationException("remembered_pause_failed");
        if (!buttons.Single(b => b.Text == "Disable all remote access").Enabled)
            throw new InvalidOperationException("emergency_disable_unreachable");
        admin.TrustPhone(credential.DeviceId, true);
        Console.WriteLine("PASS local UI trust and pause persist across state reload; emergency disable remains reachable.");
    }
    private static void Capture(Form form, string path, Size size)
    {
        form.ClientSize = size;
        form.PerformLayout();
        Application.DoEvents();
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(path, ImageFormat.Png);
    }
}
