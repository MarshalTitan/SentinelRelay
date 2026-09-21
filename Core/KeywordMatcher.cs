using System.Text.RegularExpressions;
using SentinelRelay.Models;

namespace SentinelRelay.Core;

public static class KeywordMatcher
{
    public static IReadOnlyList<KeywordRule> FindMatches(
        IEnumerable<KeywordRule> rules,
        RelayChatType channel,
        string message)
    {
        if (string.IsNullOrEmpty(message))
            return [];

        return rules
            .Where(rule => rule.Channels.Contains(channel) && IsMatch(rule, message))
            .GroupBy(rule => rule.Keyword, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    public static bool IsMatch(KeywordRule rule, string message)
    {
        var keyword = rule.Keyword.Trim();
        if (keyword.Length == 0)
            return false;

        if (!rule.WholeWord)
            return message.Contains(keyword, StringComparison.OrdinalIgnoreCase);

        return Regex.IsMatch(
            message,
            $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(keyword)}(?![\p{{L}}\p{{N}}_])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));
    }
}

