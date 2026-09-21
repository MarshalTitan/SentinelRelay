using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using SentinelRelay.Core;
using SentinelRelay.Models;

namespace SentinelRelay.Services;

public sealed class GameChatSender
{
    public unsafe void Send(RelayChatType destination, string message)
    {
        if (!ChannelPolicy.ImplementedOutboundChannels.Contains(destination))
            throw new InvalidOperationException($"{destination} is not an implemented outbound chat destination.");

        var cleaned = MessageSanitizer.SanitizePlainText(message);
        if (!ChannelPolicy.IsValidOutboundMessage(cleaned, out var error))
            throw new InvalidOperationException(error);

        var uiModule = UIModule.Instance();
        var shellModule = RaptureShellModule.Instance();
        if (uiModule == null || shellModule == null)
            throw new InvalidOperationException("The FFXIV chat shell is unavailable.");

        // The destination prefix comes only from the fixed allowlist above.
        // Discord content can never become the command prefix.
        using var command = new Utf8String($"{ChannelPolicy.GetCommandPrefix(destination)} {cleaned}");
        if (command.Length > 500)
            throw new InvalidOperationException("The encoded FFXIV chat command is too long.");
        shellModule->ExecuteCommandInner(&command, uiModule);
    }
}
