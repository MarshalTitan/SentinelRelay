namespace SentinelRelay.Models;

public sealed record CapturedChat(
    RelayChatType ChatType,
    string Sender,
    string? SenderWorld,
    string Message,
    DateTime TimestampUtc);

