using System.Text.Json.Serialization;
using SentinelRelay.Models;

namespace SentinelRelay.Protocol;

public static class RelayProtocol
{
    public const int Version = 1;
}

public sealed record ClientEnvelope(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("requestId")] string? RequestId,
    [property: JsonPropertyName("payload")] object Payload);

public sealed record HelloPayload(
    string InstallationId,
    string? ClientToken,
    int ProtocolVersion,
    string PluginVersion,
    string CharacterName,
    string HomeWorld,
    string CharacterKey,
    bool Paused);

public sealed record InboundChatPayload(
    string EventId,
    string ChatType,
    string ChannelLabel,
    string Sender,
    string? SenderWorld,
    string Message,
    DateTime TimestampUtc,
    IReadOnlyList<KeywordMatchPayload> KeywordMatches);

public sealed record KeywordMatchPayload(string Keyword, string AlertMethod);

public sealed record OutboundAckPayload(string EventId, bool Delivered, string? Error);

public sealed record PairRequestPayload;

public sealed record PausePayload(bool Paused);

public sealed record UnlinkPayload;

public sealed record ServerEnvelope(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("requestId")] string? RequestId,
    [property: JsonPropertyName("payload")] System.Text.Json.JsonElement Payload);

public sealed record HelloAckPayload(
    bool Linked,
    string? DiscordUsername,
    string? RelayChannelName,
    string BackendVersion,
    bool ServerPaused);

public sealed record PairCodePayload(string Code, DateTime ExpiresAtUtc);

public sealed record PairCompletedPayload(
    string ClientToken,
    string DiscordUsername,
    string? RelayChannelName);

public sealed record OutboundChatPayload(
    string EventId,
    string ChatType,
    string Message,
    DateTime IssuedAtUtc,
    DateTime ExpiresAtUtc);

public sealed record ErrorPayload(string Code, string Message);

