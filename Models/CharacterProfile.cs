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

    // Explicit, per-character opt-in. This control is accepted only through
    // the same authenticated Discord reader used by chat replies.
    public bool RemoteScreenshotsEnabled { get; set; }

    public string DiscordRelayChannelId { get; set; } = string.Empty;

    public string AuthorizedDiscordUserId { get; set; } = string.Empty;

    // Persisted Discord snowflake. Commands at or below this ID are never replayed.
    public string LastProcessedDiscordMessageId { get; set; } = string.Empty;

    // Every outbound destination still requires an explicit parser and fixed
    // sender mapping in addition to membership in this per-character set.
    public HashSet<RelayChatType> EnabledOutboundChannels { get; set; } = [];

    public bool Paused { get; set; }

    // Privacy-first: every chat channel is off until the user selects it.
    public HashSet<RelayChatType> EnabledInboundChannels { get; set; } = [];

    // Opt-in, per-character, in-memory diagnostics control. Diagnostic
    // observations themselves are never persisted.
    public bool CaptureRewardDiagnostics { get; set; }

    public bool IncludeSenderWorld { get; set; } = true;

    public bool UseDiscordEmbeds { get; set; }

    public string DiscordMentionUserId { get; set; } = string.Empty;

    public List<KeywordRule> Keywords { get; set; } = [];

    public DateTime? LastWebhookSuccessUtc { get; set; }

    public DateTime? LastDiscordReaderSuccessUtc { get; set; }

    public DateTime? LastRemoteScreenshotSuccessUtc { get; set; }
}
