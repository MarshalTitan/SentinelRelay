using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using SentinelRelay.Models;

namespace SentinelRelay.Services;

public sealed record WebhookSendResult(bool Success, int Attempts, string? Error, bool WasRateLimited);

public sealed class WebhookHttpSender(
    HttpClient httpClient,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Func<TimeSpan, CancellationToken, Task> delay = delay ?? Task.Delay;

    public async Task<WebhookSendResult> SendAsync(
        Uri endpoint,
        IReadOnlyList<DiscordWebhookPayload> payloads,
        CancellationToken cancellationToken)
    {
        var totalAttempts = 0;
        var rateLimited = false;
        foreach (var payload in payloads)
        {
            var result = await SendPayloadAsync(endpoint, payload, cancellationToken).ConfigureAwait(false);
            totalAttempts += result.Attempts;
            rateLimited |= result.WasRateLimited;
            if (!result.Success)
                return result with { Attempts = totalAttempts, WasRateLimited = rateLimited };
        }

        return new WebhookSendResult(true, totalAttempts, null, rateLimited);
    }

    private async Task<WebhookSendResult> SendPayloadAsync(
        Uri endpoint,
        DiscordWebhookPayload payload,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var wasRateLimited = false;
        const int maximumAttempts = 4;

        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            try
            {
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await httpClient.PostAsync(endpoint, content, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                    return new WebhookSendResult(true, attempt, null, wasRateLimited);

                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < maximumAttempts)
                {
                    wasRateLimited = true;
                    var wait = await ReadRetryAfterAsync(response, cancellationToken).ConfigureAwait(false);
                    await delay(wait, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if ((int)response.StatusCode >= 500 && attempt < maximumAttempts)
                {
                    await delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                return new WebhookSendResult(
                    false,
                    attempt,
                    $"Discord returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase ?? "error"}).",
                    wasRateLimited);
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

                return new WebhookSendResult(
                    false,
                    attempt,
                    "Discord could not be reached after retries.",
                    wasRateLimited);
            }
            catch (TaskCanceledException)
            {
                if (attempt < maximumAttempts)
                {
                    await delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                return new WebhookSendResult(
                    false,
                    attempt,
                    "Discord webhook request timed out after retries.",
                    wasRateLimited);
            }
        }

        return new WebhookSendResult(false, maximumAttempts, "Discord delivery failed after retries.", wasRateLimited);
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
        else if (response.Headers.TryGetValues("X-RateLimit-Reset-After", out var values)
                 && double.TryParse(values.FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out var headerSeconds))
        {
            seconds = headerSeconds;
        }
        else
        {
            try
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.TryGetProperty("retry_after", out var retryAfter))
                {
                    if (retryAfter.ValueKind == JsonValueKind.Number && retryAfter.TryGetDouble(out var jsonSeconds))
                        seconds = jsonSeconds;
                    else if (retryAfter.ValueKind == JsonValueKind.String
                             && double.TryParse(retryAfter.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out jsonSeconds))
                        seconds = jsonSeconds;
                }
            }
            catch (JsonException)
            {
                // The HTTP fallback below is safe when Discord omits or changes the JSON body.
            }
        }

        return TimeSpan.FromSeconds(Math.Clamp(seconds, 0.1, 60));
    }
}
