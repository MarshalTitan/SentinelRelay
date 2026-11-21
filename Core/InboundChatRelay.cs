using SentinelRelay.Models;

namespace SentinelRelay.Core;

// One admission/batching path for normal chat and keyword-only monitoring.
// The plugin still owns active-character and validated-webhook routing checks.
public sealed class InboundChatRelay
{
    private readonly DuplicateMessageFilter duplicateFilter = new();
    private readonly RewardMessageBatcher rewardBatcher = new();

    public IReadOnlyList<CapturedChat> Capture(CharacterProfile? profile, CapturedChat chat, DateTime utcNow)
    {
        if (!ShouldForward(profile, chat) || duplicateFilter.IsDuplicate(chat, utcNow))
            return [];

        if (chat.ChatType == RelayChatType.RewardsHuntResults)
        {
            var fullBatch = rewardBatcher.Add(chat, utcNow);
            return fullBatch is null ? [] : [fullBatch];
        }

        // Preserve event order when ordinary chat arrives during a reward batch.
        var pendingRewards = rewardBatcher.Flush();
        return pendingRewards is null ? [chat] : [pendingRewards, chat];
    }

    public CapturedChat? FlushIfDue(DateTime utcNow) => rewardBatcher.FlushIfDue(utcNow);

    public void ClearPendingRewards() => rewardBatcher.Clear();

    public void Clear()
    {
        rewardBatcher.Clear();
        duplicateFilter.Clear();
    }

    public static bool TryPrepare(
        CharacterProfile? profile,
        CapturedChat? chat,
        out IReadOnlyList<DiscordWebhookPayload> payloads)
    {
        payloads = [];
        if (chat is null || !RelayFilter.CanRelay(profile))
            return false;

        var matches = KeywordMatcher.FindMatches(profile!.Keywords, chat.ChatType, chat.Message);
        if (!RelayFilter.ShouldForward(profile, chat.ChatType, matches))
            return false;

        payloads = WebhookMessageFormatter.Format(
            chat,
            profile.IncludeSenderWorld,
            profile.UseDiscordEmbeds || chat.ChatType == RelayChatType.RewardsHuntResults,
            matches,
            profile.DiscordMentionUserId);
        return true;
    }

    private static bool ShouldForward(CharacterProfile? profile, CapturedChat chat)
    {
        if (!RelayFilter.CanRelay(profile))
            return false;

        var matches = KeywordMatcher.FindMatches(profile!.Keywords, chat.ChatType, chat.Message);
        return RelayFilter.ShouldForward(profile, chat.ChatType, matches);
    }
}
