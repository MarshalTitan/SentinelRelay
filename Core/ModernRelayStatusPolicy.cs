namespace SentinelRelay.Core;

public enum ModernRelayStatusKind
{
    Disconnected,
    NotConfigured,
    WebhookReady,
    Linked,
    Paused,
    Connecting,
    Delivering,
    ReaderOffline,
    Error,
}

public readonly record struct ModernRelayStatusInput(
    bool HasCharacter,
    bool Paused,
    bool HasWebhook,
    bool WebhookSending,
    bool WebhookError,
    bool RepliesEnabled,
    bool ReaderInitializing,
    bool ReaderConnected,
    bool ReaderError,
    bool ScreenshotsEnabled,
    bool ScreenshotError);

public static class ModernRelayStatusPolicy
{
    public static ModernRelayStatusKind Resolve(ModernRelayStatusInput input)
    {
        if (!input.HasCharacter)
            return ModernRelayStatusKind.Disconnected;
        if (input.Paused)
            return ModernRelayStatusKind.Paused;
        if (input.WebhookError
            || (input.RepliesEnabled && input.ReaderError)
            || (input.ScreenshotsEnabled && input.ScreenshotError))
            return ModernRelayStatusKind.Error;
        if (input.WebhookSending)
            return ModernRelayStatusKind.Delivering;
        if (input.RepliesEnabled)
        {
            if (input.ReaderConnected)
                return ModernRelayStatusKind.Linked;
            if (input.ReaderInitializing)
                return ModernRelayStatusKind.Connecting;
            return ModernRelayStatusKind.ReaderOffline;
        }

        return input.HasWebhook
            ? ModernRelayStatusKind.WebhookReady
            : ModernRelayStatusKind.NotConfigured;
    }
}
