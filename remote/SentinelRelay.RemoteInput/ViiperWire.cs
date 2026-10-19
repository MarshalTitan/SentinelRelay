using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using SentinelRelay.Remote;

namespace SentinelRelay.RemoteInput;

/// <summary>Independent, gamepad-only implementation of VIIPER's documented TCP wire contract.</summary>
public static class ViiperWire
{
    public static byte[] Encode(ControllerFrame frame)
    {
        if (!frame.IsValid) throw new InvalidDataException("invalid_controller_frame");
        var bytes = new byte[20]; // Six reserved bytes remain zero.
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, frame.Buttons);
        bytes[4] = (byte)Math.Round(frame.LeftTrigger * 255, MidpointRounding.AwayFromZero);
        bytes[5] = (byte)Math.Round(frame.RightTrigger * 255, MidpointRounding.AwayFromZero);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(6), Axis(frame.LeftX));
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(8), Axis(frame.LeftY));
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(10), Axis(frame.RightX));
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(12), Axis(frame.RightY));
        return bytes;
    }
    private static short Axis(double value) => (short)Math.Round(value * (value < 0 ? 32768 : 32767), MidpointRounding.AwayFromZero);

    // Internal only: no phone-selected address, request text, profile, bus or device.
    internal static async Task<JsonDocument> Request(int port, string command, CancellationToken cancellation, Func<bool>? owned = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        using var tcp = new TcpClient(AddressFamily.InterNetwork);
        await tcp.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
        var stream = tcp.GetStream();
        if (owned is not null && !owned()) throw new InputBackendException("backend_endpoint_owner_changed");
        await stream.WriteAsync(Encoding.UTF8.GetBytes(command + "\0"), timeout.Token);
        using var bytes = new MemoryStream();
        var buffer = new byte[512];
        while (true)
        {
            var length = await stream.ReadAsync(buffer, timeout.Token);
            if (length == 0) break;
            if (bytes.Length + length > 4096) throw new InvalidDataException("backend_response_size");
            bytes.Write(buffer, 0, length);
        }
        var result = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
        if (result.RootElement.TryGetProperty("status", out _))
        {
            result.Dispose();
            throw new InputBackendException("backend_request_rejected");
        }
        return result;
    }
}
