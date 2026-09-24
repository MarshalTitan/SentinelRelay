using Dalamud.Configuration;
using SentinelRelay.Models;

namespace SentinelRelay;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 4;

    public Dictionary<string, CharacterProfile> CharacterProfiles { get; set; } = new(StringComparer.Ordinal);

    public bool ShowPrivacyWarning { get; set; } = true;

    public CharacterProfile GetOrCreateProfile(string characterKey, string characterName, string homeWorld)
    {
        if (!CharacterProfiles.TryGetValue(characterKey, out var profile))
        {
            profile = new CharacterProfile
            {
                CharacterKey = characterKey,
                CharacterName = characterName,
                HomeWorld = homeWorld,
            };
            CharacterProfiles[characterKey] = profile;
        }

        profile.CharacterName = characterName;
        profile.HomeWorld = homeWorld;
        profile.EnabledInboundChannels ??= [];
        profile.EnabledOutboundChannels ??= [];
        profile.Keywords ??= [];
        return profile;
    }
}
