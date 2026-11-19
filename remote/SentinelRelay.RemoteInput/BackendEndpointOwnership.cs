using System.Globalization;
using System.Runtime.InteropServices;

namespace SentinelRelay.RemoteInput;

internal static class BackendEndpointOwnership
{
    // Verify the locally owned child, not an existing service that happened to occupy a selected port.
    public static bool Matches(int processId, int apiPort, int usbPort)
    {
        if (processId <= 0 || apiPort is <= 0 or > 65535 || usbPort is <= 0 or > 65535 || apiPort == usbPort) return false;
        try
        {
            return OperatingSystem.IsWindows() ? Windows(processId, apiPort, usbPort) :
                OperatingSystem.IsLinux() && Linux(processId, apiPort, usbPort);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or
            System.ComponentModel.Win32Exception or FormatException or OverflowException) { return false; }
    }

    private static bool Windows(int processId, int apiPort, int usbPort)
    {
        var size = 0;
        if (GetExtendedTcpTable(0, ref size, false, 2, 3, 0) != 122 || size is < 4 or > 4 * 1024 * 1024) return false;
        var table = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(table, ref size, false, 2, 3, 0) != 0) return false;
            var count = Marshal.ReadInt32(table);
            if (count < 0 || count > (size - 4) / 24) return false;
            var ports = new List<int>();
            for (var i = 0; i < count; i++)
            {
                var row = table + 4 + i * 24;
                if (Marshal.ReadInt32(row + 20) != processId) continue;
                if (Marshal.ReadInt32(row) != 2 || Marshal.ReadByte(row + 4) != 127 || Marshal.ReadByte(row + 5) != 0 ||
                    Marshal.ReadByte(row + 6) != 0 || Marshal.ReadByte(row + 7) != 1) return false;
                ports.Add(Marshal.ReadByte(row + 8) * 256 + Marshal.ReadByte(row + 9));
            }
            return ports.Count == 2 && ports.Contains(apiPort) && ports.Contains(usbPort);
        }
        finally { Marshal.FreeHGlobal(table); }
    }

    private static bool Linux(int processId, int apiPort, int usbPort)
    {
        var sockets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles("/proc/" + processId + "/fd"))
        {
            var target = new FileInfo(file).LinkTarget;
            if (target is not null && target.StartsWith("socket:[", StringComparison.Ordinal) && target.EndsWith(']'))
                sockets.Add(target[8..^1]);
        }
        var ports = new List<int>();
        foreach (var line in File.ReadLines("/proc/net/tcp").Skip(1))
        {
            var columns = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (columns.Length < 10 || columns[3] != "0A" || !sockets.Contains(columns[9])) continue;
            var local = columns[1].Split(':');
            if (local.Length != 2 || !local[0].Equals("0100007F", StringComparison.OrdinalIgnoreCase)) return false;
            ports.Add(int.Parse(local[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }
        return ports.Count == 2 && ports.Contains(apiPort) && ports.Contains(usbPort);
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(nint table, ref int size, [MarshalAs(UnmanagedType.Bool)] bool order,
        int family, int tableClass, uint reserved);
}
