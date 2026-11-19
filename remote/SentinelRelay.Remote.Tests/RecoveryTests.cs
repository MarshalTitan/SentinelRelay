using System.Text.Json;
using SentinelRelay.Remote;
using SentinelRelay.RemoteHost;

static class RecoveryTests
{
    public static void Run(Action<bool, string> check)
    {
        var store = new Store(); var admin = new LocalAdministration(store);
        admin.SetEnabled(true);
        var slot = admin.AddInstance("Saved character");
        var other = admin.AddInstance("Other character");
        var binding = new GameBinding(slot.InstanceId, 123, 456, "game.exe", "character");
        admin.Bind(binding);
        var launches = 0; var restores = 0;
        var actions = new RecoveryActions(_ => { launches++; return new(true, "launcher_opened"); },
            b => { if (b == binding) restores++; return new(true, "game_restore_requested"); });
        var authority = new RemoteAuthority(store, recovery: actions);
        DeviceCredential Pair(bool control) => (DeviceCredential)authority.Enroll(new(1,
            admin.Invite("Phone", [slot.InstanceId], control))).Data!;
        string Session(DeviceCredential device) => ((SessionCredential)authority.OpenSession(device.DeviceToken, new(1, device.DeviceId)).Data!).SessionToken;
        var phone = Pair(true); var viewer = Pair(false);
        var token = Session(phone); long sequence = 0;
        Reply Do(string operation, string? instance = null, string? confirmation = null) =>
            authority.Handle(token, new(1, ++sequence, operation, instance, confirmation));
        check(Do("launcher.open").Code == "recovery_not_permitted" && launches == 0,
            "rescue permission alone cannot launch local programs");
        actions.Permit(phone.DeviceId, true);
        check(Do("launcher.open").Code == "launcher_not_configured" && launches == 0,
            "launcher must be configured locally");
        actions.Configure(new("XIVLauncher.exe", "local-hash"));
        check(Do("launcher.open", slot.InstanceId).Code == "invalid_request" &&
            Do("launcher.open", confirmation: "argument").Code == "invalid_request", "launcher accepts no remote target or arguments");
        check(Do("launcher.open").Success && launches == 1,
            "authenticated locally permitted phone launches without a live bridge or gamepad");
        check(Do("launcher.open").Code == "cooldown" && launches == 1,
            "repeated recovery actions consume a per-device cooldown budget");
        var oldToken = token; token = Session(phone);
        check(authority.Handle(oldToken, new(1, 100, "launcher.open")).Code == "unauthorized",
            "replaced recovery sessions cannot launch");
        check(Do("launcher.open").Code == "cooldown" && launches == 1,
            "recovery reconnect does not reset action cooldown");
        check(authority.Handle(Session(viewer), new(1, 1, "launcher.open")).Code == "control_not_authorized",
            "view-only phone cannot launch");
        token = Session(phone);
        check(Do("game.restore", other.InstanceId).Code == "instance_not_authorized" && restores == 0,
            "window recovery cannot restore another granted target");
        check(Do("shell.open").Code == "operation_not_allowed", "arbitrary executable operations remain denied");
        // Direct dispatcher also enforces the separate local grant and fixed action set.
        check(actions.Execute(viewer.DeviceId, "launcher.open", null).Code == "recovery_not_permitted",
            "recovery dispatcher denies unpermitted devices");
        check(actions.Execute(phone.DeviceId, "game.restore", binding).Success && restores == 1,
            "window dispatcher receives only its verified saved binding");
        check(actions.Execute(phone.DeviceId, "shell.open", null).Code == "operation_not_allowed",
            "recovery dispatcher admits only fixed operations");
        actions.Clear();
        check(actions.Execute(phone.DeviceId, "launcher.open", null).Code == "recovery_not_permitted",
            "host shutdown clears recovery permissions");
        actions.Permit(phone.DeviceId, true);
        admin.Revoke(phone.DeviceId);
        check(Do("launcher.open").Code == "unauthorized" && launches == 1,
            "revoked phone cannot launch even if local permission remained");
        admin.SetEnabled(false);
        check(Do("launcher.open").Code == "remote_disabled", "emergency disable covers independent launcher action");
    }
    private sealed class Store : IStateStore
    {
        private RemoteState state = new();
        public T Access<T>(Func<RemoteState, (T Result, bool Save)> action)
        {
            var clone = JsonSerializer.Deserialize<RemoteState>(JsonSerializer.Serialize(state, Protocol.Json), Protocol.Json)!;
            var (result, save) = action(clone);
            if (save) state = clone;
            return result;
        }
    }
}
