using System.Buffers.Binary;
using System.Text.Json;
using SentinelRelay.Bridge;
using SentinelRelay.Remote;

static class BridgeTests
{
    public static async Task Run(Action<bool, string> check)
    {
        var snapshot = new BridgeSnapshot("cid:A", "game-one", "First Character", "World", 1, "Map", DateTimeOffset.UtcNow, null, true, true);
        check(BridgeWire.Reject(new(1, "srank.skip", "cid:B", "game-one"), snapshot) == "character_changed", "bridge rejects wrong character");
        check(BridgeWire.Reject(new(1, "srank.skip", "cid:A", "old-game"), snapshot) == "character_changed", "bridge rejects retired game epoch");
        check(BridgeWire.Reject(new(1, "srank.skip", "cid:A", "game-one"), snapshot with { ControlAllowed = false }) == "control_not_authorized", "plugin control opt-in independent of host grant");
        check(BridgeWire.Reject(new(1, "screenshot", "cid:A", "game-one"), snapshot with { ScreenshotAllowed = false }) == "screenshot_disabled", "plugin screenshot opt-in independent of host grant");
        check(BridgeWire.Reject(new(1, "powershell"), snapshot) == "protocol_mismatch", "bridge rejects generic commands");
        check(BridgeWire.Reject(new(1, "hello"), null) == "plugin_disabled", "bridge denies disabled plugin");
        using (var stream = new MemoryStream())
        {
            await BridgeWire.Write(stream, new BridgeReply(true, "status", snapshot), 16384, CancellationToken.None);
            stream.Position = 0;
            var decoded = await BridgeWire.Read<BridgeReply>(stream, 16384, CancellationToken.None);
            check(decoded.Snapshot?.GameEpoch == "game-one" && decoded.CaptureWindow == 0, "bounded bridge frame round trip");
        }
        using (var stream = new MemoryStream())
        {
            var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, int.MaxValue); stream.Write(header); stream.Position = 0;
            try { await BridgeWire.Read<BridgeRequest>(stream, 4096, CancellationToken.None); check(false, "oversize bridge frame"); }
            catch (InvalidDataException) { check(true, "oversize bridge frame rejected before allocation"); }
        }
        var store = new MemoryStore(); var admin = new LocalAdministration(store); admin.SetEnabled(true);
        var first = admin.AddInstance("First"); var second = admin.AddInstance("Second");
        var binding = new GameBinding(first.InstanceId, 1234, 123, "approved.exe", "cid:A"); admin.Bind(binding);
        try { admin.Bind(binding with { InstanceId = second.InstanceId }); check(false, "duplicate process binding"); }
        catch (InvalidOperationException) { check(true, "one process cannot bind to two slots"); }
        var fake = new FakeBridge { Snapshot = snapshot };
        var authority = new RemoteAuthority(store, gameBridge: fake);
        var invitation = admin.Invite("Phone", [first.InstanceId], true);
        var device = (DeviceCredential)authority.Enroll(new(1, invitation)).Data!;
        string Session() => ((SessionCredential)authority.OpenSession(device.DeviceToken, new(1, device.DeviceId)).Data!).SessionToken;
        var session = Session();
        var pending = authority.Handle(session, new(1, 1, "srank.reset.request", first.InstanceId));
        var nonce = JsonSerializer.SerializeToElement(pending.Data, Protocol.Json).GetProperty("confirmation").GetString();
        fake.Snapshot = snapshot with { GameEpoch = "game-two" };
        check(authority.Handle(session, new(1, 2, "srank.reset.confirm", first.InstanceId, nonce)).Code == "character_changed" && fake.Executions == 0,
            "reset intent cannot cross plugin reconnect");
        fake.Snapshot = snapshot;
        check(authority.Handle(session, new(1, 3, "srank.skip", second.InstanceId)).Code == "instance_not_authorized" && fake.Executions == 0,
            "unauthorized client never reaches bridge");
        check(authority.Handle(session, new(1, 4, "srank.skip", first.InstanceId)).Success && fake.Executions == 1, "authorized fixed operation reaches bridge once");
        check(authority.Handle(session, new(1, 4, "srank.skip", first.InstanceId)).Code == "replay_rejected" && fake.Executions == 1,
            "replay cannot repeat bridge execution");
        fake.BeforeExecute = () => admin.Revoke(device.DeviceId);
        check(!authority.Handle(session, new(1, 5, "srank.skip", first.InstanceId)).Success && fake.Executions == 1,
            "revocation between authorization and dispatch blocks execution");
        var secondInvitation = admin.Invite("New phone", [first.InstanceId], true);
        var newDevice = (DeviceCredential)authority.Enroll(new(1, secondInvitation)).Data!;
        var newSession = ((SessionCredential)authority.OpenSession(newDevice.DeviceToken, new(1, newDevice.DeviceId)).Data!).SessionToken;
        admin.Bind(binding with { StartedUtcTicks = 999 });
        check(authority.Handle(newSession, new(1, 1, "srank.skip", first.InstanceId)).Code == "unauthorized", "local process rebind invalidates existing sessions");
        var limitedSession = ((SessionCredential)authority.OpenSession(newDevice.DeviceToken, new(1, newDevice.DeviceId)).Data!).SessionToken;
        fake.BeforeExecute = null;
        for (var n = 1; n <= 5; n++) authority.Handle(limitedSession, new(1, n, "srank.skip", first.InstanceId));
        var reconnect = ((SessionCredential)authority.OpenSession(newDevice.DeviceToken, new(1, newDevice.DeviceId)).Data!).SessionToken;
        check(authority.Handle(reconnect, new(1, 1, "srank.skip", first.InstanceId)).Code == "cooldown", "mutation budget cannot be reset by reconnect");
    }
    private sealed class FakeBridge : IGameBridge
    {
        public BridgeSnapshot? Snapshot;
        public int Executions;
        public Action? BeforeExecute;
        public BridgeSnapshot? Read(GameBinding binding) => Snapshot;
        public Reply Status(GameBinding binding) => new(true, "status");
        public Reply Execute(GameBinding binding, string operation, string? expectedGameEpoch, Func<bool> authorized, CancellationToken cancellation)
        {
            BeforeExecute?.Invoke();
            if (!authorized()) return new(false, "unauthorized");
            if (expectedGameEpoch is not null && expectedGameEpoch != Snapshot?.GameEpoch) return new(false, "character_changed");
            Executions++; return new(true, "completed");
        }
    }
}
