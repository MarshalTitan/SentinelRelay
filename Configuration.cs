using Dalamud.Configuration;
using SentinelRelay.Models;

namespace SentinelRelay;

public sealed class Configuration : IPluginConfiguration
{
    public const int CurrentVersion = 5;
    public const int ClassicTheme = 0;
    public const int SentinelModernTheme = 1;

    public int Version { get; set; } = CurrentVersion;

    // Global presentation preference. Existing configurations are explicitly
    // migrated to Classic; users opt into Sentinel Modern from /srelay.
    public int ConfigurationTheme { get; set; } = ClassicTheme;

    public Dictionary<string, CharacterProfile> CharacterProfiles { get; set; } = new(StringComparer.Ordinal);

    public bool ShowPrivacyWarning { get; set; } = true;

    public bool NormalizeAndMigrate()
    {
        var changed = false;

        if (Version < CurrentVersion)
        {
            ConfigurationTheme = ClassicTheme;
            Version = CurrentVersion;
            changed = true;
        }

        if (ConfigurationTheme is not ClassicTheme and not SentinelModernTheme)
        {
            ConfigurationTheme = ClassicTheme;
            changed = true;
        }

        CharacterProfiles ??= new Dictionary<string, CharacterProfile>(StringComparer.Ordinal);
        foreach (var profile in CharacterProfiles.Values)
        {
            profile.EnabledInboundChannels ??= [];
            profile.EnabledOutboundChannels ??= [];
            profile.Keywords ??= [];
        }

        return changed;
    }

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
