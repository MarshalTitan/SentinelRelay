using System.Collections.Concurrent;
using SentinelRelay.Core;
using SentinelRelay.Models;
using SentinelRelay.Protocol;

namespace SentinelRelay.Services;

public sealed class OutboundCoordinator
{
    private static readonly TimeSpan ConfirmationTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan MinimumSendSpacing = TimeSpan.FromMilliseconds(1500);

    private readonly ConcurrentQueue<OutboundChatPayload> queue = new();
    private readonly GameChatSender sender;
    private readonly ReplayGuard replayGuard = new();
    private readonly SlidingWindowRateLimiter limiter = new(5, TimeSpan.FromSeconds(30));
    private readonly Action<string, bool, string?> acknowledge;
    private PendingOutbound? pending;
    private DateTime nextSendUtc = DateTime.MinValue;

    public OutboundCoordinator(GameChatSender sender, Action<string, bool, string?> acknowledge)
    {
        this.sender = sender;
        this.acknowledge = acknowledge;
    }

    public int QueueLength => queue.Count;

    public string? PendingEventId => pending?.Payload.EventId;

    public void Enqueue(OutboundChatPayload payload, DateTime utcNow)
    {
        if (!Enum.TryParse<RelayChatType>(payload.ChatType, true, out var channel)
            || !ChannelPolicy.OutboundChannels.Contains(channel))
        {
            acknowledge(payload.EventId, false, "Channel is not in the outbound whitelist.");
            return;
        }

        if (payload.ExpiresAtUtc <= utcNow || payload.IssuedAtUtc > utcNow.AddSeconds(30))
        {
            acknowledge(payload.EventId, false, "Message is stale or has an invalid timestamp.");
            return;
        }

        if (!replayGuard.TryAccept(payload.EventId, utcNow))
        {
            acknowledge(payload.EventId, false, "Duplicate or replayed message was rejected.");
            return;
        }

        var cleaned = MessageSanitizer.SanitizePlainText(payload.Message);
        if (!MessageSanitizer.IsValidOutbound(cleaned, out var error))
        {
            acknowledge(payload.EventId, false, error);
            return;
        }

        if (queue.Count >= 5)
        {
            acknowledge(payload.EventId, false, "The local outbound queue is full.");
            return;
        }

        queue.Enqueue(payload with { ChatType = channel.ToString(), Message = cleaned });
    }

    public void Update(DateTime utcNow)
    {
        if (pending is not null && utcNow - pending.SentAtUtc >= ConfirmationTimeout)
        {
            acknowledge(pending.Payload.EventId, false, "FFXIV did not confirm the chat message before timeout.");
            pending = null;
        }

        if (pending is not null || utcNow < nextSendUtc || !queue.TryDequeue(out var payload))
            return;

        if (payload.ExpiresAtUtc <= utcNow)
        {
            acknowledge(payload.EventId, false, "Message expired in the local queue.");
            return;
        }

        if (!limiter.TryAcquire(utcNow, out var retryAfter))
        {
            acknowledge(payload.EventId, false, $"Local rate limit reached. Retry in {Math.Ceiling(retryAfter.TotalSeconds)} seconds.");
            return;
        }

        var channel = Enum.Parse<RelayChatType>(payload.ChatType, true);
        pending = new PendingOutbound(payload, channel, MessageSanitizer.Signature(payload.Message), utcNow);
        nextSendUtc = utcNow + MinimumSendSpacing;
        try
        {
            sender.Send(channel, payload.Message);
        }
        catch (Exception ex)
        {
            pending = null;
            acknowledge(payload.EventId, false, ex.Message);
        }
    }

    public bool TryConfirmAndSuppress(CapturedChat chat, CharacterIdentity? identity)
    {
        if (pending is null || identity is null || pending.Channel != chat.ChatType)
            return false;

        if (!MessageSanitizer.Signature(chat.Message).Equals(pending.Signature, StringComparison.Ordinal))
            return false;

        if (!chat.Sender.Equals(identity.CharacterName, StringComparison.OrdinalIgnoreCase)
            && !chat.Sender.Contains(identity.CharacterName, StringComparison.OrdinalIgnoreCase))
            return false;

        var eventId = pending.Payload.EventId;
        pending = null;
        acknowledge(eventId, true, null);
        return true;
    }

    public void Clear(string reason)
    {
        if (pending is not null)
            acknowledge(pending.Payload.EventId, false, reason);
        pending = null;
        while (queue.TryDequeue(out var queued))
            acknowledge(queued.EventId, false, reason);
    }

    private sealed record PendingOutbound(
        OutboundChatPayload Payload,
        RelayChatType Channel,
        string Signature,
        DateTime SentAtUtc);
}

