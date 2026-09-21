namespace SentinelRelay.Models;

public sealed class CharacterProfile
{
    public string CharacterKey { get; set; } = string.Empty;

    public string CharacterName { get; set; } = string.Empty;

    public string HomeWorld { get; set; } = string.Empty;

    // Windows-DPAPI protected. Never contains a plaintext Discord webhook URL.
    public string ProtectedWebhookUrl { get; set; } = string.Empty;

    // Windows-DPAPI protected with separate entropy from the webhook secret.
    // This is an experimental, opt-in credential used only for Discord REST reads.
    public string ProtectedDiscordBotToken { get; set; } = string.Empty;

    public bool DiscordRepliesEnabled { get; set; }

    public string DiscordRelayChannelId { get; set; } = string.Empty;

    public string AuthorizedDiscordUserId { get; set; } = string.Empty;

    // Persisted Discord snowflake. Commands at or below this ID are never replayed.
    public string LastProcessedDiscordMessageId { get; set; } = string.Empty;

    // V1 intentionally contains only Free Company. Future destinations still
    // require an explicit parser and sender implementation in addition to this set.
    public HashSet<RelayChatType> EnabledOutboundChannels { get; set; } = [];

    public bool Paused { get; set; }

    // Privacy-first: every chat channel is off until the user selects it.
    public HashSet<RelayChatType> EnabledInboundChannels { get; set; } = [];

    public bool IncludeSenderWorld { get; set; } = true;

    public bool UseDiscordEmbeds { get; set; }

    public string DiscordMentionUserId { get; set; } = string.Empty;

    public List<KeywordRule> Keywords { get; set; } = [];

    public DateTime? LastWebhookSuccessUtc { get; set; }

    public DateTime? LastDiscordReaderSuccessUtc { get; set; }
}
