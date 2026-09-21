using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using SentinelRelay.Core;
using SentinelRelay.Models;
using SentinelRelay.Services;

namespace SentinelRelay.UI;

public sealed class MainWindow : Window
{
    private static readonly RelayChatType[] PublicChannels =
    [
        RelayChatType.Say, RelayChatType.Yell, RelayChatType.Shout, RelayChatType.FreeCompany,
        RelayChatType.StandardEmote, RelayChatType.CustomEmote,
    ];

    private static readonly RelayChatType[] PrivateChannels =
    [
        RelayChatType.Tell, RelayChatType.Party, RelayChatType.Alliance, RelayChatType.PvPTeam,
        RelayChatType.NoviceNetwork,
    ];

    private static readonly RelayChatType[] LinkshellChannels =
    [
        RelayChatType.Linkshell1, RelayChatType.Linkshell2, RelayChatType.Linkshell3, RelayChatType.Linkshell4,
        RelayChatType.Linkshell5, RelayChatType.Linkshell6, RelayChatType.Linkshell7, RelayChatType.Linkshell8,
        RelayChatType.CrossWorldLinkshell1, RelayChatType.CrossWorldLinkshell2,
        RelayChatType.CrossWorldLinkshell3, RelayChatType.CrossWorldLinkshell4,
        RelayChatType.CrossWorldLinkshell5, RelayChatType.CrossWorldLinkshell6,
        RelayChatType.CrossWorldLinkshell7, RelayChatType.CrossWorldLinkshell8,
    ];

    private readonly Configuration configuration;
    private readonly Func<CharacterIdentity?> getIdentity;
    private readonly Func<CharacterProfile?> getProfile;
    private readonly RelayClient relay;
    private readonly Action save;
    private readonly Action reconnect;
    private readonly Func<bool> requestPairing;
    private readonly Action<bool> setPaused;
    private readonly Action requestUnlink;
    private readonly Func<int> getOutboundQueueLength;
    private readonly Func<string?> getPendingEventId;
    private string serviceUrl;
    private string newKeyword = string.Empty;
    private bool newWholeWord;
    private AlertMethod newAlertMethod = AlertMethod.DirectMessage;
    private readonly HashSet<RelayChatType> newKeywordChannels = [];

    public MainWindow(
        Configuration configuration,
        Func<CharacterIdentity?> getIdentity,
        Func<CharacterProfile?> getProfile,
        RelayClient relay,
        Action save,
        Action reconnect,
        Func<bool> requestPairing,
        Action<bool> setPaused,
        Action requestUnlink,
        Func<int> getOutboundQueueLength,
        Func<string?> getPendingEventId)
        : base("Sentinel Relay###SentinelRelayMain")
    {
        this.configuration = configuration;
        this.getIdentity = getIdentity;
        this.getProfile = getProfile;
        this.relay = relay;
        this.save = save;
        this.reconnect = reconnect;
        this.requestPairing = requestPairing;
        this.setPaused = setPaused;
        this.requestUnlink = requestUnlink;
        this.getOutboundQueueLength = getOutboundQueueLength;
        this.getPendingEventId = getPendingEventId;
        serviceUrl = configuration.ServiceWebSocketUrl;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560, 430),
            MaximumSize = new Vector2(1100, 900),
        };
    }

    public override void Draw()
    {
        var identity = getIdentity();
        var profile = getProfile();
        DrawStatus(identity, profile);
        ImGui.Separator();

        if (ImGui.BeginTabBar("RelayTabs"))
        {
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
            if (ImGui.BeginTabItem("Advanced"))
            {
                DrawAdvanced();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Debug"))
            {
                DrawDebug(identity, profile);
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
    }

    private void DrawStatus(CharacterIdentity? identity, CharacterProfile? profile)
    {
        var color = relay.State == RelayConnectionState.Authenticated
            ? new Vector4(0.28f, 0.9f, 0.48f, 1f)
            : relay.State is RelayConnectionState.ConnectedUnlinked or RelayConnectionState.Connecting
                ? new Vector4(0.95f, 0.78f, 0.25f, 1f)
                : new Vector4(0.95f, 0.35f, 0.38f, 1f);
        ImGui.TextColored(color, relay.State.ToString());
        ImGui.SameLine();
        ImGui.TextUnformatted($" | {identity?.CharacterName ?? "No character"}"
            + $" | {(profile?.Paused == true ? "PAUSED" : "Running")}");
    }

    private void DrawGeneral(CharacterIdentity? identity, CharacterProfile? profile)
    {
        ImGui.TextUnformatted($"Character: {identity?.CharacterName ?? "Not logged in"}");
        ImGui.TextUnformatted($"Home world: {identity?.HomeWorld ?? "—"}");
        ImGui.TextUnformatted($"Discord user: {relay.DiscordUsername ?? "Not linked"}");
        ImGui.TextUnformatted($"Destination: {relay.RelayChannelName ?? "Not configured"}");
        ImGui.Spacing();

        if (profile is null)
        {
            ImGui.TextWrapped("Log into a character before linking or configuring relay settings.");
            return;
        }

        if (profile.Paused)
        {
            ImGui.TextColored(new Vector4(1f, 0.35f, 0.35f, 1f),
                "RELAY PAUSED — no FFXIV chat enters Discord and no Discord chat enters FFXIV.");
            if (ImGui.Button("Resume Relay"))
                setPaused(false);
        }
        else if (ImGui.Button("Pause Relay"))
        {
            setPaused(true);
        }

        ImGui.SameLine();
        if (relay.State == RelayConnectionState.ConnectedUnlinked && ImGui.Button("Generate Link Code"))
            requestPairing();
        if (relay.State == RelayConnectionState.Authenticated)
        {
            ImGui.SameLine();
            if (ImGui.Button("Unlink Discord"))
                requestUnlink();
        }

        if (!string.IsNullOrWhiteSpace(relay.PairCode))
        {
            ImGui.Spacing();
            ImGui.TextUnformatted("In Discord, run: /relay link code:");
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.35f, 0.85f, 1f, 1f), relay.PairCode);
            ImGui.TextDisabled($"Expires {relay.PairCodeExpiresAtUtc?.ToLocalTime():T}");
        }
    }

    private void DrawFilters(CharacterProfile? profile)
    {
        if (profile is null)
        {
            ImGui.TextUnformatted("Log into a character to edit its filters.");
            return;
        }

        ImGui.TextWrapped("Privacy warning: enabling a channel sends its contents from this PC to the Sentinel Relay service and then Discord. Filtering happens locally; disabled channels are not transmitted.");
        ImGui.Spacing();
        DrawChannelGroup("Common / public channels", PublicChannels, profile);
        DrawChannelGroup("Private or group channels (all off by default)", PrivateChannels, profile);
        DrawChannelGroup("Linkshells and cross-world linkshells (all off by default)", LinkshellChannels, profile);
    }

    private void DrawChannelGroup(string title, IReadOnlyList<RelayChatType> channels, CharacterProfile profile)
    {
        if (!ImGui.CollapsingHeader(title))
            return;
        var columns = channels.Count > 8 ? 2 : 1;
        if (ImGui.BeginTable($"channels-{title}", columns))
        {
            for (var i = 0; i < channels.Count; i++)
            {
                ImGui.TableNextColumn();
                var channel = channels[i];
                var enabled = profile.EnabledInboundChannels.Contains(channel);
                if (ImGui.Checkbox(ChannelPolicy.GetLabel(channel), ref enabled))
                {
                    if (enabled)
                        profile.EnabledInboundChannels.Add(channel);
                    else
                        profile.EnabledInboundChannels.Remove(channel);
                    save();
                }
            }
            ImGui.EndTable();
        }
    }

    private void DrawKeywords(CharacterIdentity? identity, CharacterProfile? profile)
    {
        if (profile is null)
        {
            ImGui.TextUnformatted("Log into a character to edit keyword alerts.");
            return;
        }

        ImGui.TextWrapped("Keywords are matched locally, case-insensitively, only in the channels selected for that rule. One chat message produces at most one alert bundle.");
        ImGui.SetNextItemWidth(260);
        ImGui.InputTextWithHint("##new-keyword", "Keyword", ref newKeyword, 80);
        ImGui.SameLine();
        ImGui.Checkbox("Whole word", ref newWholeWord);
        ImGui.SetNextItemWidth(180);
        if (ImGui.BeginCombo("Alert method", AlertMethodLabel(newAlertMethod)))
        {
            foreach (var method in Enum.GetValues<AlertMethod>())
            {
                if (ImGui.Selectable(AlertMethodLabel(method), newAlertMethod == method))
                    newAlertMethod = method;
            }
            ImGui.EndCombo();
        }

        if (newKeywordChannels.Count == 0 && profile.EnabledInboundChannels.Count > 0)
            newKeywordChannels.UnionWith(profile.EnabledInboundChannels);
        if (ImGui.CollapsingHeader("Channels for new keyword"))
        {
            foreach (var channel in ChannelPolicy.InboundChannels)
            {
                var selected = newKeywordChannels.Contains(channel);
                if (ImGui.Checkbox($"{ChannelPolicy.GetLabel(channel)}##keyword-{channel}", ref selected))
                {
                    if (selected)
                        newKeywordChannels.Add(channel);
                    else
                        newKeywordChannels.Remove(channel);
                }
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
                WholeWord = newWholeWord,
                AlertMethod = newAlertMethod,
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
                newKeyword = identity.CharacterName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? identity.CharacterName;
        }

        ImGui.Separator();
        foreach (var rule in profile.Keywords.ToArray())
        {
            ImGui.PushID(rule.Id.ToString("N"));
            ImGui.TextUnformatted(rule.Keyword);
            ImGui.SameLine();
            ImGui.TextDisabled($"{AlertMethodLabel(rule.AlertMethod)} | {(rule.WholeWord ? "whole word" : "contains")} | {rule.Channels.Count} channel(s)");
            ImGui.SameLine();
            if (ImGui.SmallButton("Remove"))
            {
                profile.Keywords.Remove(rule);
                save();
            }
            ImGui.PopID();
        }
    }

    private void DrawAdvanced()
    {
        ImGui.TextWrapped("Use the secure WebSocket address from your Sentinel Relay deployment. Production addresses must start with wss://. Plain ws:// is accepted only for localhost development.");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("Service WebSocket URL", ref serviceUrl, 512);
        if (ImGui.Button("Save and Reconnect"))
        {
            configuration.ServiceWebSocketUrl = serviceUrl.Trim();
            save();
            reconnect();
        }
        ImGui.Spacing();
        ImGui.TextDisabled("The Discord bot token is never entered into this plugin. It belongs only in the hosted service environment.");
    }

    private void DrawDebug(CharacterIdentity? identity, CharacterProfile? profile)
    {
        ImGui.TextUnformatted($"Plugin state: {relay.State}");
        ImGui.TextUnformatted($"Reconnect count: {relay.ReconnectCount}");
        ImGui.TextUnformatted($"Active character: {identity?.CharacterName ?? "none"}");
        ImGui.TextUnformatted($"Installation ID: {profile?.InstallationId ?? "none"}");
        ImGui.TextUnformatted($"Backend version: {relay.BackendVersion ?? "unknown"}");
        ImGui.TextUnformatted($"Outbound queue: {getOutboundQueueLength()}");
        ImGui.TextUnformatted($"Pending confirmation: {getPendingEventId() ?? "none"}");
        ImGui.TextUnformatted($"Last FFXIV → Discord: {FormatTimestamp(relay.LastInboundSentUtc)}");
        ImGui.TextUnformatted($"Last Discord → FFXIV: {FormatTimestamp(relay.LastOutboundReceivedUtc)}");
        ImGui.TextUnformatted($"Last error: {relay.LastError ?? "none"}");
        ImGui.TextDisabled("Credentials and Discord secrets are intentionally excluded from diagnostics.");
    }

    private static string AlertMethodLabel(AlertMethod method) => method switch
    {
        AlertMethod.DirectMessage => "Discord DM",
        AlertMethod.ChannelMention => "Relay-channel mention",
        AlertMethod.Both => "DM and mention",
        _ => method.ToString(),
    };

    private static string FormatTimestamp(DateTime? value) => value?.ToLocalTime().ToString("G") ?? "never";
}

