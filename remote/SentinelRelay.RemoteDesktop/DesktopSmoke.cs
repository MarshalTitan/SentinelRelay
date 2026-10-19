using System.Drawing.Imaging;

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
            using var form = new MainForm(directory, smoke: true);
            form.Show();
            Application.DoEvents();
            Directory.CreateDirectory(output);
            Capture(form, Path.Combine(output, "desktop-wide.png"), new Size(860, 850));
            Capture(form, Path.Combine(output, "desktop-narrow.png"), new Size(720, 640));
            using var pair = new PairingForm("https://example.test.ts.net", new string('A', 64));
            pair.Show();
            Application.DoEvents();
            Capture(pair, Path.Combine(output, "pairing.png"), new Size(650, 330));
            return 0;
        }
        finally
        {
            // This path is created above solely for CI state, never a user's actual settings.
            Directory.Delete(directory, true);
        }
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
