using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using SentinelRelay.Remote;
using SentinelRelay.RemoteHost;

static class DesktopStreamTests
{
    public static async Task Run(Action<bool, string> check)
    {
        var store = new MemoryStore(); var admin = new LocalAdministration(store);
        admin.SetEnabled(true);
        var slot = admin.AddInstance("Saved character"); // No game binding or live game.
        var authority = new RemoteAuthority(store);
        DeviceCredential Pair(bool control) => (DeviceCredential)authority.Enroll(new(1,
            admin.Invite("Phone", [slot.InstanceId], control))).Data!;
        string Session(DeviceCredential device) => ((SessionCredential)authority.OpenSession(device.DeviceToken, new(1, device.DeviceId)).Data!).SessionToken;
        var phone = Pair(true); var viewer = Pair(false); var token = Session(phone);
        check(authority.DesktopSession(token) is not null && authority.InputOwner(token, slot.InstanceId) is null,
            "desktop authorization works before any game process or bridge exists");
        check(authority.DesktopSession(Session(viewer)) is null, "view-only character permission cannot authorize full desktop");
        var owner = authority.DesktopSession(token)!;
        var adapter = new FakeAdapter();
        using var desktop = new DesktopStream(authority, adapter);
        var fresh = Protocol.Secret();
        check(DesktopStream.Valid(new(1, 1, "tap", fresh, .5, .5)), "normalized desktop tap accepted");
        check(!DesktopStream.Valid(new(1, 1, "tap", fresh, double.NaN, .5)) &&
            !DesktopStream.Valid(new(1, 1, "tap", fresh, 1.01, .5)), "nonfinite or out-of-screen desktop input rejected");
        check(!DesktopStream.Valid(new(1, 1, "key", fresh, Key: "shell")) &&
            !DesktopStream.Valid(new(1, 1, "text", fresh, Text: new string('a', 257))) &&
            !DesktopStream.Valid(new(1, 1, "text", fresh, Text: "a\nb")), "desktop keyboard schema is bounded and admits only known keys");
        check(!DesktopStream.Valid(new(1, 1, "frame", Text: "ignored")) &&
            !DesktopStream.Valid(new(1, 1, "tap", fresh, .5, .5, Text: "ignored")),
            "desktop requests reject mixed frame/input payloads");

        await using var web = RemoteWeb.Create(authority, "pc.tail.ts.net", "test@example.invalid", 0, desktop: desktop);
        await web.StartAsync();
        var address = web.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        async Task<ClientWebSocket> Connect(string session)
        {
            var socket = new ClientWebSocket();
            socket.Options.SetRequestHeader("Host", "pc.tail.ts.net");
            socket.Options.SetRequestHeader("Tailscale-User-Login", "test@example.invalid");
            socket.Options.SetRequestHeader("Authorization", "Bearer " + session);
            await socket.ConnectAsync(new Uri(address.Replace("http:", "ws:") + "/remote/v1/desktop"), CancellationToken.None);
            return socket;
        }
        async Task<JsonElement?> Read(ClientWebSocket socket)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                var bytes = new byte[600000]; var count = 0;
                while (true)
                {
                    var part = await socket.ReceiveAsync(bytes.AsMemory(count), timeout.Token);
                    if (part.MessageType != WebSocketMessageType.Text) return null;
                    count += part.Count;
                    if (part.EndOfMessage) return JsonDocument.Parse(bytes.AsMemory(0, count)).RootElement.Clone();
                }
            }
            catch { return null; }
        }
        async Task Send(ClientWebSocket socket, DesktopRequest request) =>
            await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(request, Protocol.Json), WebSocketMessageType.Text, true, CancellationToken.None);
        async Task WaitStopped()
        {
            var until = DateTime.UtcNow.AddSeconds(4);
            while (desktop.Active && DateTime.UtcNow < until) await Task.Delay(20);
        }
        using (var denied = await Connect(token))
        {
            var code = (await Read(denied))?.GetProperty("code").GetString();
            check(code == "desktop_not_permitted" && adapter.Captures == 0,
                "desktop route requires separate local per-phone permission before capture; observed " + (code ?? "closed"));
        }
        desktop.Permit(phone.DeviceId, true);
        using (var socket = await Connect(token))
        {
            check((await Read(socket))?.GetProperty("code").GetString() == "desktop_ready", "authenticated desktop socket starts without FFXIV");
            await Send(socket, new(1, 1, "frame"));
            var frame = (await Read(socket))!.Value.GetProperty("data").Deserialize<DesktopImage>(Protocol.Json)!;
            check(adapter.Captures == 1 && frame.FrameId == adapter.FrameId, "desktop route delivers bounded current frame");
            await Send(socket, new(1, 2, "tap", frame.FrameId, .25, .75));
            check((await Read(socket))?.GetProperty("code").GetString() == "desktop_applied" && adapter.Inputs == 1,
                "desktop tap dispatches once against the displayed frame");
            await Task.Delay(350);
            await Send(socket, new(1, 3, "frame"));
            await Send(socket, new(1, 4, "tap", frame.FrameId, .25, .75));
            await Read(socket);
            check((await Read(socket))?.GetProperty("code").GetString() == "desktop_applied" && adapter.Inputs == 2,
                "one capture in flight does not discard a click on the prior recent displayed frame");
            await Send(socket, new(1, 4, "tap", frame.FrameId, .25, .75));
            check((await Read(socket))?.GetProperty("code").GetString() == "desktop_request_rejected" && adapter.Inputs == 2,
                "replayed desktop request cannot repeat a click");
        }
        await WaitStopped();
        using (var socket = await Connect(token))
        {
            await Read(socket); await Send(socket, new(1, 1, "frame")); await Read(socket);
            await Send(socket, new(1, 2, "key", Protocol.Secret(), Key: "Enter"));
            check((await Read(socket))?.GetProperty("code").GetString() == "desktop_stale_frame" && adapter.Inputs == 2,
                "keyboard input cannot apply to a different or old view");
        }
        await WaitStopped();
        using (var socket = await Connect(token))
        {
            await Read(socket); await Send(socket, new(1, 1, "frame")); await Read(socket);
            desktop.Stop();
            check(await Read(socket) is null, "local desktop Stop closes an active view");
        }
        await WaitStopped();
        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var socket = await Connect(token);
            var code = (await Read(socket))?.GetProperty("code").GetString();
            check(code == "desktop_not_permitted",
                "local desktop Stop prevents reacquisition and rapid denied reconnect completes its handshake; observed " + (code ?? "closed"));
        }
        desktop.Permit(phone.DeviceId, true);
        adapter.Block = true;
        using (var socket = await Connect(token))
        {
            await Read(socket); await Send(socket, new(1, 1, "frame"));
            check(adapter.Started.Wait(2000), "capture began before in-flight revoke test");
            admin.Revoke(phone.DeviceId);
            adapter.Release.Set();
            check(await Read(socket) is null, "revocation suppresses a frame captured in flight");
        }
        await WaitStopped();
        check(!authority.DesktopAllowed(owner), "desktop lease rechecks persisted device revocation");
        var another = Pair(true); var anotherToken = Session(another);
        var oldOwner = authority.DesktopSession(anotherToken)!;
        Session(another);
        check(!authority.DesktopAllowed(oldOwner), "replaced session ends old desktop access");
        var current = authority.DesktopSession(Session(another))!;
        admin.SetEnabled(false); admin.SetEnabled(true);
        check(!authority.DesktopAllowed(current), "disable and enable between polls invalidates old desktop access");
    }
    private sealed class FakeAdapter : IDesktopAdapter
    {
        public int Captures, Inputs;
        public string FrameId = Protocol.Secret();
        public bool Block;
        public readonly ManualResetEventSlim Started = new(), Release = new();
        public DesktopImage Capture()
        {
            Interlocked.Increment(ref Captures);
            if (Block) { Started.Set(); if (!Release.Wait(3000)) throw new TimeoutException(); }
            FrameId = Protocol.Secret();
            return new(FrameId, 100, 100, "AA==", DateTimeOffset.UtcNow);
        }
        public bool Apply(DesktopRequest request) { Interlocked.Increment(ref Inputs); return true; }
    }
}
