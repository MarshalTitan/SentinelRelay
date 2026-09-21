namespace SentinelRelay.Core;

public sealed class ReplayGuard(int capacity = 256, TimeSpan? retention = null)
{
    private readonly int capacity = capacity;
    private readonly TimeSpan retention = retention ?? TimeSpan.FromMinutes(5);
    private readonly Dictionary<string, DateTime> seen = new(StringComparer.Ordinal);

    public bool TryAccept(string eventId, DateTime utcNow)
    {
        foreach (var expired in seen.Where(pair => utcNow - pair.Value > retention).Select(pair => pair.Key).ToArray())
            seen.Remove(expired);

        if (seen.ContainsKey(eventId))
            return false;

        if (seen.Count >= capacity)
        {
            var oldest = seen.MinBy(pair => pair.Value).Key;
            seen.Remove(oldest);
        }

        seen[eventId] = utcNow;
        return true;
    }
}

