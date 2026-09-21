using Dalamud.Game.Text;
using SentinelRelay.Models;

namespace SentinelRelay.Services;

public static class ChatChannelMapper
{
    public static bool TryMap(XivChatType xivType, out RelayChatType relayType)
    {
        relayType = xivType switch
        {
            XivChatType.Say => RelayChatType.Say,
            XivChatType.Yell => RelayChatType.Yell,
            XivChatType.Shout => RelayChatType.Shout,
            XivChatType.TellIncoming or XivChatType.TellOutgoing => RelayChatType.Tell,
            XivChatType.Party or XivChatType.CrossParty => RelayChatType.Party,
            XivChatType.Alliance => RelayChatType.Alliance,
            XivChatType.FreeCompany => RelayChatType.FreeCompany,
            XivChatType.PvPTeam => RelayChatType.PvPTeam,
            XivChatType.Ls1 => RelayChatType.Linkshell1,
            XivChatType.Ls2 => RelayChatType.Linkshell2,
            XivChatType.Ls3 => RelayChatType.Linkshell3,
            XivChatType.Ls4 => RelayChatType.Linkshell4,
            XivChatType.Ls5 => RelayChatType.Linkshell5,
            XivChatType.Ls6 => RelayChatType.Linkshell6,
            XivChatType.Ls7 => RelayChatType.Linkshell7,
            XivChatType.Ls8 => RelayChatType.Linkshell8,
            XivChatType.CrossLinkShell1 => RelayChatType.CrossWorldLinkshell1,
            XivChatType.CrossLinkShell2 => RelayChatType.CrossWorldLinkshell2,
            XivChatType.CrossLinkShell3 => RelayChatType.CrossWorldLinkshell3,
            XivChatType.CrossLinkShell4 => RelayChatType.CrossWorldLinkshell4,
            XivChatType.CrossLinkShell5 => RelayChatType.CrossWorldLinkshell5,
            XivChatType.CrossLinkShell6 => RelayChatType.CrossWorldLinkshell6,
            XivChatType.CrossLinkShell7 => RelayChatType.CrossWorldLinkshell7,
            XivChatType.CrossLinkShell8 => RelayChatType.CrossWorldLinkshell8,
            XivChatType.NoviceNetwork => RelayChatType.NoviceNetwork,
            XivChatType.StandardEmote => RelayChatType.StandardEmote,
            XivChatType.CustomEmote => RelayChatType.CustomEmote,
            _ => default,
        };

        return xivType is XivChatType.Say or XivChatType.Yell or XivChatType.Shout
            or XivChatType.TellIncoming or XivChatType.TellOutgoing
            or XivChatType.Party or XivChatType.CrossParty or XivChatType.Alliance
            or XivChatType.FreeCompany or XivChatType.PvPTeam
            or >= XivChatType.Ls1 and <= XivChatType.Ls8
            or >= XivChatType.CrossLinkShell2 and <= XivChatType.CrossLinkShell8
            or XivChatType.CrossLinkShell1 or XivChatType.NoviceNetwork
            or XivChatType.StandardEmote or XivChatType.CustomEmote;
    }
}

