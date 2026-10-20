using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using SentinelRelay.Remote;
using SentinelRelay.RemoteHost;

static class ManualInputTests
{
    public static async Task Run(Action<bool, string> check)
    {
        var store = new MemoryStore();
        var admin = new LocalAdministration(store); admin.SetEnabled(true);
        var slot = admin.AddInstance("One"); var other = admin.AddInstance("Other");
        var binding = new GameBinding(slot.InstanceId, 123, 123, "game.exe", "cid");
        admin.Bind(binding);
        var seen = new ManualTarget.ObservedProcess(binding.ProcessId, binding.StartedUtcTicks, binding.Executable);
        check(ManualTarget.MatchesOnlyProcess(binding, [seen], binding.ProcessId), "single matching foreground process allowed");
        check(!ManualTarget.MatchesOnlyProcess(binding, [seen, new(456, 999, "unbound.exe")], binding.ProcessId),
            "an unbound second FFXIV process vetoes manual input");
        check(!ManualTarget.MatchesOnlyProcess(binding, null, binding.ProcessId), "unknown process visibility vetoes manual input");
        check(!ManualTarget.MatchesOnlyProcess(binding, [seen with { StartedUtcTicks = 999 }], binding.ProcessId),
            "reused PID cannot acquire manual input");
        check(!ManualTarget.MatchesOnlyProcess(binding, [seen with { Executable = "other.exe" }], binding.ProcessId),
            "wrong executable cannot acquire manual input");
        check(!ManualTarget.MatchesOnlyProcess(binding, [seen], 0), "screen lock or foreground loss vetoes manual input");
        var authority = new RemoteAuthority(store);
        DeviceCredential Pair(bool control, string instance) => (DeviceCredential)authority.Enroll(
            new(1, admin.Invite("Phone", [instance], control))).Data!;
        var device = Pair(true, slot.InstanceId);
        var viewer = Pair(false, slot.InstanceId);
        var wrong = Pair(true, other.InstanceId);
        string Session(DeviceCredential d) => ((SessionCredential)authority.OpenSession(d.DeviceToken, new(1, d.DeviceId)).Data!).SessionToken;
        var session = Session(device);
        check(authority.InputOwner(session, slot.InstanceId) is not null, "input uses a live granted process binding");
        check(authority.InputOwner(Session(viewer), slot.InstanceId) is null, "input denies view-only grant");
        check(authority.InputOwner(Session(wrong), slot.InstanceId) is null, "input denies cross-instance grant");
        check(authority.InputOwner("bad", slot.InstanceId) is null, "input denies malformed credential");
        var oldOwner = authority.InputOwner(session, slot.InstanceId)!;
        session = Session(device);
        check(!authority.InputAllowed(oldOwner), "input reconnect invalidates old session owner");
        var output = new FakeOutput(); var target = new FakeTarget();
        using var input = new ManualInput(authority, _ => Task.FromResult<IManualTarget>(target),
            () => Task.FromResult<IManualOutput>(output = new FakeOutput()));
        var poll = new InputPoll(1, 1, Protocol.Secret());
        InputFrame Frame(InputPoll p) => new(1, p.Sequence, p.Challenge, .5, -.5, .25, -.25, .75, .25, 0x1201);
        check(ManualInput.Accept(Frame(poll), poll, TimeSpan.FromMilliseconds(249)), "input accepts fresh bounded complete state");
        check(!ManualInput.Accept(Frame(poll), poll, TimeSpan.FromMilliseconds(250)), "input challenge age boundary is monotonic and strict");
        check(!ManualInput.Accept(Frame(poll) with { Sequence = 2 }, poll, TimeSpan.Zero), "input rejects out-of-order sequence");
        check(!ManualInput.Accept(Frame(poll) with { Challenge = Protocol.Secret() }, poll, TimeSpan.Zero), "input rejects wrong freshness challenge");
        check(!ManualInput.Accept(Frame(poll) with { Buttons = 0x400 }, poll, TimeSpan.Zero), "input denies reserved buttons");
        check(!ManualInput.Accept(Frame(poll) with { LeftX = double.NaN }, poll, TimeSpan.Zero), "input denies nonfinite axes");

        await using var web = RemoteWeb.Create(authority, "pc.tail.ts.net", "test@example.invalid", 0, input);
        await web.StartAsync();
        var address = web.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        async Task<ClientWebSocket> Connect(string token)
        {
            var socket = new ClientWebSocket();
            socket.Options.SetRequestHeader("Host", "pc.tail.ts.net");
            socket.Options.SetRequestHeader("Tailscale-User-Login", "test@example.invalid");
            socket.Options.SetRequestHeader("Authorization", "Bearer " + token);
            await socket.ConnectAsync(new Uri(address.Replace("http:", "ws:") + "/remote/v1/gamepad"), CancellationToken.None);
            await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new InputHello(1, slot.InstanceId), Protocol.Json),
                WebSocketMessageType.Text, true, CancellationToken.None);
            return socket;
        }
        async Task<InputPoll?> Read(ClientWebSocket socket)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var buffer = new byte[1024];
                var r = await socket.ReceiveAsync(buffer.AsMemory(), timeout.Token);
                return r.MessageType == WebSocketMessageType.Text
                    ? JsonSerializer.Deserialize<InputPoll>(buffer.AsSpan(0, r.Count), Protocol.Json) : null;
            }
            catch { return null; }
        }
        async Task Send(ClientWebSocket socket, InputFrame frame)
        {
            // Frame is an internal convenience property, not a phone field.
            var body = new { frame.Version, frame.Sequence, frame.Challenge, frame.LeftX, frame.LeftY,
                frame.RightX, frame.RightY, frame.LeftTrigger, frame.RightTrigger, frame.Buttons };
            await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(body, Protocol.Json), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        async Task Wait(Func<bool> condition)
        {
            var until = DateTime.UtcNow.AddSeconds(3);
            while (!condition() && DateTime.UtcNow < until) await Task.Delay(20);
        }
        using (var denied = await Connect(session))
            check(await Read(denied) is null, "websocket rescue grant alone does not allow gamepad");
        input.Permit(device.DeviceId, true);
        using (var good = await Connect(session))
        {
            var p = await Read(good); check(p is not null, "authorized locally opted-in websocket receives freshness poll");
            await Send(good, Frame(p!));
            check(await Read(good) is not null && output.Last.LeftX == .5 && output.Last.Buttons == 0x1201, "real websocket routes complete state through lease");
            // Keep the pipe open and stop replying: held controls must release.
            await Wait(() => output.Disposed);
            check(output.Disposed && output.Last == ControllerFrame.Neutral, "silent websocket independently neutralizes and disposes output");
        }
        await Wait(() => !input.Active);
        using (var replay = await Connect(session))
        {
            var p = (await Read(replay))!;
            await Send(replay, Frame(p));
            var next = await Read(replay); check(next is not null, "fresh input renews only one current challenge");
            await Send(replay, Frame(p));
            await Wait(() => output.Disposed);
            check(output.Disposed && output.Last == ControllerFrame.Neutral, "replayed challenge releases held websocket controls");
        }
        await Wait(() => !input.Active);
        using (var local = await Connect(session))
        {
            var p = (await Read(local))!; await Send(local, Frame(p)); await Read(local);
            input.Stop(); await Wait(() => output.Disposed);
            check(output.Disposed && output.Last == ControllerFrame.Neutral, "local stop releases held controls and clears manual permissions");
        }
        await Wait(() => !input.Active);
        using (var denied = await Connect(session))
            check(await Read(denied) is null, "local stop requires a new explicit PC permission");
        input.Permit(device.DeviceId, true);
        using (var changed = await Connect(session))
        {
            var p = (await Read(changed))!; await Send(changed, Frame(p)); await Read(changed);
            target.Valid = false;
            await Wait(() => output.Disposed);
            check(output.Disposed && output.Last == ControllerFrame.Neutral, "target second-client/focus/identity veto neutralizes held output");
        }
        await Wait(() => !input.Active); target.Valid = true;
        using (var revoked = await Connect(session))
        {
            var p = (await Read(revoked))!; await Send(revoked, Frame(p)); await Read(revoked);
            admin.Revoke(device.DeviceId);
            await Wait(() => output.Disposed);
            check(output.Disposed && output.Last == ControllerFrame.Neutral, "live device revocation releases held websocket input");
        }
        await web.StopAsync();
    }
    private sealed class FakeTarget : IManualTarget
    {
        public volatile bool Valid = true;
        public string? GameEpoch => Valid ? "game-epoch" : null;
        public bool Allowed(ControllerOwner owner) => Valid && owner.GameEpoch == "game-epoch";
        public void Dispose() { }
    }
    private sealed class FakeOutput : IManualOutput
    {
        public ControllerFrame Last;
        public volatile bool Disposed;
        public void Send(ControllerFrame f) { Last = f; }
        public void Neutralize() { Last = ControllerFrame.Neutral; }
        public void Dispose() { Last = ControllerFrame.Neutral; Disposed = true; }
    }
}
