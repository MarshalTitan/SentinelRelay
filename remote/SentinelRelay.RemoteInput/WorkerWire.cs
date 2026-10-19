using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;
using SentinelRelay.Remote;

namespace SentinelRelay.RemoteInput;

// Local anonymous pipe only. This is not a phone protocol and carries no arbitrary operation.
public sealed record WorkerPacket(int Version, long Sequence, long IssuedAt,
    double LeftX, double LeftY, double RightX, double RightY, double LeftTrigger, double RightTrigger, ushort Buttons)
{
    public ControllerFrame ToFrame() => new(LeftX, LeftY, RightX, RightY, LeftTrigger, RightTrigger, Buttons);
    public static WorkerPacket Create(long sequence, ControllerFrame frame) => new(1, sequence, Stopwatch.GetTimestamp(),
        frame.LeftX, frame.LeftY, frame.RightX, frame.RightY, frame.LeftTrigger, frame.RightTrigger, frame.Buttons);
}

public static class WorkerWire
{
    public const int MaxBytes = 512;
    public static readonly TimeSpan MaxPacketAge = TimeSpan.FromMilliseconds(250);

    public static bool Accept(WorkerPacket packet, long previousSequence, long now) =>
        packet.Version == 1 && packet.Sequence > 0 && packet.Sequence > previousSequence &&
        packet.IssuedAt > 0 && packet.IssuedAt <= now &&
        Stopwatch.GetElapsedTime(packet.IssuedAt, now) < MaxPacketAge && packet.ToFrame().IsValid;

    public static async Task Write(Stream stream, WorkerPacket packet, CancellationToken cancellation = default)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(packet, Protocol.Json);
        if (bytes.Length > MaxBytes) throw new InvalidDataException("worker_packet_too_large");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, cancellation);
        await stream.WriteAsync(bytes, cancellation);
        await stream.FlushAsync(cancellation);
    }

    public static async Task<WorkerPacket> Read(Stream stream, CancellationToken cancellation)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellation);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > MaxBytes) throw new InvalidDataException("worker_packet_size");
        var bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, cancellation);
        return JsonSerializer.Deserialize<WorkerPacket>(bytes, Protocol.Json) ?? throw new InvalidDataException("worker_packet_missing");
    }
}
