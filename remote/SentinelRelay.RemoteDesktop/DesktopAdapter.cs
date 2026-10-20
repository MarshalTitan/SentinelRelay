using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SentinelRelay.Remote;
using SentinelRelay.RemoteHost;

namespace SentinelRelay.RemoteDesktop;

public sealed class DesktopAdapter : IDesktopAdapter
{
    private readonly object gate = new();
    private readonly Queue<(string Id, long Stamp, Rectangle Bounds)> frames = new();
    private static Rectangle CurrentBounds()
    {
        using var process = Process.GetCurrentProcess();
        if (!InteractiveDesktop.Available(process.SessionId)) throw new InvalidOperationException();
        var area = Screen.PrimaryScreen?.Bounds ?? throw new InvalidOperationException();
        if (area.Width < 1 || area.Height < 1 || (long)area.Width * area.Height > 16777216) throw new InvalidOperationException();
        return area;
    }
    public DesktopImage Capture()
    {
        lock (gate)
        {
            var area = CurrentBounds(); var stamp = Stopwatch.GetTimestamp();
            using var original = new Bitmap(area.Width, area.Height, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(original))
            {
                graphics.CopyFromScreen(area.Location, Point.Empty, area.Size, CopyPixelOperation.SourceCopy | CopyPixelOperation.CaptureBlt);
                var cursor = Cursor.Position;
                if (area.Contains(cursor))
                    Cursors.Default.Draw(graphics, new Rectangle(cursor.X - area.X, cursor.Y - area.Y, 24, 24));
            }
            var ratio = Math.Min(1, Math.Min(1280.0 / area.Width, 720.0 / area.Height));
            var width = Math.Max(1, (int)(area.Width * ratio)); var height = Math.Max(1, (int)(area.Height * ratio));
            using var scaled = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(scaled))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                graphics.DrawImage(original, 0, 0, width, height);
            }
            using var encoded = new MemoryStream();
            using var options = new EncoderParameters(1);
            options.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 50L);
            scaled.Save(encoded, ImageCodecInfo.GetImageEncoders().Single(c => c.FormatID == ImageFormat.Jpeg.Guid), options);
            if (encoded.Length is < 1 or > 327680 || CurrentBounds() != area ||
                Stopwatch.GetElapsedTime(stamp) >= TimeSpan.FromSeconds(2)) throw new InvalidDataException();
            var frameId = Protocol.Secret();
            frames.Enqueue((frameId, stamp, area));
            while (frames.Count > 3) frames.Dequeue();
            return new(frameId, width, height, Convert.ToBase64String(encoded.ToArray()), DateTimeOffset.UtcNow);
        }
    }
    public bool Apply(DesktopRequest request)
    {
        lock (gate)
        {
            var frame = frames.FirstOrDefault(f => f.Id == request.FrameId);
            if (!DesktopStream.Valid(request) || frame.Id is null ||
                Stopwatch.GetElapsedTime(frame.Stamp) >= TimeSpan.FromSeconds(2) || CurrentBounds() != frame.Bounds) return false;
            var bounds = frame.Bounds;
            if (request.Kind is "tap" or "doubleTap" or "rightClick")
            {
                var x = bounds.X + (int)Math.Round(request.X!.Value * (bounds.Width - 1));
                var y = bounds.Y + (int)Math.Round(request.Y!.Value * (bounds.Height - 1));
                if (!SetCursorPos(x, y)) return false;
                var down = request.Kind == "rightClick" ? 0x8u : 0x2u;
                var up = request.Kind == "rightClick" ? 0x10u : 0x4u;
                return Submit(request.Kind == "doubleTap" ? [Mouse(down), Mouse(up), Mouse(down), Mouse(up)] : [Mouse(down), Mouse(up)]);
            }
            if (request.Kind == "text")
                return Submit(request.Text!.SelectMany(c => new[] { Key(0, c, 4), Key(0, c, 6) }).ToArray());
            if (request.Key is "ScrollUp" or "ScrollDown")
                return Submit([Mouse(0x800, request.Key == "ScrollUp" ? 120u : unchecked((uint)-120))]);
            ushort vk = request.Key switch
            {
                "Enter" => 0x0D, "Escape" => 0x1B, "Tab" => 9, "Backspace" => 8,
                "Up" => 0x26, "Down" => 0x28, "Left" => 0x25, "Right" => 0x27, "Start" => 0x5B,
                _ => 0,
            };
            return vk != 0 && Submit([Key(vk, 0, 0), Key(vk, 0, 2)]);
        }
    }
    private static Input Mouse(uint flags, uint data = 0) => new() { Type = 0, Value = new() { Mouse = new() { X = 0, Y = 0, Flags = flags, Data = data, Time = 0, Extra = 0 } } };
    private static Input Key(ushort vk, ushort scan, uint flags) => new() { Type = 1, Value = new() { Keyboard = new() { VirtualKey = vk, Scan = scan, Flags = flags, Time = 0, Extra = 0 } } };
    private static bool Submit(Input[] inputs)
    {
        var applied = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (applied == inputs.Length) return true;
        var releases = inputs.Where(i => i.Type == 1).Select(i => Key(i.Value.Keyboard.VirtualKey, i.Value.Keyboard.Scan,
            i.Value.Keyboard.Flags | 2)).Append(Mouse(0x4 | 0x10)).ToArray();
        SendInput((uint)releases.Length, releases, Marshal.SizeOf<Input>());
        return false;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Value; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput
    { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput
    { public ushort VirtualKey, Scan; public uint Flags, Time; public nuint Extra; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
}
