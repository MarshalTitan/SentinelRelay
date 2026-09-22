using System.Net.Http.Headers;
using Dalamud.Plugin.Services;
using SentinelRelay.Core;
using SentinelRelay.Models;

namespace SentinelRelay.Services;

public enum WebhookRelayState
{
    NotConfigured,
    Ready,
    Sending,
    Error,
    Disposed,
}

public sealed record WebhookDeliveryResult(
    string CharacterKey,
    bool IsTest,
    bool Success,
    string? Error,
    DateTime CompletedAtUtc,
    bool WasRateLimited);

public sealed class WebhookRelayClient : IDisposable
{
    private const int QueueCapacity = 100;
    private static readonly TimeSpan MaximumAge = TimeSpan.FromMinutes(2);

    private readonly IPluginLog log;
    private readonly BoundedOrderedQueue<QueuedWebhookDelivery> queue = new(QueueCapacity);
    private readonly SemaphoreSlim signal = new(0);
    private readonly CancellationTokenSource cancellation = new();
    private readonly object deliveryGate = new();
    private readonly HttpClient httpClient;
    private readonly WebhookHttpSender sender;
    private readonly Task worker;
    private CancellationTokenSource? activeDeliveryCancellation;
    private int deliveryGeneration;
    private int droppedCount;
    private bool disposed;

    public WebhookRelayClient(IPluginLog log)
    {
        this.log = log;
        httpClient = new HttpClient(new HttpClientHandler
        {
            // Keep the webhook credential on the validated Discord origin.
            AllowAutoRedirect = false,
        })
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        var version = typeof(WebhookRelayClient).Assembly.GetName().Version?.ToString() ?? "unknown";
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SentinelRelay", version));
        sender = new WebhookHttpSender(httpClient);
        worker = Task.Run(() => RunAsync(cancellation.Token));
    }

    public event Action<WebhookDeliveryResult>? DeliveryCompleted;

    public WebhookRelayState State { get; private set; } = WebhookRelayState.NotConfigured;

    public int QueueLength => queue.Count;

    public int DroppedCount => Volatile.Read(ref droppedCount);

    public DateTime? LastSuccessUtc { get; private set; }

    public string? LastError { get; private set; }

    public bool TryEnqueue(
        string characterKey,
        Uri endpoint,
        IReadOnlyList<DiscordWebhookPayload> payloads,
        bool isTest = false)
    {
        if (disposed || payloads.Count == 0)
            return false;

        if (!queue.TryEnqueue(new QueuedWebhookDelivery(
                characterKey,
                endpoint,
                payloads,
                DateTime.UtcNow,
                isTest)))
        {
            Interlocked.Increment(ref droppedCount);
            LastError = $"Discord queue is full ({QueueCapacity}); newest message was dropped.";
            State = WebhookRelayState.Error;
            return false;
        }

        State = WebhookRelayState.Ready;
        signal.Release();
        return true;
    }

    public void SetConfigured(bool configured)
    {
        if (disposed)
            return;
        State = configured ? WebhookRelayState.Ready : WebhookRelayState.NotConfigured;
        if (!configured)
            LastError = null;
    }

    public void ClearQueue()
    {
        queue.Clear();
        if (!disposed && State == WebhookRelayState.Sending)
            State = WebhookRelayState.Ready;
        CancellationTokenSource? active;
        lock (deliveryGate)
        {
            deliveryGeneration++;
            active = activeDeliveryCancellation;
        }
        active?.Cancel();
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        State = WebhookRelayState.Disposed;
        cancellation.Cancel();
        ClearQueue();
        signal.Release();
        httpClient.Dispose();

        // The worker may still be unwinding an HTTP request. Dispose its wait
        // primitives only after it exits so plugin shutdown cannot race a
        // pending WaitAsync/Release call.
        _ = worker.ContinueWith(
            _ =>
            {
                signal.Dispose();
                cancellation.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        GC.SuppressFinalize(this);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await signal.WaitAsync(cancellationToken).ConfigureAwait(false);
                while (queue.TryDequeue(out var delivery))
                {
                    var age = DateTime.UtcNow - delivery.CreatedAtUtc;
                    if (age >= MaximumAge)
                    {
                        Interlocked.Increment(ref droppedCount);
                        continue;
                    }

                    State = WebhookRelayState.Sending;
                    WebhookSendResult result;
                    int generation;
                    using (var deliveryCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    {
                        lock (deliveryGate)
                        {
                            generation = deliveryGeneration;
                            activeDeliveryCancellation = deliveryCancellation;
                        }

                        try
                        {
                            deliveryCancellation.CancelAfter(MaximumAge - age);
                            result = await sender.SendAsync(
                                delivery.Endpoint,
                                delivery.Payloads,
                                deliveryCancellation.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                        {
                            Interlocked.Increment(ref droppedCount);
                            lock (deliveryGate)
                            {
                                if (generation != deliveryGeneration)
                                    continue;
                            }

                            LastError = "A Discord delivery expired after two minutes and was dropped.";
                            State = WebhookRelayState.Error;
                            DeliveryCompleted?.Invoke(new WebhookDeliveryResult(
                                delivery.CharacterKey,
                                delivery.IsTest,
                                false,
                                LastError,
                                DateTime.UtcNow,
                                false));
                            continue;
                        }
                        finally
                        {
                            lock (deliveryGate)
                            {
                                if (ReferenceEquals(activeDeliveryCancellation, deliveryCancellation))
                                    activeDeliveryCancellation = null;
                            }
                        }
                    }

                    lock (deliveryGate)
                    {
                        if (generation != deliveryGeneration)
                            continue;
                    }

                    if (result.Success)
                    {
                        LastSuccessUtc = DateTime.UtcNow;
                        LastError = null;
                        State = WebhookRelayState.Ready;
                    }
                    else
                    {
                        LastError = result.Error;
                        State = WebhookRelayState.Error;
                        log.Warning("Sentinel Relay Discord delivery failed: {Error}", result.Error ?? "unknown error");
                    }

                    DeliveryCompleted?.Invoke(new WebhookDeliveryResult(
                        delivery.CharacterKey,
                        delivery.IsTest,
                        result.Success,
                        result.Error,
                        DateTime.UtcNow,
                        result.WasRateLimited));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal plugin shutdown.
        }
        catch (Exception ex)
        {
            LastError = "The Discord delivery worker stopped unexpectedly.";
            State = WebhookRelayState.Error;
            log.Error("Sentinel Relay Discord delivery worker stopped unexpectedly ({ExceptionType}).", ex.GetType().Name);
        }
    }

    private sealed record QueuedWebhookDelivery(
        string CharacterKey,
        Uri Endpoint,
        IReadOnlyList<DiscordWebhookPayload> Payloads,
        DateTime CreatedAtUtc,
        bool IsTest);
}
