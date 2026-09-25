using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SentinelRelay.Core;
using SentinelRelay.Models;

namespace SentinelRelay.Services;

public sealed record ScreenshotWebhookSendResult(
    bool Success,
    int Attempts,
    string? Error,
    bool WasRateLimited);

public sealed class ScreenshotWebhookSender(
    HttpClient httpClient,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
    private readonly Func<TimeSpan, CancellationToken, Task> delay = delay ?? Task.Delay;

    public async Task<ScreenshotWebhookSendResult> SendAsync(
        Uri endpoint,
        string characterName,
        ReadOnlyMemory<byte> pngBytes,
        CancellationToken cancellationToken)
    {
        if (pngBytes.IsEmpty || pngBytes.Length > 7_500_000)
            return new(false, 0, "The screenshot is empty or exceeds the safe upload size.", false);

        var safeName = MessageSanitizer.SanitizeForDiscord(characterName);
        var payload = new DiscordWebhookPayload(
            $"【SCREENSHOT】 {safeName}",
            null,
            new DiscordAllowedMentions([]));
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var wasRateLimited = false;
        const int maximumAttempts = 4;

        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            try
            {
                using var multipart = new MultipartFormDataContent();
                using var payloadContent = new StringContent(json, Encoding.UTF8, "application/json");
                using var imageContent = new ByteArrayContent(pngBytes.ToArray());
                imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                multipart.Add(payloadContent, "payload_json");
                multipart.Add(imageContent, "files[0]", "sentinel-relay.png");

                using var response = await httpClient.PostAsync(endpoint, multipart, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                    return new(true, attempt, null, wasRateLimited);

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

                return new(
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
                return new(false, attempt, "Discord could not be reached after retries.", wasRateLimited);
            }
            catch (TaskCanceledException)
            {
                if (attempt < maximumAttempts)
                {
                    await delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), cancellationToken).ConfigureAwait(false);
                    continue;
                }
                return new(false, attempt, "The Discord screenshot upload timed out after retries.", wasRateLimited);
            }
        }

        return new(false, maximumAttempts, "The Discord screenshot upload failed after retries.", wasRateLimited);
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
                    if (retryAfter.ValueKind == JsonValueKind.Number && retryAfter.TryGetDouble(out var numeric))
                        seconds = numeric;
                    else if (retryAfter.ValueKind == JsonValueKind.String
                             && double.TryParse(retryAfter.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out numeric))
                        seconds = numeric;
                }
            }
            catch (JsonException)
            {
                // A conservative delay is safe when Discord omits the JSON body.
            }
        }
        return TimeSpan.FromSeconds(Math.Clamp(seconds, 0.1, 60));
    }
}
