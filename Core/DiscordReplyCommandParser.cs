using SentinelRelay.Models;

namespace SentinelRelay.Core;

public static class DiscordReplyCommandParser
{
    private static readonly IReadOnlyDictionary<string, RelayChatType> Commands =
        new Dictionary<string, RelayChatType>(StringComparer.OrdinalIgnoreCase)
        {
            ["/say"] = RelayChatType.Say,
            ["/s"] = RelayChatType.Say,
            ["/yell"] = RelayChatType.Yell,
            ["/y"] = RelayChatType.Yell,
            ["/shout"] = RelayChatType.Shout,
            ["/sh"] = RelayChatType.Shout,
            ["/r"] = RelayChatType.IncomingTell,
            ["/party"] = RelayChatType.Party,
            ["/p"] = RelayChatType.Party,
            ["/alliance"] = RelayChatType.Alliance,
            ["/a"] = RelayChatType.Alliance,
            ["/fc"] = RelayChatType.FreeCompany,
            ["/pvpteam"] = RelayChatType.PvPTeam,
            ["/ls1"] = RelayChatType.Linkshell1,
            ["/ls2"] = RelayChatType.Linkshell2,
            ["/ls3"] = RelayChatType.Linkshell3,
            ["/ls4"] = RelayChatType.Linkshell4,
            ["/ls5"] = RelayChatType.Linkshell5,
            ["/ls6"] = RelayChatType.Linkshell6,
            ["/ls7"] = RelayChatType.Linkshell7,
            ["/ls8"] = RelayChatType.Linkshell8,
            ["/cwls1"] = RelayChatType.CrossWorldLinkshell1,
            ["/cwls2"] = RelayChatType.CrossWorldLinkshell2,
            ["/cwls3"] = RelayChatType.CrossWorldLinkshell3,
            ["/cwls4"] = RelayChatType.CrossWorldLinkshell4,
            ["/cwls5"] = RelayChatType.CrossWorldLinkshell5,
            ["/cwls6"] = RelayChatType.CrossWorldLinkshell6,
            ["/cwls7"] = RelayChatType.CrossWorldLinkshell7,
            ["/cwls8"] = RelayChatType.CrossWorldLinkshell8,
            ["/novice"] = RelayChatType.NoviceNetwork,
            ["/n"] = RelayChatType.NoviceNetwork,
        };

    public static bool TryParse(string? content, out RelayChatType destination, out string message)
    {
        destination = default;
        message = string.Empty;
        if (string.IsNullOrEmpty(content) || content[0] != '/')
            return false;

        var separator = -1;
        for (var index = 1; index < content.Length; index++)
        {
            if (!char.IsWhiteSpace(content[index]))
                continue;
            separator = index;
            break;
        }

        if (separator < 0 || !Commands.TryGetValue(content[..separator], out destination))
            return false;

        var cleaned = MessageSanitizer.SanitizePlainText(content[(separator + 1)..]);
        if (!ChannelPolicy.IsValidOutboundMessage(cleaned, out _))
            return false;

        message = cleaned;
        return true;
    }
}
