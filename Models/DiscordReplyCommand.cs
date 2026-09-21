namespace SentinelRelay.Models;

public sealed record DiscordReplyCommand(
    string DiscordMessageId,
    string CharacterKey,
    RelayChatType Destination,
    string Message,
    DateTime ReceivedAtUtc);
