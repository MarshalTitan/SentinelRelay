namespace SentinelRelay.Models;

public sealed class CharacterProfile
{
    public string CharacterKey { get; set; } = string.Empty;

    public string CharacterName { get; set; } = string.Empty;

    public string HomeWorld { get; set; } = string.Empty;

    public string InstallationId { get; set; } = Guid.NewGuid().ToString("D");

    // Windows-DPAPI protected. Never contains a plaintext service token.
    public string ProtectedClientToken { get; set; } = string.Empty;

    public bool Paused { get; set; }

    // Privacy-first: every chat channel is off until the user selects it.
    public HashSet<RelayChatType> EnabledInboundChannels { get; set; } = [];

    public List<KeywordRule> Keywords { get; set; } = [];
}

