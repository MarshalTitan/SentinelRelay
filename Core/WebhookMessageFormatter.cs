using System.Text;
using SentinelRelay.Models;

namespace SentinelRelay.Core;

public static class WebhookMessageFormatter
{
    private static readonly DiscordAllowedMentions NoMentions = new([]);

    public static IReadOnlyList<DiscordWebhookPayload> Format(
        CapturedChat chat,
        bool includeWorld,
        bool useEmbeds,
        IReadOnlyList<KeywordRule>? matchedKeywords = null,
        string? mentionUserId = null)
    {
        var sender = MessageSanitizer.SanitizeForDiscord(chat.Sender);
        var world = MessageSanitizer.SanitizeForDiscord(chat.SenderWorld);
        var message = chat.ChatType == RelayChatType.RewardsHuntResults
            ? SanitizeMultilineForDiscord(chat.Message)
            : MessageSanitizer.SanitizeForDiscord(chat.Message);
        var senderLabel = includeWorld && world.Length > 0 ? $"{sender} @ {world}" : sender;
        var channel = ChannelPolicy.GetShortLabel(chat.ChatType);
        var alert = BuildAlert(matchedKeywords, mentionUserId);

        if (useEmbeds)
        {
            var chunks = SplitByRune(message, ChannelPolicy.SafeDiscordEmbedDescriptionLimit);
            var reward = chat.ChatType == RelayChatType.RewardsHuntResults;
            return chunks.Select((chunk, index) => new DiscordWebhookPayload(
                index == 0 ? alert.Content : null,
                [new DiscordEmbedPayload(
                    reward
                        ? index == 0 ? "【REWARD】" : "【REWARD】 (continued)"
                        : index == 0 ? $"【{channel}】 {senderLabel}" : $"【{channel}】 {senderLabel} (continued)",
                    chunk,
                    ChannelPolicy.GetDiscordColor(chat.ChatType))],
                index == 0 ? alert.AllowedMentions : NoMentions)).ToArray();
        }

        var prefix = chat.ChatType == RelayChatType.RewardsHuntResults
            ? "【REWARD】 "
            : $"【{channel}】 {senderLabel}: ";
        var alertPrefix = alert.Content is null ? string.Empty : alert.Content + "\n";
        var firstLimit = Math.Max(1, ChannelPolicy.SafeDiscordContentLimit - prefix.Length - alertPrefix.Length);
        var chunksForContent = SplitByRune(message, firstLimit);
        var payloads = new List<DiscordWebhookPayload>(chunksForContent.Count);
        for (var index = 0; index < chunksForContent.Count; index++)
        {
            var contentPrefix = index == 0 ? prefix : $"【{channel} continued】 ";
            var notificationPrefix = index == 0 ? alertPrefix : string.Empty;
            var available = ChannelPolicy.SafeDiscordContentLimit - contentPrefix.Length - notificationPrefix.Length;
            foreach (var part in SplitByRune(chunksForContent[index], Math.Max(1, available)))
            {
                payloads.Add(new DiscordWebhookPayload(
                    notificationPrefix + contentPrefix + part,
                    null,
                    index == 0 ? alert.AllowedMentions : NoMentions));
                notificationPrefix = string.Empty;
            }
        }
        return payloads;
    }

    public static DiscordWebhookPayload TestMessage(string characterName) => new(
        $"Sentinel Relay connected successfully for {MessageSanitizer.SanitizeForDiscord(characterName)}.",
        null,
        NoMentions);

    private static string SanitizeMultilineForDiscord(string value) => string.Join('\n', value
        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(MessageSanitizer.SanitizeForDiscord)
        .Where(line => line.Length > 0));

    private static (string? Content, DiscordAllowedMentions AllowedMentions) BuildAlert(
        IReadOnlyList<KeywordRule>? matches,
        string? mentionUserId)
    {
        if (matches is null || matches.Count == 0)
            return (null, NoMentions);

        var labels = string.Join(", ", matches
            .Select(rule => $"\"{MessageSanitizer.SanitizeForDiscord(rule.Keyword)}\"")
            .Distinct(StringComparer.OrdinalIgnoreCase));
        var canPing = matches.Any(rule => rule.PingDiscordUser)
            && WebhookEndpoint.IsValidDiscordUserId(mentionUserId);
        var mention = canPing ? $"<@{mentionUserId}> " : string.Empty;
        var allowed = canPing
            ? new DiscordAllowedMentions([], [mentionUserId!])
            : NoMentions;
        return ($"{mention}🔔 Keyword alert: {labels}", allowed);
    }

    internal static IReadOnlyList<string> SplitByRune(string value, int maxCharacters)
    {
        if (maxCharacters <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxCharacters));
        if (value.Length == 0)
            return [string.Empty];

        var results = new List<string>();
        var builder = new StringBuilder(Math.Min(value.Length, maxCharacters));
        var count = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            var text = rune.ToString();
            if (count + text.Length > maxCharacters && builder.Length > 0)
            {
                results.Add(builder.ToString());
                builder.Clear();
                count = 0;
            }
            builder.Append(text);
            count += text.Length;
        }
        if (builder.Length > 0)
            results.Add(builder.ToString());
        return results;
    }
}
