using System.Text.Json.Serialization;

namespace SentinelRelay.Models;

public sealed record DiscordWebhookPayload(
    [property: JsonPropertyName("content")] string? Content,
    [property: JsonPropertyName("embeds")] IReadOnlyList<DiscordEmbedPayload>? Embeds,
    [property: JsonPropertyName("allowed_mentions")] DiscordAllowedMentions AllowedMentions);

public sealed record DiscordAllowedMentions(
    [property: JsonPropertyName("parse")] IReadOnlyList<string> Parse,
    [property: JsonPropertyName("users")] IReadOnlyList<string>? Users = null);

public sealed record DiscordEmbedPayload(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("color")] int Color);
