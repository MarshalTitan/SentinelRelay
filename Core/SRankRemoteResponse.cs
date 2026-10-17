using System.Text.Json;
using SentinelRelay.Models;

namespace SentinelRelay.Core;

public sealed record SRankRemoteResponse(bool Success, string Reason, SRankRemoteStatus? Status, string Version = "unknown")
{
    public static SRankRemoteResponse Unavailable => new(false, "Unavailable", null);
    public static SRankRemoteResponse Decode(string? json)
    {
        try
        {
            if (json is null || json.Length > 8192) return Unavailable;
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.GetProperty("Schema").GetString() != "sentinel.srank.remote.v1") return Unavailable;
            var success = root.GetProperty("Success").GetBoolean();
            var reason = Text(root, "Reason", 80);
            if (reason is not ("Status" or "SkipRequested" or "ResetRequested" or "FrameworkThreadRequired" or
                "Unavailable" or "WrongCharacter" or "CharacterNotReady" or "SentinelDisabled" or
                "RecoveryAlreadyInProgress" or "NoActiveHunt" or "UnsupportedOperation" or "RecoveryFailed"))
                return Unavailable;
            SRankRemoteStatus? status = null;
            if (root.TryGetProperty("Status", out var value) && value.ValueKind == JsonValueKind.Object)
                status = new(value.GetProperty("Loaded").GetBoolean(), value.GetProperty("Enabled").GetBoolean(),
                    Text(value, "Character", 80), Text(value, "State", 80), Text(value, "StatusText", 300),
                    value.GetProperty("Territory").GetUInt32(), Text(value, "ActiveHunt", 80),
                    Math.Clamp(value.GetProperty("QueueCount").GetInt32(), 0, 10000));
            if (success && status is not { Loaded: true }) return Unavailable;
            return new(success, reason, status, Text(root, "Version", 40));
        }
        catch { return Unavailable; }
    }
    private static string Text(JsonElement source, string key, int limit)
        => MessageSanitizer.SanitizeForDiscord(source.GetProperty(key).GetString())[..Math.Min(limit,
            MessageSanitizer.SanitizeForDiscord(source.GetProperty(key).GetString()).Length)];
}

public sealed record SRankRemoteStatus(bool Loaded, bool Enabled, string Character, string State,
    string StatusText, uint Territory, string ActiveHunt, int QueueCount);

public static class SRankRemoteResponseFormatter
{
    public static DiscordWebhookPayload Format(string character, SRankRemoteDecision decision, SRankRemoteResponse response)
    {
        var title = "S Rank Sentinel — " + MessageSanitizer.SanitizeForDiscord(character);
        string body;
        if (response.Reason == "Unavailable") body = "S Rank Sentinel is unavailable on this client.";
        else if (!response.Success) body = "Request rejected: " + MessageSanitizer.SanitizeForDiscord(response.Reason);
        else body = decision switch
        {
            SRankRemoteDecision.Skip => "Current hunt skip accepted; queue preserved. Recovery through Ul'dah is pending.",
            SRankRemoteDecision.Reset => "STOP + RESET THROUGH UL'DAH accepted; queue cleared. Recovery is pending.",
            _ => "Status",
        };
        if (response.Status is { } s)
            body += $"\nVersion: {response.Version}\nEnabled: {(s.Enabled ? "Yes" : "No")}\nState: {s.State}\nStatus: {s.StatusText}\n"
                + $"Hunt: {(s.ActiveHunt.Length == 0 ? "none" : s.ActiveHunt)}\nQueue: {s.QueueCount}\nTerritory: {s.Territory}";
        return new(null, [new(title, body, response.Success ? 0x68B5AE : 0xCBAA72)], new([]));
    }

    public static DiscordWebhookPayload Notice(string message) => new(message, null, new([]));
}
