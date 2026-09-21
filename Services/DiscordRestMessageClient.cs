using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SentinelRelay.Core;
using SentinelRelay.Models;

namespace SentinelRelay.Services;

public sealed record DiscordMessageReadResult(
    bool Success,
    IReadOnlyList<DiscordChannelMessage> Messages,
    string? Error,
    bool WasRateLimited);

public sealed class DiscordRestMessageClient(
    HttpClient httpClient,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Func<TimeSpan, CancellationToken, Task> delay = delay ?? Task.Delay;

    public Task<DiscordMessageReadResult> GetLatestAsync(
        string botToken,
        string channelId,
        CancellationToken cancellationToken) =>
        GetAsync(botToken, channelId, null, 1, cancellationToken);

    public Task<DiscordMessageReadResult> GetAfterAsync(
        string botToken,
        string channelId,
        string checkpoint,
        CancellationToken cancellationToken) =>
        GetAsync(botToken, channelId, checkpoint, 25, cancellationToken);

    private async Task<DiscordMessageReadResult> GetAsync(
        string botToken,
        string channelId,
        string? after,
        int limit,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(botToken))
            return new(false, [], "The Discord bot credential is not configured.", false);
        if (!DiscordSnowflake.IsValid(channelId))
            return new(false, [], "The Discord relay channel ID is invalid.", false);
        if (!string.IsNullOrWhiteSpace(after) && !DiscordSnowflake.IsValid(after))
            return new(false, [], "The Discord processing checkpoint is invalid.", false);

        var afterQuery = string.IsNullOrWhiteSpace(after) ? string.Empty : $"&after={after}";
        var endpoint = new Uri($"https://discord.com/api/v10/channels/{channelId}/messages?limit={limit}{afterQuery}");
        var wasRateLimited = false;
        const int maximumAttempts = 4;

        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bot", botToken.Trim());
                using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    var messages = await JsonSerializer.DeserializeAsync<List<DiscordChannelMessage>>(
                        stream,
                        JsonOptions,
                        cancellationToken).ConfigureAwait(false);
                    return new(true, messages ?? [], null, wasRateLimited);
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < maximumAttempts)
                {
                    wasRateLimited = true;
                    await delay(await ReadRetryAfterAsync(response, cancellationToken).ConfigureAwait(false), cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                if ((int)response.StatusCode >= 500 && attempt < maximumAttempts)
                {
                    await delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var error = response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "Discord rejected the bot credential (HTTP 401).",
                    HttpStatusCode.Forbidden => "The Discord bot cannot view this channel or read its history (HTTP 403).",
                    HttpStatusCode.NotFound => "The configured Discord relay channel was not found (HTTP 404).",
                    _ => $"Discord returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase ?? "error"}).",
                };
                return new(false, [], error, wasRateLimited);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException)
            {
                if (attempt < maximumAttempts)
                {
                    await delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                return new(false, [], "Discord could not be reached after retries.", wasRateLimited);
            }
            catch (JsonException)
            {
                return new(false, [], "Discord returned an unreadable message response.", wasRateLimited);
            }
        }

        return new(false, [], "Discord message reading failed after retries.", wasRateLimited);
    }

    private static async Task<TimeSpan> ReadRetryAfterAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        double seconds = 1;
        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            seconds = delta.TotalSeconds;
        }
        else
        {
            try
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.TryGetProperty("retry_after", out var retryAfter))
                {
                    if (retryAfter.ValueKind == JsonValueKind.Number && retryAfter.TryGetDouble(out var numeric))
                        seconds = numeric;
                    else if (retryAfter.ValueKind == JsonValueKind.String
                             && double.TryParse(retryAfter.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out numeric))
                        seconds = numeric;
                }
            }
            catch (JsonException)
            {
                // A conservative delay is used when Discord omits or changes the body.
            }
        }

        return TimeSpan.FromSeconds(Math.Clamp(seconds, 0.1, 60));
    }
}
