namespace SentinelRelay.Core;

public static class DiscordSnowflake
{
    public static bool IsValid(string? value) =>
        value is { Length: >= 17 and <= 20 }
        && ulong.TryParse(value, out _);

    public static int Compare(string? left, string? right)
    {
        var hasLeft = ulong.TryParse(left, out var leftValue);
        var hasRight = ulong.TryParse(right, out var rightValue);
        if (!hasLeft && !hasRight)
            return 0;
        if (!hasLeft)
            return -1;
        if (!hasRight)
            return 1;
        return leftValue.CompareTo(rightValue);
    }
}
