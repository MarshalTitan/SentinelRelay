using SentinelRelay.Models;

namespace SentinelRelay.Core;

public static class DiscordReplyCommandParser
{
    private const string FreeCompanyPrefix = "/fc";

    public static bool TryParse(string? content, out RelayChatType destination, out string message)
    {
        destination = default;
        message = string.Empty;
        if (string.IsNullOrEmpty(content)
            || !content.StartsWith(FreeCompanyPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        if (content.Length == FreeCompanyPrefix.Length
            || !char.IsWhiteSpace(content[FreeCompanyPrefix.Length]))
            return false;

        var cleaned = MessageSanitizer.SanitizePlainText(content[(FreeCompanyPrefix.Length + 1)..]);
        if (!ChannelPolicy.IsValidOutboundMessage(cleaned, out _))
            return false;

        destination = RelayChatType.FreeCompany;
        message = cleaned;
        return true;
    }
}
