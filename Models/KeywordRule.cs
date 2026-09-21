namespace SentinelRelay.Models;

public sealed class KeywordRule
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Keyword { get; set; } = string.Empty;

    public bool WholeWord { get; set; }

    public AlertMethod AlertMethod { get; set; } = AlertMethod.DirectMessage;

    public HashSet<RelayChatType> Channels { get; set; } = [];
}

