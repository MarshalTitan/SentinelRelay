using SentinelRelay.Models;

namespace SentinelRelay.Core;

public sealed class DuplicateMessageFilter(TimeSpan? retention = null, int capacity = 256)
{
    private readonly TimeSpan retention = retention ?? TimeSpan.FromSeconds(2);
    private readonly int capacity = capacity;
    private readonly Dictionary<string, DateTime> recent = new(StringComparer.Ordinal);

    public bool IsDuplicate(CapturedChat chat, DateTime utcNow)
    {
        foreach (var expired in recent
                     .Where(pair => utcNow - pair.Value > retention)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            recent.Remove(expired);
        }

        var signature = string.Join('\u001F',
            chat.ChatType,
            MessageSanitizer.Signature(chat.Sender),
            MessageSanitizer.Signature(chat.SenderWorld ?? string.Empty),
            MessageSanitizer.Signature(chat.Message));

        if (recent.ContainsKey(signature))
            return true;

        if (recent.Count >= capacity)
        {
            var oldest = recent.MinBy(pair => pair.Value).Key;
            recent.Remove(oldest);
        }

        recent[signature] = utcNow;
        return false;
    }

    public void Clear() => recent.Clear();
}
