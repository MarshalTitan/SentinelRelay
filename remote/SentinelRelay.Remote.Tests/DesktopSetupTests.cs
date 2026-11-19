using System.Text.Json;
using SentinelRelay.Remote;

internal static class DesktopSetupTests
{
    public static async Task Run(Action<bool, string> check)
    {
        check(!DesktopLaunchPolicy.RequiresStandardToken(1, false, false), "desktop accepts normal Windows sessions without a linked token, including administrator-enabled accounts");
        check(!DesktopLaunchPolicy.RequiresStandardToken(3, false, false), "desktop accepts a standard filtered user token");
        check(DesktopLaunchPolicy.RequiresStandardToken(2, false, false), "desktop redirects an elevated split token to its standard counterpart");
        Throws(() => DesktopLaunchPolicy.RequiresStandardToken(2, false, true), check, "desktop standard-user relaunch cannot loop");
        Throws(() => DesktopLaunchPolicy.RequiresStandardToken(1, true, false), check, "desktop refuses service accounts");
        Throws(() => DesktopLaunchPolicy.RequiresStandardToken(0, false, false), check, "desktop fails closed for an unknown token type");
        check(DesktopLaunchPolicy.QuoteArgument("C:\\Program Files\\SentinelRemote.exe") == "\"C:\\Program Files\\SentinelRemote.exe\"", "desktop quotes executable paths containing spaces");
        check(DesktopLaunchPolicy.QuoteArgument("a\"b") == "\"a\\\"b\"", "desktop quotes literal quote characters");
        check(DesktopLaunchPolicy.QuoteArgument("C:\\test\\") == "\"C:\\test\\\\\"", "desktop quotes trailing backslashes");
        var stopped = false;
        var nextWrite = false;
        var saved = await LocalEmergencyStop.Run(() => { stopped = true; return Task.CompletedTask; },
            () => throw new IOException("locked"), () => nextWrite = true);
        check(stopped && nextWrite && !saved, "desktop emergency stop closes listener even when a state write fails");
        stopped = false;
        saved = await LocalEmergencyStop.Run(() => { stopped = true; return Task.CompletedTask; }, () => { });
        check(stopped && saved, "desktop emergency stop reports successfully persisted disable");
        var json = """{"BackendState":"Running","Self":{"DNSName":"main.test.ts.net.","UserID":7},"User":{"7":{"LoginName":"owner@example.com"},"9":{"LoginName":"peer@example.com"}}}""";
        var identity = DesktopSetupPolicy.ReadIdentity(json);
        check(identity.Host == "main.test.ts.net" && identity.Login == "owner@example.com", "desktop uses the local node and its exact owner login");
        Throws(() => DesktopSetupPolicy.ReadIdentity(json.Replace("Running", "Stopped")), check, "desktop refuses disconnected Tailscale");
        Throws(() => DesktopSetupPolicy.ReadIdentity(json.Replace("main.test.ts.net.", "example.com")), check, "desktop refuses non-tailnet host");
        Throws(() => DesktopSetupPolicy.ReadIdentity(json.Replace("\"UserID\":7", "\"Tags\":[\"tag:server\"],\"UserID\":7")), check, "desktop refuses tagged identity without user headers");
        var route = """{"TCP":{"443":{"HTTPS":true}},"Web":{"main.test.ts.net:443":{"Handlers":{"/":{"Proxy":"http://127.0.0.1:32179"}}}}}""";
        check(DesktopSetupPolicy.ServeState("null", identity.Host) == "missing", "desktop accepts unconfigured Serve");
        check(DesktopSetupPolicy.ServeState("{}", identity.Host) == "missing", "desktop accepts empty Serve");
        check(DesktopSetupPolicy.ServeState(route, identity.Host) == "ready", "desktop recognizes its exact private route");
        check(DesktopSetupPolicy.ServeState(route.Replace("32179", "3000"), identity.Host) == "conflict", "desktop preserves someone else's route");
        check(DesktopSetupPolicy.ServeState(route.Replace("main.test", "other.test"), identity.Host) == "conflict", "desktop refuses a different Serve host");
        check(DesktopSetupPolicy.ServeState(route[..^1] + ""","AllowFunnel":{"main.test.ts.net:443":true}}""", identity.Host) == "conflict", "desktop refuses public Funnel");
        check(DesktopSetupPolicy.ServeState(route.Replace("\"HTTPS\":true", "\"TCPForward\":\"localhost:3000\""), identity.Host) == "conflict", "desktop refuses TCP forwarding");
        var old = new GameBinding("slot-a", 100, 1000, @"C:\Game\ffxiv_dx11.exe", "character-a");
        var restarted = old with { InstanceId = "", ProcessId = 200, StartedUtcTicks = 2000 };
        check(DesktopSetupPolicy.Rebindings([old], [old]).Length == 0, "desktop leaves live binding epochs unchanged");
        check(DesktopSetupPolicy.Rebindings([old], [restarted]).Single() == restarted with { InstanceId = "slot-a" }, "desktop reconnects the exact enrolled character");
        check(DesktopSetupPolicy.Rebindings([old], [restarted with { CharacterKey = "character-b" }]).Length == 0, "desktop never selects the first other character");
        check(DesktopSetupPolicy.Rebindings([old], [restarted, restarted with { ProcessId = 300 }]).Length == 0, "desktop refuses ambiguous duplicate identities");
        check(DesktopSetupPolicy.Rebindings([old], [restarted with { Executable = @"C:\Other\ffxiv_dx11.exe" }]).Length == 0, "desktop requires approval for changed executable");
        check(DesktopSetupPolicy.Rebindings([old, old with { InstanceId = "slot-b" }], [restarted]).Length == 0, "desktop does not reconnect ambiguous saved slots");
        var migrated = JsonSerializer.Deserialize<Device>("""{"id":"legacy","label":"Old phone","tokenHash":"hash","instances":[],"control":true,"revoked":false}""", Protocol.Json)!;
        check(!migrated.Trusted, "upgrade does not silently trust existing paired phones");
        var state = new TestStore();
        var admin = new LocalAdministration(state);
        var slot = admin.ConnectGame(old with { InstanceId = "" }, "First character");
        check(admin.Snapshot().Bindings.Single().InstanceId == slot.InstanceId && !admin.Snapshot().Enabled, "desktop enrollment never enables remote access");
        admin.SetEnabled(true);
        var authority = new RemoteAuthority(state);
        var fullInvitation = admin.Invite("Full phone", [], true, trusted: true);
        var full = (DeviceCredential)authority.Enroll(new(1, fullInvitation)).Data!;
        var rescue = (DeviceCredential)authority.Enroll(new(1, admin.Invite("Rescue phone", [slot.InstanceId], true))).Data!;
        var viewer = (DeviceCredential)authority.Enroll(new(1, admin.Invite("Viewer", [slot.InstanceId], false))).Data!;
        check(new LocalAdministration(state).Snapshot().Devices.Single(d => d.Id == full.DeviceId).Trusted,
            "full-PC approval survives serialization and local administration restart");
        check(!admin.Snapshot().Devices.Single(d => d.Id == rescue.DeviceId).Trusted,
            "ordinary control pairing does not imply desktop/gamepad trust");
        Throws(() => admin.TrustPhone(viewer.DeviceId, true), check, "view-only device cannot gain full-PC trust");
        Throws(() => admin.Invite("Invalid", [], false, trusted: true), check, "full-PC invitation requires control permission");
        Throws(() => admin.Invite("Invalid", [], true), check, "rescue-only invitation still requires an explicit character grant");
        var later = admin.ConnectGame(old with { ProcessId = 101, StartedUtcTicks = 1001, CharacterKey = "later-character" }, "Later character");
        check(state.State.Devices.Single(d => d.Id == full.DeviceId).Instances.Contains(later.InstanceId),
            "trusted phone receives a character explicitly approved later on this PC");
        check(!state.State.Devices.Single(d => d.Id == rescue.DeviceId).Instances.Contains(later.InstanceId),
            "approving another character does not expand rescue-only phone grants");
        var pendingFull = admin.Invite("Pending full phone", [], true, trusted: true);
        admin.PauseTrustedPhones();
        check(admin.Snapshot().Devices.All(d => !d.Trusted), "pause removes remembered full-PC approvals");
        check(!authority.Enroll(new(1, pendingFull)).Success, "pause invalidates outstanding full-PC invitations");
        admin.TrustPhone(full.DeviceId, true);
        check(admin.Snapshot().Devices.Single(d => d.Id == full.DeviceId).Trusted, "local approval can restore a paused phone");
        admin.Revoke(full.DeviceId);
        check(!admin.Snapshot().Devices.Single(d => d.Id == full.DeviceId).Trusted, "revoke removes remembered trust");
        Throws(() => admin.TrustPhone(full.DeviceId, true), check, "revoked phone cannot be trusted again");
        var firstEpoch = state.State.Epoch;
        check(admin.ReconnectKnown([restarted]) == 1 && state.State.Epoch != firstEpoch, "desktop restart reconnect invalidates sessions");
        var sameEpoch = state.State.Epoch;
        check(admin.ReconnectKnown([restarted]) == 0 && state.State.Epoch == sameEpoch, "desktop refresh preserves current sessions");
        var reused = admin.ConnectGame(restarted, "New display label");
        check(reused.InstanceId == slot.InstanceId && admin.Snapshot().Instances.Length == 2, "desktop keeps phone grants on explicit reconnect");
        admin.SetEnabled(false);
        admin.ReconnectKnown([restarted with { ProcessId = 400, StartedUtcTicks = 4000 }]);
        check(!admin.Snapshot().Enabled, "desktop discovery cannot undo emergency disable");
    }
    private static void Throws(Action action, Action<bool, string> check, string label)
    {
        bool threw = false;
        try { action(); } catch { threw = true; }
        check(threw, label);
    }
    private sealed class TestStore : IStateStore
    {
        public RemoteState State = new();
        public T Access<T>(Func<RemoteState, (T Result, bool Save)> action)
        {
            var clone = JsonSerializer.Deserialize<RemoteState>(JsonSerializer.Serialize(State, Protocol.Json), Protocol.Json)!;
            var (result, save) = action(clone);
            if (save) State = clone;
            return result;
        }
    }
}
