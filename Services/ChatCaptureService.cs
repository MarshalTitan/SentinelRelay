using Dalamud.Game.Chat;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using SentinelRelay.Core;
using SentinelRelay.Models;

namespace SentinelRelay.Services;

public sealed class ChatCaptureService : IDisposable
{
    private readonly IChatGui chatGui;
    private readonly IPluginLog log;
    private readonly Action<CapturedChat> captured;
    private readonly Func<bool> rewardDiagnosticsEnabled;

    public ChatCaptureService(
        IChatGui chatGui,
        IPluginLog log,
        Action<CapturedChat> captured,
        Func<bool> rewardDiagnosticsEnabled)
    {
        this.chatGui = chatGui;
        this.log = log;
        this.captured = captured;
        this.rewardDiagnosticsEnabled = rewardDiagnosticsEnabled;
        chatGui.ChatMessage += OnChatMessage;
        chatGui.LogMessage += OnLogMessage;
    }

    public RewardDiagnosticBuffer RewardDiagnostics { get; } = new();

    public void Dispose()
    {
        chatGui.ChatMessage -= OnChatMessage;
        chatGui.LogMessage -= OnLogMessage;
    }

    private void OnChatMessage(IHandleableChatMessage message)
    {
        try
        {
            var text = MessageSanitizer.SanitizePlainText(message.Message.TextValue);
            if (text.Length == 0)
                return;

            var logKindValue = (int)message.LogKind;
            var rewardLineKind = RewardMessageClassifier.ClassifyText(text);
            var candidateRewardLogKind = RewardMessageClassifier.IsCandidateLogKind(logKindValue);
            if (rewardDiagnosticsEnabled() && rewardLineKind != RewardLineKind.None)
            {
                RewardDiagnostics.RecordChatMessage(
                    message.LogKind.ToString(),
                    logKindValue,
                    candidateRewardLogKind,
                    rewardLineKind,
                    text,
                    DateTime.UtcNow);
            }

            RelayChatType channel;
            if (candidateRewardLogKind && rewardLineKind != RewardLineKind.None)
                channel = RelayChatType.RewardsHuntResults;
            else if (!ChatChannelMapper.TryMap(message.LogKind, out channel))
                return;

            var player = channel == RelayChatType.RewardsHuntResults
                ? null
                : message.Sender.Payloads.OfType<PlayerPayload>().FirstOrDefault();
            var sender = channel == RelayChatType.RewardsHuntResults
                ? "FFXIV"
                : MessageSanitizer.SanitizePlainText(player?.PlayerName ?? message.Sender.TextValue);
            if (sender.Length == 0)
                sender = "Unknown";
            string? senderWorld = null;
            if (player is not null)
            {
                try
                {
                    senderWorld = player.World.Value.Name.ToString();
                }
                catch
                {
                    // Some local channel payloads intentionally omit a world.
                }
            }

            var timestamp = message.Timestamp > 0
                ? DateTimeOffset.FromUnixTimeSeconds(message.Timestamp).UtcDateTime
                : DateTime.UtcNow;
            captured(new CapturedChat(channel, sender, senderWorld, text, timestamp));
        }
        catch (Exception ex)
        {
            log.Warning("Sentinel Relay ignored a chat message that could not be sanitized ({ExceptionType}).", ex.GetType().Name);
        }
    }

    private void OnLogMessage(ILogMessage message)
    {
        try
        {
            if (rewardDiagnosticsEnabled())
                RewardDiagnostics.RecordLogMessage(message.LogMessageId, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            log.Warning("Sentinel Relay ignored a LogMessage diagnostic that could not be recorded ({ExceptionType}).", ex.GetType().Name);
        }
    }
}
