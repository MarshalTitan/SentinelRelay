using System.Text.Json;
using Lumina.Excel.Sheets;
using SentinelRelay.Bridge;
using SentinelRelay.Core;
using SentinelRelay.Services;
namespace SentinelRelay;

public sealed partial class Plugin
{
    private MobilePipeServer? mobilePipe;
    private long lastMobileScreenshot;
    private bool hasMobileScreenshot;
    private void StopMobileBridge() { mobilePipe?.Dispose(); mobilePipe = null; hasMobileScreenshot = false; }
    private void UpdateMobileBridge()
    {
        var current = characterContext.Current;
        if (disposed || current is null || activeProfile?.CharacterKey != current.CharacterKey ||
            !activeProfile.MobileRemoteEnabled || activeProfile.Paused)
        { StopMobileBridge(); return; }
        if (mobilePipe?.CharacterKey != current.CharacterKey)
        { StopMobileBridge(); mobilePipe = new MobilePipeServer(current.CharacterKey); }
        mobilePipe.Pump(ExecuteMobileRequest);
    }
    private BridgeReply ExecuteMobileRequest(BridgeRequest request, bool validateOnly)
    {
        var identity = characterContext.Current;
        if (!Framework.IsInFrameworkUpdateThread || disposed || identity is null || mobilePipe is null ||
            activeProfile?.CharacterKey != identity.CharacterKey || activeProfile.Paused || !activeProfile.MobileRemoteEnabled)
            return new(false, "plugin_disabled");
        var status = new SRankRemoteClient(PluginInterface).Invoke(SRankRemoteDecision.Status, identity.CharacterKey);
        uint territory = ClientState.TerritoryType;
        string? world = null;
        try { world = PlayerState.CurrentWorld.Value.Name.ToString(); } catch { }
        bool? vnavmesh = null;
        try { vnavmesh = PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady").InvokeFunc(); } catch { }
        var snapshot = new BridgeSnapshot(identity.CharacterKey, mobilePipe.GameEpoch, identity.CharacterName, world,
            territory, ResolveSRankTerritoryName(territory), DateTimeOffset.UtcNow,
            status.Status is null ? null : JsonSerializer.SerializeToElement(status.Status, BridgeWire.Json),
            activeProfile.MobileRemoteControlEnabled, activeProfile.MobileRemoteScreenshotsEnabled, vnavmesh);
        var rejection = BridgeWire.Reject(request, snapshot);
        if (rejection is not null) return new(false, rejection);
        if (validateOnly || request.Operation is "hello" or "srank.status") return new(true, "status", snapshot);
        if (request.Operation == "screenshot")
        {
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (hasMobileScreenshot && System.Diagnostics.Stopwatch.GetElapsedTime(lastMobileScreenshot, now) < TimeSpan.FromSeconds(15))
                return new(false, "cooldown");
            var window = GameWindowCapture.FindCurrentProcessGameWindow();
            if (window == 0) return new(false, "capture_failed");
            hasMobileScreenshot = true; lastMobileScreenshot = now;
            ChatGui.Print("[Sentinel Relay] Authorized mobile screenshot requested.");
            return new(true, "capture", snapshot) { CaptureWindow = window };
        }
        // Reuse exactly the same existing SRank IPC endpoints; never send game chat or arbitrary commands.
        ChatGui.Print("[Sentinel Relay] Authorized mobile SRank rescue requested.");
        var result = new SRankRemoteClient(PluginInterface).Invoke(request.Operation == "srank.skip"
            ? SRankRemoteDecision.Skip : SRankRemoteDecision.Reset, identity.CharacterKey);
        Log.Information("Sentinel mobile rescue {Operation} completed: {Success}.", request.Operation, result.Success);
        return new(result.Success, result.Success ? "completed" : "operation_failed", snapshot);
    }
    private void SetMobilePermissions(bool enabled, bool control, bool screenshots)
    {
        var key = characterContext.Current?.CharacterKey;
        if (activeProfile is null || key != activeProfile.CharacterKey) return;
        activeProfile.MobileRemoteEnabled = enabled;
        activeProfile.MobileRemoteControlEnabled = control;
        activeProfile.MobileRemoteScreenshotsEnabled = screenshots;
        StopMobileBridge();
        SaveConfiguration();
        // The next framework update starts the bridge. UI never pumps game IPC.
    }
    private bool MobileCommand(string arguments)
    {
        var parts = arguments.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts[0] != "mobile") return false;
        if (activeProfile is null || characterContext.Current?.CharacterKey != activeProfile.CharacterKey)
        { ChatGui.PrintError("[Sentinel Relay] Log into the intended character first."); return true; }
        switch (parts)
        {
            case ["mobile", "all", "on"]:
                activeProfile.MobileRemoteEnabled = true;
                activeProfile.MobileRemoteControlEnabled = true;
                activeProfile.MobileRemoteScreenshotsEnabled = true;
                break;
            case ["mobile", "all", "off"]:
            case ["mobile", "off"]:
                activeProfile.MobileRemoteEnabled = false;
                activeProfile.MobileRemoteControlEnabled = false;
                activeProfile.MobileRemoteScreenshotsEnabled = false;
                break;
            case ["mobile", "on"]: activeProfile.MobileRemoteEnabled = true; break;
            case ["mobile", "control", "on"]: activeProfile.MobileRemoteControlEnabled = true; break;
            case ["mobile", "control", "off"]: activeProfile.MobileRemoteControlEnabled = false; break;
            case ["mobile", "screenshots", "on"]: activeProfile.MobileRemoteScreenshotsEnabled = true; break;
            case ["mobile", "screenshots", "off"]: activeProfile.MobileRemoteScreenshotsEnabled = false; break;
            default:
                ChatGui.Print("[Sentinel Relay] /srelay mobile all on|off; mobile on|off; mobile control on|off; mobile screenshots on|off. Saved for this character.");
                return true;
        }
        StopMobileBridge(); // Every local permission change retires the old game epoch and queued work.
        SaveConfiguration(); UpdateMobileBridge();
        ChatGui.Print($"[Sentinel Relay] Mobile: {(activeProfile.MobileRemoteEnabled ? "on" : "off")}; control: {(activeProfile.MobileRemoteControlEnabled ? "on" : "off")}; screenshots: {(activeProfile.MobileRemoteScreenshotsEnabled ? "on" : "off")}. Game PID: {Environment.ProcessId}.");
        return true;
    }
}
