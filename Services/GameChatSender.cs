using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using SentinelRelay.Core;
using SentinelRelay.Models;

namespace SentinelRelay.Services;

public sealed class GameChatSender
{
    public unsafe void Send(RelayChatType channel, string message)
    {
        if (!ChannelPolicy.OutboundChannels.Contains(channel))
            throw new InvalidOperationException($"{channel} is not allowed for outbound relay.");

        var cleaned = MessageSanitizer.SanitizePlainText(message);
        if (!MessageSanitizer.IsValidOutbound(cleaned, out var error))
            throw new InvalidOperationException(error);

        var uiModule = UIModule.Instance();
        var shellModule = RaptureShellModule.Instance();
        if (uiModule == null || shellModule == null)
            throw new InvalidOperationException("The FFXIV chat shell is unavailable.");

        using var command = new Utf8String($"{ChannelPolicy.GetCommandPrefix(channel)} {cleaned}");
        if (command.Length > 500)
            throw new InvalidOperationException("The encoded FFXIV command is too long.");
        shellModule->ExecuteCommandInner(&command, uiModule);
    }
}

