namespace SentinelRelay.Core;

public enum DiscordControlCommand
{
    Screenshot,
}

public static class DiscordControlCommandParser
{
    public static bool TryParse(string? content, out DiscordControlCommand command)
    {
        command = default;
        if (!string.Equals(content?.Trim(), "/screenshot", StringComparison.OrdinalIgnoreCase))
            return false;

        command = DiscordControlCommand.Screenshot;
        return true;
    }
}
