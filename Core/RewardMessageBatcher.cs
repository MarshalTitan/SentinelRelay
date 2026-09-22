using SentinelRelay.Models;

namespace SentinelRelay.Core;

public sealed class RewardMessageBatcher(
    TimeSpan? quietPeriod = null,
    TimeSpan? maximumAge = null,
    int maximumLines = 20)
{
    private readonly TimeSpan quietPeriod = quietPeriod ?? TimeSpan.FromMilliseconds(1250);
    private readonly TimeSpan maximumAge = maximumAge ?? TimeSpan.FromSeconds(4);
    private readonly int maximumLines = Math.Max(1, maximumLines);
    private readonly List<CapturedChat> pending = [];
    private DateTime firstReceivedUtc;
    private DateTime lastReceivedUtc;

    public int Count => pending.Count;

    public CapturedChat? Add(CapturedChat chat, DateTime receivedUtc)
    {
        if (chat.ChatType != RelayChatType.RewardsHuntResults)
            throw new ArgumentException("Only reward messages can be batched.", nameof(chat));

        if (pending.Count == 0)
            firstReceivedUtc = receivedUtc;
        pending.Add(chat);
        lastReceivedUtc = receivedUtc;
        return pending.Count >= maximumLines ? Flush() : null;
    }

    public CapturedChat? FlushIfDue(DateTime utcNow)
    {
        if (pending.Count == 0)
            return null;
        return utcNow - lastReceivedUtc >= quietPeriod || utcNow - firstReceivedUtc >= maximumAge
            ? Flush()
            : null;
    }

    public CapturedChat? Flush()
    {
        if (pending.Count == 0)
            return null;

        var first = pending[0];
        var combined = new CapturedChat(
            RelayChatType.RewardsHuntResults,
            "FFXIV",
            null,
            string.Join('\n', pending.Select(item => item.Message)),
            first.TimestampUtc);
        Clear();
        return combined;
    }

    public void Clear()
    {
        pending.Clear();
        firstReceivedUtc = default;
        lastReceivedUtc = default;
    }
}
