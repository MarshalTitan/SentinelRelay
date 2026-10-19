using System.Text.Json;
using SentinelRelay.Remote;
using SentinelRelay.RemoteHost;

try
{
    var stateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sentinel", "Remote");
    var store = new FileStateStore(stateDirectory);
    var admin = new LocalAdministration(store);
    object? result;
    switch (args)
    {
        case ["enable"]: admin.SetEnabled(true); result = new { Enabled = true }; break;
        case ["disable"]: admin.SetEnabled(false); result = new { Enabled = false }; break;
        case ["status"]: result = admin.Status(); break;
        case ["add-instance", var label]: result = admin.AddInstance(label); break;
        case ["bind", var instance, var pid] when int.TryParse(pid, out var processId):
            admin.Bind(PipeGameBridge.Enroll(instance, processId)); result = new { Bound = instance }; break;
        case ["pair", var label, var permission, var grants] when permission is "view" or "control":
            // The invitation is intentionally printed once locally, never in host logs.
            result = new { Invitation = admin.Invite(label, grants.Split(','), permission == "control"), ExpiresInSeconds = 120 }; break;
        case ["revoke", var id]: result = new { Revoked = admin.Revoke(id) }; break;
        case ["serve", var host, var user]:
            var app = RemoteWeb.Create(new RemoteAuthority(store, gameBridge: new PipeGameBridge()), host, user);
            Console.WriteLine("Sentinel Remote listening on loopback. Only locally bound, opted-in game clients are available.");
            await app.RunAsync(); return;
        default:
            Console.WriteLine("SentinelRemoteHost status | enable | disable | add-instance LABEL | bind INSTANCE_ID GAME_PID | pair LABEL view|control INSTANCE_ID[,ID] | revoke DEVICE_ID | serve HOST.ts.net TAILSCALE_LOGIN[,LOGIN]");
            return;
    }
    Console.WriteLine(JsonSerializer.Serialize(result, Protocol.Json));
}
catch
{
    Console.Error.WriteLine("Sentinel Remote stopped: local configuration/state unavailable or invalid. No remote action executed.");
    Environment.ExitCode = 1;
}
