using System.Text.RegularExpressions;

namespace SentinelRelay.Core;

public static partial class WebhookEndpoint
{
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "discord.com",
        "www.discord.com",
        "ptb.discord.com",
        "canary.discord.com",
        "discordapp.com",
        "www.discordapp.com",
    };

    public static bool TryCreate(string? value, out Uri endpoint, out string error)
    {
        endpoint = null!;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Paste a Discord webhook URL first.";
            return false;
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var parsed)
            || !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !AllowedHosts.Contains(parsed.Host)
            || !parsed.IsDefaultPort
            || !string.IsNullOrEmpty(parsed.UserInfo)
            || !string.IsNullOrEmpty(parsed.Fragment))
        {
            error = "Use the HTTPS webhook URL copied directly from Discord.";
            return false;
        }

        var match = WebhookPathRegex().Match(parsed.AbsolutePath);
        if (!match.Success)
        {
            error = "The URL is not a valid Discord webhook address.";
            return false;
        }

        endpoint = new UriBuilder(parsed)
        {
            Path = parsed.AbsolutePath.TrimEnd('/'),
            Query = "wait=true",
            Fragment = string.Empty,
        }.Uri;
        error = string.Empty;
        return true;
    }

    public static string Mask(Uri? endpoint)
    {
        if (endpoint is null)
            return "Not configured";

        var segments = endpoint.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var id = segments.Length >= 3 ? segments[^2] : "unknown";
        var visible = id.Length > 6 ? id[^6..] : id;
        return $"Configured (webhook …{visible})";
    }

    public static bool IsValidDiscordUserId(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length is >= 17 and <= 20
        && value.All(char.IsAsciiDigit);

    [GeneratedRegex(@"^/api(?:/v\d+)?/webhooks/(?<id>\d{17,20})/(?<token>[A-Za-z0-9._-]{20,})/?$", RegexOptions.CultureInvariant)]
    private static partial Regex WebhookPathRegex();
}
