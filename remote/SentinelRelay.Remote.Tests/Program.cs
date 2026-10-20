using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using SentinelRelay.Remote;
using SentinelRelay.RemoteHost;

if (args is ["--test-input-child", var mode]) return await WorkerStartupTests.Child(mode);
var count = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL " + name);
    Console.WriteLine("PASS " + name); count++;
}
void Code(Reply reply, string expected, string name) => Check(reply.Code == expected, name + " (" + reply.Code + ")");
var clock = new TestClock();
var store = new MemoryStore();
var admin = new LocalAdministration(store, clock);
var authority = new RemoteAuthority(store, clock);
Code(authority.Enroll(new(1, Protocol.Secret())), "remote_disabled", "disabled by default");
admin.SetEnabled(true);
var a = admin.AddInstance("Wrothy");
var b = admin.AddInstance("Elektra");
DeviceCredential Pair(string label, string[] grants, bool control)
{
    var invitation = admin.Invite(label, grants, control);
    var reply = authority.Enroll(new(1, invitation));
    Check(reply.Success, "pair " + label);
    Code(authority.Enroll(new(1, invitation)), "pairing_rejected", "invitation is single-use");
    return (DeviceCredential)reply.Data!;
}
SessionCredential Open(DeviceCredential device) => (SessionCredential)authority.OpenSession(device.DeviceToken, new(1, device.DeviceId)).Data!;
var viewer = Pair("view phone", [a.InstanceId], false);
var controller = Pair("control phone", [a.InstanceId, b.InstanceId], true);
Code(authority.OpenSession(Protocol.Secret(), new(1, viewer.DeviceId)), "unauthorized", "wrong device credential");
Code(authority.OpenSession(viewer.DeviceToken, new(2, viewer.DeviceId)), "protocol_mismatch", "session version mismatch");
var view = Open(viewer);
var both = Open(controller);
Code(authority.Handle(null, new(1, 1, "instances.list")), "unauthorized", "anonymous denied");
var list = authority.Handle(view.SessionToken, new(1, 1, "instances.list"));
Check(list.Data is InstanceInfo[] { Length: 1 } rows && rows[0].InstanceId == a.InstanceId, "instance inventory filtered by local grants");
Code(authority.Handle(view.SessionToken, new(1, 2, "srank.status", b.InstanceId)), "instance_not_authorized", "cross-client request denied");
Code(authority.Handle(view.SessionToken, new(1, 3, "srank.skip", a.InstanceId)), "control_not_authorized", "view cannot mutate");
Code(authority.Handle(view.SessionToken, new(1, 4, "powershell", a.InstanceId)), "operation_not_allowed", "generic execution denied");
Code(authority.Handle(view.SessionToken, new(1, 4, "srank.status", a.InstanceId)), "replay_rejected", "denied requests consume sequence");
Code(authority.Handle(view.SessionToken, new(2, 5, "session.ping")), "protocol_mismatch", "request version mismatch");
Code(authority.Handle(view.SessionToken, new(1, 5, "screenshot", a.InstanceId)), "bridge_unavailable", "no false execution claims");
var concurrent = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => authority.Handle(both.SessionToken, new(1, 1, "session.ping")))));
Check(concurrent.Count(r => r.Success) == 1 && concurrent.Count(r => r.Code == "replay_rejected") == 19, "concurrent duplicate is accepted once");
var reset = authority.Handle(both.SessionToken, new(1, 2, "srank.reset.request", a.InstanceId));
var nonce = JsonSerializer.SerializeToElement(reset.Data, Protocol.Json).GetProperty("confirmation").GetString();
Code(authority.Handle(both.SessionToken, new(1, 3, "srank.reset.confirm", b.InstanceId, nonce)), "confirmation_rejected", "reset bound to instance");
Code(authority.Handle(both.SessionToken, new(1, 4, "srank.reset.confirm", a.InstanceId, nonce)), "confirmation_rejected", "bad confirmation consumed intent");
reset = authority.Handle(both.SessionToken, new(1, 5, "srank.reset.request", a.InstanceId));
nonce = JsonSerializer.SerializeToElement(reset.Data, Protocol.Json).GetProperty("confirmation").GetString();
Code(authority.Handle(view.SessionToken, new(1, 6, "srank.reset.confirm", a.InstanceId, nonce)), "control_not_authorized", "confirmation cannot transfer device");
Code(authority.Handle(both.SessionToken, new(1, 6, "srank.reset.confirm", a.InstanceId, nonce)), "bridge_unavailable", "confirmed reset reaches unavailable bridge once");
Code(authority.Handle(both.SessionToken, new(1, 7, "srank.reset.confirm", a.InstanceId, nonce)), "confirmation_rejected", "reset cannot repeat");
reset = authority.Handle(both.SessionToken, new(1, 8, "srank.reset.request", a.InstanceId));
nonce = JsonSerializer.SerializeToElement(reset.Data, Protocol.Json).GetProperty("confirmation").GetString();
clock.Advance(TimeSpan.FromSeconds(30));
Code(authority.Handle(both.SessionToken, new(1, 9, "srank.reset.confirm", a.InstanceId, nonce)), "confirmation_rejected", "reset expiry boundary");
var renewed = Open(controller);
Code(authority.Handle(both.SessionToken, new(1, 10, "session.ping")), "unauthorized", "reconnect invalidates old session");
clock.Advance(TimeSpan.FromSeconds(60));
Code(authority.Handle(renewed.SessionToken, new(1, 1, "session.ping")), "unauthorized", "idle timeout boundary");
var absolute = Open(controller);
for (int i = 1; i <= 5; i++)
{
    clock.Advance(TimeSpan.FromSeconds(50));
    Check(authority.Handle(absolute.SessionToken, new(1, i, "session.ping")).Success, "active session heartbeat " + i);
}
clock.Advance(TimeSpan.FromSeconds(50));
Code(authority.Handle(absolute.SessionToken, new(1, 6, "session.ping")), "unauthorized", "absolute lifetime cannot be extended");
var wallClockSession = Open(controller);
clock.Advance(TimeSpan.FromSeconds(60));
clock.MoveWallClock(TimeSpan.FromHours(-2));
Code(authority.Handle(wallClockSession.SessionToken, new(1, 1, "session.ping")), "unauthorized", "wall-clock rollback cannot extend session");
var expiredInvite = admin.Invite("expired", [a.InstanceId], false);
clock.Advance(TimeSpan.FromMinutes(2));
Code(authority.Enroll(new(1, expiredInvite)), "pairing_rejected", "invitation expiry boundary");
var revokedSession = Open(viewer);
admin.Revoke(viewer.DeviceId);
Code(authority.Handle(revokedSession.SessionToken, new(1, 1, "instances.list")), "unauthorized", "revocation ends existing sessions");
Code(authority.OpenSession(viewer.DeviceToken, new(1, viewer.DeviceId)), "unauthorized", "revocation prevents reconnect");
var disabledSession = Open(controller);
admin.SetEnabled(false);
Code(authority.Handle(disabledSession.SessionToken, new(1, 1, "session.ping")), "remote_disabled", "local emergency disable");
admin.SetEnabled(true);
Code(authority.Handle(disabledSession.SessionToken, new(1, 2, "session.ping")), "unauthorized", "enable does not restore old sessions");
Check(!JsonSerializer.Serialize(admin.Status(), Protocol.Json).Contains(controller.DeviceToken) &&
    !JsonSerializer.Serialize(authority.AuditSnapshot(), Protocol.Json).Contains(controller.DeviceToken), "admin status and audit exclude credentials");
var abuse = Open(controller);
for (var i = 1; i < 200; i++) authority.Handle(abuse.SessionToken, new(1, i, "untrusted-secret-body"));
Check(authority.AuditSnapshot().Length == 128 && !JsonSerializer.Serialize(authority.AuditSnapshot()).Contains("untrusted-secret-body"), "audit bounded and excludes arbitrary input");

var temp = Path.Combine(Path.GetTempPath(), "SentinelRemoteTest-" + Guid.NewGuid().ToString("N"));
try
{
    var disk = new FileStateStore(temp);
    var local = new LocalAdministration(disk, clock);
    local.SetEnabled(true);
    var instance = local.AddInstance("private installation");
    var invitation = local.Invite("disk phone", [instance.InstanceId], true);
    var first = new RemoteAuthority(disk, clock);
    var credential = (DeviceCredential)first.Enroll(new(1, invitation)).Data!;
    var session = (SessionCredential)first.OpenSession(credential.DeviceToken, new(1, credential.DeviceId)).Data!;
    var restarted = new RemoteAuthority(new FileStateStore(temp), clock);
    Code(restarted.Enroll(new(1, invitation)), "pairing_rejected", "consumption survives host restart");
    Code(restarted.Handle(session.SessionToken, new(1, 1, "session.ping")), "unauthorized", "restart drops sessions and intent");
    Check(restarted.OpenSession(credential.DeviceToken, new(1, credential.DeviceId)).Success, "device pairing survives restart");
    var raw = File.ReadAllText(Path.Combine(temp, "authority.json"));
    Check(!raw.Contains(invitation) && !raw.Contains(credential.DeviceToken) && !raw.Contains(session.SessionToken), "only credential hashes persisted");
    new LocalAdministration(new FileStateStore(temp)).Revoke(credential.DeviceId);
    Code(restarted.OpenSession(credential.DeviceToken, new(1, credential.DeviceId)), "unauthorized", "another local process revokes immediately");
    File.WriteAllText(Path.Combine(temp, "authority.json"), "{broken");
    try { restarted.OpenSession(credential.DeviceToken, new(1, credential.DeviceId)); Check(false, "corruption fails closed"); }
    catch (JsonException) { Check(true, "corruption fails closed"); }
}
finally { Directory.Delete(temp, true); }

// Real Kestrel boundary; tests emulate the trusted local Serve proxy, not external TLS.
var web = RemoteWeb.Create(authority, "sentinel.test.ts.net", "owner@example.test", 0);
await web.StartAsync();
try
{
    var url = web.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    using var client = new HttpClient { BaseAddress = new(url) };
    async Task<HttpResponseMessage> Send(string json, string path = "/remote/v1/request", string? token = null, bool proxy = true, string? origin = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        req.Headers.Host = "sentinel.test.ts.net";
        if (proxy) req.Headers.Add("Tailscale-User-Login", "owner@example.test");
        if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (origin is not null) req.Headers.Add("Origin", origin);
        return await client.SendAsync(req);
    }
    Check((await Send("{}", proxy: false)).StatusCode == HttpStatusCode.Forbidden, "unverified proxy requests denied");
    Check((await Send("{}", origin: "https://evil.test")).StatusCode == HttpStatusCode.Forbidden, "cross-origin request denied");
    Check((await Send("{broken")).StatusCode == HttpStatusCode.BadRequest, "malformed JSON rejected");
    Check((await Send("{\"version\":1,\"sequence\":1,\"operation\":\"session.ping\",\"shell\":\"secret\"}")).StatusCode == HttpStatusCode.BadRequest, "unknown fields rejected");
    Check((await Send("{\"version\":1,\"sequence\":1,\"operation\":null}")).StatusCode == HttpStatusCode.BadRequest, "null operation rejected");
    Check((await Send("{}", path: "/remote/v1/request?token=secret")).StatusCode == HttpStatusCode.BadRequest, "query strings rejected");
    Check((await Send("{}", path: "/remote/v1/admin/enable")).StatusCode == HttpStatusCode.NotFound, "no remote administration route");
    Check((await Send(JsonSerializer.Serialize(new RemoteRequest(1, 1, "session.ping"), Protocol.Json))).StatusCode == HttpStatusCode.Unauthorized, "HTTP anonymous request denied");
    var httpInvite = admin.Invite("HTTP phone", [a.InstanceId], false);
    var response = await Send(JsonSerializer.Serialize(new Enrollment(1, httpInvite), Protocol.Json), "/remote/v1/pair");
    Check(response.IsSuccessStatusCode && response.Headers.CacheControl?.NoStore == true, "HTTP pairing response never cached");
    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    var httpDevice = document.RootElement.GetProperty("data").Deserialize<DeviceCredential>(Protocol.Json)!;
    response = await Send(JsonSerializer.Serialize(new SessionRequest(1, httpDevice.DeviceId), Protocol.Json), "/remote/v1/session", httpDevice.DeviceToken);
    using var sessionDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    var httpSession = sessionDocument.RootElement.GetProperty("data").Deserialize<SessionCredential>(Protocol.Json)!;
    var requestJson = JsonSerializer.Serialize(new RemoteRequest(1, 1, "instances.list"), Protocol.Json);
    Check((await Send(requestJson, token: httpSession.SessionToken)).IsSuccessStatusCode, "HTTP authenticated session works");
    Check((await Send(requestJson, token: httpSession.SessionToken)).StatusCode == HttpStatusCode.BadRequest, "HTTP replay rejected");
    Check((await Send("{\"pad\":\"" + new string('x', 5000) + "\"}")).StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge, "body size bounded");
    Check((await Send(JsonSerializer.Serialize(new RemoteRequest(1, 2, "session.ping"), Protocol.Json), token: httpSession.SessionToken)).IsSuccessStatusCode, "valid request reconnects after oversized body rejection");
    for (int i = 0; i < 10; i++) response = await Send(JsonSerializer.Serialize(new Enrollment(1, Protocol.Secret()), Protocol.Json), "/remote/v1/pair");
    Check(response.StatusCode == HttpStatusCode.TooManyRequests, "pairing rate limit enforced");
}
finally { await web.StopAsync(); await web.DisposeAsync(); }
await BridgeTests.Run(Check);
await PipeServerTests.Run(Check);
await DesktopSetupTests.Run(Check);
StatusWireTests.Run(Check);
ControllerLeaseTests.Run(Check);
await ManualInputTests.Run(Check);
RecoveryTests.Run(Check);
await DesktopStreamTests.Run(Check);
await WorkerStartupTests.Run(Check);
Console.WriteLine($"{count} remote foundation checks passed.");
return 0;

sealed class TestClock : TimeProvider
{
    private DateTimeOffset now = new(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => now;
    private long timestamp;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => timestamp;
    public void Advance(TimeSpan duration) { now += duration; timestamp += duration.Ticks; }
    public void MoveWallClock(TimeSpan duration) => now += duration;
}
sealed class MemoryStore : IStateStore
{
    private RemoteState state = new();
    private readonly object gate = new();
    public T Access<T>(Func<RemoteState, (T Result, bool Save)> action)
    {
        lock (gate)
        {
            var copy = JsonSerializer.Deserialize<RemoteState>(JsonSerializer.Serialize(state, Protocol.Json), Protocol.Json)!;
            var (result, save) = action(copy);
            if (save) state = copy;
            return result;
        }
    }
}
