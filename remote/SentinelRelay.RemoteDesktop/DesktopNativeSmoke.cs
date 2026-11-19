using System.Diagnostics;
using System.Runtime.InteropServices;
using SentinelRelay.RemoteHost;

namespace SentinelRelay.RemoteDesktop;

internal static class DesktopNativeSmoke
{
    // CI only. Captured desktop pixels stay in memory; only this fixed result is written.
    public static void Run(string output)
    {
        using var host = Process.GetCurrentProcess();
        if (!InteractiveDesktop.Available(host.SessionId))
        {
            Result(output, "SKIP native desktop: no active ordinary input desktop in this runner.");
            return;
        }
        using var window = new Form
        {
            Text = "Sentinel isolated desktop check", StartPosition = FormStartPosition.Manual,
            Location = new Point(80, 80), ClientSize = new Size(400, 180), TopMost = true,
        };
        using var marker = new Panel { Location = new Point(12, 12), Size = new Size(64, 64), BackColor = Color.FromArgb(235, 30, 80) };
        using var field = new TextBox { Location = new Point(90, 30), Width = 260 };
        window.Controls.Add(marker); window.Controls.Add(field);
        window.Show(); window.Activate(); Application.DoEvents();
        GetWindowThreadProcessId(GetForegroundWindow(), out var foreground);
        if (foreground != host.Id)
        {
            Result(output, "SKIP native desktop input: runner did not activate the isolated check window.");
            return;
        }
        var adapter = new DesktopAdapter();
        var area = Screen.PrimaryScreen!.Bounds;
        var frame = adapter.Capture();
        using (var bytes = new MemoryStream(Convert.FromBase64String(frame.JpegBase64)))
        using (var image = new Bitmap(bytes))
        {
            var point = marker.PointToScreen(new Point(32, 32));
            var sample = image.GetPixel(Math.Clamp((point.X - area.X) * image.Width / area.Width, 0, image.Width - 1),
                Math.Clamp((point.Y - area.Y) * image.Height / area.Height, 0, image.Height - 1));
            if (sample.R < 150 || sample.G > 110 || sample.B > 160) throw new InvalidOperationException("native_desktop_capture_marker_failed");
        }
        var target = field.PointToScreen(new Point(field.Width / 2, field.Height / 2));
        if (!adapter.Apply(new(1, 1, "tap", frame.FrameId,
            (double)(target.X - area.X) / (area.Width - 1), (double)(target.Y - area.Y) / (area.Height - 1))))
            throw new InvalidOperationException("native_desktop_click_failed");
        Application.DoEvents();
        frame = adapter.Capture();
        if (!field.Focused || !adapter.Apply(new(1, 2, "text", frame.FrameId, Text: "Sentinel probe")))
            throw new InvalidOperationException("native_desktop_typing_failed");
        Pump(() => field.Text == "Sentinel probe");
        frame = adapter.Capture();
        if (!adapter.Apply(new(1, 3, "key", frame.FrameId, Key: "Backspace"))) throw new InvalidOperationException("native_desktop_key_failed");
        Pump(() => field.Text == "Sentinel prob");
        Result(output, "PASS native primary desktop JPEG marker, normalized click, Unicode typing and Backspace in isolated Windows form.");
    }
    private static void Pump(Func<bool> done)
    {
        var limit = Stopwatch.StartNew();
        while (!done() && limit.Elapsed < TimeSpan.FromSeconds(2)) { Application.DoEvents(); Thread.Sleep(10); }
        if (!done()) throw new InvalidOperationException("native_desktop_input_readback_failed");
    }
    private static void Result(string output, string result)
    { Console.WriteLine("SENTINEL_DESKTOP_NATIVE: " + result); File.WriteAllText(Path.Combine(output, "desktop-native-result.txt"), result); }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
}
