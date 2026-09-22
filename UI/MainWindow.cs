using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using SentinelRelay.Core;
using SentinelRelay.Models;
using SentinelRelay.Services;

namespace SentinelRelay.UI;

public sealed class MainWindow : Window
{
    private static readonly RelayChatType[] CommonChannels =
    [
        RelayChatType.Say,
        RelayChatType.Yell,
        RelayChatType.Shout,
        RelayChatType.FreeCompany,
    ];

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

    private static readonly (RelayChatType Channel, string Command, string Label)[] CommonReplyChannels =
    [
        (RelayChatType.Say, "/say", "Say"),
        (RelayChatType.Yell, "/yell", "Yell"),
        (RelayChatType.Shout, "/shout", "Shout"),
        (RelayChatType.FreeCompany, "/fc", "Free Company"),
    ];

    private static readonly (RelayChatType Channel, string Command, string Label)[] GroupReplyChannels =
    [
        (RelayChatType.Party, "/party", "Party / Cross-world Party"),
        (RelayChatType.Alliance, "/alliance", "Alliance"),
        (RelayChatType.PvPTeam, "/pvpteam", "PvP Team"),
        (RelayChatType.NoviceNetwork, "/novice", "Novice Network"),
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
    private readonly WebhookRelayClient relay;
    private readonly DiscordReplyReader replyReader;
    private readonly Func<string, WebhookConfigurationResult> saveWebhook;
    private readonly Action removeWebhook;
    private readonly Func<bool> testWebhook;
    private readonly Func<DiscordReplyConfigurationInput, WebhookConfigurationResult> saveReplySettings;
    private readonly Action removeDiscordBotCredential;
    private readonly Func<bool> testDiscordReader;
    private readonly Action<bool> setPaused;
    private readonly Action save;
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
    private readonly HashSet<RelayChatType> outboundChannels = [];
    private string? replyFeedback;
    private bool replyFeedbackIsError;

    public MainWindow(
        Func<CharacterIdentity?> getIdentity,
        Func<CharacterProfile?> getProfile,
        Func<Uri?> getWebhookEndpoint,
        WebhookRelayClient relay,
        Func<string, WebhookConfigurationResult> saveWebhook,
        Action removeWebhook,
        Func<bool> testWebhook,
        DiscordReplyReader replyReader,
        Func<DiscordReplyConfigurationInput, WebhookConfigurationResult> saveReplySettings,
        Action removeDiscordBotCredential,
        Func<bool> testDiscordReader,
        Action<bool> setPaused,
        Action save)
        : base("Sentinel Relay###SentinelRelayMain")
    {
        this.getIdentity = getIdentity;
        this.getProfile = getProfile;
        this.getWebhookEndpoint = getWebhookEndpoint;
        this.relay = relay;
        this.replyReader = replyReader;
        this.saveWebhook = saveWebhook;
        this.removeWebhook = removeWebhook;
        this.testWebhook = testWebhook;
        this.saveReplySettings = saveReplySettings;
        this.removeDiscordBotCredential = removeDiscordBotCredential;
        this.testDiscordReader = testDiscordReader;
        this.setPaused = setPaused;
        this.save = save;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(590, 440),
            MaximumSize = new Vector2(1100, 900),
        };
    }

    public override void Draw()
    {
        var identity = getIdentity();
        var profile = getProfile();
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
            outboundChannels.Clear();
            if (profile is not null)
                outboundChannels.UnionWith(profile.EnabledOutboundChannels.Where(ChannelPolicy.ImplementedOutboundChannels.Contains));
            newKeywordChannels.Clear();
            keywordChannelsInitialized = false;
        }

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
            DrawExperimentalReplies(identity, profile);
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Debug"))
        {
            DrawDebug(identity, profile);
            ImGui.EndTabItem();
        }
        ImGui.EndTabBar();
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

        ImGui.Spacing();
        ImGui.TextWrapped("Sentinel Relay sends enabled chat directly from this PC to the configured Discord webhook.");
    }

    private void DrawExperimentalReplies(CharacterIdentity? identity, CharacterProfile? profile)
    {
        if (profile is null || identity is null)
        {
            ImGui.TextUnformatted("Log into a character to configure Discord replies.");
            return;
        }

        ImGui.TextWrapped("This optional reader checks one private Discord channel and submits only explicitly allowed chat destinations. It does not use a hosted Sentinel service, Discord Gateway connection, or arbitrary FFXIV command execution.");
        ImGui.Spacing();
        ImGui.TextWrapped("The bot token is a powerful secret. Use a dedicated bot with only View Channel and Read Message History access to the single relay channel. It is masked here and protected locally with Windows DPAPI.");
        ImGui.Spacing();

        ImGui.Checkbox("Enable Discord → FFXIV Replies", ref repliesEnabled);
        ImGui.TextUnformatted("Allowed Discord reply destinations");
        ImGui.TextDisabled("These permissions are separate from Chat Filters. Monitoring a channel never automatically permits sending to it.");
        DrawReplyChannelGroup("Common chats", CommonReplyChannels, defaultOpen: true);
        DrawReplyChannelGroup("Group chats", GroupReplyChannels, defaultOpen: true);
        if (ImGui.CollapsingHeader("Tell reply", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawReplyChannelToggle(RelayChatType.IncomingTell, "/r", "Reply to the latest incoming Tell");
            ImGui.TextDisabled("/r is accepted only for 30 minutes after this character receives a Tell during the current session.");
        }
        DrawReplyChannelGroup("Linkshells and cross-world linkshells", LinkshellReplyChannels, defaultOpen: false);
        ImGui.Spacing();
        ImGui.TextUnformatted($"Discord Bot Credential: {(string.IsNullOrWhiteSpace(profile.ProtectedDiscordBotToken) ? "Not Configured" : "Configured (secret hidden)")}");
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

        if (ImGui.Button("Save Reply Settings"))
        {
            var result = saveReplySettings(new DiscordReplyConfigurationInput(
                repliesEnabled,
                botTokenInput,
                replyChannelId,
                authorizedUserId,
                new HashSet<RelayChatType>(outboundChannels)));
            replyFeedback = result.Success
                ? "Reply settings saved. Starting the reader establishes a fresh checkpoint so old messages cannot execute."
                : result.Error;
            replyFeedbackIsError = !result.Success;
            if (result.Success)
                botTokenInput = string.Empty;
        }
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
            replyFeedback = "Bot credential removed and Discord replies disabled for this character.";
            replyFeedbackIsError = false;
        }
        if (!hasBotCredential)
            ImGui.EndDisabled();

        if (!string.IsNullOrWhiteSpace(replyFeedback))
        {
            var color = replyFeedbackIsError
                ? new Vector4(0.95f, 0.35f, 0.38f, 1f)
                : new Vector4(0.35f, 0.85f, 1f, 1f);
            ImGui.TextColored(color, replyFeedback);
        }

        ImGui.Separator();
        var readerStatus = profile.DiscordRepliesEnabled ? replyReader.State.ToString() : "Disabled";
        ImGui.TextUnformatted($"Reader Status: {readerStatus}");
        ImGui.TextUnformatted($"Last Reader Success: {FormatTimestamp(profile.LastDiscordReaderSuccessUtc)}");
        ImGui.TextUnformatted($"Checkpoint: {(string.IsNullOrWhiteSpace(profile.LastProcessedDiscordMessageId) ? "not established" : "established")}");
        if (!string.IsNullOrWhiteSpace(replyReader.LastError))
            ImGui.TextColored(new Vector4(0.95f, 0.35f, 0.38f, 1f), $"Reader Error: {replyReader.LastError}");
        ImGui.TextDisabled("Sentinel Relay never prints the bot token and never treats Discord text as an arbitrary FFXIV command.");
    }

    private void DrawReplyChannelGroup(
        string title,
        IReadOnlyList<(RelayChatType Channel, string Command, string Label)> channels,
        bool defaultOpen)
    {
        var flags = defaultOpen ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;
        if (!ImGui.CollapsingHeader(title, flags))
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
        if (!ImGui.Checkbox($"Allow {command} ({label})", ref enabled))
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

        ImGui.TextWrapped("Privacy warning: enabling a channel sends its sender and message directly from this PC to Discord. Disabled channels are filtered locally and never sent.");
        ImGui.Spacing();
        if (ImGui.Button("Enable common chats"))
        {
            profile.EnabledInboundChannels.UnionWith(CommonChannels);
            save();
        }
        ImGui.SameLine();
        if (ImGui.Button("Disable all"))
        {
            profile.EnabledInboundChannels.Clear();
            save();
        }
        ImGui.TextDisabled("Bulk enable never includes Party, Cross-world Party, Tells, linkshells, Alliance, PvP Team, or Novice Network.");
        ImGui.Spacing();
        DrawChannelGroup("Common and public chats", PublicChannels, profile, defaultOpen: true);
        DrawChannelGroup("Private or group chats", PrivateChannels, profile, defaultOpen: true);
        DrawChannelGroup("Linkshells and cross-world linkshells", LinkshellChannels, profile, defaultOpen: false);
    }

    private void DrawChannelGroup(
        string title,
        IReadOnlyList<RelayChatType> channels,
        CharacterProfile profile,
        bool defaultOpen)
    {
        var flags = defaultOpen ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;
        if (!ImGui.CollapsingHeader(title, flags))
            return;
        var columns = channels.Count > 8 ? 2 : 1;
        if (!ImGui.BeginTable($"channels-{title}", columns))
            return;
        foreach (var channel in channels)
        {
            ImGui.TableNextColumn();
            var enabled = profile.EnabledInboundChannels.Contains(channel);
            if (!ImGui.Checkbox(ChannelPolicy.GetLabel(channel), ref enabled))
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
        ImGui.TextWrapped("Paste the webhook created in this character's private Discord relay channel. The URL is a secret: it is masked here and protected in the local Dalamud configuration with Windows DPAPI.");
        ImGui.Spacing();
        ImGui.TextUnformatted($"Status: {WebhookEndpoint.Mask(endpoint)}");
        ImGui.TextUnformatted(profile.LastWebhookSuccessUtc is null
            ? "Verification: not yet confirmed by Discord"
            : $"Verification: Discord accepted a delivery at {FormatTimestamp(profile.LastWebhookSuccessUtc)}");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("Webhook URL", ref webhookInput, 512, ImGuiInputTextFlags.Password);

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
        ImGui.TextUnformatted("Message formatting");
        var includeWorld = profile.IncludeSenderWorld;
        if (ImGui.Checkbox("Include sender world when available", ref includeWorld))
        {
            profile.IncludeSenderWorld = includeWorld;
            save();
        }
        var embeds = profile.UseDiscordEmbeds;
        if (ImGui.Checkbox("Use compact Discord embeds", ref embeds))
        {
            profile.UseDiscordEmbeds = embeds;
            save();
        }
        ImGui.TextDisabled("Relayed chat cannot create mentions. Only the explicit keyword-ping feature may mention its configured user ID.");
    }

    private void DrawKeywords(CharacterIdentity? identity, CharacterProfile? profile)
    {
        if (profile is null)
        {
            ImGui.TextUnformatted("Log into a character to edit its keyword alerts.");
            return;
        }

        ImGui.TextWrapped("Keyword matching happens locally and only in enabled channels. A match is highlighted in the relay channel. Sentinel Relay can optionally ping one explicitly configured Discord user, but webhooks cannot send DMs.");
        ImGui.Spacing();
        ImGui.SetNextItemWidth(260);
        ImGui.InputText("Discord User ID", ref mentionUserId, 24);
        ImGui.SameLine();
        if (ImGui.Button("Save User ID"))
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
        ImGui.Spacing();

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
        if (ImGui.CollapsingHeader("Channels for new keyword", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (ImGui.BeginTable("keyword-channels", 2))
            {
                foreach (var channel in ChannelPolicy.InboundChannels)
                {
                    ImGui.TableNextColumn();
                    var selected = newKeywordChannels.Contains(channel);
                    if (!ImGui.Checkbox($"{ChannelPolicy.GetLabel(channel)}##keyword-{channel}", ref selected))
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
        ImGui.TextDisabled("Webhook URLs, bot tokens, and message bodies are intentionally excluded from diagnostics and logs.");
    }

    private static string FormatTimestamp(DateTime? value) => value?.ToLocalTime().ToString("G") ?? "never";
}
