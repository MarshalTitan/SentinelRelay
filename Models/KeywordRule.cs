namespace SentinelRelay.Models;

public sealed class KeywordRule
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Keyword { get; set; } = string.Empty;

    public bool WholeWord { get; set; }

    public bool PingDiscordUser { get; set; }

    public HashSet<RelayChatType> Channels { get; set; } = [];
}
