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

public sealed record DiscordReplyConfigurationInput(
    bool Enabled,
    string BotToken,
    string ChannelId,
    string AuthorizedUserId,
    IReadOnlySet<RelayChatType> EnabledOutboundChannels);

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
    private readonly DiscordReplyReader discordReplyReader;
    private readonly GameChatSender gameChatSender = new();
    private readonly BoundedOrderedQueue<DiscordReplyCommand> outboundQueue = new(5);
    private readonly SlidingWindowRateLimiter outboundRateLimiter = new(5, TimeSpan.FromSeconds(30));
    private readonly DuplicateMessageFilter duplicateFilter = new();
    private readonly ChatCaptureService chatCapture;
    private readonly MainWindow mainWindow;
    private CharacterIdentity? activeIdentity;
    private CharacterProfile? activeProfile;
    private Uri? activeWebhookEndpoint;
    private DateTime nextCharacterCheckUtc = DateTime.MinValue;
    private DateTime nextOutboundSendUtc = DateTime.MinValue;
    private int droppedOutboundCount;
    private string? lastOutboundError;
    private DateTime? lastOutboundSubmitUtc;
    private DateTime? lastIncomingTellUtc;
    private bool disposed;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        characterContext = new CharacterContextService(PlayerState);
        webhookRelay = new WebhookRelayClient(Log);
        webhookRelay.DeliveryCompleted += result => mainThreadActions.Enqueue(() => OnDeliveryCompleted(result));
        discordReplyReader = new DiscordReplyReader(Log);
        discordReplyReader.CheckpointEstablished += (characterKey, checkpoint, timestamp) =>
            mainThreadActions.Enqueue(() => OnDiscordCheckpointEstablished(characterKey, checkpoint, timestamp));
        discordReplyReader.MessagesReceived += batch =>
            mainThreadActions.Enqueue(() => OnDiscordMessagesReceived(batch));
        chatCapture = new ChatCaptureService(ChatGui, Log, OnChatCaptured);
        mainWindow = new MainWindow(
            () => activeIdentity,
            () => activeProfile,
            () => activeWebhookEndpoint,
            webhookRelay,
            SaveWebhook,
            RemoveWebhook,
            TestWebhook,
            discordReplyReader,
            SaveDiscordReplySettings,
            RemoveDiscordBotCredential,
            TestDiscordReader,
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
        discordReplyReader.Dispose();
        outboundQueue.Clear();
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

        ProcessOutboundQueue(now);
    }

    private void RefreshCharacter(bool force)
    {
        var identity = characterContext.Current;
        if (!force && identity?.CharacterKey == activeIdentity?.CharacterKey)
            return;

        webhookRelay.ClearQueue();
        discordReplyReader.Stop();
        outboundQueue.Clear();
        outboundRateLimiter.Clear();
        lastOutboundError = null;
        lastIncomingTellUtc = null;
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
        RefreshDiscordReplyReader();
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

    private void RefreshDiscordReplyReader()
    {
        discordReplyReader.Stop();
        if (activeProfile is null
            || activeIdentity is null
            || activeProfile.Paused
            || !activeProfile.DiscordRepliesEnabled
            || !activeProfile.EnabledOutboundChannels.Overlaps(ChannelPolicy.ImplementedOutboundChannels))
            return;

        var token = secretProtector.UnprotectDiscordBotToken(activeProfile.ProtectedDiscordBotToken);
        if (!IsPlausibleBotToken(token)
            || !DiscordSnowflake.IsValid(activeProfile.DiscordRelayChannelId)
            || !DiscordSnowflake.IsValid(activeProfile.AuthorizedDiscordUserId))
            return;

        discordReplyReader.Start(new DiscordReaderConfiguration(
            activeIdentity.CharacterKey,
            token!,
            activeProfile.DiscordRelayChannelId,
            activeProfile.LastProcessedDiscordMessageId));
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

    private WebhookConfigurationResult SaveDiscordReplySettings(DiscordReplyConfigurationInput input)
    {
        if (activeProfile is null || activeIdentity is null)
            return new WebhookConfigurationResult(false, "Log into a character before configuring Discord replies.");

        var channelId = input.ChannelId.Trim();
        var userId = input.AuthorizedUserId.Trim();
        if (!DiscordSnowflake.IsValid(channelId))
            return new WebhookConfigurationResult(false, "Relay Channel ID must be a 17–20 digit Discord ID.");
        if (!DiscordSnowflake.IsValid(userId))
            return new WebhookConfigurationResult(false, "Authorized Discord User ID must be a 17–20 digit Discord ID.");

        var newToken = input.BotToken.Trim();
        if (newToken.Length > 0 && !IsPlausibleBotToken(newToken))
            return new WebhookConfigurationResult(false, "The Discord bot credential is not valid.");
        if (newToken.Length == 0
            && string.IsNullOrWhiteSpace(activeProfile.ProtectedDiscordBotToken))
            return new WebhookConfigurationResult(false, "Paste the Discord bot token before saving.");
        if (input.EnabledOutboundChannels.Any(channel => !ChannelPolicy.ImplementedOutboundChannels.Contains(channel)))
            return new WebhookConfigurationResult(false, "The outbound reply list contains an unsupported destination.");
        if (input.Enabled && input.EnabledOutboundChannels.Count == 0)
            return new WebhookConfigurationResult(false, "Enable at least one outbound chat destination before enabling Discord replies.");

        try
        {
            if (newToken.Length > 0)
                activeProfile.ProtectedDiscordBotToken = secretProtector.ProtectDiscordBotToken(newToken);
            activeProfile.DiscordRelayChannelId = channelId;
            activeProfile.AuthorizedDiscordUserId = userId;
            activeProfile.DiscordRepliesEnabled = input.Enabled;
            activeProfile.EnabledOutboundChannels = [.. input.EnabledOutboundChannels];
            SaveConfiguration();
            RefreshDiscordReplyReader();
            return new WebhookConfigurationResult(true, null);
        }
        catch (Exception ex)
        {
            Log.Error("Sentinel Relay could not protect the Discord bot credential ({ExceptionType}).", ex.GetType().Name);
            return new WebhookConfigurationResult(false, "Windows could not securely protect the Discord bot credential.");
        }
    }

    private void RemoveDiscordBotCredential()
    {
        if (activeProfile is null)
            return;
        activeProfile.ProtectedDiscordBotToken = string.Empty;
        activeProfile.DiscordRepliesEnabled = false;
        activeProfile.LastProcessedDiscordMessageId = string.Empty;
        activeProfile.LastDiscordReaderSuccessUtc = null;
        discordReplyReader.Stop();
        outboundQueue.Clear();
        SaveConfiguration();
    }

    private bool TestDiscordReader()
    {
        if (activeProfile is null || activeIdentity is null)
            return false;
        var token = secretProtector.UnprotectDiscordBotToken(activeProfile.ProtectedDiscordBotToken);
        if (!IsPlausibleBotToken(token)
            || !DiscordSnowflake.IsValid(activeProfile.DiscordRelayChannelId))
            return false;

        var configuration = new DiscordReaderConfiguration(
            activeIdentity.CharacterKey,
            token!,
            activeProfile.DiscordRelayChannelId,
            activeProfile.LastProcessedDiscordMessageId);
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await discordReplyReader.TestAsync(configuration, CancellationToken.None).ConfigureAwait(false);
                mainThreadActions.Enqueue(() => OnDiscordReaderTestCompleted(result));
            }
            catch (Exception ex)
            {
                Log.Warning("Sentinel Relay Discord reader test failed ({ExceptionType}).", ex.GetType().Name);
                mainThreadActions.Enqueue(() => ChatGui.PrintError("[Sentinel Relay] Discord reader test failed."));
            }
        });
        return true;
    }

    private void OnChatCaptured(CapturedChat chat)
    {
        // Drop rather than risk routing through a stale profile if the active
        // content ID changes between framework refreshes. The next 250 ms
        // framework check loads the new profile; the chat callback stays free
        // of secret decryption and configuration I/O.
        if (characterContext.Current?.CharacterKey != activeIdentity?.CharacterKey)
            return;

        // /r is deliberately available only after this active character has
        // received a Tell during the current session. The target remains under
        // FFXIV's own /reply semantics and expires locally after 30 minutes.
        if (chat.ChatType == RelayChatType.IncomingTell)
            lastIncomingTellUtc = DateTime.UtcNow;

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

    private void OnDiscordCheckpointEstablished(string characterKey, string checkpoint, DateTime timestamp)
    {
        if (!Configuration.CharacterProfiles.TryGetValue(characterKey, out var profile)
            || DiscordSnowflake.Compare(checkpoint, profile.LastProcessedDiscordMessageId) <= 0)
            return;
        profile.LastProcessedDiscordMessageId = checkpoint;
        profile.LastDiscordReaderSuccessUtc = timestamp;
        SaveConfiguration();
    }

    private void OnDiscordMessagesReceived(DiscordMessageBatch batch)
    {
        if (!Configuration.CharacterProfiles.TryGetValue(batch.CharacterKey, out var profile))
            return;

        // Commit the high-water mark before queueing any game action. A process
        // interruption can drop a command, but can never replay it after restart.
        if (DiscordSnowflake.Compare(batch.NewCheckpoint, profile.LastProcessedDiscordMessageId) > 0)
            profile.LastProcessedDiscordMessageId = batch.NewCheckpoint;
        profile.LastDiscordReaderSuccessUtc = DateTime.UtcNow;
        SaveConfiguration();

        if (activeIdentity?.CharacterKey != batch.CharacterKey || activeProfile != profile)
            return;

        foreach (var source in batch.Messages)
        {
            if (!DiscordReplyPolicy.TryAuthorize(
                    profile,
                    activeIdentity,
                    source,
                    batch.CheckpointBeforeBatch,
                    DateTime.UtcNow,
                    lastIncomingTellUtc,
                    out var command,
                    out var rejection)
                || command is null)
            {
                if (rejection == DiscordReplyRejection.NoRecentTellTarget)
                    lastOutboundError = "Ignored /r: no incoming Tell has been seen for this character in the last 30 minutes.";
                continue;
            }

            if (!outboundRateLimiter.TryAcquire(DateTime.UtcNow, out var retryAfter))
            {
                lastOutboundError = $"Local Discord reply rate limit reached; retry in {Math.Ceiling(retryAfter.TotalSeconds)} seconds.";
                droppedOutboundCount++;
                continue;
            }

            if (!outboundQueue.TryEnqueue(command))
            {
                lastOutboundError = "The Discord reply queue is full; newest command was dropped.";
                droppedOutboundCount++;
            }
        }
    }

    private void ProcessOutboundQueue(DateTime utcNow)
    {
        if (utcNow < nextOutboundSendUtc || !outboundQueue.TryDequeue(out var command))
            return;
        nextOutboundSendUtc = utcNow.AddMilliseconds(1500);

        if (activeIdentity?.CharacterKey != command.CharacterKey
            || activeProfile?.CharacterKey != command.CharacterKey
            || activeProfile.Paused
            || !activeProfile.DiscordRepliesEnabled
            || !activeProfile.EnabledOutboundChannels.Contains(command.Destination))
            return;
        if (command.Destination == RelayChatType.IncomingTell
            && !DiscordReplyPolicy.HasRecentTellTarget(lastIncomingTellUtc, utcNow))
        {
            lastOutboundError = "The recent Tell reply target expired before the message could be sent.";
            droppedOutboundCount++;
            return;
        }

        try
        {
            gameChatSender.Send(command.Destination, command.Message);
            lastOutboundSubmitUtc = utcNow;
            lastOutboundError = null;
        }
        catch (Exception ex)
        {
            lastOutboundError = "FFXIV rejected the experimental chat submission.";
            Log.Warning("Sentinel Relay FFXIV chat submission failed ({ExceptionType}).", ex.GetType().Name);
            ChatGui.PrintError("[Sentinel Relay] Discord reply could not be submitted to FFXIV.");
        }
    }

    private void OnDiscordReaderTestCompleted(DiscordReaderTestResult result)
    {
        if (!Configuration.CharacterProfiles.TryGetValue(result.CharacterKey, out var profile))
            return;
        if (result.Success)
        {
            if (result.LatestMessageId is not null
                && DiscordSnowflake.Compare(result.LatestMessageId, profile.LastProcessedDiscordMessageId) > 0)
                profile.LastProcessedDiscordMessageId = result.LatestMessageId;
            profile.LastDiscordReaderSuccessUtc = DateTime.UtcNow;
            SaveConfiguration();
            ChatGui.Print("[Sentinel Relay] Discord reader test succeeded. Channel access and history permission are available.");
        }
        else
        {
            ChatGui.PrintError($"[Sentinel Relay] Discord reader test failed: {result.Error ?? "unknown error"}");
        }
    }

    private void SetPaused(bool paused)
    {
        if (activeProfile is null)
            return;
        activeProfile.Paused = paused;
        if (paused)
        {
            webhookRelay.ClearQueue();
            discordReplyReader.Stop();
            outboundQueue.Clear();
        }
        SaveConfiguration();
        if (!paused)
            RefreshDiscordReplyReader();
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
                ChatGui.Print("[Sentinel Relay] Relay paused. Webhook delivery and Discord replies are stopped.");
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
                    + $"replyReader={discordReplyReader.State}, replyQueue={outboundQueue.Count}, replyDropped={droppedOutboundCount}, "
                    + $"recentTellTarget={(DiscordReplyPolicy.HasRecentTellTarget(lastIncomingTellUtc, DateTime.UtcNow) ? "available" : "none")}, "
                    + $"lastReplySubmit={lastOutboundSubmitUtc?.ToLocalTime().ToString("G") ?? "never"}, "
                    + $"lastSuccess={webhookRelay.LastSuccessUtc?.ToLocalTime().ToString("G") ?? "never"}, "
                    + $"lastError={webhookRelay.LastError ?? "none"}, replyError={lastOutboundError ?? discordReplyReader.LastError ?? "none"}");
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
        var replyState = activeProfile?.DiscordRepliesEnabled == true
            ? discordReplyReader.State.ToString()
            : "disabled";
        ChatGui.Print($"[Sentinel Relay] Character: {activeIdentity?.CharacterName ?? "not logged in"}; "
            + $"Discord webhook: {(activeWebhookEndpoint is null ? "not configured" : "configured")}; "
            + $"relay: {relayState}; "
            + $"enabled chats: {(enabled.Length == 0 ? "none" : enabled)}; queue: {webhookRelay.QueueLength}; "
            + $"Discord replies: {replyState}.");
    }

    private static bool IsPlausibleBotToken(string? value) =>
        value is { Length: >= 30 }
        && !value.Any(char.IsWhiteSpace);

    private void OpenMainWindow() => mainWindow.IsOpen = true;
}
