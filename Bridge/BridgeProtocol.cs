using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SentinelRelay.Bridge;

public sealed record BridgeRequest(int Version, string Operation, string? CharacterKey = null, string? GameEpoch = null);
public sealed record BridgeSnapshot(string CharacterKey, string GameEpoch, string Character, string? World,
    uint Territory, string? Map, DateTimeOffset HeartbeatUtc, JsonElement? SRank, bool ControlAllowed, bool ScreenshotAllowed);
public sealed record BridgeReply(bool Success, string Code, BridgeSnapshot? Snapshot = null, string? PngBase64 = null,
    [property: JsonIgnore] nint CaptureWindow = default);

public static class BridgeWire
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 12,
      RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true };
    public static string PipeName(int pid) => "SentinelRelay.Remote.v1." + pid;
    public static bool Allowed(string op) => op is "hello" or "srank.status" or "srank.skip" or "srank.reset.confirm" or "screenshot";
    public static string? Reject(BridgeRequest request, BridgeSnapshot? snapshot)
    {
        if (request.Version != 1 || !Allowed(request.Operation)) return "protocol_mismatch";
        if (snapshot is null) return "plugin_disabled";
        if (request.Operation == "hello") return null;
        if (request.CharacterKey != snapshot.CharacterKey || request.GameEpoch != snapshot.GameEpoch) return "character_changed";
        if (request.Operation is "srank.skip" or "srank.reset.confirm" && !snapshot.ControlAllowed) return "control_not_authorized";
        if (request.Operation == "screenshot" && !snapshot.ScreenshotAllowed) return "screenshot_disabled";
        return null;
    }
    public static async Task Write<T>(Stream stream, T value, int maximum, CancellationToken token)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        if (body.Length > maximum) throw new InvalidDataException("frame_too_large");
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
        await stream.WriteAsync(header, token); await stream.WriteAsync(body, token); await stream.FlushAsync(token);
    }
    public static async Task<T> Read<T>(Stream stream, int maximum, CancellationToken token)
    {
        var header = new byte[4]; await stream.ReadExactlyAsync(header, token);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 2 || length > maximum) throw new InvalidDataException("invalid_frame");
        var bytes = new byte[length]; await stream.ReadExactlyAsync(bytes, token);
        return JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new InvalidDataException("invalid_frame");
    }
}
