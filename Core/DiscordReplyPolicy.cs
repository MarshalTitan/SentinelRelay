using SentinelRelay.Models;

namespace SentinelRelay.Core;

public enum DiscordReplyRejection
{
    None,
    Disabled,
    Paused,
    NoActiveCharacter,
    WrongCharacter,
    WrongChannel,
    WrongUser,
    BotAuthor,
    WebhookAuthor,
    InvalidMessageId,
    Duplicate,
    Stale,
    UnknownOrInvalidCommand,
    DestinationNotAllowed,
    NoRecentTellTarget,
    ScreenshotDisabled,
    ScreenshotCooldown,
}

public static class DiscordReplyPolicy
{
    public static readonly TimeSpan MaximumCommandAge = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan MaximumTellReplyAge = TimeSpan.FromMinutes(30);
    internal static readonly TimeSpan MaximumFutureSkew = TimeSpan.FromSeconds(30);

    public static bool TryAuthorize(
        CharacterProfile profile,
        CharacterIdentity? activeIdentity,
        DiscordChannelMessage source,
        string checkpointBeforeBatch,
        DateTime utcNow,
        DateTime? lastIncomingTellUtc,
        out DiscordReplyCommand? command,
        out DiscordReplyRejection rejection)
    {
        command = null;
        rejection = DiscordReplyRejection.None;

        if (!TryAuthorizeSource(
                profile,
                activeIdentity,
                source,
                checkpointBeforeBatch,
                utcNow,
                out rejection))
            return false;
        if (!DiscordReplyCommandParser.TryParse(source.Content, out var destination, out var message))
            return Reject(DiscordReplyRejection.UnknownOrInvalidCommand, out rejection);
        if (!ChannelPolicy.ImplementedOutboundChannels.Contains(destination)
            || !profile.EnabledOutboundChannels.Contains(destination))
            return Reject(DiscordReplyRejection.DestinationNotAllowed, out rejection);
        if (destination == RelayChatType.IncomingTell
            && !HasRecentTellTarget(lastIncomingTellUtc, utcNow))
            return Reject(DiscordReplyRejection.NoRecentTellTarget, out rejection);

        command = new DiscordReplyCommand(
            source.Id,
            profile.CharacterKey,
            destination,
            message,
            utcNow);
        return true;
    }

    public static bool HasRecentTellTarget(DateTime? lastIncomingTellUtc, DateTime utcNow) =>
        lastIncomingTellUtc is { } timestamp
        && timestamp >= utcNow - MaximumTellReplyAge
        && timestamp <= utcNow + MaximumFutureSkew;

    internal static bool TryAuthorizeSource(
        CharacterProfile profile,
        CharacterIdentity? activeIdentity,
        DiscordChannelMessage source,
        string checkpointBeforeBatch,
        DateTime utcNow,
        out DiscordReplyRejection rejection)
    {
        rejection = DiscordReplyRejection.None;
        if (!profile.DiscordRepliesEnabled)
            return Reject(DiscordReplyRejection.Disabled, out rejection);
        if (profile.Paused)
            return Reject(DiscordReplyRejection.Paused, out rejection);
        if (activeIdentity is null)
            return Reject(DiscordReplyRejection.NoActiveCharacter, out rejection);
        if (!string.Equals(profile.CharacterKey, activeIdentity.CharacterKey, StringComparison.Ordinal))
            return Reject(DiscordReplyRejection.WrongCharacter, out rejection);
        if (!string.Equals(profile.DiscordRelayChannelId, source.ChannelId, StringComparison.Ordinal))
            return Reject(DiscordReplyRejection.WrongChannel, out rejection);
        if (source.Author.Bot)
            return Reject(DiscordReplyRejection.BotAuthor, out rejection);
        if (!string.IsNullOrWhiteSpace(source.WebhookId))
            return Reject(DiscordReplyRejection.WebhookAuthor, out rejection);
        if (!string.Equals(profile.AuthorizedDiscordUserId, source.Author.Id, StringComparison.Ordinal))
            return Reject(DiscordReplyRejection.WrongUser, out rejection);
        if (!DiscordSnowflake.IsValid(source.Id))
            return Reject(DiscordReplyRejection.InvalidMessageId, out rejection);
        if (DiscordSnowflake.Compare(source.Id, checkpointBeforeBatch) <= 0)
            return Reject(DiscordReplyRejection.Duplicate, out rejection);

        var timestamp = source.Timestamp.UtcDateTime;
        if (timestamp < utcNow - MaximumCommandAge || timestamp > utcNow + MaximumFutureSkew)
            return Reject(DiscordReplyRejection.Stale, out rejection);
        return true;
    }

    private static bool Reject(DiscordReplyRejection value, out DiscordReplyRejection rejection)
    {
        rejection = value;
        return false;
    }
}

public static class RemoteScreenshotPolicy
{
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(15);

    public static bool TryAuthorize(
        CharacterProfile profile,
        CharacterIdentity? activeIdentity,
        DiscordChannelMessage source,
        string checkpointBeforeBatch,
        DateTime utcNow,
        DateTime? lastAcceptedScreenshotUtc,
        out DiscordReplyRejection rejection)
    {
        rejection = DiscordReplyRejection.None;
        if (!DiscordControlCommandParser.TryParse(source.Content, out var command)
            || command != DiscordControlCommand.Screenshot)
            return false;
        if (!DiscordReplyPolicy.TryAuthorizeSource(
                profile,
                activeIdentity,
                source,
                checkpointBeforeBatch,
                utcNow,
                out rejection))
            return false;
        if (!profile.RemoteScreenshotsEnabled)
            return Reject(DiscordReplyRejection.ScreenshotDisabled, out rejection);
        if (lastAcceptedScreenshotUtc is { } last
            && last >= utcNow - Cooldown
            && last <= utcNow + DiscordReplyPolicy.MaximumFutureSkew)
            return Reject(DiscordReplyRejection.ScreenshotCooldown, out rejection);
        return true;
    }

    private static bool Reject(DiscordReplyRejection value, out DiscordReplyRejection rejection)
    {
        rejection = value;
        return false;
    }
}
