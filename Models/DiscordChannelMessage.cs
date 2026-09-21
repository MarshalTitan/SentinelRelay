using System.Text.Json.Serialization;

namespace SentinelRelay.Models;

public sealed class DiscordChannelMessage
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("channel_id")]
    public string ChannelId { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonPropertyName("author")]
    public DiscordMessageAuthor Author { get; set; } = new();

    [JsonPropertyName("webhook_id")]
    public string? WebhookId { get; set; }
}

public sealed class DiscordMessageAuthor
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("bot")]
    public bool Bot { get; set; }
}
