namespace SentinelRelay.Models;

public sealed class CharacterProfile
{
    public string CharacterKey { get; set; } = string.Empty;

    public string CharacterName { get; set; } = string.Empty;

    public string HomeWorld { get; set; } = string.Empty;

    // Windows-DPAPI protected. Never contains a plaintext Discord webhook URL.
    public string ProtectedWebhookUrl { get; set; } = string.Empty;

    public bool Paused { get; set; }

    // Privacy-first: every chat channel is off until the user selects it.
    public HashSet<RelayChatType> EnabledInboundChannels { get; set; } = [];

    public bool IncludeSenderWorld { get; set; } = true;

    public bool UseDiscordEmbeds { get; set; }

    public string DiscordMentionUserId { get; set; } = string.Empty;

    public List<KeywordRule> Keywords { get; set; } = [];

    public DateTime? LastWebhookSuccessUtc { get; set; }
}
