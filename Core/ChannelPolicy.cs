using SentinelRelay.Models;
using System.Text;

namespace SentinelRelay.Core;

public static class ChannelPolicy
{
    public const int DiscordContentLimit = 2000;
    public const int DiscordEmbedDescriptionLimit = 4096;
    public const int SafeDiscordContentLimit = 1900;
    public const int SafeDiscordEmbedDescriptionLimit = 3900;
    public const int MaxOutboundMessageCharacters = 180;
    public const int MaxOutboundMessageUtf8Bytes = 400;

    public static readonly IReadOnlyList<RelayChatType> InboundChannels = Enum.GetValues<RelayChatType>();

    // Every outbound destination must also have an explicit Discord parser entry
    // and a fixed FFXIV command prefix. This is a chat allowlist, never a raw
    // command passthrough.
    public static readonly IReadOnlySet<RelayChatType> ImplementedOutboundChannels =
        new HashSet<RelayChatType>
        {
            RelayChatType.Say,
            RelayChatType.Yell,
            RelayChatType.Shout,
            RelayChatType.IncomingTell,
            RelayChatType.Party,
            RelayChatType.Alliance,
            RelayChatType.FreeCompany,
            RelayChatType.PvPTeam,
            RelayChatType.Linkshell1,
            RelayChatType.Linkshell2,
            RelayChatType.Linkshell3,
            RelayChatType.Linkshell4,
            RelayChatType.Linkshell5,
            RelayChatType.Linkshell6,
            RelayChatType.Linkshell7,
            RelayChatType.Linkshell8,
            RelayChatType.CrossWorldLinkshell1,
            RelayChatType.CrossWorldLinkshell2,
            RelayChatType.CrossWorldLinkshell3,
            RelayChatType.CrossWorldLinkshell4,
            RelayChatType.CrossWorldLinkshell5,
            RelayChatType.CrossWorldLinkshell6,
            RelayChatType.CrossWorldLinkshell7,
            RelayChatType.CrossWorldLinkshell8,
            RelayChatType.NoviceNetwork,
        };

    public static string GetCommandPrefix(RelayChatType channel) => channel switch
    {
        RelayChatType.Say => "/say",
        RelayChatType.Yell => "/yell",
        RelayChatType.Shout => "/shout",
        RelayChatType.IncomingTell => "/reply",
        RelayChatType.Party => "/party",
        RelayChatType.Alliance => "/alliance",
        RelayChatType.FreeCompany => "/freecompany",
        RelayChatType.PvPTeam => "/pvpteam",
        RelayChatType.Linkshell1 => "/linkshell1",
        RelayChatType.Linkshell2 => "/linkshell2",
        RelayChatType.Linkshell3 => "/linkshell3",
        RelayChatType.Linkshell4 => "/linkshell4",
        RelayChatType.Linkshell5 => "/linkshell5",
        RelayChatType.Linkshell6 => "/linkshell6",
        RelayChatType.Linkshell7 => "/linkshell7",
        RelayChatType.Linkshell8 => "/linkshell8",
        RelayChatType.CrossWorldLinkshell1 => "/cwlinkshell1",
        RelayChatType.CrossWorldLinkshell2 => "/cwlinkshell2",
        RelayChatType.CrossWorldLinkshell3 => "/cwlinkshell3",
        RelayChatType.CrossWorldLinkshell4 => "/cwlinkshell4",
        RelayChatType.CrossWorldLinkshell5 => "/cwlinkshell5",
        RelayChatType.CrossWorldLinkshell6 => "/cwlinkshell6",
        RelayChatType.CrossWorldLinkshell7 => "/cwlinkshell7",
        RelayChatType.CrossWorldLinkshell8 => "/cwlinkshell8",
        RelayChatType.NoviceNetwork => "/novice",
        _ => throw new InvalidOperationException($"{channel} is not an implemented outbound chat destination."),
    };

    public static bool IsValidOutboundMessage(string value, out string error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "The message is empty.";
            return false;
        }

        if (value.EnumerateRunes().Count() > MaxOutboundMessageCharacters)
        {
            error = $"The message exceeds {MaxOutboundMessageCharacters} characters.";
            return false;
        }

        if (Encoding.UTF8.GetByteCount(value) > MaxOutboundMessageUtf8Bytes)
        {
            error = $"The message exceeds {MaxOutboundMessageUtf8Bytes} UTF-8 bytes.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static string GetLabel(RelayChatType channel) => channel switch
    {
        RelayChatType.IncomingTell => "Incoming Tell",
        RelayChatType.OutgoingTell => "Outgoing Tell",
        RelayChatType.CrossWorldParty => "Cross-world Party",
        RelayChatType.FreeCompany => "Free Company",
        RelayChatType.PvPTeam => "PvP Team",
        RelayChatType.NoviceNetwork => "Novice Network",
        RelayChatType.StandardEmote => "Standard Emotes",
        RelayChatType.CustomEmote => "Custom Emotes",
        RelayChatType.CrossWorldLinkshell1 => "CWLS 1",
        RelayChatType.CrossWorldLinkshell2 => "CWLS 2",
        RelayChatType.CrossWorldLinkshell3 => "CWLS 3",
        RelayChatType.CrossWorldLinkshell4 => "CWLS 4",
        RelayChatType.CrossWorldLinkshell5 => "CWLS 5",
        RelayChatType.CrossWorldLinkshell6 => "CWLS 6",
        RelayChatType.CrossWorldLinkshell7 => "CWLS 7",
        RelayChatType.CrossWorldLinkshell8 => "CWLS 8",
        RelayChatType.Linkshell1 => "LS 1",
        RelayChatType.Linkshell2 => "LS 2",
        RelayChatType.Linkshell3 => "LS 3",
        RelayChatType.Linkshell4 => "LS 4",
        RelayChatType.Linkshell5 => "LS 5",
        RelayChatType.Linkshell6 => "LS 6",
        RelayChatType.Linkshell7 => "LS 7",
        RelayChatType.Linkshell8 => "LS 8",
        _ => channel.ToString(),
    };

    public static string GetShortLabel(RelayChatType channel) => channel switch
    {
        RelayChatType.IncomingTell => "TELL IN",
        RelayChatType.OutgoingTell => "TELL OUT",
        RelayChatType.CrossWorldParty => "CROSS-PARTY",
        RelayChatType.FreeCompany => "FC",
        RelayChatType.PvPTeam => "PVP",
        RelayChatType.NoviceNetwork => "NN",
        RelayChatType.StandardEmote => "EMOTE",
        RelayChatType.CustomEmote => "EMOTE",
        RelayChatType.CrossWorldLinkshell1 => "CWLS1",
        RelayChatType.CrossWorldLinkshell2 => "CWLS2",
        RelayChatType.CrossWorldLinkshell3 => "CWLS3",
        RelayChatType.CrossWorldLinkshell4 => "CWLS4",
        RelayChatType.CrossWorldLinkshell5 => "CWLS5",
        RelayChatType.CrossWorldLinkshell6 => "CWLS6",
        RelayChatType.CrossWorldLinkshell7 => "CWLS7",
        RelayChatType.CrossWorldLinkshell8 => "CWLS8",
        RelayChatType.Linkshell1 => "LS1",
        RelayChatType.Linkshell2 => "LS2",
        RelayChatType.Linkshell3 => "LS3",
        RelayChatType.Linkshell4 => "LS4",
        RelayChatType.Linkshell5 => "LS5",
        RelayChatType.Linkshell6 => "LS6",
        RelayChatType.Linkshell7 => "LS7",
        RelayChatType.Linkshell8 => "LS8",
        _ => channel.ToString().ToUpperInvariant(),
    };

    public static int GetDiscordColor(RelayChatType channel) => channel switch
    {
        RelayChatType.FreeCompany => 0x4CAF50,
        RelayChatType.Party or RelayChatType.CrossWorldParty => 0x4FC3F7,
        RelayChatType.Alliance => 0xF06292,
        RelayChatType.IncomingTell or RelayChatType.OutgoingTell => 0xEC407A,
        RelayChatType.Shout => 0xFF7043,
        RelayChatType.Yell => 0xFFB74D,
        RelayChatType.PvPTeam => 0xEF5350,
        >= RelayChatType.Linkshell1 and <= RelayChatType.CrossWorldLinkshell8 => 0xAB47BC,
        RelayChatType.NoviceNetwork => 0x43A047,
        RelayChatType.StandardEmote or RelayChatType.CustomEmote => 0x9E9E9E,
        _ => 0x5C6BC0,
    };
}
