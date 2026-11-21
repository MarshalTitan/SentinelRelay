using SentinelRelay.Models;

namespace SentinelRelay.Core;

public static class RelayFilter
{
    public static bool CanRelay(CharacterProfile? profile) =>
        profile is { Paused: false }
        && !string.IsNullOrWhiteSpace(profile.ProtectedWebhookUrl);

    public static bool IsNormallyEnabled(CharacterProfile? profile, RelayChatType channel) =>
        profile?.EnabledInboundChannels.Contains(channel) == true;

    public static bool ShouldForward(
        CharacterProfile? profile,
        RelayChatType channel,
        IReadOnlyList<KeywordRule>? keywordMatches = null) =>
        CanRelay(profile)
        && (IsNormallyEnabled(profile, channel) || keywordMatches is { Count: > 0 });
}
