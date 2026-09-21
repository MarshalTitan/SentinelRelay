using System.Collections.Concurrent;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using SentinelRelay.Core;
using SentinelRelay.Models;
using SentinelRelay.Services;
using SentinelRelay.UI;

namespace SentinelRelay;

public sealed record WebhookConfigurationResult(bool Success, string? Error);

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/srelay";
    private static readonly string PluginVersion = typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "unknown";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private readonly WindowSystem windows = new("SentinelRelay");
    private readonly ConcurrentQueue<Action> mainThreadActions = new();
    private readonly WebhookSecretProtector secretProtector = new();
    private readonly CharacterContextService characterContext;
    private readonly WebhookRelayClient webhookRelay;
    private readonly DuplicateMessageFilter duplicateFilter = new();
    private readonly ChatCaptureService chatCapture;
    private readonly MainWindow mainWindow;
    private CharacterIdentity? activeIdentity;
    private CharacterProfile? activeProfile;
    private Uri? activeWebhookEndpoint;
    private DateTime nextCharacterCheckUtc = DateTime.MinValue;
    private bool disposed;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        characterContext = new CharacterContextService(PlayerState);
        webhookRelay = new WebhookRelayClient(Log);
        webhookRelay.DeliveryCompleted += result => mainThreadActions.Enqueue(() => OnDeliveryCompleted(result));
        chatCapture = new ChatCaptureService(ChatGui, Log, OnChatCaptured);
        mainWindow = new MainWindow(
            () => activeIdentity,
            () => activeProfile,
            () => activeWebhookEndpoint,
            webhookRelay,
            SaveWebhook,
            RemoveWebhook,
            TestWebhook,
            SetPaused,
            SaveConfiguration);

        windows.AddWindow(mainWindow);
        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Sentinel Relay. Options: status, pause, resume, debug",
        });
        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi += OpenMainWindow;
        PluginInterface.UiBuilder.OpenConfigUi += OpenMainWindow;
        Framework.Update += OnFrameworkUpdate;
        RefreshCharacter(force: true);
        Log.Information("Sentinel Relay {Version} loaded in direct Discord webhook mode.", PluginVersion);
    }

    public Configuration Configuration { get; }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw -= windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= OpenMainWindow;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenMainWindow;
        CommandManager.RemoveHandler(CommandName);
        chatCapture.Dispose();
        webhookRelay.Dispose();
        windows.RemoveAllWindows();
    }

    public void SaveConfiguration() => PluginInterface.SavePluginConfig(Configuration);

    private void OnFrameworkUpdate(IFramework _)
    {
        while (mainThreadActions.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Warning("Sentinel Relay main-thread action failed ({ExceptionType}).", ex.GetType().Name);
            }
        }

        var now = DateTime.UtcNow;
        if (now >= nextCharacterCheckUtc)
        {
            nextCharacterCheckUtc = now.AddMilliseconds(250);
            RefreshCharacter(force: false);
        }
    }

    private void RefreshCharacter(bool force)
    {
        var identity = characterContext.Current;
        if (!force && identity?.CharacterKey == activeIdentity?.CharacterKey)
            return;

        webhookRelay.ClearQueue();
        duplicateFilter.Clear();
        activeIdentity = identity;
        activeProfile = null;
        activeWebhookEndpoint = null;
        if (identity is null)
        {
            webhookRelay.SetConfigured(false);
            return;
        }

        activeProfile = Configuration.GetOrCreateProfile(
            identity.CharacterKey,
            identity.CharacterName,
            identity.HomeWorld);
        LoadActiveWebhook();
        SaveConfiguration();
    }

    private void LoadActiveWebhook()
    {
        activeWebhookEndpoint = null;
        if (activeProfile is null)
        {
            webhookRelay.SetConfigured(false);
            return;
        }

        var value = secretProtector.Unprotect(activeProfile.ProtectedWebhookUrl);
        if (WebhookEndpoint.TryCreate(value, out var endpoint, out _))
            activeWebhookEndpoint = endpoint;
        webhookRelay.SetConfigured(activeWebhookEndpoint is not null);
    }

    private WebhookConfigurationResult SaveWebhook(string value)
    {
        if (activeProfile is null)
            return new WebhookConfigurationResult(false, "Log into a character before configuring its webhook.");
        if (!WebhookEndpoint.TryCreate(value, out var endpoint, out var error))
            return new WebhookConfigurationResult(false, error);

        try
        {
            activeProfile.ProtectedWebhookUrl = secretProtector.Protect(value.Trim());
            activeProfile.LastWebhookSuccessUtc = null;
            activeWebhookEndpoint = endpoint;
            webhookRelay.ClearQueue();
            webhookRelay.SetConfigured(true);
            SaveConfiguration();
            return new WebhookConfigurationResult(true, null);
        }
        catch (Exception ex)
        {
            Log.Error("Sentinel Relay could not protect the Discord webhook URL ({ExceptionType}).", ex.GetType().Name);
            return new WebhookConfigurationResult(false, "Windows could not securely protect the webhook URL.");
        }
    }

    private void RemoveWebhook()
    {
        if (activeProfile is null)
            return;
        activeProfile.ProtectedWebhookUrl = string.Empty;
        activeProfile.LastWebhookSuccessUtc = null;
        activeWebhookEndpoint = null;
        webhookRelay.ClearQueue();
        webhookRelay.SetConfigured(false);
        SaveConfiguration();
    }

    private bool TestWebhook()
    {
        if (activeIdentity is null || activeWebhookEndpoint is null)
            return false;
        return webhookRelay.TryEnqueue(
            activeIdentity.CharacterKey,
            activeWebhookEndpoint,
            [WebhookMessageFormatter.TestMessage(activeIdentity.CharacterName)],
            isTest: true);
    }

    private void OnChatCaptured(CapturedChat chat)
    {
        // Drop rather than risk routing through a stale profile if the active
        // content ID changes between framework refreshes. The next 250 ms
        // framework check loads the new profile; the chat callback stays free
        // of secret decryption and configuration I/O.
        if (characterContext.Current?.CharacterKey != activeIdentity?.CharacterKey)
            return;

        if (!RelayFilter.ShouldForward(activeProfile, chat.ChatType) || activeWebhookEndpoint is null || activeIdentity is null)
            return;
        if (duplicateFilter.IsDuplicate(chat, DateTime.UtcNow))
            return;

        var keywordMatches = KeywordMatcher.FindMatches(
            activeProfile!.Keywords,
            chat.ChatType,
            chat.Message);
        var payloads = WebhookMessageFormatter.Format(
            chat,
            activeProfile.IncludeSenderWorld,
            activeProfile.UseDiscordEmbeds,
            keywordMatches,
            activeProfile.DiscordMentionUserId);
        webhookRelay.TryEnqueue(activeIdentity.CharacterKey, activeWebhookEndpoint, payloads);
    }

    private void OnDeliveryCompleted(WebhookDeliveryResult result)
    {
        if (Configuration.CharacterProfiles.TryGetValue(result.CharacterKey, out var profile) && result.Success)
        {
            profile.LastWebhookSuccessUtc = result.CompletedAtUtc;
            SaveConfiguration();
        }

        if (!result.IsTest || result.CharacterKey != activeIdentity?.CharacterKey)
            return;
        if (result.Success)
            ChatGui.Print("[Sentinel Relay] Discord webhook test succeeded.");
        else
            ChatGui.PrintError($"[Sentinel Relay] Discord webhook test failed: {result.Error ?? "unknown error"}");
    }

    private void SetPaused(bool paused)
    {
        if (activeProfile is null)
            return;
        activeProfile.Paused = paused;
        if (paused)
            webhookRelay.ClearQueue();
        SaveConfiguration();
    }

    private void OnCommand(string _, string arguments)
    {
        var option = arguments.Trim().ToLowerInvariant();
        switch (option)
        {
            case "":
                mainWindow.IsOpen = !mainWindow.IsOpen;
                break;
            case "status":
                PrintStatus();
                break;
            case "pause":
                if (activeProfile is null)
                {
                    ChatGui.PrintError("[Sentinel Relay] Log into a character before changing relay state.");
                    break;
                }
                SetPaused(true);
                ChatGui.Print("[Sentinel Relay] Relay paused. No enabled FFXIV chat will be sent to Discord.");
                break;
            case "resume":
                if (activeProfile is null)
                {
                    ChatGui.PrintError("[Sentinel Relay] Log into a character before changing relay state.");
                    break;
                }
                SetPaused(false);
                ChatGui.Print("[Sentinel Relay] Relay resumed.");
                break;
            case "debug":
                ChatGui.Print($"[Sentinel Relay] version={PluginVersion}, state={webhookRelay.State}, queue={webhookRelay.QueueLength}, "
                    + $"dropped={webhookRelay.DroppedCount}, character={activeIdentity?.CharacterName ?? "none"}, "
                    + $"webhook={(activeWebhookEndpoint is null ? "not configured" : "configured")}, "
                    + $"lastSuccess={webhookRelay.LastSuccessUtc?.ToLocalTime().ToString("G") ?? "never"}, "
                    + $"lastError={webhookRelay.LastError ?? "none"}");
                break;
            default:
                ChatGui.PrintError("[Sentinel Relay] Use /srelay, status, pause, resume, or debug.");
                break;
        }
    }

    private void PrintStatus()
    {
        var enabled = activeProfile is null
            ? "none"
            : string.Join(", ", activeProfile.EnabledInboundChannels.Select(ChannelPolicy.GetShortLabel));
        var relayState = activeProfile is null
            ? "inactive"
            : activeProfile.Paused
                ? "paused"
                : activeWebhookEndpoint is null ? "not configured" : "active";
        ChatGui.Print($"[Sentinel Relay] Character: {activeIdentity?.CharacterName ?? "not logged in"}; "
            + $"Discord webhook: {(activeWebhookEndpoint is null ? "not configured" : "configured")}; "
            + $"relay: {relayState}; "
            + $"enabled chats: {(enabled.Length == 0 ? "none" : enabled)}; queue: {webhookRelay.QueueLength}.");
    }

    private void OpenMainWindow() => mainWindow.IsOpen = true;
}
