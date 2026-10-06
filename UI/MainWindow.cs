using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using SentinelCore.UI;
using SentinelRelay.Core;
using SentinelRelay.Models;
using SentinelRelay.Services;

namespace SentinelRelay.UI;

public sealed class MainWindow : Window, IDisposable
{
    private enum ConfigurationPage
    {
        General,
        ChatFilters,
        Keywords,
        DiscordWebhook,
        DiscordReplies,
        Theme,
        Debug,
    }

    private static readonly string[] ConfigurationThemes = ["Classic", "Sentinel Modern"];
    private static readonly SentinelModernNavItem[] ModernPrimaryNavigation =
    [
        new(nameof(ConfigurationPage.General), null, "Connection and general status")
        {
            DrawIcon = static context => DrawModernNavigationIcon(FontAwesomeIcon.Link, context),
        },
        new(nameof(ConfigurationPage.ChatFilters), null, "Chat filters")
        {
            DrawIcon = static context => DrawModernNavigationIcon(FontAwesomeIcon.Filter, context),
        },
        new(nameof(ConfigurationPage.Keywords), null, "Keywords")
        {
            DrawIcon = static context => DrawModernNavigationIcon(FontAwesomeIcon.Key, context),
        },
        new(nameof(ConfigurationPage.DiscordWebhook), null, "Discord webhook")
        {
            DrawIcon = static context => DrawModernNavigationIcon(FontAwesomeIcon.CloudUploadAlt, context),
        },
        new(nameof(ConfigurationPage.DiscordReplies), null, "Replies and controls")
        {
            DrawIcon = static context => DrawModernNavigationIcon(FontAwesomeIcon.ExchangeAlt, context),
        },
        new(nameof(ConfigurationPage.Theme), null, "Theme")
        {
            DrawIcon = static context => DrawModernNavigationIcon(FontAwesomeIcon.Palette, context),
        },
        new(nameof(ConfigurationPage.Debug), null, "Diagnostics")
        {
            DrawIcon = static context => DrawModernNavigationIcon(FontAwesomeIcon.Bug, context),
        },
    ];
    private static readonly Vector2 ClassicMinimumWindowSize = new(620f, 520f);
    private static readonly Vector2 ClassicMaximumWindowSize = new(1100f, 900f);
    private static readonly RelayChatType[] PublicChannels =
    [
        RelayChatType.Say,
        RelayChatType.Yell,
        RelayChatType.Shout,
        RelayChatType.FreeCompany,
        RelayChatType.StandardEmote,
        RelayChatType.CustomEmote,
    ];

    private static readonly RelayChatType[] PrivateChannels =
    [
        RelayChatType.Party,
        RelayChatType.CrossWorldParty,
        RelayChatType.Alliance,
        RelayChatType.PvPTeam,
        RelayChatType.IncomingTell,
        RelayChatType.OutgoingTell,
        RelayChatType.NoviceNetwork,
    ];

    private static readonly RelayChatType[] LinkshellChannels =
    [
        RelayChatType.Linkshell1,
        RelayChatType.Linkshell2,
        RelayChatType.Linkshell3,
        RelayChatType.Linkshell4,
        RelayChatType.Linkshell5,
        RelayChatType.Linkshell6,
        RelayChatType.Linkshell7,
        RelayChatType.Linkshell8,
        RelayChatType.CrossWorldLinkshell1,
        RelayChatType.CrossWorldLinkshell2,
        RelayChatType.CrossWorldLinkshell3,
        RelayChatType.CrossWorldLinkshell4,
        RelayChatType.CrossWorldLinkshell5,
        RelayChatType.CrossWorldLinkshell6,
        RelayChatType.CrossWorldLinkshell7,
        RelayChatType.CrossWorldLinkshell8,
    ];

    private static readonly RelayChatType[] SystemInboundChannels =
    [
        RelayChatType.RewardsHuntResults,
    ];

    private static readonly (RelayChatType Channel, string Command, string Label)[] CommonReplyChannels =
    [
        (RelayChatType.Say, "/say or /s", "Say"),
        (RelayChatType.Yell, "/yell or /y", "Yell"),
        (RelayChatType.Shout, "/shout or /sh", "Shout"),
        (RelayChatType.FreeCompany, "/fc", "Free Company"),
    ];

    private static readonly (RelayChatType Channel, string Command, string Label)[] GroupReplyChannels =
    [
        (RelayChatType.Party, "/party or /p", "Party / Cross-world Party"),
        (RelayChatType.Alliance, "/alliance or /a", "Alliance"),
        (RelayChatType.PvPTeam, "/pvpteam", "PvP Team"),
        (RelayChatType.NoviceNetwork, "/novice or /n", "Novice Network"),
    ];

    private static readonly (RelayChatType Channel, string Command, string Label)[] LinkshellReplyChannels =
    [
        (RelayChatType.Linkshell1, "/ls1", "Linkshell 1"),
        (RelayChatType.Linkshell2, "/ls2", "Linkshell 2"),
        (RelayChatType.Linkshell3, "/ls3", "Linkshell 3"),
        (RelayChatType.Linkshell4, "/ls4", "Linkshell 4"),
        (RelayChatType.Linkshell5, "/ls5", "Linkshell 5"),
        (RelayChatType.Linkshell6, "/ls6", "Linkshell 6"),
        (RelayChatType.Linkshell7, "/ls7", "Linkshell 7"),
        (RelayChatType.Linkshell8, "/ls8", "Linkshell 8"),
        (RelayChatType.CrossWorldLinkshell1, "/cwls1", "Cross-world Linkshell 1"),
        (RelayChatType.CrossWorldLinkshell2, "/cwls2", "Cross-world Linkshell 2"),
        (RelayChatType.CrossWorldLinkshell3, "/cwls3", "Cross-world Linkshell 3"),
        (RelayChatType.CrossWorldLinkshell4, "/cwls4", "Cross-world Linkshell 4"),
        (RelayChatType.CrossWorldLinkshell5, "/cwls5", "Cross-world Linkshell 5"),
        (RelayChatType.CrossWorldLinkshell6, "/cwls6", "Cross-world Linkshell 6"),
        (RelayChatType.CrossWorldLinkshell7, "/cwls7", "Cross-world Linkshell 7"),
        (RelayChatType.CrossWorldLinkshell8, "/cwls8", "Cross-world Linkshell 8"),
    ];

    private readonly Func<CharacterIdentity?> getIdentity;
    private readonly Func<CharacterProfile?> getProfile;
    private readonly Func<Uri?> getWebhookEndpoint;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly WebhookRelayClient relay;
    private readonly ChatCaptureService chatCapture;
    private readonly DiscordReplyReader replyReader;
    private readonly RemoteScreenshotService remoteScreenshotService;
    private readonly Func<string, WebhookConfigurationResult> saveWebhook;
    private readonly Action removeWebhook;
    private readonly Func<bool> testWebhook;
    private readonly Func<DiscordReplyConfigurationInput, WebhookConfigurationResult> saveReplySettings;
    private readonly Action removeDiscordBotCredential;
    private readonly Func<bool> testDiscordReader;
    private readonly Action<bool> setPaused;
    private readonly Func<int> getConfigurationTheme;
    private readonly Action<int> setConfigurationTheme;
    private readonly Configuration configuration;
    private readonly Action save;
    private readonly SentinelModernStyleScope modernStyle = new();
    private readonly SentinelModernAppShellState modernShellState = new();
    private readonly SentinelThemeState<ConfigurationPage> themeState;
    private readonly Action drawModernContent;
    private readonly Action drawModernActionDock;
    private readonly Action<string> selectModernPrimaryPage;
    private readonly Action requestModernCollapse;
    private readonly Action requestModernClose;
    private readonly Action<SentinelModernIconDrawContext> drawModernPluginIcon;
    private readonly Action drawWebhookUrlInput;
    private readonly Action drawKeywordUserIdInput;
    private readonly Action drawBotTokenInput;
    private readonly Action drawReplyChannelIdInput;
    private readonly Action drawAuthorizedUserIdInput;
    private readonly ImGuiWindowFlags classicWindowFlags;
    private string webhookInput = string.Empty;
    private string? webhookFeedback;
    private bool webhookFeedbackIsError;
    private string? keywordFeedback;
    private bool keywordFeedbackIsError;
    private string mentionUserId = string.Empty;
    private string newKeyword = string.Empty;
    private bool newKeywordWholeWord;
    private bool newKeywordPing = true;
    private readonly HashSet<RelayChatType> newKeywordChannels = [];
    private bool keywordChannelsInitialized;
    private string? lastCharacterKey;
    private string botTokenInput = string.Empty;
    private string replyChannelId = string.Empty;
    private string authorizedUserId = string.Empty;
    private bool repliesEnabled;
    private bool remoteScreenshotsEnabled;
    private readonly HashSet<RelayChatType> outboundChannels = [];
    private string? replyFeedback;
    private bool replyFeedbackIsError;
    private bool modernThemeActive;
    private Vector2 modernFrameWindowSize;
    private Vector2? pendingModernSize;
    private CharacterIdentity? drawingIdentity;
    private CharacterProfile? drawingProfile;

    public MainWindow(
        Func<CharacterIdentity?> getIdentity,
        Func<CharacterProfile?> getProfile,
        Func<Uri?> getWebhookEndpoint,
        IDalamudPluginInterface pluginInterface,
        WebhookRelayClient relay,
        ChatCaptureService chatCapture,
        Func<string, WebhookConfigurationResult> saveWebhook,
        Action removeWebhook,
        Func<bool> testWebhook,
        DiscordReplyReader replyReader,
        Func<DiscordReplyConfigurationInput, WebhookConfigurationResult> saveReplySettings,
        Action removeDiscordBotCredential,
        Func<bool> testDiscordReader,
        RemoteScreenshotService remoteScreenshotService,
        Action<bool> setPaused,
        Func<int> getConfigurationTheme,
        Action<int> setConfigurationTheme,
        Configuration configuration,
        Action save)
        : base("Sentinel Relay###SentinelRelayMain")
    {
        this.getIdentity = getIdentity;
        this.getProfile = getProfile;
        this.getWebhookEndpoint = getWebhookEndpoint;
        this.pluginInterface = pluginInterface;
        this.relay = relay;
        this.chatCapture = chatCapture;
        this.replyReader = replyReader;
        this.saveWebhook = saveWebhook;
        this.removeWebhook = removeWebhook;
        this.testWebhook = testWebhook;
        this.saveReplySettings = saveReplySettings;
        this.removeDiscordBotCredential = removeDiscordBotCredential;
        this.testDiscordReader = testDiscordReader;
        this.remoteScreenshotService = remoteScreenshotService;
        this.setPaused = setPaused;
        this.getConfigurationTheme = getConfigurationTheme;
        this.setConfigurationTheme = setConfigurationTheme;
        this.configuration = configuration;
        this.save = save;
        themeState = new SentinelThemeState<ConfigurationPage>(
            ConfigurationPage.General,
            SentinelThemeState<ConfigurationPage>.NormalizeTheme(getConfigurationTheme()));
        drawModernContent = DrawModernContent;
        drawModernActionDock = DrawModernActionDock;
        selectModernPrimaryPage = SelectModernPrimaryPage;
        requestModernCollapse = RequestModernCollapse;
        requestModernClose = RequestModernClose;
        drawModernPluginIcon = DrawModernPluginIcon;
        drawWebhookUrlInput = DrawWebhookUrlInput;
        drawKeywordUserIdInput = DrawKeywordUserIdInput;
        drawBotTokenInput = DrawBotTokenInput;
        drawReplyChannelIdInput = DrawReplyChannelIdInput;
        drawAuthorizedUserIdInput = DrawAuthorizedUserIdInput;
        classicWindowFlags = Flags;
        Size = new Vector2(920, 720);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = ClassicMinimumWindowSize,
            MaximumSize = ClassicMaximumWindowSize,
        };
    }

    public override void PreDraw()
    {
        modernThemeActive = SentinelThemeState<ConfigurationPage>.NormalizeTheme(getConfigurationTheme())
                            == SentinelThemeKind.Modern;

        if (modernThemeActive)
        {
            Flags = SentinelModernWindowChrome.UseCustomHeader(classicWindowFlags);
            modernStyle.PushAppShell(ImGuiHelpers.GlobalScale);
            var headerHeight = SentinelModernAppLayoutOptions.Default.HeaderHeight;
            var shellMinimum = SentinelModernAppLayout.MinimumWindowSize(
                1f,
                hasSecondarySidebar: false,
                hasActionDock: false);
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = configuration.ModernWindowCollapsed
                    ? new Vector2(ClassicMinimumWindowSize.X, headerHeight)
                    : Vector2.Max(shellMinimum, ClassicMinimumWindowSize),
                MaximumSize = new Vector2(
                    1400f,
                    configuration.ModernWindowCollapsed ? headerHeight : 1100f),
            };

            if (pendingModernSize is { } requestedSize)
            {
                Size = requestedSize;
                SizeCondition = ImGuiCond.Always;
                pendingModernSize = null;
            }
            else
            {
                SizeCondition = ImGuiCond.FirstUseEver;
            }
        }
        else
        {
            Flags = classicWindowFlags;
            SizeCondition = ImGuiCond.FirstUseEver;
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = ClassicMinimumWindowSize,
                MaximumSize = ClassicMaximumWindowSize,
            };
        }
    }

    public override void PostDraw() => modernStyle.Pop();

    public void Dispose()
    {
        modernStyle.Dispose();
        modernShellState.Dispose();
    }

    public override void Draw()
    {
        var identity = getIdentity();
        var profile = getProfile();
        drawingIdentity = identity;
        drawingProfile = profile;
        if (identity?.CharacterKey != lastCharacterKey)
        {
            lastCharacterKey = identity?.CharacterKey;
            webhookInput = string.Empty;
            webhookFeedback = null;
            keywordFeedback = null;
            replyFeedback = null;
            mentionUserId = profile?.DiscordMentionUserId ?? string.Empty;
            botTokenInput = string.Empty;
            replyChannelId = profile?.DiscordRelayChannelId ?? string.Empty;
            authorizedUserId = profile?.AuthorizedDiscordUserId ?? string.Empty;
            repliesEnabled = profile?.DiscordRepliesEnabled ?? false;
            remoteScreenshotsEnabled = profile?.RemoteScreenshotsEnabled ?? false;
            outboundChannels.Clear();
            if (profile is not null)
                outboundChannels.UnionWith(profile.EnabledOutboundChannels.Where(ChannelPolicy.ImplementedOutboundChannels.Contains));
            newKeywordChannels.Clear();
            keywordChannelsInitialized = false;
        }

        if (modernThemeActive)
        {
            modernFrameWindowSize = ImGui.GetWindowSize();
            DrawModernShell(identity, profile);
            return;
        }

        DrawClassicShell(identity, profile);
    }

    private void DrawClassicShell(CharacterIdentity? identity, CharacterProfile? profile)
    {
        DrawStatus(identity, profile);
        ImGui.Separator();

        if (!ImGui.BeginTabBar("RelayTabs"))
            return;
        if (ImGui.BeginTabItem("General"))
        {
            DrawGeneral(identity, profile);
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Chat Filters"))
        {
            DrawFilters(profile);
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Keywords"))
        {
            DrawKeywords(identity, profile);
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Discord Webhook"))
        {
            DrawWebhook(identity, profile);
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Discord Replies"))
        {
            DrawDiscordReplies(identity, profile);
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Debug"))
        {
            DrawDebug(identity, profile);
            ImGui.EndTabItem();
        }
        ImGui.EndTabBar();
    }

    private void DrawModernShell(CharacterIdentity? identity, CharacterProfile? profile)
    {
        themeState.SelectTheme(SentinelThemeKind.Modern);
        var direction = profile?.DiscordRepliesEnabled == true ? "FFXIV ↔ Discord" : "FFXIV → Discord";
        var options = new SentinelModernAppShellOptions(
            "SentinelRelay.Modern2",
            "Sentinel Relay",
            themeState.SelectedPage.ToString())
        {
            DrawPluginIcon = drawModernPluginIcon,
            Scale = ImGuiHelpers.GlobalScale,
            ContextLabel = $"{identity?.CharacterName ?? "No character"} · {direction}",
            Status = GetModernStatus(identity, profile),
            DeltaTime = ImGui.GetIO().DeltaTime,
            ReducedMotion = pluginInterface.UiBuilder.ShouldUseReducedMotion,
            AmbientIntensity = 1f,
            SurfaceStyle = SentinelModernAppSurfaceStyle.Unified,
            EnableWindowDragging = true,
            RequestCollapse = requestModernCollapse,
            CollapseTooltip = configuration.ModernWindowCollapsed ? "Expand" : "Minimize",
            RequestClose = requestModernClose,
        };

        SentinelModernAppShell.Draw(
            options,
            modernShellState,
            ModernPrimaryNavigation,
            selectModernPrimaryPage,
            drawModernContent,
            drawActionDock: themeState.SelectedPage == ConfigurationPage.Theme
                            && !configuration.ModernWindowCollapsed
                ? drawModernActionDock
                : null);
    }

    private SentinelModernStatusPillOptions GetModernStatus(
        CharacterIdentity? identity,
        CharacterProfile? profile)
    {
        var status = ModernRelayStatusPolicy.Resolve(new ModernRelayStatusInput(
            HasCharacter: identity is not null && profile is not null,
            Paused: profile?.Paused == true,
            HasWebhook: getWebhookEndpoint() is not null,
            WebhookSending: relay.State == WebhookRelayState.Sending,
            WebhookError: relay.State == WebhookRelayState.Error,
            RepliesEnabled: profile?.DiscordRepliesEnabled == true,
            ReaderInitializing: replyReader.State == DiscordReplyReaderState.Initializing,
            ReaderConnected: replyReader.State == DiscordReplyReaderState.Connected,
            ReaderError: replyReader.State == DiscordReplyReaderState.Error,
            ScreenshotsEnabled: profile?.RemoteScreenshotsEnabled == true,
            ScreenshotError: remoteScreenshotService.State == RemoteScreenshotState.Error));

        return status switch
        {
            ModernRelayStatusKind.Paused => new SentinelModernStatusPillOptions(
                "PAUSED",
                SentinelModernPillTone.Warning)
            {
                Tooltip = "Webhook delivery, Discord replies, and remote screenshots are paused.",
            },
            ModernRelayStatusKind.Error => new SentinelModernStatusPillOptions(
                "ERROR",
                SentinelModernPillTone.Error)
            {
                Tooltip = "Open Diagnostics for the latest privacy-safe error.",
            },
            ModernRelayStatusKind.Delivering => new SentinelModernStatusPillOptions(
                "DELIVERING",
                SentinelModernPillTone.Running)
            {
                Pulse = true,
                Tooltip = "A queued FFXIV message is being delivered to Discord.",
            },
            ModernRelayStatusKind.Linked => new SentinelModernStatusPillOptions(
                "LINKED",
                SentinelModernPillTone.Ready)
            {
                Tooltip = "Webhook delivery and the authorized Discord reply reader are connected.",
            },
            ModernRelayStatusKind.Connecting => new SentinelModernStatusPillOptions(
                "CONNECTING",
                SentinelModernPillTone.Running)
            {
                Pulse = true,
                Tooltip = "The authorized Discord reply reader is establishing its checkpoint.",
            },
            ModernRelayStatusKind.ReaderOffline => new SentinelModernStatusPillOptions(
                "READER OFFLINE",
                SentinelModernPillTone.Warning)
            {
                Tooltip = "Discord replies are enabled, but the reader is not connected.",
            },
            ModernRelayStatusKind.WebhookReady => new SentinelModernStatusPillOptions(
                "WEBHOOK READY",
                SentinelModernPillTone.Ready)
            {
                Tooltip = "FFXIV-to-Discord webhook delivery is configured.",
            },
            ModernRelayStatusKind.NotConfigured => new SentinelModernStatusPillOptions(
                "NOT CONFIGURED",
                SentinelModernPillTone.Accent)
            {
                Tooltip = "Configure this character's Discord webhook to begin relaying.",
            },
            _ => new SentinelModernStatusPillOptions(
                "DISCONNECTED",
                SentinelModernPillTone.Neutral)
            {
                Tooltip = "Log into a character to load its private Relay profile.",
            },
        };
    }

    private void SelectModernPrimaryPage(string id)
    {
        if (Enum.TryParse<ConfigurationPage>(id, out var page))
            themeState.SelectPage(page);
    }

    private void DrawModernContent()
    {
        if (configuration.ModernWindowCollapsed)
            return;

        switch (themeState.SelectedPage)
        {
            case ConfigurationPage.General: DrawGeneral(drawingIdentity, drawingProfile); break;
            case ConfigurationPage.ChatFilters: DrawFilters(drawingProfile); break;
            case ConfigurationPage.Keywords: DrawKeywords(drawingIdentity, drawingProfile); break;
            case ConfigurationPage.DiscordWebhook: DrawWebhook(drawingIdentity, drawingProfile); break;
            case ConfigurationPage.DiscordReplies: DrawDiscordReplies(drawingIdentity, drawingProfile); break;
            case ConfigurationPage.Theme: break;
            case ConfigurationPage.Debug: DrawDebug(drawingIdentity, drawingProfile); break;
        }
    }

    private void DrawModernActionDock()
    {
        SentinelModernActionDock.Status("Sentinel Modern 2 is active");
        ImGui.SameLine();
        if (SentinelModernActionDock.PrimaryButton(
                "SentinelRelay.UseClassic",
                "Use Classic Theme",
                new Vector2(190f * ImGuiHelpers.GlobalScale, 0f),
                ImGuiHelpers.GlobalScale))
            SelectTheme(SentinelThemeKind.Classic);
    }

    private void DrawStatus(CharacterIdentity? identity, CharacterProfile? profile)
    {
        var configured = getWebhookEndpoint() is not null;
        var color = profile?.Paused == true
            ? new Vector4(0.95f, 0.72f, 0.2f, 1f)
            : relay.State == WebhookRelayState.Error
                ? new Vector4(0.95f, 0.35f, 0.38f, 1f)
                : configured
                    ? new Vector4(0.28f, 0.9f, 0.48f, 1f)
                    : new Vector4(0.95f, 0.72f, 0.2f, 1f);
        var status = profile?.Paused == true ? "PAUSED" : configured ? "READY" : "NOT CONFIGURED";
        ImGui.TextColored(color, status);
        ImGui.SameLine();
        var direction = profile?.DiscordRepliesEnabled == true
            ? "FFXIV ↔ Discord"
            : "FFXIV → Discord";
        ImGui.TextUnformatted($" | {identity?.CharacterName ?? "No character"} | {direction}");
    }

    private void DrawGeneral(CharacterIdentity? identity, CharacterProfile? profile)
    {
        if (modernThemeActive)
        {
            using var card = SentinelModernGlassCard.Begin(
                "SentinelRelay.Modern2.GeneralStatus",
                new SentinelModernGlassCardOptions
                {
                    Size = new Vector2(0f, 210f),
                    Accent = SentinelModernPalette.Accent,
                    AccentStrength = 0.12f,
                    Elevated = true,
                },
                ImGuiHelpers.GlobalScale);
            if (card.IsVisible)
                DrawGeneralDetails(identity, profile, includeDescription: false);
            return;
        }

        DrawThemeSelector();
        ImGui.Spacing();
        DrawGeneralDetails(identity, profile, includeDescription: true);
    }

    private void DrawGeneralDetails(
        CharacterIdentity? identity,
        CharacterProfile? profile,
        bool includeDescription)
    {
        ImGui.TextUnformatted($"Character: {identity?.CharacterName ?? "Not logged in"}");
        ImGui.TextUnformatted($"Home world: {identity?.HomeWorld ?? "—"}");
        ImGui.TextUnformatted($"Discord webhook: {WebhookEndpoint.Mask(getWebhookEndpoint())}");
        ImGui.TextUnformatted($"Enabled chats: {profile?.EnabledInboundChannels.Count ?? 0}");
        ImGui.TextUnformatted($"Plugin version: {typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "unknown"}");
        ImGui.Spacing();

        if (profile is null)
        {
            ImGui.TextWrapped("Log into a character before configuring Sentinel Relay.");
            return;
        }

        if (profile.Paused)
        {
            ImGui.TextColored(new Vector4(1f, 0.35f, 0.35f, 1f),
                "RELAY PAUSED — webhook delivery and Discord replies are stopped.");
            if (ImGui.Button("Resume Relay"))
                setPaused(false);
        }
        else if (ImGui.Button("Pause Relay"))
        {
            setPaused(true);
        }

        if (includeDescription)
        {
            ImGui.Spacing();
            ImGui.TextWrapped("Sentinel Relay sends enabled chat directly from this PC to the configured Discord webhook.");
        }
    }

    private void DrawThemeSelector()
    {
        var selectedTheme = (int)SentinelThemeState<ConfigurationPage>.NormalizeTheme(getConfigurationTheme());
        ImGui.SetNextItemWidth(220f * ImGuiHelpers.GlobalScale);
        if (ImGui.Combo("Configuration theme", ref selectedTheme, ConfigurationThemes, ConfigurationThemes.Length))
            SelectTheme((SentinelThemeKind)selectedTheme);
        ImGui.TextDisabled("Existing configurations remain on Classic until Sentinel Modern is selected.");
    }

    private void SelectTheme(SentinelThemeKind theme)
    {
        if (!themeState.SelectTheme(theme))
            return;

        if (modernThemeActive
            && theme == SentinelThemeKind.Classic
            && configuration.ModernWindowCollapsed)
        {
            pendingModernSize = ExpandedModernWindowSize();
            configuration.ModernWindowCollapsed = false;
        }

        setConfigurationTheme((int)theme);
    }

    private void DrawDiscordReplies(CharacterIdentity? identity, CharacterProfile? profile)
    {
        if (profile is null || identity is null)
        {
            ImGui.TextUnformatted("Log into a character to configure Discord replies.");
            return;
        }

        DrawBooleanControl("discord-replies-enabled", "Enable Discord → FFXIV Replies", ref repliesEnabled);
        DrawBooleanControl("remote-screenshots-enabled", "Allow authorized /screenshot window capture", ref remoteScreenshotsEnabled);
        ImGui.TextDisabled("Captures only this FFXIV process's client area into memory, then uploads a bounded PNG through this character's webhook.");
        ImGui.TextDisabled("The game must not be minimized. A 15-second cooldown applies, and every accepted request prints an in-game notice.");
        ImGui.Spacing();
        ImGui.TextUnformatted("Allowed Discord reply destinations");
        ImGui.TextDisabled("These permissions are separate from Chat Filters. Monitoring a channel never automatically permits sending to it.");
        DrawReplyChannelGroup("Common chats", CommonReplyChannels, defaultOpen: true);
        DrawReplyChannelGroup("Group chats", GroupReplyChannels, defaultOpen: true);
        if (OpenSection("Tell reply", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawReplyChannelToggle(RelayChatType.IncomingTell, "/r", "Reply to the latest incoming Tell");
            ImGui.TextDisabled("/r is accepted only for 30 minutes after this character receives a Tell during the current session.");
        }
        DrawReplyChannelGroup("Linkshells and cross-world linkshells", LinkshellReplyChannels, defaultOpen: false);
        ImGui.Separator();
        if (ImGui.Button("Save Reply Permissions"))
            SaveReplyConfiguration("Reply permissions saved.");
        DrawReplyFeedback();
    }

    private void DrawReplyChannelGroup(
        string title,
        IReadOnlyList<(RelayChatType Channel, string Command, string Label)> channels,
        bool defaultOpen)
    {
        var flags = defaultOpen ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;
        if (!OpenSection(title, flags))
            return;
        var columns = channels.Count > 8 ? 2 : 1;
        if (!ImGui.BeginTable($"reply-channels-{title}", columns))
            return;
        foreach (var item in channels)
        {
            ImGui.TableNextColumn();
            DrawReplyChannelToggle(item.Channel, item.Command, item.Label);
        }
        ImGui.EndTable();
    }

    private void DrawReplyChannelToggle(RelayChatType channel, string command, string label)
    {
        var enabled = outboundChannels.Contains(channel);
        if (!DrawBooleanControl($"outbound-{channel}", $"Allow {command} ({label})", ref enabled))
            return;
        if (enabled)
            outboundChannels.Add(channel);
        else
            outboundChannels.Remove(channel);
    }

    private void DrawFilters(CharacterProfile? profile)
    {
        if (profile is null)
        {
            ImGui.TextUnformatted("Log into a character to edit its filters.");
            return;
        }

        DrawChannelGroup("Common and public chats", PublicChannels, profile, defaultOpen: true);
        DrawChannelGroup("Private or group chats", PrivateChannels, profile, defaultOpen: true);
        DrawChannelGroup("System messages (inbound only)", SystemInboundChannels, profile, defaultOpen: true);
        ImGui.TextDisabled("Rewards / Hunt Results forwards only recognized reward lines from narrow system LogKinds. It has no Discord reply command.");
        DrawChannelGroup("Linkshells and cross-world linkshells", LinkshellChannels, profile, defaultOpen: false);
    }

    private void DrawChannelGroup(
        string title,
        IReadOnlyList<RelayChatType> channels,
        CharacterProfile profile,
        bool defaultOpen)
    {
        var flags = defaultOpen ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;
        if (!OpenSection(title, flags))
            return;
        var columns = channels.Count > 8 ? 2 : 1;
        if (!ImGui.BeginTable($"channels-{title}", columns))
            return;
        foreach (var channel in channels)
        {
            ImGui.TableNextColumn();
            var enabled = profile.EnabledInboundChannels.Contains(channel);
            if (!DrawBooleanControl($"inbound-{channel}", ChannelPolicy.GetLabel(channel), ref enabled))
                continue;
            if (enabled)
                profile.EnabledInboundChannels.Add(channel);
            else
                profile.EnabledInboundChannels.Remove(channel);
            save();
        }
        ImGui.EndTable();
    }

    private void DrawWebhook(CharacterIdentity? identity, CharacterProfile? profile)
    {
        if (profile is null || identity is null)
        {
            ImGui.TextUnformatted("Log into a character to configure its Discord webhook.");
            return;
        }

        var endpoint = getWebhookEndpoint();
        ImGui.TextUnformatted($"Status: {WebhookEndpoint.Mask(endpoint)}");
        ImGui.TextUnformatted(profile.LastWebhookSuccessUtc is null
            ? "Verification: not yet confirmed by Discord"
            : $"Verification: Discord accepted a delivery at {FormatTimestamp(profile.LastWebhookSuccessUtc)}");
        if (modernThemeActive)
        {
            SentinelModernSettingsRow.Draw(
                "SentinelRelay.WebhookUrl",
                "Webhook URL",
                "Character-specific Discord destination; the saved secret remains hidden.",
                drawWebhookUrlInput,
                controlWidth: 280f,
                scale: ImGuiHelpers.GlobalScale);
        }
        else
        {
            ImGui.SetNextItemWidth(-1);
            ImGui.InputText("Webhook URL", ref webhookInput, 512, ImGuiInputTextFlags.Password);
        }

        var saveLabel = endpoint is null ? "Save Webhook" : "Replace Webhook";
        if (ImGui.Button(saveLabel))
        {
            var result = saveWebhook(webhookInput);
            webhookFeedback = result.Success
                ? "Webhook saved securely. Use Test Webhook to verify the channel."
                : result.Error;
            webhookFeedbackIsError = !result.Success;
            if (result.Success)
                webhookInput = string.Empty;
        }
        ImGui.SameLine();
        if (endpoint is null)
            ImGui.BeginDisabled();
        if (ImGui.Button("Test Webhook"))
        {
            var queued = testWebhook();
            webhookFeedback = queued ? "Test queued; watch the Discord channel." : "The test could not be queued.";
            webhookFeedbackIsError = !queued;
        }
        ImGui.SameLine();
        if (ImGui.Button("Remove Webhook"))
        {
            removeWebhook();
            webhookInput = string.Empty;
            webhookFeedback = "Webhook removed from this character profile.";
            webhookFeedbackIsError = false;
        }
        if (endpoint is null)
            ImGui.EndDisabled();

        if (!string.IsNullOrWhiteSpace(webhookFeedback))
        {
            var color = webhookFeedbackIsError
                ? new Vector4(0.95f, 0.35f, 0.38f, 1f)
                : new Vector4(0.35f, 0.85f, 1f, 1f);
            ImGui.TextColored(color, webhookFeedback);
        }

        ImGui.Separator();
        DrawKeywordUserConnection(profile);
        ImGui.Separator();
        DrawDiscordReaderConnection(profile);
        ImGui.Separator();
        ImGui.TextUnformatted("Message formatting");
        var includeWorld = profile.IncludeSenderWorld;
        if (DrawBooleanControl("include-sender-world", "Include sender world when available", ref includeWorld))
        {
            profile.IncludeSenderWorld = includeWorld;
            save();
        }
        var embeds = profile.UseDiscordEmbeds;
        if (DrawBooleanControl("compact-discord-embeds", "Use compact Discord embeds", ref embeds))
        {
            profile.UseDiscordEmbeds = embeds;
            save();
        }
        ImGui.TextDisabled("Relayed chat cannot create mentions. Only the explicit keyword-ping feature may mention its configured user ID.");
    }

    private void DrawKeywordUserConnection(CharacterProfile profile)
    {
        if (modernThemeActive)
        {
            SentinelModernSettingsRow.Draw(
                "SentinelRelay.KeywordUserId",
                "Keyword alert Discord user ID",
                "Optional explicit user mention for matched keyword rules.",
                drawKeywordUserIdInput,
                controlWidth: 220f,
                scale: ImGuiHelpers.GlobalScale);
        }
        else
        {
            ImGui.SetNextItemWidth(260);
            ImGui.InputText("Keyword Alert Discord User ID", ref mentionUserId, 24);
            ImGui.SameLine();
        }

        if (ImGui.Button("Save Keyword User ID"))
        {
            var trimmed = mentionUserId.Trim();
            if (trimmed.Length == 0 || WebhookEndpoint.IsValidDiscordUserId(trimmed))
            {
                profile.DiscordMentionUserId = trimmed;
                mentionUserId = trimmed;
                keywordFeedback = trimmed.Length == 0
                    ? "Keyword ping user cleared; highlighting remains available."
                    : "Keyword ping user saved for this character.";
                keywordFeedbackIsError = false;
                save();
            }
            else
            {
                keywordFeedback = "Discord User ID must be a 17–20 digit number.";
                keywordFeedbackIsError = true;
            }
        }
        ImGui.TextDisabled("Discord: User Settings → Advanced → Developer Mode, then right-click your name → Copy User ID.");
        if (!string.IsNullOrWhiteSpace(keywordFeedback))
        {
            var color = keywordFeedbackIsError
                ? new Vector4(0.95f, 0.35f, 0.38f, 1f)
                : new Vector4(0.35f, 0.85f, 1f, 1f);
            ImGui.TextColored(color, keywordFeedback);
        }
    }

    private void DrawDiscordReaderConnection(CharacterProfile profile)
    {
        ImGui.TextUnformatted($"Discord Bot Credential: {(string.IsNullOrWhiteSpace(profile.ProtectedDiscordBotToken) ? "Not Configured" : "Configured (secret hidden)")}");
        if (modernThemeActive)
        {
            var scale = ImGuiHelpers.GlobalScale;
            SentinelModernSettingsRow.Draw(
                "SentinelRelay.BotToken",
                "Discord bot token",
                string.IsNullOrWhiteSpace(profile.ProtectedDiscordBotToken)
                    ? "Paste once; the saved credential is protected and never displayed again."
                    : "Leave blank to retain the protected credential, or paste a replacement.",
                drawBotTokenInput,
                controlWidth: 260f,
                scale: scale);
            SentinelModernSettingsRow.Draw(
                "SentinelRelay.ReplyChannel",
                "Relay channel ID",
                "Only messages from this exact Discord channel are accepted.",
                drawReplyChannelIdInput,
                controlWidth: 220f,
                scale: scale);
            SentinelModernSettingsRow.Draw(
                "SentinelRelay.AuthorizedUser",
                "Authorized Discord user ID",
                "Only this exact Discord user may issue allowed replies or controls.",
                drawAuthorizedUserIdInput,
                controlWidth: 220f,
                scale: scale);
        }
        else
        {
            ImGui.SetNextItemWidth(-1);
            ImGui.InputText("Discord Bot Token", ref botTokenInput, 256, ImGuiInputTextFlags.Password);
            ImGui.TextDisabled(string.IsNullOrWhiteSpace(profile.ProtectedDiscordBotToken)
                ? "Paste the token once. It is never displayed again."
                : "Leave blank to keep the saved credential, or paste a replacement.");
            ImGui.SetNextItemWidth(300);
            ImGui.InputText("Relay Channel ID", ref replyChannelId, 24);
            ImGui.SetNextItemWidth(300);
            ImGui.InputText("Authorized Discord User ID", ref authorizedUserId, 24);
            ImGui.TextDisabled("Discord Developer Mode: right-click the channel/user, then Copy ID.");
        }

        if (ImGui.Button("Save Discord Connection"))
            SaveReplyConfiguration("Discord connection saved. Starting the reader establishes a fresh checkpoint so old messages cannot execute.");
        ImGui.SameLine();
        var hasBotCredential = !string.IsNullOrWhiteSpace(profile.ProtectedDiscordBotToken);
        if (!hasBotCredential)
            ImGui.BeginDisabled();
        if (ImGui.Button("Test Discord Reader"))
        {
            var started = testDiscordReader();
            replyFeedback = started
                ? "Reader test started. The result will appear in FFXIV chat."
                : "Save a valid bot token and channel ID before testing.";
            replyFeedbackIsError = !started;
        }
        ImGui.SameLine();
        if (ImGui.Button("Remove Bot Credential"))
        {
            removeDiscordBotCredential();
            botTokenInput = string.Empty;
            repliesEnabled = false;
            remoteScreenshotsEnabled = false;
            replyFeedback = "Bot credential removed; Discord replies and remote screenshots are disabled for this character.";
            replyFeedbackIsError = false;
        }
        if (!hasBotCredential)
            ImGui.EndDisabled();

        DrawReplyFeedback();
        ImGui.Separator();
        var readerStatus = profile.DiscordRepliesEnabled ? replyReader.State.ToString() : "Disabled";
        ImGui.TextUnformatted($"Reader Status: {readerStatus}");
        ImGui.TextUnformatted($"Last Reader Success: {FormatTimestamp(profile.LastDiscordReaderSuccessUtc)}");
        ImGui.TextUnformatted($"Checkpoint: {(string.IsNullOrWhiteSpace(profile.LastProcessedDiscordMessageId) ? "not established" : "established")}");
        ImGui.TextUnformatted($"Screenshot Status: {(profile.RemoteScreenshotsEnabled ? remoteScreenshotService.State.ToString() : "Disabled")}");
        ImGui.TextUnformatted($"Last Screenshot Upload: {FormatTimestamp(profile.LastRemoteScreenshotSuccessUtc)}");
        ImGui.TextUnformatted($"Last Screenshot Size: {remoteScreenshotService.LastDimensions ?? "never"}");
        if (!string.IsNullOrWhiteSpace(remoteScreenshotService.LastError))
            ImGui.TextColored(new Vector4(0.95f, 0.35f, 0.38f, 1f), $"Screenshot Error: {remoteScreenshotService.LastError}");
        if (!string.IsNullOrWhiteSpace(replyReader.LastError))
            ImGui.TextColored(new Vector4(0.95f, 0.35f, 0.38f, 1f), $"Reader Error: {replyReader.LastError}");
        ImGui.TextDisabled("Sentinel Relay never prints the bot token and never treats Discord text as an arbitrary FFXIV command.");
    }

    private void SaveReplyConfiguration(string successMessage)
    {
        var result = saveReplySettings(new DiscordReplyConfigurationInput(
            repliesEnabled,
            remoteScreenshotsEnabled,
            botTokenInput,
            replyChannelId,
            authorizedUserId,
            new HashSet<RelayChatType>(outboundChannels)));
        replyFeedback = result.Success ? successMessage : result.Error;
        replyFeedbackIsError = !result.Success;
        if (result.Success)
            botTokenInput = string.Empty;
    }

    private void DrawReplyFeedback()
    {
        if (string.IsNullOrWhiteSpace(replyFeedback))
            return;

        var color = replyFeedbackIsError
            ? new Vector4(0.95f, 0.35f, 0.38f, 1f)
            : new Vector4(0.35f, 0.85f, 1f, 1f);
        ImGui.TextColored(color, replyFeedback);
    }

    private void DrawKeywords(CharacterIdentity? identity, CharacterProfile? profile)
    {
        if (profile is null)
        {
            ImGui.TextUnformatted("Log into a character to edit its keyword alerts.");
            return;
        }

        ImGui.SetNextItemWidth(260);
        ImGui.InputTextWithHint("##new-keyword", "Keyword", ref newKeyword, 80);
        ImGui.SameLine();
        ImGui.Checkbox("Whole word", ref newKeywordWholeWord);
        ImGui.SameLine();
        ImGui.Checkbox("Ping configured user", ref newKeywordPing);

        if (!keywordChannelsInitialized)
        {
            newKeywordChannels.UnionWith(profile.EnabledInboundChannels);
            keywordChannelsInitialized = true;
        }
        if (OpenSection("Channels for new keyword", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (ImGui.BeginTable("keyword-channels", 2))
            {
                foreach (var channel in ChannelPolicy.InboundChannels)
                {
                    ImGui.TableNextColumn();
                    var selected = newKeywordChannels.Contains(channel);
                    if (!DrawBooleanControl($"keyword-{channel}", ChannelPolicy.GetLabel(channel), ref selected))
                        continue;
                    if (selected)
                        newKeywordChannels.Add(channel);
                    else
                        newKeywordChannels.Remove(channel);
                }
                ImGui.EndTable();
            }
        }

        var canAdd = !string.IsNullOrWhiteSpace(newKeyword) && newKeywordChannels.Count > 0;
        if (!canAdd)
            ImGui.BeginDisabled();
        if (ImGui.Button("Add Keyword"))
        {
            profile.Keywords.Add(new KeywordRule
            {
                Keyword = newKeyword.Trim(),
                WholeWord = newKeywordWholeWord,
                PingDiscordUser = newKeywordPing,
                Channels = [.. newKeywordChannels],
            });
            newKeyword = string.Empty;
            save();
        }
        if (!canAdd)
            ImGui.EndDisabled();

        if (identity is not null)
        {
            ImGui.SameLine();
            if (ImGui.Button("Use My Character Name"))
                newKeyword = identity.CharacterName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
                    ?? identity.CharacterName;
        }

        ImGui.Separator();
        foreach (var rule in profile.Keywords.ToArray())
        {
            ImGui.PushID(rule.Id.ToString("N"));
            ImGui.TextUnformatted(rule.Keyword);
            ImGui.SameLine();
            ImGui.TextDisabled($"{(rule.WholeWord ? "whole word" : "contains")} | {rule.Channels.Count} channel(s) | {(rule.PingDiscordUser ? "highlight + ping" : "highlight")}");
            ImGui.SameLine();
            if (ImGui.SmallButton("Remove"))
            {
                profile.Keywords.Remove(rule);
                save();
            }
            ImGui.PopID();
        }
    }

    private void DrawDebug(CharacterIdentity? identity, CharacterProfile? profile)
    {
        ImGui.TextUnformatted($"Delivery state: {relay.State}");
        ImGui.TextUnformatted($"Plugin version: {typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "unknown"}");
        ImGui.TextUnformatted($"Active character: {identity?.CharacterName ?? "none"}");
        ImGui.TextUnformatted($"Character profile key: {profile?.CharacterKey ?? "none"}");
        ImGui.TextUnformatted($"Webhook: {(getWebhookEndpoint() is null ? "not configured" : "configured (secret hidden)")}");
        ImGui.TextUnformatted($"Queue length: {relay.QueueLength} / 100");
        ImGui.TextUnformatted($"Dropped messages: {relay.DroppedCount}");
        ImGui.TextUnformatted($"Last successful delivery: {FormatTimestamp(relay.LastSuccessUtc)}");
        ImGui.TextUnformatted($"Last error: {relay.LastError ?? "none"}");
        ImGui.TextUnformatted($"Discord reply reader: {replyReader.State}");
        ImGui.TextUnformatted($"Reader last success: {FormatTimestamp(replyReader.LastSuccessUtc)}");
        ImGui.TextUnformatted($"Reader last error: {replyReader.LastError ?? "none"}");
        ImGui.TextUnformatted($"Remote screenshot state: {remoteScreenshotService.State}");
        ImGui.TextUnformatted($"Remote screenshot last success: {FormatTimestamp(remoteScreenshotService.LastSuccessUtc)}");
        ImGui.TextUnformatted($"Remote screenshot size: {remoteScreenshotService.LastDimensions ?? "never"}");
        ImGui.TextUnformatted($"Remote screenshot error: {remoteScreenshotService.LastError ?? "none"}");
        ImGui.TextDisabled("Webhook URLs, bot tokens, and message bodies are intentionally excluded from diagnostics and logs.");

        ImGui.Separator();
        ImGui.TextUnformatted("Reward type diagnostics");
        if (profile is null)
        {
            ImGui.TextDisabled("Log into a character to record reward diagnostics.");
            return;
        }

        var captureRewardDiagnostics = profile.CaptureRewardDiagnostics;
        if (DrawBooleanControl(
                "reward-diagnostics",
                "Record reward XivChatType / LogKind diagnostics",
                ref captureRewardDiagnostics))
        {
            profile.CaptureRewardDiagnostics = captureRewardDiagnostics;
            if (!captureRewardDiagnostics)
                chatCapture.RewardDiagnostics.Clear();
            save();
        }
        ImGui.TextWrapped("When enabled, only reward-pattern observations are shown below. Raw LogMessage IDs are correlated in memory; Sentinel Relay does not call Dalamud's side-effecting debug formatter or write message bodies to logs.");
        if (ImGui.Button("Clear Reward Diagnostics"))
            chatCapture.RewardDiagnostics.Clear();

        var observations = chatCapture.RewardDiagnostics.Snapshot();
        ImGui.SameLine();
        ImGui.TextDisabled($"{observations.Count} observation(s), cleared on character switch or reload");
        foreach (var observation in observations.Reverse())
        {
            var ids = observation.NearbyLogMessageIds.Count == 0
                ? "none observed nearby"
                : string.Join(", ", observation.NearbyLogMessageIds);
            var accepted = observation.CandidateLogKind ? "relay candidate" : "diagnostic only";
            ImGui.TextUnformatted($"{observation.TimestampUtc.ToLocalTime():T} | XivChatType={observation.LogKindName} ({observation.LogKindValue}) | {observation.MatchKind} | {accepted}");
            ImGui.TextDisabled($"Nearby LogMessage IDs: {ids}");
            ImGui.TextWrapped(observation.Message);
            ImGui.Spacing();
        }
    }

    private bool DrawBooleanControl(string id, string label, ref bool value)
        => modernThemeActive
            ? SentinelModernSwitch.Draw(
                id,
                label,
                ref value,
                modernShellState.Motion,
                ImGuiHelpers.GlobalScale)
            : ImGui.Checkbox($"{label}##{id}", ref value);

    private bool OpenSection(string title, ImGuiTreeNodeFlags flags)
        => modernThemeActive
            ? SentinelModernControls.CollapsingSection(title, flags)
            : ImGui.CollapsingHeader(title, flags);

    public void OpenAndExpand()
    {
        IsOpen = true;
        if (!configuration.ModernWindowCollapsed)
            return;

        pendingModernSize = ExpandedModernWindowSize();
        configuration.ModernWindowCollapsed = false;
        save();
    }

    public void ToggleFromCommand()
    {
        if (!IsOpen || configuration.ModernWindowCollapsed)
        {
            OpenAndExpand();
            return;
        }

        IsOpen = false;
    }

    private void RequestModernCollapse()
    {
        var scale = ImGuiHelpers.GlobalScale;
        if (configuration.ModernWindowCollapsed)
        {
            pendingModernSize = ExpandedModernWindowSize();
        }
        else
        {
            configuration.ModernExpandedWidth = modernFrameWindowSize.X / scale;
            configuration.ModernExpandedHeight = modernFrameWindowSize.Y / scale;
            pendingModernSize = new Vector2(
                MathF.Max(ClassicMinimumWindowSize.X, configuration.ModernExpandedWidth),
                SentinelModernAppLayoutOptions.Default.HeaderHeight);
        }

        configuration.ModernWindowCollapsed = !configuration.ModernWindowCollapsed;
        save();
    }

    private void RequestModernClose()
    {
        configuration.ModernWindowCollapsed = false;
        save();
        IsOpen = false;
    }

    private Vector2 ExpandedModernWindowSize()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var currentWidth = modernFrameWindowSize.X > 0f
            ? modernFrameWindowSize.X / scale
            : configuration.ModernExpandedWidth;
        return new Vector2(
            MathF.Max(ClassicMinimumWindowSize.X, currentWidth),
            MathF.Max(ClassicMinimumWindowSize.Y, configuration.ModernExpandedHeight));
    }

    private void DrawWebhookUrlInput()
        => ImGui.InputText("##WebhookUrl", ref webhookInput, 512, ImGuiInputTextFlags.Password);

    private void DrawKeywordUserIdInput()
        => ImGui.InputText("##KeywordUserId", ref mentionUserId, 24);

    private void DrawBotTokenInput()
        => ImGui.InputText("##DiscordBotToken", ref botTokenInput, 256, ImGuiInputTextFlags.Password);

    private void DrawReplyChannelIdInput()
        => ImGui.InputText("##RelayChannelId", ref replyChannelId, 24);

    private void DrawAuthorizedUserIdInput()
        => ImGui.InputText("##AuthorizedDiscordUserId", ref authorizedUserId, 24);

    private static void DrawModernPluginIcon(SentinelModernIconDrawContext context)
        => DrawFontAwesomeIcon(
            FontAwesomeIcon.ShieldAlt,
            context.DrawList,
            context.Minimum,
            context.Maximum,
            SentinelModernPalette.Text);

    private static void DrawModernNavigationIcon(
        FontAwesomeIcon icon,
        SentinelModernNavIconDrawContext context)
        => DrawFontAwesomeIcon(
            icon,
            context.DrawList,
            context.Minimum,
            context.Maximum,
            context.Colour);

    private static void DrawFontAwesomeIcon(
        FontAwesomeIcon icon,
        ImDrawListPtr drawList,
        Vector2 minimum,
        Vector2 maximum,
        Vector4 colour)
    {
        var glyph = icon.ToIconString();
        ImGui.PushFont(UiBuilder.IconFont);
        try
        {
            var size = ImGui.CalcTextSize(glyph);
            drawList.AddText(
                minimum + (((maximum - minimum) - size) * 0.5f),
                ImGui.ColorConvertFloat4ToU32(colour),
                glyph);
        }
        finally
        {
            ImGui.PopFont();
        }
    }

    private static string FormatTimestamp(DateTime? value) => value?.ToLocalTime().ToString("G") ?? "never";
}
