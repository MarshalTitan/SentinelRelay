using System.Text.Json;
using SentinelRelay.Remote;

internal static class StatusWireTests
{
    public static void Run(Action<bool, string> check)
    {
        // This is the exact case-sensitive field read by the shipped Android 0.1.0 client.
        // Automatic camel casing turns C# SRank into sRank, which that client cannot read.
        var hunt = JsonSerializer.SerializeToElement(new
        {
            Loaded = true, Enabled = true, Character = "Test character", State = "Approaching",
            StatusText = "Approaching the reported coordinates", Territory = 815,
            ActiveHunt = "Tarchia", QueueCount = 2,
        }, Protocol.Json);
        var reply = new Reply(true, "status", new RemoteGameStatus("running", "connected",
            Character: "Test character", World: "Mateus", Territory: 815, Map: "Amh Araeng", SRank: hunt));
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(reply, Protocol.Json));
        var data = wire.RootElement.GetProperty("data");
        check(data.TryGetProperty("srank", out var srank) && srank.GetProperty("state").GetString() == "Approaching" &&
            srank.GetProperty("activeHunt").GetString() == "Tarchia" && srank.GetProperty("queueCount").GetInt32() == 2,
            "status wire exposes live SRank under the exact field consumed by shipped Android");
        check(data.GetProperty("profile").ValueKind == JsonValueKind.Null,
            "status wire keeps unsupported Profiles unknown rather than inventing a profile");
        using var unavailable = JsonDocument.Parse(JsonSerializer.Serialize(
            new Reply(true, "status", new RemoteGameStatus("running", "connected")), Protocol.Json));
        check(unavailable.RootElement.GetProperty("data").GetProperty("srank").ValueKind == JsonValueKind.Null,
            "status wire preserves unavailable SRank as unknown");
    }
}
