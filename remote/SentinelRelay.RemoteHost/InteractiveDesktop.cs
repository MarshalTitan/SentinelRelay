using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SentinelRelay.RemoteHost;

// Window focus/minimization is irrelevant. Lock, secure desktop and a disconnected
// Windows session still end input; a dark monitor alone does not change these checks.
public static class InteractiveDesktop
{
    public static bool Matches(int hostSession, int gameSession, bool sessionActive, string? threadDesktop, string? inputDesktop) =>
        hostSession > 0 && hostSession == gameSession && sessionActive &&
        string.Equals(threadDesktop, "Default", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(inputDesktop, threadDesktop, StringComparison.OrdinalIgnoreCase);

    public static bool Available(int gameSession)
    {
        if (!OperatingSystem.IsWindows()) return false;
        nint input = 0, state = 0;
        try
        {
            using var host = Process.GetCurrentProcess();
            var session = host.SessionId;
            if (session != gameSession) return false;
            if (!WTSQuerySessionInformationW(0, session, 8 /* WTSConnectState */, out state, out var bytes) ||
                state == 0 || bytes < sizeof(int)) return false;
            var active = Marshal.ReadInt32(state) == 0; // WTSActive
            input = OpenInputDesktop(0, false, 1 /* DESKTOP_READOBJECTS */);
            return Matches(session, gameSession, active,
                Name(GetThreadDesktop(GetCurrentThreadId())), Name(input));
        }
        catch { return false; }
        finally
        {
            if (input != 0) CloseDesktop(input);
            if (state != 0) WTSFreeMemory(state);
        }
    }
    private static string? Name(nint desktop)
    {
        if (desktop == 0) return null;
        var name = new StringBuilder(256);
        return GetUserObjectInformationW(desktop, 2 /* UOI_NAME */, name, name.Capacity * sizeof(char), out _)
            ? name.ToString() : null;
    }
    [DllImport("user32.dll")] private static extern nint OpenInputDesktop(uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint access);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseDesktop(nint desktop);
    [DllImport("user32.dll")] private static extern nint GetThreadDesktop(uint thread);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserObjectInformationW(nint handle, int index, StringBuilder information, int length, out int needed);
    [DllImport("wtsapi32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQuerySessionInformationW(nint server, int session, int informationClass, out nint buffer, out int bytes);
    [DllImport("wtsapi32.dll")] private static extern void WTSFreeMemory(nint buffer);
}
