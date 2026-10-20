using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using SentinelRelay.Remote;
using SentinelRelay.RemoteHost;
using SentinelRelay.RemoteInput;

static class ManualInputTests
{
    public static async Task Run(Action<bool, string> check)
    {
        var store = new SlowStore();
        var admin = new LocalAdministration(store); admin.SetEnabled(true);
        var slot = admin.AddInstance("One"); var other = admin.AddInstance("Other");
        var binding = new GameBinding(slot.InstanceId, 123, 123, "game.exe", "cid");
        admin.Bind(binding);
        var seen = new ManualTarget.ObservedProcess(binding.ProcessId, binding.StartedUtcTicks, binding.Executable);
        check(ManualTarget.MatchesOnlyProcess(binding, [seen], true), "single matching process allowed regardless of window focus");
        check(!ManualTarget.MatchesOnlyProcess(binding, [seen, new(456, 999, "unbound.exe")], true),
            "an unbound second FFXIV process vetoes manual input");
        check(!ManualTarget.MatchesOnlyProcess(binding, null, true), "unknown process visibility vetoes manual input");
        check(!ManualTarget.MatchesOnlyProcess(binding, [seen with { StartedUtcTicks = 999 }], true),
            "reused PID cannot acquire manual input");
        check(!ManualTarget.MatchesOnlyProcess(binding, [seen with { Executable = "other.exe" }], true),
            "wrong executable cannot acquire manual input");
        check(ManualTarget.MatchesOnlyProcess(binding, [seen], true), "background or minimized window does not veto the verified game");
        check(!ManualTarget.MatchesOnlyProcess(binding, [seen], false), "unavailable interactive desktop vetoes manual input");
        check(InteractiveDesktop.Matches(1, 1, true, "Default", "Default"), "ordinary desktop permits background input");
        check(!InteractiveDesktop.Matches(1, 1, true, "Default", "Winlogon"), "locked or secure desktop vetoes input");
        check(!InteractiveDesktop.Matches(1, 1, false, "Default", "Default"), "disconnected Windows session vetoes input");
        check(!InteractiveDesktop.Matches(1, 2, true, "Default", "Default"), "another Windows session cannot receive input");
        check(!InteractiveDesktop.Matches(1, 1, true, "Default", null), "unknown input desktop vetoes input");
        check(!InteractiveDesktop.Matches(0, 0, true, "Default", "Default"), "service session cannot receive input");
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
        check(ManualTarget.ExplainProcess(binding, [], true) == "target_no_game", "diagnostic distinguishes no game");
        check(ManualTarget.ExplainProcess(binding, [seen], false) == "target_desktop_unavailable", "diagnostic distinguishes lock or desktop loss");
        check(ManualTarget.ExplainProcess(binding, [seen, seen], true) == "target_extra_game", "diagnostic distinguishes extra game");
        var output = new FakeOutput(); var target = new FakeTarget();
        string? startupFailure = null;
        using var input = new ManualInput(authority, _ => Task.FromResult<IManualTarget>(target),
            () => startupFailure is null ? Task.FromResult<IManualOutput>(output = new FakeOutput()) :
                Task.FromException<IManualOutput>(new InputBackendException(startupFailure)));
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
        check(input.Diagnostic.Code == "manual_not_permitted", "missing local manual permission has a stable startup diagnostic");
        input.Permit(device.DeviceId, true);
        store.Delay = 12;
        using (var sustained = await Connect(session))
        {
            var current = await Read(sustained);
            var accepted = 0;
            while (current is not null && accepted < 30)
            {
                await Send(sustained, Frame(current));
                current = await Read(sustained);
                if (current is not null) accepted++;
            }
            check(accepted == 30 && input.Diagnostic.AcceptedFrames >= 30,
                "sustained input survives overlapping watchdog and frame authorization with slow state reads");
        }
        await Wait(() => !input.Active);
        store.Delay = 0;
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
        using (var extra = await Connect(session))
        {
            var p = (await Read(extra))!; await Send(extra, Frame(p)); var next = (await Read(extra))!;
            var body = JsonSerializer.Serialize(new { version = 1, sequence = next.Sequence, challenge = next.Challenge,
                leftX = .5, leftY = 0, rightX = 0, rightY = 0, leftTrigger = 0, rightTrigger = 0, buttons = 0, frame = new { key = "denied" } }, Protocol.Json);
            await extra.SendAsync(Encoding.UTF8.GetBytes(body), WebSocketMessageType.Text, true, CancellationToken.None);
            await Wait(() => output.Disposed);
            check(output.Disposed && output.Last == ControllerFrame.Neutral, "unknown input fields release held controls");
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
        using (var removed = await Connect(session))
        {
            var p = (await Read(removed))!; await Send(removed, Frame(p)); await Read(removed);
            input.Permit(device.DeviceId, false);
            await Wait(() => output.Disposed && !input.Active);
            check(output.Disposed && input.Diagnostic.Code == "permission_removed",
                "per-phone permission removal releases held output and preserves its diagnostic");
        }
        input.Permit(device.DeviceId, true);
        startupFailure = "backend_attach_failed";
        using (var failed = await Connect(session)) check(await Read(failed) is null, "backend startup failure closes phone connection");
        check(input.Diagnostic.Code == "backend_attach_failed", "backend attach failure survives cleanup in the PC diagnostic");
        startupFailure = "private_arbitrary_backend_text";
        using (var failed = await Connect(session)) check(await Read(failed) is null, "unknown backend failure closes phone connection");
        check(input.Diagnostic.Code == "input_backend_unavailable" &&
            !JsonSerializer.Serialize(input.Diagnostic).Contains("private_arbitrary_backend_text"),
            "diagnostic excludes arbitrary backend details");
        startupFailure = null;
        using (var revoked = await Connect(session))
        {
            var p = (await Read(revoked))!; await Send(revoked, Frame(p)); await Read(revoked);
            admin.Revoke(device.DeviceId);
            await Wait(() => output.Disposed);
            check(output.Disposed && output.Last == ControllerFrame.Neutral, "live device revocation releases held websocket input");
        }
        await web.StopAsync();
    }
    private sealed class SlowStore : IStateStore
    {
        private readonly MemoryStore inner = new();
        public volatile int Delay;
        public T Access<T>(Func<RemoteState, (T Result, bool Save)> action) => inner.Access(state =>
        {
            if (Delay > 0) Thread.Sleep(Delay); // Model real on-disk authority read latency.
            return action(state);
        });
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
