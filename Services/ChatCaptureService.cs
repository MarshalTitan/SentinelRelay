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

    public ChatCaptureService(IChatGui chatGui, IPluginLog log, Action<CapturedChat> captured)
    {
        this.chatGui = chatGui;
        this.log = log;
        this.captured = captured;
        chatGui.ChatMessage += OnChatMessage;
    }

    public void Dispose() => chatGui.ChatMessage -= OnChatMessage;

    private void OnChatMessage(IHandleableChatMessage message)
    {
        try
        {
            if (!ChatChannelMapper.TryMap(message.LogKind, out var channel))
                return;

            var text = MessageSanitizer.SanitizePlainText(message.Message.TextValue);
            if (text.Length == 0)
                return;

            var player = message.Sender.Payloads.OfType<PlayerPayload>().FirstOrDefault();
            var sender = MessageSanitizer.SanitizePlainText(player?.PlayerName ?? message.Sender.TextValue);
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
}
