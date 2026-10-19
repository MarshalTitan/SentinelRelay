using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;
using SentinelRelay.Remote;

namespace SentinelRelay.RemoteDesktop;

internal static class WindowsLaunch
{
    private const int TokenElevationType = 18;
    private const int TokenLinkedToken = 19;
    private const string RetryFlag = "--standard-user-retry";

    public static bool RedirectIfNeeded(string[] args)
    {
        using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
        var service = identity.IsSystem || identity.User is null ||
            identity.User.IsWellKnown(WellKnownSidType.LocalServiceSid) ||
            identity.User.IsWellKnown(WellKnownSidType.NetworkServiceSid);
        if (!GetTokenInformation(identity.AccessToken, TokenElevationType, out int type, sizeof(int), out _))
            throw new InvalidOperationException("launch_token_unavailable");
        if (!DesktopLaunchPolicy.RequiresStandardToken(type, service, args.Contains(RetryFlag))) return false;

        // Drop privileges only through the same user's own linked standard token.
        // Default/no-linked-token Windows sessions keep their normal asInvoker context.
        if (!GetLinkedToken(identity.AccessToken, TokenLinkedToken, out nint linkedHandle, nint.Size, out _) || linkedHandle == 0)
            throw new InvalidOperationException("standard_token_unavailable");
        using var linked = new SafeAccessTokenHandle(linkedHandle);
        using var linkedIdentity = new WindowsIdentity(linked.DangerousGetHandle());
        if (linkedIdentity.User?.Value != identity.User?.Value ||
            !GetTokenInformation(linked, TokenElevationType, out int linkedType, sizeof(int), out _) || linkedType != 3)
            throw new InvalidOperationException("standard_token_identity_mismatch");
        const uint queryDuplicateAssign = 0x0008 | 0x0002 | 0x0001;
        if (!DuplicateTokenEx(linked, queryDuplicateAssign, 0, 2, 1, out var primary))
            throw new InvalidOperationException("standard_token_unavailable");
        using (primary)
        {
            var executable = Application.ExecutablePath;
            var arguments = new List<string> { executable, RetryFlag };
            if (args.Contains("--minimized")) arguments.Add("--minimized");
            // CI exercises this exact launch path rather than bypassing the token check.
            var smoke = Array.IndexOf(args, "--smoke-test");
            if (smoke >= 0 && smoke + 1 < args.Length) arguments.AddRange(["--smoke-test", args[smoke + 1]]);
            var command = new StringBuilder(string.Join(" ", arguments.Select(DesktopLaunchPolicy.QuoteArgument)));
            var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>() };
            if (!CreateProcessWithTokenW(primary, 1, executable, command, 0, 0, AppContext.BaseDirectory, ref startup, out var process))
                throw new InvalidOperationException("standard_user_launch_failed");
            try { return true; }
            finally { CloseHandle(process.Process); CloseHandle(process.Thread); }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct StartupInfo
    {
        public int Size;
        public string? Reserved, Desktop, Title;
        public uint X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public ushort ShowWindow, ReservedBytes;
        public nint ReservedPointer, StandardInput, StandardOutput, StandardError;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessInfo
    {
        public nint Process, Thread;
        public uint ProcessId, ThreadId;
    }
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int informationClass, out int information, int length, out int returned);
    [DllImport("advapi32.dll", EntryPoint = "GetTokenInformation", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLinkedToken(SafeAccessTokenHandle token, int informationClass, out nint information, int length, out int returned);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateTokenEx(SafeAccessTokenHandle existing, uint access, nint attributes, int impersonation, int tokenType, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessWithTokenW(SafeAccessTokenHandle token, uint flags, string application, StringBuilder commandLine,
        uint creationFlags, nint environment, string directory, ref StartupInfo startup, out ProcessInfo process);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
