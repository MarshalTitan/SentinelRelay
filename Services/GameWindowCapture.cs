using System.Diagnostics;
using System.Runtime.InteropServices;
using SentinelRelay.Core;

namespace SentinelRelay.Services;

public sealed record GameWindowCaptureResult(
    bool Success,
    byte[]? PngBytes,
    int Width,
    int Height,
    string? Error);

public static class GameWindowCapture
{
    private const int MaxOutputWidth = 1280;
    private const int MaxOutputHeight = 720;
    private const uint DibRgbColors = 0;
    private const uint BiRgb = 0;
    private const uint PwClientOnly = 0x00000001;
    private const uint PwRenderFullContent = 0x00000002;
    private const uint SrcCopy = 0x00cc0020;
    private const uint Blackness = 0x00000042;
    private const int Halftone = 4;

    public static nint FindCurrentProcessGameWindow()
    {
        using var process = Process.GetCurrentProcess();
        var candidate = process.MainWindowHandle;
        if (IsOwnedVisibleWindow(candidate, (uint)process.Id))
            return candidate;

        nint best = 0;
        long bestArea = 0;
        EnumWindows((window, _) =>
        {
            if (!IsOwnedVisibleWindow(window, (uint)process.Id) || !GetClientRect(window, out var rect))
                return true;
            var area = (long)Math.Max(0, rect.Right - rect.Left) * Math.Max(0, rect.Bottom - rect.Top);
            if (area <= bestArea)
                return true;
            best = window;
            bestArea = area;
            return true;
        }, 0);
        return best;
    }

    public static GameWindowCaptureResult CapturePng(nint window)
    {
        if (!IsOwnedVisibleWindow(window, (uint)Environment.ProcessId))
            return Failure("The FFXIV game window could not be identified safely.");
        if (IsIconic(window))
            return Failure("FFXIV is minimized. Restore the game window and request another screenshot.");
        if (!GetClientRect(window, out var clientRect))
            return Failure("Windows could not read the FFXIV client area.");

        var sourceWidth = clientRect.Right - clientRect.Left;
        var sourceHeight = clientRect.Bottom - clientRect.Top;
        if (sourceWidth <= 0 || sourceHeight <= 0 || sourceWidth > 16384 || sourceHeight > 16384)
            return Failure("The FFXIV client area has invalid dimensions.");

        var (outputWidth, outputHeight) = FitWithin(sourceWidth, sourceHeight, MaxOutputWidth, MaxOutputHeight);
        var windowDc = GetDC(window);
        if (windowDc == 0)
            return Failure("Windows could not access the FFXIV window surface.");

        nint sourceDc = 0;
        nint sourceBitmap = 0;
        nint sourceOriginal = 0;
        nint outputDc = 0;
        nint outputBitmap = 0;
        nint outputOriginal = 0;
        try
        {
            sourceDc = CreateCompatibleDC(windowDc);
            if (sourceDc == 0)
                return Failure("Windows could not create the screenshot surface.");
            sourceBitmap = CreateTopDownDib(windowDc, sourceWidth, sourceHeight, out var sourceBits);
            if (sourceBitmap == 0 || sourceBits == 0)
                return Failure("Windows could not allocate the screenshot buffer.");
            sourceOriginal = SelectObject(sourceDc, sourceBitmap);
            if (sourceOriginal == 0 || sourceOriginal == -1)
                return Failure("Windows could not select the screenshot buffer.");
            PatBlt(sourceDc, 0, 0, sourceWidth, sourceHeight, Blackness);

            // PrintWindow targets this process's verified FFXIV HWND and draws
            // into an in-memory DIB. It never reads the desktop framebuffer.
            if (!PrintWindow(window, sourceDc, PwClientOnly | PwRenderFullContent))
                return Failure("Windows could not capture the FFXIV render output.");

            byte[] pixels;
            if (outputWidth == sourceWidth && outputHeight == sourceHeight)
            {
                pixels = CopyPixels(sourceBits, outputWidth, outputHeight);
            }
            else
            {
                outputDc = CreateCompatibleDC(windowDc);
                if (outputDc == 0)
                    return Failure("Windows could not create the resized screenshot surface.");
                outputBitmap = CreateTopDownDib(windowDc, outputWidth, outputHeight, out var outputBits);
                if (outputBitmap == 0 || outputBits == 0)
                    return Failure("Windows could not allocate the resized screenshot buffer.");
                outputOriginal = SelectObject(outputDc, outputBitmap);
                if (outputOriginal == 0 || outputOriginal == -1)
                    return Failure("Windows could not select the resized screenshot buffer.");
                SetStretchBltMode(outputDc, Halftone);
                SetBrushOrgEx(outputDc, 0, 0, out _);
                if (!StretchBlt(
                        outputDc,
                        0,
                        0,
                        outputWidth,
                        outputHeight,
                        sourceDc,
                        0,
                        0,
                        sourceWidth,
                        sourceHeight,
                        SrcCopy))
                    return Failure("Windows could not resize the captured FFXIV frame.");
                pixels = CopyPixels(outputBits, outputWidth, outputHeight);
            }

            if (LooksBlank(pixels))
                return Failure("The captured FFXIV frame was blank. Try windowed or borderless-windowed mode with the game restored.");

            var png = PngEncoder.EncodeBgra(pixels, outputWidth, outputHeight);
            if (png.Length > 7_500_000)
                return Failure("The in-memory screenshot exceeded the safe Discord upload size.");
            return new GameWindowCaptureResult(true, png, outputWidth, outputHeight, null);
        }
        catch (Exception ex) when (ex is ExternalException or OverflowException or ArgumentException)
        {
            return Failure("Windows could not encode the captured FFXIV frame.");
        }
        finally
        {
            if (outputOriginal != 0 && outputOriginal != -1 && outputDc != 0)
                SelectObject(outputDc, outputOriginal);
            if (outputBitmap != 0)
                DeleteObject(outputBitmap);
            if (outputDc != 0)
                DeleteDC(outputDc);
            if (sourceOriginal != 0 && sourceOriginal != -1 && sourceDc != 0)
                SelectObject(sourceDc, sourceOriginal);
            if (sourceBitmap != 0)
                DeleteObject(sourceBitmap);
            if (sourceDc != 0)
                DeleteDC(sourceDc);
            ReleaseDC(window, windowDc);
        }
    }

    private static bool IsOwnedVisibleWindow(nint window, uint expectedProcessId)
    {
        if (window == 0 || !IsWindow(window) || !IsWindowVisible(window))
            return false;
        GetWindowThreadProcessId(window, out var processId);
        return processId == expectedProcessId;
    }

    private static nint CreateTopDownDib(nint dc, int width, int height, out nint bits)
    {
        var info = new BitmapInfo
        {
            Header = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32,
                Compression = BiRgb,
            },
        };
        return CreateDIBSection(dc, ref info, DibRgbColors, out bits, 0, 0);
    }

    private static byte[] CopyPixels(nint bits, int width, int height)
    {
        var pixels = new byte[checked(width * height * 4)];
        Marshal.Copy(bits, pixels, 0, pixels.Length);
        return pixels;
    }

    private static bool LooksBlank(ReadOnlySpan<byte> pixels)
    {
        var maximum = 0;
        var stride = Math.Max(4, pixels.Length / 4096 / 4 * 4);
        for (var offset = 0; offset + 2 < pixels.Length; offset += stride)
        {
            maximum = Math.Max(maximum, Math.Max(pixels[offset], Math.Max(pixels[offset + 1], pixels[offset + 2])));
            if (maximum > 4)
                return false;
        }
        return true;
    }

    private static (int Width, int Height) FitWithin(int width, int height, int maxWidth, int maxHeight)
    {
        var scale = Math.Min(1d, Math.Min((double)maxWidth / width, (double)maxHeight / height));
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    private static GameWindowCaptureResult Failure(string error) => new(false, null, 0, 0, error);

    private delegate bool EnumWindowsProc(nint window, nint parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint window, out Rect rect);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint window, nint dc);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(nint window, nint destination, uint flags);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint dc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateDIBSection(
        nint dc,
        ref BitmapInfo bitmapInfo,
        uint usage,
        out nint bits,
        nint section,
        uint offset);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint dc, nint value);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint value);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(nint dc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PatBlt(nint dc, int x, int y, int width, int height, uint operation);

    [DllImport("gdi32.dll")]
    private static extern int SetStretchBltMode(nint dc, int mode);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetBrushOrgEx(nint dc, int x, int y, out Point previous);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StretchBlt(
        nint destination,
        int destinationX,
        int destinationY,
        int destinationWidth,
        int destinationHeight,
        nint source,
        int sourceX,
        int sourceY,
        int sourceWidth,
        int sourceHeight,
        uint operation);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }
}
