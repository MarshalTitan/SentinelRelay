using SentinelRelay.Models;

namespace SentinelRelay.Core;

public static class RelayFilter
{
    public static bool ShouldForward(CharacterProfile? profile, RelayChatType channel) =>
        profile is { Paused: false }
        && profile.EnabledInboundChannels.Contains(channel)
        && !string.IsNullOrWhiteSpace(profile.ProtectedWebhookUrl);
}
