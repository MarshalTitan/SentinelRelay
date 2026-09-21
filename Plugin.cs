using System.Collections.Concurrent;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using SentinelRelay.Core;
using SentinelRelay.Models;
using SentinelRelay.Protocol;
using SentinelRelay.Services;
using SentinelRelay.UI;

namespace SentinelRelay;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/srelay";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private readonly WindowSystem windows = new("SentinelRelay");
    private readonly ConcurrentQueue<Action> mainThreadActions = new();
    private readonly CredentialProtector credentialProtector = new();
    private readonly CharacterContextService characterContext;
    private readonly RelayClient relayClient;
    private readonly OutboundCoordinator outboundCoordinator;
    private readonly ChatCaptureService chatCapture;
    private readonly MainWindow mainWindow;
    private CharacterIdentity? activeIdentity;
    private CharacterProfile? activeProfile;
    private DateTime nextCharacterCheckUtc = DateTime.MinValue;
    private bool disposed;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        characterContext = new CharacterContextService(PlayerState);
        relayClient = new RelayClient(Log);
        outboundCoordinator = new OutboundCoordinator(new GameChatSender(), AcknowledgeOutbound);
        chatCapture = new ChatCaptureService(ChatGui, Log, OnChatCaptured);
        mainWindow = new MainWindow(
            Configuration,
            () => activeIdentity,
            () => activeProfile,
            relayClient,
            SaveConfiguration,
            ReconnectActiveProfile,
            RequestPairing,
            SetPaused,
            RequestUnlink,
            () => outboundCoordinator.QueueLength,
            () => outboundCoordinator.PendingEventId);

        relayClient.OutboundReceived += payload => mainThreadActions.Enqueue(
            () => outboundCoordinator.Enqueue(payload, DateTime.UtcNow));
        relayClient.PairCompleted += (token, _, _) => mainThreadActions.Enqueue(() => PersistClientToken(token));
        relayClient.LinkRevoked += () => mainThreadActions.Enqueue(ClearClientToken);

        windows.AddWindow(mainWindow);
        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Sentinel Relay. Options: status, link, pause, resume, unlink, debug",
        });
        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi += OpenMainWindow;
        PluginInterface.UiBuilder.OpenConfigUi += OpenMainWindow;
        Framework.Update += OnFrameworkUpdate;
        RefreshCharacter(force: true);
        Log.Information("Sentinel Relay {Version} loaded with privacy-first chat filters.",
            GetType().Assembly.GetName().Version?.ToString() ?? "unknown");
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
        outboundCoordinator.Clear("Sentinel Relay is unloading.");
        relayClient.Dispose();
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
                Log.Warning(ex, "Sentinel Relay main-thread action failed.");
            }
        }

        var now = DateTime.UtcNow;
        if (now >= nextCharacterCheckUtc)
        {
            nextCharacterCheckUtc = now.AddSeconds(2);
            RefreshCharacter(force: false);
        }
        outboundCoordinator.Update(now);
    }

    private void RefreshCharacter(bool force)
    {
        var identity = characterContext.Current;
        if (!force && identity?.CharacterKey == activeIdentity?.CharacterKey)
            return;

        outboundCoordinator.Clear("The active FFXIV character changed.");
        activeIdentity = identity;
        activeProfile = null;
        relayClient.Disconnect();
        if (identity is null)
            return;

        activeProfile = Configuration.GetOrCreateProfile(
            identity.CharacterKey,
            identity.CharacterName,
            identity.HomeWorld);
        SaveConfiguration();
        ReconnectActiveProfile();
    }

    private void ReconnectActiveProfile()
    {
        if (activeIdentity is null || activeProfile is null)
        {
            relayClient.Disconnect();
            return;
        }

        var token = credentialProtector.Unprotect(activeProfile.ProtectedClientToken);
        relayClient.Activate(Configuration.ServiceWebSocketUrl, activeProfile, activeIdentity, token);
    }

    private void OnChatCaptured(CapturedChat chat)
    {
        if (outboundCoordinator.TryConfirmAndSuppress(chat, activeIdentity))
            return;
        if (activeProfile is null || activeProfile.Paused)
            return;
        if (!activeProfile.EnabledInboundChannels.Contains(chat.ChatType))
            return;

        var matches = KeywordMatcher.FindMatches(activeProfile.Keywords, chat.ChatType, chat.Message)
            .Select(rule => new KeywordMatchPayload(rule.Keyword, rule.AlertMethod.ToString()))
            .ToArray();
        var payload = new InboundChatPayload(
            Guid.NewGuid().ToString("N"),
            chat.ChatType.ToString(),
            ChannelPolicy.GetShortLabel(chat.ChatType),
            chat.Sender,
            chat.SenderWorld,
            chat.Message,
            chat.TimestampUtc,
            matches);
        relayClient.SendInbound(payload);
    }

    private void AcknowledgeOutbound(string eventId, bool delivered, string? error)
    {
        relayClient.SendOutboundAck(eventId, delivered, error);
        if (!delivered && !string.IsNullOrWhiteSpace(error))
            ChatGui.PrintError($"[Sentinel Relay] Discord message was not sent: {error}");
    }

    private void PersistClientToken(string token)
    {
        if (activeProfile is null)
            return;
        try
        {
            activeProfile.ProtectedClientToken = credentialProtector.Protect(token);
            SaveConfiguration();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Sentinel Relay could not protect the new client credential.");
            ChatGui.PrintError("[Sentinel Relay] Linking succeeded, but the local credential could not be protected. Relink after restarting.");
        }
    }

    private void ClearClientToken()
    {
        if (activeProfile is null)
            return;
        activeProfile.ProtectedClientToken = string.Empty;
        SaveConfiguration();
    }

    private bool RequestPairing()
    {
        if (activeProfile is null)
            return false;
        return relayClient.RequestPairing();
    }

    private void SetPaused(bool paused)
    {
        if (activeProfile is null)
            return;
        activeProfile.Paused = paused;
        SaveConfiguration();
        relayClient.SetPaused(paused);
        if (paused)
            outboundCoordinator.Clear("Relay was paused locally.");
    }

    private void RequestUnlink()
    {
        if (!relayClient.Unlink())
            ChatGui.PrintError("[Sentinel Relay] Unlink could not be sent while the relay is offline.");
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
            case "link":
                if (!RequestPairing())
                    ChatGui.PrintError("[Sentinel Relay] Connect to the service first, then try /srelay link again.");
                else
                    ChatGui.Print("[Sentinel Relay] Pairing code requested. Open /srelay to view it.");
                mainWindow.IsOpen = true;
                break;
            case "pause":
                SetPaused(true);
                ChatGui.Print("[Sentinel Relay] Relay paused. No chat will enter or leave FFXIV.");
                break;
            case "resume":
                SetPaused(false);
                ChatGui.Print("[Sentinel Relay] Relay resumed.");
                break;
            case "unlink":
                RequestUnlink();
                break;
            case "debug":
                ChatGui.Print($"[Sentinel Relay] state={relayClient.State}, reconnects={relayClient.ReconnectCount}, "
                    + $"queue={outboundCoordinator.QueueLength}, character={activeIdentity?.CharacterName ?? "none"}, "
                    + $"backend={relayClient.BackendVersion ?? "unknown"}");
                break;
            default:
                ChatGui.PrintError("[Sentinel Relay] Use /srelay, status, link, pause, resume, unlink, or debug.");
                break;
        }
    }

    private void PrintStatus()
    {
        ChatGui.Print($"[Sentinel Relay] Character: {activeIdentity?.CharacterName ?? "not logged in"}; "
            + $"service: {relayClient.State}; relay: {(activeProfile?.Paused == true ? "paused" : "running")}; "
            + $"Discord: {relayClient.DiscordUsername ?? "not linked"}; destination: {relayClient.RelayChannelName ?? "not configured"}.");
    }

    private void OpenMainWindow() => mainWindow.IsOpen = true;
}

