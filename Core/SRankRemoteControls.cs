using SentinelRelay.Models;

namespace SentinelRelay.Core;

public enum SRankRemoteCommand { Status, Skip, ResetRequest, ResetConfirm }
public enum SRankRemoteDecision { Rejected, Status, Skip, ConfirmationRequired, Reset }
public sealed record SRankRemoteAuthorization(SRankRemoteDecision Decision, string Reason, bool SourceAuthorized = false)
{
    public bool Accepted => Decision != SRankRemoteDecision.Rejected;
}

public static class SRankRemoteCommandParser
{
    public static bool IsSRank(string? content) =>
        content?.TrimStart().StartsWith("/srank", StringComparison.OrdinalIgnoreCase) == true;

    public static bool TryParse(string? content, out SRankRemoteCommand command)
    {
        command = default;
        switch (content?.Trim().ToLowerInvariant())
        {
            case "/srank status": command = SRankRemoteCommand.Status; return true;
            case "/srank skip": command = SRankRemoteCommand.Skip; return true;
            case "/srank reset": command = SRankRemoteCommand.ResetRequest; return true;
            case "/srank reset confirm": command = SRankRemoteCommand.ResetConfirm; return true;
            default: return false;
        }
    }
}

/// <summary>Framework-thread session only. Nothing here executes game actions or persists intent.</summary>
public sealed class SRankRemoteControlSession
{
    public static readonly TimeSpan ConfirmationWindow = TimeSpan.FromSeconds(30);
    private PendingReset? pending;
    private string highWater = string.Empty;
    private int readerEpoch;
    private sealed record PendingReset(string CharacterKey, string Channel, string User,
        string RequestId, DateTime ExpiresUtc);

    // Clear pending intent on pause/logout/settings/reconnect/reload. The consumed high-water
    // mark survives an in-session reset; persisted checkpoints protect a new process.
    public void Clear() => pending = null;

    public SRankRemoteAuthorization Accept(CharacterProfile profile, CharacterIdentity? identity,
        DiscordChannelMessage source, string checkpointBeforeBatch, DateTime now, int readerGeneration = 0)
    {
        if (readerGeneration < readerEpoch) return Reject("StaleReaderGeneration");
        if (readerGeneration > readerEpoch)
        {
            Clear();
            readerEpoch = readerGeneration;
        }
        if (!profile.RemoteSRankControlsEnabled)
        {
            Clear();
            return Reject("RemoteControlsDisabled");
        }
        if (!DiscordSnowflake.IsValid(profile.DiscordRelayChannelId) ||
            !DiscordSnowflake.IsValid(profile.AuthorizedDiscordUserId))
            return Reject("ConfigurationInvalid");
        var checkpoint = DiscordSnowflake.Compare(highWater, checkpointBeforeBatch) > 0
            ? highWater : checkpointBeforeBatch;
        if (!DiscordReplyPolicy.TryAuthorizeSource(profile, identity, source, checkpoint,
                now, out var rejection, requireReplies: false))
            return Reject(rejection.ToString());
        highWater = source.Id; // Consume before parse/confirmation/IPC, including failures.
        if (!SRankRemoteCommandParser.TryParse(source.Content, out var command))
            return Reject("UnknownSRankCommand", true);
        if (pending is not null && now >= pending.ExpiresUtc) pending = null;
        switch (command)
        {
            case SRankRemoteCommand.Status: return new(SRankRemoteDecision.Status, "Status", true);
            case SRankRemoteCommand.Skip: return new(SRankRemoteDecision.Skip, "Skip", true);
            case SRankRemoteCommand.ResetRequest:
                var issued = source.Timestamp.UtcDateTime < now ? source.Timestamp.UtcDateTime : now;
                var expires = issued + ConfirmationWindow;
                if (expires <= now) return Reject("ConfirmationExpired", true);
                pending = new(profile.CharacterKey, source.ChannelId, source.Author.Id, source.Id, expires);
                return new(SRankRemoteDecision.ConfirmationRequired, "ResetConfirmationRequired", true);
            case SRankRemoteCommand.ResetConfirm:
                if (pending is null) return Reject("NoPendingResetOrExpired", true);
                if (pending.CharacterKey != profile.CharacterKey || pending.Channel != source.ChannelId ||
                    pending.User != source.Author.Id || DiscordSnowflake.Compare(source.Id, pending.RequestId) <= 0)
                    return Reject("ConfirmationBindingMismatch", true);
                if (source.Timestamp.UtcDateTime >= pending.ExpiresUtc)
                {
                    pending = null;
                    return Reject("ConfirmationExpired", true);
                }
                pending = null; // One-shot, consumed even if IPC or response delivery fails.
                return new(SRankRemoteDecision.Reset, "ResetConfirmed", true);
            default: return Reject("UnknownSRankCommand", true);
        }
    }

    private static SRankRemoteAuthorization Reject(string reason, bool authorized = false) => new(SRankRemoteDecision.Rejected, reason, authorized);
}
