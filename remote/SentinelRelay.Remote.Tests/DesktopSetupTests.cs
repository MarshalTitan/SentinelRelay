using System.Text.Json;
using SentinelRelay.Remote;

internal static class DesktopSetupTests
{
    public static async Task Run(Action<bool, string> check)
    {
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
        var state = new TestStore();
        var admin = new LocalAdministration(state);
        var slot = admin.ConnectGame(old with { InstanceId = "" }, "First character");
        check(admin.Snapshot().Bindings.Single().InstanceId == slot.InstanceId && !admin.Snapshot().Enabled, "desktop enrollment never enables remote access");
        admin.SetEnabled(true);
        var firstEpoch = state.State.Epoch;
        check(admin.ReconnectKnown([restarted]) == 1 && state.State.Epoch != firstEpoch, "desktop restart reconnect invalidates sessions");
        var sameEpoch = state.State.Epoch;
        check(admin.ReconnectKnown([restarted]) == 0 && state.State.Epoch == sameEpoch, "desktop refresh preserves current sessions");
        var reused = admin.ConnectGame(restarted, "New display label");
        check(reused.InstanceId == slot.InstanceId && admin.Snapshot().Instances.Length == 1, "desktop keeps phone grants on explicit reconnect");
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
