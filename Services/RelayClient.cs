using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dalamud.Plugin.Services;
using SentinelRelay.Models;
using SentinelRelay.Protocol;

namespace SentinelRelay.Services;

public sealed class RelayClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IPluginLog log;
    private readonly ConcurrentQueue<QueuedEnvelope> outgoing = new();
    private readonly SemaphoreSlim outgoingSignal = new(0);
    private readonly object lifecycleGate = new();
    private CancellationTokenSource? sessionCancellation;
    private Task? sessionTask;
    private ActiveSession? active;
    private int outgoingCount;
    private bool disposed;
    private string? liveClientToken;
    private DateTime connectionStartedUtc;

    public RelayClient(IPluginLog log)
    {
        this.log = log;
    }

    public event Action<string, string, string?>? PairCompleted;

    public event Action? LinkRevoked;

    public event Action<OutboundChatPayload>? OutboundReceived;

    public RelayConnectionState State { get; private set; } = RelayConnectionState.Disabled;

    public int ReconnectCount { get; private set; }

    public string? PairCode { get; private set; }

    public DateTime? PairCodeExpiresAtUtc { get; private set; }

    public string? DiscordUsername { get; private set; }

    public string? RelayChannelName { get; private set; }

    public string? BackendVersion { get; private set; }

    public string? LastError { get; private set; }

    public DateTime? LastInboundSentUtc { get; private set; }

    public DateTime? LastOutboundReceivedUtc { get; private set; }

    public int OutgoingQueueLength => Volatile.Read(ref outgoingCount);

    public TimeSpan ConnectedDuration => connectionStartedUtc == default ? TimeSpan.Zero : DateTime.UtcNow - connectionStartedUtc;

    public void Activate(string serviceUrl, CharacterProfile profile, CharacterIdentity identity, string? clientToken)
    {
        lock (lifecycleGate)
        {
            ThrowIfDisposed();
            StopSessionLocked();
            active = new ActiveSession(serviceUrl.Trim(), profile, identity);
            liveClientToken = clientToken;
            PairCode = null;
            PairCodeExpiresAtUtc = null;
            DiscordUsername = null;
            RelayChannelName = null;
            BackendVersion = null;
            LastError = null;
            ReconnectCount = 0;

            if (!TryValidateEndpoint(active.ServiceUrl, out var endpoint, out var error))
            {
                State = string.IsNullOrWhiteSpace(active.ServiceUrl) ? RelayConnectionState.Disabled : RelayConnectionState.Offline;
                LastError = error;
                return;
            }

            sessionCancellation = new CancellationTokenSource();
            sessionTask = Task.Run(() => ConnectionLoopAsync(endpoint, sessionCancellation.Token));
        }
    }

    public bool RequestPairing() => Queue("pair.request", new PairRequestPayload(), false, TimeSpan.FromSeconds(15));

    public bool SendInbound(InboundChatPayload payload)
    {
        if (State != RelayConnectionState.Authenticated)
            return false;
        var queued = Queue("chat.inbound", payload, true, TimeSpan.FromSeconds(15));
        if (queued)
            LastInboundSentUtc = DateTime.UtcNow;
        return queued;
    }

    public bool SendOutboundAck(string eventId, bool delivered, string? error) =>
        Queue("chat.outbound.ack", new OutboundAckPayload(eventId, delivered, error), true, TimeSpan.FromSeconds(15));

    public bool SetPaused(bool paused) => Queue("relay.pause", new PausePayload(paused), true, TimeSpan.FromSeconds(15));

    public bool Unlink() => Queue("link.unlink", new UnlinkPayload(), true, TimeSpan.FromSeconds(15));

    public void Disconnect()
    {
        lock (lifecycleGate)
        {
            StopSessionLocked();
            active = null;
            liveClientToken = null;
            State = RelayConnectionState.Disabled;
        }
    }

    public void Dispose()
    {
        lock (lifecycleGate)
        {
            if (disposed)
                return;
            disposed = true;
            StopSessionLocked();
            State = RelayConnectionState.Disposed;
        }
        outgoingSignal.Dispose();
    }

    private async Task ConnectionLoopAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                State = attempt == 0 ? RelayConnectionState.Connecting : RelayConnectionState.Reconnecting;
                using var socket = new ClientWebSocket();
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                await socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
                connectionStartedUtc = DateTime.UtcNow;
                LastError = null;

                var session = active ?? throw new InvalidOperationException("No active character session.");
                await SendEnvelopeAsync(socket, new ClientEnvelope(
                    "hello",
                    Guid.NewGuid().ToString("N"),
                    new HelloPayload(
                        session.Profile.InstallationId,
                        liveClientToken,
                        RelayProtocol.Version,
                        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0",
                        session.Identity.CharacterName,
                        session.Identity.HomeWorld,
                        session.Identity.CharacterKey,
                        session.Profile.Paused)), cancellationToken).ConfigureAwait(false);

                using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var reader = ReadLoopAsync(socket, linkedCancellation.Token);
                var writer = WriteLoopAsync(socket, linkedCancellation.Token);
                var completed = await Task.WhenAny(reader, writer).ConfigureAwait(false);
                linkedCancellation.Cancel();
                await IgnoreCancellationAsync(reader).ConfigureAwait(false);
                await IgnoreCancellationAsync(writer).ConfigureAwait(false);
                await completed.ConfigureAwait(false);
                if (!cancellationToken.IsCancellationRequested)
                    throw new WebSocketException("The relay connection closed.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                attempt++;
                ReconnectCount++;
                State = RelayConnectionState.Offline;
                LastError = SanitizeError(ex.Message);
                log.Warning(ex, "Sentinel Relay connection attempt {Attempt} failed.", attempt);
                var seconds = Math.Min(60, Math.Pow(2, Math.Min(attempt - 1, 5)));
                var jitter = Random.Shared.NextDouble() * Math.Min(3, seconds * 0.25);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(seconds + jitter), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task ReadLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            message.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    return;
                if (result.MessageType != WebSocketMessageType.Text)
                    throw new WebSocketException("Only text protocol frames are accepted.");
                message.Write(buffer, 0, result.Count);
                if (message.Length > 64 * 1024)
                    throw new WebSocketException("Relay frame exceeded 64 KiB.");
            }
            while (!result.EndOfMessage);

            var envelope = JsonSerializer.Deserialize<ServerEnvelope>(message.ToArray(), JsonOptions)
                ?? throw new JsonException("Relay returned an empty envelope.");
            HandleServerEnvelope(envelope);
        }
    }

    private async Task WriteLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            await outgoingSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!outgoing.TryDequeue(out var queued))
                continue;
            Interlocked.Decrement(ref outgoingCount);

            if (DateTime.UtcNow - queued.CreatedAtUtc > queued.MaxAge)
                continue;
            if (queued.RequiresAuthentication && State != RelayConnectionState.Authenticated)
                continue;

            await SendEnvelopeAsync(socket, queued.Envelope, cancellationToken).ConfigureAwait(false);
        }
    }

    private void HandleServerEnvelope(ServerEnvelope envelope)
    {
        switch (envelope.Type)
        {
            case "hello.ack":
            {
                var payload = envelope.Payload.Deserialize<HelloAckPayload>(JsonOptions)
                    ?? throw new JsonException("Invalid hello acknowledgement.");
                DiscordUsername = payload.DiscordUsername;
                RelayChannelName = payload.RelayChannelName;
                BackendVersion = payload.BackendVersion;
                State = payload.Linked ? RelayConnectionState.Authenticated : RelayConnectionState.ConnectedUnlinked;
                break;
            }
            case "pair.code":
            {
                var payload = envelope.Payload.Deserialize<PairCodePayload>(JsonOptions)
                    ?? throw new JsonException("Invalid pairing code response.");
                PairCode = payload.Code;
                PairCodeExpiresAtUtc = payload.ExpiresAtUtc;
                break;
            }
            case "pair.completed":
            {
                var payload = envelope.Payload.Deserialize<PairCompletedPayload>(JsonOptions)
                    ?? throw new JsonException("Invalid pairing completion response.");
                liveClientToken = payload.ClientToken;
                DiscordUsername = payload.DiscordUsername;
                RelayChannelName = payload.RelayChannelName;
                PairCode = null;
                PairCodeExpiresAtUtc = null;
                State = RelayConnectionState.Authenticated;
                PairCompleted?.Invoke(payload.ClientToken, payload.DiscordUsername, payload.RelayChannelName);
                break;
            }
            case "chat.outbound":
            {
                var payload = envelope.Payload.Deserialize<OutboundChatPayload>(JsonOptions)
                    ?? throw new JsonException("Invalid outbound chat payload.");
                LastOutboundReceivedUtc = DateTime.UtcNow;
                OutboundReceived?.Invoke(payload);
                break;
            }
            case "link.revoked":
                liveClientToken = null;
                DiscordUsername = null;
                RelayChannelName = null;
                State = RelayConnectionState.ConnectedUnlinked;
                LinkRevoked?.Invoke();
                break;
            case "error":
            {
                var payload = envelope.Payload.Deserialize<ErrorPayload>(JsonOptions);
                LastError = payload is null ? "Relay service returned an error." : $"{payload.Code}: {payload.Message}";
                if (payload?.Code == "authentication_failed")
                {
                    liveClientToken = null;
                    DiscordUsername = null;
                    RelayChannelName = null;
                    LinkRevoked?.Invoke();
                }
                break;
            }
        }
    }

    private bool Queue(string type, object payload, bool requiresAuthentication, TimeSpan maxAge)
    {
        if (disposed || active is null)
            return false;
        if (State is RelayConnectionState.Disabled or RelayConnectionState.Offline or RelayConnectionState.Disposed)
            return false;
        if (requiresAuthentication && State != RelayConnectionState.Authenticated)
            return false;
        if (Volatile.Read(ref outgoingCount) >= 64)
            return false;

        outgoing.Enqueue(new QueuedEnvelope(
            new ClientEnvelope(type, Guid.NewGuid().ToString("N"), payload),
            DateTime.UtcNow,
            maxAge,
            requiresAuthentication));
        Interlocked.Increment(ref outgoingCount);
        outgoingSignal.Release();
        return true;
    }

    private static async Task SendEnvelopeAsync(
        ClientWebSocket socket,
        ClientEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
    }

    private void StopSessionLocked()
    {
        sessionCancellation?.Cancel();
        sessionCancellation?.Dispose();
        sessionCancellation = null;
        sessionTask = null;
        while (outgoing.TryDequeue(out _))
            Interlocked.Decrement(ref outgoingCount);
        connectionStartedUtc = default;
    }

    private static bool TryValidateEndpoint(string value, out Uri endpoint, out string? error)
    {
        endpoint = null!;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = null;
            return false;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsedEndpoint)
            || parsedEndpoint is null
            || (parsedEndpoint.Scheme != "wss" && parsedEndpoint.Scheme != "ws"))
        {
            error = "Service URL must be an absolute wss:// WebSocket address.";
            return false;
        }

        if (parsedEndpoint.Scheme == "ws"
            && parsedEndpoint.Host is not "localhost" and not "127.0.0.1" and not "::1")
        {
            error = "Unencrypted ws:// is allowed only for local development.";
            return false;
        }

        endpoint = parsedEndpoint;
        error = null;
        return true;
    }

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static string SanitizeError(string error)
    {
        var singleLine = error.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return singleLine.Length <= 240 ? singleLine : singleLine[..240];
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    private sealed record ActiveSession(string ServiceUrl, CharacterProfile Profile, CharacterIdentity Identity);

    private sealed record QueuedEnvelope(
        ClientEnvelope Envelope,
        DateTime CreatedAtUtc,
        TimeSpan MaxAge,
        bool RequiresAuthentication);
}
