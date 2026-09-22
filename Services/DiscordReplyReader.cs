using System.Net.Http.Headers;
using Dalamud.Plugin.Services;
using SentinelRelay.Core;
using SentinelRelay.Models;

namespace SentinelRelay.Services;

public enum DiscordReplyReaderState
{
    Disabled,
    Initializing,
    Connected,
    Error,
    Disposed,
}

public sealed record DiscordReaderConfiguration(
    string CharacterKey,
    string BotToken,
    string ChannelId,
    string Checkpoint);

public sealed record DiscordMessageBatch(
    string CharacterKey,
    string CheckpointBeforeBatch,
    string NewCheckpoint,
    IReadOnlyList<DiscordChannelMessage> Messages);

public sealed record DiscordReaderTestResult(
    string CharacterKey,
    bool Success,
    string? LatestMessageId,
    string? Error);

public sealed class DiscordReplyReader : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ErrorBackoff = TimeSpan.FromSeconds(10);

    private readonly IPluginLog log;
    private readonly HttpClient httpClient;
    private readonly DiscordRestMessageClient client;
    private readonly object gate = new();
    private CancellationTokenSource? activeCancellation;
    private Task? activeWorker;
    private int activeGeneration;
    private bool disposed;
    private DiscordReplyReaderState state = DiscordReplyReaderState.Disabled;
    private string? lastError;
    private DateTime? lastSuccessUtc;

    public DiscordReplyReader(IPluginLog log)
    {
        this.log = log;
        httpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        var version = typeof(DiscordReplyReader).Assembly.GetName().Version?.ToString() ?? "unknown";
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SentinelRelay", version));
        client = new DiscordRestMessageClient(httpClient);
    }

    public event Action<DiscordMessageBatch>? MessagesReceived;

    public event Action<string, string, DateTime>? CheckpointEstablished;

    public DiscordReplyReaderState State
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

    public void Start(DiscordReaderConfiguration configuration)
    {
        Stop();
        lock (gate)
        {
            if (disposed)
                return;
            state = DiscordReplyReaderState.Initializing;
            lastError = null;
            var generation = ++activeGeneration;
            var cancellation = new CancellationTokenSource();
            activeCancellation = cancellation;
            activeWorker = Task.Run(() => RunAsync(configuration, generation, cancellation.Token));
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cancellation;
        Task? worker;
        lock (gate)
        {
            activeGeneration++;
            cancellation = activeCancellation;
            worker = activeWorker;
            activeCancellation = null;
            activeWorker = null;
            if (!disposed)
                state = DiscordReplyReaderState.Disabled;
        }
        cancellation?.Cancel();
        if (cancellation is not null)
            _ = (worker ?? Task.CompletedTask).ContinueWith(
                _ => cancellation.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
    }

    public async Task<DiscordReaderTestResult> TestAsync(
        DiscordReaderConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var result = await client.GetLatestAsync(
            configuration.BotToken,
            configuration.ChannelId,
            cancellationToken).ConfigureAwait(false);
        var latest = result.Messages
            .Select(message => message.Id)
            .Where(DiscordSnowflake.IsValid)
            .MaxBy(id => ulong.Parse(id));
        return new DiscordReaderTestResult(
            configuration.CharacterKey,
            result.Success,
            latest,
            result.Error);
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
                return;
            disposed = true;
            state = DiscordReplyReaderState.Disposed;
        }
        Stop();
        httpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task RunAsync(
        DiscordReaderConfiguration configuration,
        int generation,
        CancellationToken cancellationToken)
    {
        var checkpoint = DiscordSnowflake.IsValid(configuration.Checkpoint)
            ? configuration.Checkpoint
            : string.Empty;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Every start/reconnect deliberately advances to the newest current
                // message before polling. Commands written while disabled/offline are
                // therefore never executed later.
                var prime = await client.GetLatestAsync(
                    configuration.BotToken,
                    configuration.ChannelId,
                    cancellationToken).ConfigureAwait(false);
                if (!prime.Success)
                {
                    SetError(generation, prime.Error ?? "Discord reader initialization failed.");
                    await Task.Delay(ErrorBackoff, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var newest = prime.Messages
                    .Select(message => message.Id)
                    .Where(DiscordSnowflake.IsValid)
                    .MaxBy(id => ulong.Parse(id));
                if (newest is not null && DiscordSnowflake.Compare(newest, checkpoint) > 0)
                {
                    checkpoint = newest;
                    if (!IsActive(generation))
                        return;
                    CheckpointEstablished?.Invoke(configuration.CharacterKey, checkpoint, DateTime.UtcNow);
                }
                SetConnected(generation);

                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
                    var result = await client.GetAfterAsync(
                        configuration.BotToken,
                        configuration.ChannelId,
                        checkpoint,
                        cancellationToken).ConfigureAwait(false);
                    if (!result.Success)
                    {
                        SetError(generation, result.Error ?? "Discord reader failed.");
                        break;
                    }

                    SetConnected(generation);
                    if (result.Messages.Count == 0)
                        continue;

                    var ordered = result.Messages
                        .Where(message => DiscordSnowflake.IsValid(message.Id))
                        .OrderBy(message => ulong.Parse(message.Id))
                        .ToArray();
                    if (ordered.Length == 0)
                        continue;

                    var previous = checkpoint;
                    checkpoint = ordered[^1].Id;
                    if (!IsActive(generation))
                        return;
                    MessagesReceived?.Invoke(new DiscordMessageBatch(
                        configuration.CharacterKey,
                        previous,
                        checkpoint,
                        ordered));
                }

                if (!cancellationToken.IsCancellationRequested)
                    await Task.Delay(ErrorBackoff, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal stop, character switch, pause, or plugin shutdown.
        }
        catch (Exception ex)
        {
            SetError(generation, "The Discord reader stopped unexpectedly.");
            log.Warning("Sentinel Relay Discord reader stopped ({ExceptionType}).", ex.GetType().Name);
        }
    }

    private void SetConnected(int generation)
    {
        lock (gate)
        {
            if (disposed || generation != activeGeneration)
                return;
            state = DiscordReplyReaderState.Connected;
            lastError = null;
            lastSuccessUtc = DateTime.UtcNow;
        }
    }

    private bool IsActive(int generation)
    {
        lock (gate)
            return !disposed && generation == activeGeneration;
    }

    private void SetError(int generation, string error)
    {
        lock (gate)
        {
            if (disposed || generation != activeGeneration)
                return;
            state = DiscordReplyReaderState.Error;
            lastError = error;
        }
    }
}
