using System.Text.Json;

namespace SentinelRelay.Remote;

public sealed record TailnetIdentity(string Host, string Login)
{
    public string Url => "https://" + Host;
}

/// <summary>Parse only the local Tailscale identity; peers never choose the server or login.</summary>
public static class DesktopSetupPolicy
{
    public static TailnetIdentity ReadIdentity(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.GetProperty("BackendState").GetString() != "Running")
            throw new InvalidOperationException("tailscale_disconnected");
        var self = root.GetProperty("Self");
        if (self.TryGetProperty("Tags", out var tags) && tags.ValueKind == JsonValueKind.Array && tags.GetArrayLength() > 0)
            throw new InvalidOperationException("tailscale_tagged_device");
        var host = self.GetProperty("DNSName").GetString()?.TrimEnd('.') ?? "";
        var userId = self.GetProperty("UserID").ToString();
        var login = root.GetProperty("User").GetProperty(userId).GetProperty("LoginName").GetString() ?? "";
        if (!host.EndsWith(".ts.net", StringComparison.Ordinal) || Uri.CheckHostName(host) != UriHostNameType.Dns ||
            host.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-')) ||
            string.IsNullOrWhiteSpace(login) || login.Length > 254 || login.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            throw new InvalidOperationException("tailscale_identity_invalid");
        return new(host, login);
    }

    // Never replace someone else's Serve routes or silently convert a public Funnel.
    public static string ServeState(string json, string host)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Null) return "missing";
        if (root.ValueKind != JsonValueKind.Object) return "conflict";
        if (root.TryGetProperty("AllowFunnel", out var funnel) && funnel.ValueKind == JsonValueKind.Object &&
            funnel.EnumerateObject().Any(p => p.Value.ValueKind == JsonValueKind.True)) return "conflict";
        bool route = false;
        foreach (var section in root.EnumerateObject())
        {
            if (section.Value.ValueKind is JsonValueKind.Null) continue;
            if (section.Name == "AllowFunnel") continue;
            if (section.Name == "TCP")
            {
                if (section.Value.ValueKind != JsonValueKind.Object) return "conflict";
                foreach (var port in section.Value.EnumerateObject())
                    if (port.Name != "443" || !port.Value.TryGetProperty("HTTPS", out var https) || https.ValueKind != JsonValueKind.True ||
                        port.Value.EnumerateObject().Any(p => p.Name != "HTTPS" && p.Value.ValueKind is not JsonValueKind.False and not JsonValueKind.Null))
                        return "conflict";
            }
            else if (section.Name == "Web")
            {
                if (section.Value.ValueKind != JsonValueKind.Object) return "conflict";
                foreach (var site in section.Value.EnumerateObject())
                {
                    if (site.Name != host + ":443" || !site.Value.TryGetProperty("Handlers", out var handlers) ||
                        handlers.ValueKind != JsonValueKind.Object) return "conflict";
                    foreach (var handler in handlers.EnumerateObject())
                    {
                        if (handler.Name != "/" || !handler.Value.TryGetProperty("Proxy", out var proxy) ||
                            proxy.GetString() != "http://127.0.0.1:32179" ||
                            handler.Value.EnumerateObject().Any(p => p.Name != "Proxy" && p.Value.ValueKind is not JsonValueKind.Null &&
                                !(p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() == "")))
                            return "conflict";
                        route = true;
                    }
                }
            }
            else if (section.Value.ValueKind != JsonValueKind.Object || section.Value.EnumerateObject().Any()) return "conflict";
        }
        return route ? "ready" : "missing";
    }

    /// <summary>Only reconnect an explicitly enrolled identity, at the same executable path, when unambiguous.</summary>
    public static GameBinding[] Rebindings(IReadOnlyList<GameBinding> saved, IReadOnlyList<GameBinding> discovered)
    {
        var changes = new List<GameBinding>();
        foreach (var previous in saved)
        {
            if (saved.Count(b => b.CharacterKey == previous.CharacterKey) != 1) continue;
            var matches = discovered.Where(b => b.CharacterKey == previous.CharacterKey).ToArray();
            if (string.IsNullOrEmpty(previous.CharacterKey) || matches.Length != 1) continue;
            var match = matches[0];
            if (!string.Equals(match.Executable, previous.Executable, StringComparison.OrdinalIgnoreCase) ||
                saved.Any(b => b.InstanceId != previous.InstanceId && b.ProcessId == match.ProcessId && b.StartedUtcTicks == match.StartedUtcTicks))
                continue;
            if (match.ProcessId != previous.ProcessId || match.StartedUtcTicks != previous.StartedUtcTicks)
                changes.Add(match with { InstanceId = previous.InstanceId });
        }
        return changes.ToArray();
    }
}
