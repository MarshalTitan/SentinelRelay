using System.Runtime.InteropServices;

static class SignatureCheck
{
    public static bool Valid(string path)
    {
        var file = new FileInfoData { Size = (uint)Marshal.SizeOf<FileInfoData>(), Path = path };
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfoData>());
        Marshal.StructureToPtr(file, pointer, false);
        var data = new TrustData
        {
            Size = (uint)Marshal.SizeOf<TrustData>(), UiChoice = 2, UnionChoice = 1, File = pointer,
            StateAction = 1, ProviderFlags = 0x80
        };
        var action = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
        try { return WinVerifyTrust(0, ref action, ref data) == 0; }
        finally
        {
            data.StateAction = 2;
            WinVerifyTrust(0, ref action, ref data);
            Marshal.DestroyStructure<FileInfoData>(pointer);
            Marshal.FreeHGlobal(pointer);
        }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileInfoData
    {
        public uint Size;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Path;
        public nint File, KnownSubject;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        public uint Size;
        public nint PolicyCallback, SipClient;
        public uint UiChoice, RevocationChecks, UnionChoice;
        public nint File;
        public uint StateAction;
        public nint StateData, UrlReference;
        public uint ProviderFlags, UiContext;
        public nint SignatureSettings;
    }
    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int WinVerifyTrust(nint window, ref Guid action, ref TrustData data);
}
