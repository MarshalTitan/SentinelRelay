using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace SentinelRelay.Services;

public sealed class CredentialProtector
{
    private const uint CryptProtectUiForbidden = 0x1;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SentinelRelay/client-token/v1");

    public string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            return string.Empty;

        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Sentinel Relay client credentials require Windows DPAPI.");

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        using var input = DataBlob.FromBytes(plaintextBytes);
        using var entropy = DataBlob.FromBytes(Entropy);
        if (!CryptProtectData(ref input.Value, null, ref entropy.Value, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out var output))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        try
        {
            var protectedBytes = new byte[output.Size];
            Marshal.Copy(output.Data, protectedBytes, 0, protectedBytes.Length);
            return Convert.ToBase64String(protectedBytes);
        }
        finally
        {
            if (output.Data != IntPtr.Zero)
                LocalFree(output.Data);
        }
    }

    public string? Unprotect(string protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
            return null;

        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            var protectedBytes = Convert.FromBase64String(protectedValue);
            using var input = DataBlob.FromBytes(protectedBytes);
            using var entropy = DataBlob.FromBytes(Entropy);
            if (!CryptUnprotectData(ref input.Value, IntPtr.Zero, ref entropy.Value, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out var output))
                return null;

            try
            {
                var plaintext = new byte[output.Size];
                Marshal.Copy(output.Data, plaintext, 0, plaintext.Length);
                return Encoding.UTF8.GetString(plaintext);
            }
            finally
            {
                if (output.Data != IntPtr.Zero)
                    LocalFree(output.Data);
            }
        }
        catch (FormatException)
        {
            return null;
        }
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref NativeDataBlob dataIn,
        string? description,
        ref NativeDataBlob optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        uint flags,
        out NativeDataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref NativeDataBlob dataIn,
        IntPtr description,
        ref NativeDataBlob optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        uint flags,
        out NativeDataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeDataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    private sealed class DataBlob : IDisposable
    {
        private DataBlob(byte[] bytes)
        {
            Value = new NativeDataBlob { Size = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
            Marshal.Copy(bytes, 0, Value.Data, bytes.Length);
        }

        public NativeDataBlob Value;

        public static DataBlob FromBytes(byte[] bytes) => new(bytes);

        public void Dispose()
        {
            if (Value.Data == IntPtr.Zero)
                return;
            Marshal.FreeHGlobal(Value.Data);
            Value.Data = IntPtr.Zero;
            Value.Size = 0;
        }
    }
}
