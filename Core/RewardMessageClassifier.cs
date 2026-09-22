namespace SentinelRelay.Core;

public enum RewardLineKind
{
    None = 0,
    HuntCredit = 1,
    ObtainedReward = 2,
    CurrencyCapped = 3,
}

public static class RewardMessageClassifier
{
    // Dalamud API 15 XivChatType values are LogKind sheet RowIds.
    // Keep this deliberately narrow; a matching player-chat line must never
    // bypass that player's disabled inbound filter.
    private static readonly IReadOnlySet<int> CandidateLogKinds = new HashSet<int>
    {
        57, // SystemMessage
        58, // SystemError
        60, // ErrorMessage
        62, // LootNotice
        64, // Progress
    };

    public static bool IsCandidateLogKind(int logKind) => CandidateLogKinds.Contains(logKind);

    public static RewardLineKind ClassifyText(string? text)
    {
        var value = text?.Trim();
        if (string.IsNullOrEmpty(value))
            return RewardLineKind.None;

        if (value.Contains("rewarded for your contribution in slaying the mark", StringComparison.OrdinalIgnoreCase))
            return RewardLineKind.HuntCredit;
        if (value.StartsWith("You obtain ", StringComparison.OrdinalIgnoreCase))
            return RewardLineKind.ObtainedReward;
        if (value.StartsWith("You cannot carry any more ", StringComparison.OrdinalIgnoreCase))
            return RewardLineKind.CurrencyCapped;

        return RewardLineKind.None;
    }

    public static bool TryClassify(int logKind, string? text, out RewardLineKind lineKind)
    {
        lineKind = ClassifyText(text);
        return lineKind != RewardLineKind.None && IsCandidateLogKind(logKind);
    }
}
