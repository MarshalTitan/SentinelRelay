using System.Net.Http.Headers;
using Dalamud.Plugin.Services;

namespace SentinelRelay.Services;

public enum RemoteScreenshotState
{
    Idle,
    Capturing,
    Uploading,
    Error,
    Disposed,
}

public sealed record RemoteScreenshotRequest(
    string CharacterKey,
    string CharacterName,
    string DiscordMessageId,
    Uri WebhookEndpoint,
    nint GameWindow);

public sealed record RemoteScreenshotResult(
    string CharacterKey,
    string DiscordMessageId,
    bool Success,
    int Width,
    int Height,
    string? Error,
    DateTime CompletedAtUtc,
    bool WasRateLimited);

public sealed class RemoteScreenshotService : IDisposable
{
    private readonly IPluginLog log;
    private readonly HttpClient httpClient;
    private readonly ScreenshotWebhookSender sender;
    private readonly object gate = new();
    private CancellationTokenSource? activeCancellation;
    private int generation;
    private bool disposed;
    private RemoteScreenshotState state = RemoteScreenshotState.Idle;
    private string? lastError;
    private DateTime? lastSuccessUtc;
    private string? lastDimensions;

    public RemoteScreenshotService(IPluginLog log)
    {
        this.log = log;
        httpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(45),
        };
        var version = typeof(RemoteScreenshotService).Assembly.GetName().Version?.ToString() ?? "unknown";
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SentinelRelay", version));
        sender = new ScreenshotWebhookSender(httpClient);
    }

    public event Action<RemoteScreenshotResult>? Completed;

    public RemoteScreenshotState State
    {
        get { lock (gate) return state; }
    }

    public string? LastError
    {
        get { lock (gate) return lastError; }
    }

    public DateTime? LastSuccessUtc
    {
        get { lock (gate) return lastSuccessUtc; }
    }

    public string? LastDimensions
    {
        get { lock (gate) return lastDimensions; }
    }

    public bool TryStart(RemoteScreenshotRequest request)
    {
        int requestGeneration;
        CancellationTokenSource cancellation;
        lock (gate)
        {
            if (disposed || activeCancellation is not null)
                return false;
            cancellation = new CancellationTokenSource();
            activeCancellation = cancellation;
            requestGeneration = ++generation;
            state = RemoteScreenshotState.Capturing;
            lastError = null;
        }

        _ = Task.Run(() => RunAsync(request, requestGeneration, cancellation));
        return true;
    }

    public void Cancel()
    {
        CancellationTokenSource? cancellation;
        lock (gate)
        {
            generation++;
            cancellation = activeCancellation;
            activeCancellation = null;
            if (!disposed)
                state = RemoteScreenshotState.Idle;
        }
        cancellation?.Cancel();
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
                return;
            disposed = true;
            state = RemoteScreenshotState.Disposed;
        }
        Cancel();
        httpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task RunAsync(
        RemoteScreenshotRequest request,
        int requestGeneration,
        CancellationTokenSource cancellation)
    {
        try
        {
            var capture = GameWindowCapture.CapturePng(request.GameWindow);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!capture.Success || capture.PngBytes is null)
            {
                Complete(request, requestGeneration, false, 0, 0, capture.Error, false);
                return;
            }

            lock (gate)
            {
                if (!IsActive(requestGeneration))
                    return;
                state = RemoteScreenshotState.Uploading;
            }
            var upload = await sender.SendAsync(
                request.WebhookEndpoint,
                request.CharacterName,
                capture.PngBytes,
                cancellation.Token).ConfigureAwait(false);
            Complete(
                request,
                requestGeneration,
                upload.Success,
                capture.Width,
                capture.Height,
                upload.Error,
                upload.WasRateLimited);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Character switch, pause, shutdown, or explicit cancellation.
        }
        catch (Exception ex)
        {
            log.Warning("Sentinel Relay remote screenshot failed ({ExceptionType}).", ex.GetType().Name);
            Complete(request, requestGeneration, false, 0, 0, "The remote screenshot failed unexpectedly.", false);
        }
        finally
        {
            lock (gate)
            {
                if (requestGeneration == generation && ReferenceEquals(activeCancellation, cancellation))
                    activeCancellation = null;
            }
            cancellation.Dispose();
        }
    }

    private void Complete(
        RemoteScreenshotRequest request,
        int requestGeneration,
        bool success,
        int width,
        int height,
        string? error,
        bool wasRateLimited)
    {
        lock (gate)
        {
            if (!IsActive(requestGeneration))
                return;
            state = success ? RemoteScreenshotState.Idle : RemoteScreenshotState.Error;
            lastError = success ? null : error;
            if (success)
            {
                lastSuccessUtc = DateTime.UtcNow;
                lastDimensions = $"{width}x{height}";
            }
        }
        Completed?.Invoke(new RemoteScreenshotResult(
            request.CharacterKey,
            request.DiscordMessageId,
            success,
            width,
            height,
            error,
            DateTime.UtcNow,
            wasRateLimited));
    }

    private bool IsActive(int requestGeneration) =>
        !disposed && requestGeneration == generation && activeCancellation is not null;
}
