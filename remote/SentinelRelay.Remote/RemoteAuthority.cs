namespace SentinelRelay.Remote;

/// <summary>Serializes authorization and one-shot intent. No game, shell, filesystem, or input dispatcher.</summary>
public sealed class RemoteAuthority(IStateStore store, TimeProvider? clock = null, IGameBridge? gameBridge = null)
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private readonly object gate = new();
    private readonly Dictionary<string, Session> sessions = [];
    private readonly Queue<AuditEvent> audit = new();
    private readonly Dictionary<string, Queue<long>> mutationTimes = [];
    private static readonly TimeSpan SessionLife = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan IdleLimit = TimeSpan.FromSeconds(60);
    private sealed class Session(string deviceId, string epoch, DateTimeOffset now, long timestamp)
    {
        public string DeviceId = deviceId;
        public string Epoch = epoch;
        public DateTimeOffset Expires = now + SessionLife;
        public long Started = timestamp;
        public long LastSeen = timestamp;
        public long Sequence;
        public string? ResetHash;
        public string? ResetInstance;
        public DateTimeOffset ResetExpires;
        public long ResetStarted;
        public string? ResetGameEpoch;
    }
    private DateTimeOffset Now => time.GetUtcNow();
    private void Record(string code, string? device = null, string? instance = null)
    {
        if (audit.Count == 128) audit.Dequeue();
        audit.Enqueue(new(Now, code, device, instance));
    }
    public AuditEvent[] AuditSnapshot() { lock (gate) return audit.ToArray(); }
    private void Prune()
    {
        var stamp = time.GetTimestamp();
        foreach (var key in sessions.Where(p => time.GetElapsedTime(p.Value.Started, stamp) >= SessionLife ||
            time.GetElapsedTime(p.Value.LastSeen, stamp) >= IdleLimit).Select(p => p.Key).ToArray()) sessions.Remove(key);
    }

    public Reply Enroll(Enrollment request)
    {
        lock (gate) return store.Access(state =>
        {
            if (request.Version != Protocol.Version) return (new Reply(false, "protocol_mismatch"), false);
            if (!state.Enabled) return (new Reply(false, "remote_disabled"), false);
            var invitation = state.Invitations.FirstOrDefault(i => Protocol.Matches(i.Hash, request.Invitation));
            if (invitation is null || Now >= invitation.Expires || invitation.Expires - Now > TimeSpan.FromMinutes(2))
                return (new Reply(false, "pairing_rejected"), false);
            state.Invitations.Remove(invitation); // Persist consumption before returning any credential.
            if (state.Devices.Count >= 32) return (new Reply(false, "device_limit"), true);
            var token = Protocol.Secret();
            var id = Guid.NewGuid().ToString("N");
            state.Devices.Add(new(id, invitation.Label, Protocol.Hash(token), invitation.Instances, invitation.Control));
            Record("device_paired", id);
            return (new Reply(true, "paired", new DeviceCredential(id, token, state.HostId)), true);
        });
    }

    public Reply OpenSession(string? deviceToken, SessionRequest request)
    {
        lock (gate) return store.Access(state =>
        {
            Prune();
            if (request.Version != Protocol.Version) return (new Reply(false, "protocol_mismatch"), false);
            if (!state.Enabled) return (new Reply(false, "remote_disabled"), false);
            var device = state.Devices.FirstOrDefault(d => d.Id == request.DeviceId && !d.Revoked && Protocol.Matches(d.TokenHash, deviceToken));
            if (device is null) return (new Reply(false, "unauthorized"), false);
            // One current session per device; reconnect invalidates old sequences and pending intent.
            foreach (var key in sessions.Where(p => p.Value.DeviceId == device.Id).Select(p => p.Key).ToArray()) sessions.Remove(key);
            if (sessions.Count >= 32) return (new Reply(false, "session_limit"), false);
            var token = Protocol.Secret();
            var session = new Session(device.Id, state.Epoch, Now, time.GetTimestamp());
            sessions.Add(Protocol.Hash(token), session);
            Record("session_opened", device.Id);
            return (new Reply(true, "session_opened", new SessionCredential(token, session.Expires)), false);
        });
    }

    private sealed record GameIntent(string DeviceId, string Epoch, GameBinding? Binding, string Operation, string? ExpectedGameEpoch, Session Session);
    public Reply Handle(string? sessionToken, RemoteRequest request, CancellationToken cancellation = default)
    {
        lock (gate)
        {
          var reply = store.Access(state =>
          {
            Prune();
            if (!state.Enabled) { sessions.Clear(); return (new Reply(false, "remote_disabled"), false); }
            if (!Protocol.IsSecret(sessionToken) || !sessions.TryGetValue(Protocol.Hash(sessionToken!), out var session))
                return (new Reply(false, "unauthorized"), false);
            var device = state.Devices.FirstOrDefault(d => d.Id == session.DeviceId && !d.Revoked);
            if (device is null || state.Epoch != session.Epoch)
            {
                sessions.Remove(Protocol.Hash(sessionToken!));
                return (new Reply(false, "unauthorized"), false);
            }
            if (request.Version != Protocol.Version) return (new Reply(false, "protocol_mismatch"), false);
            // Consume before dispatch, even on denied/unknown requests. Never queue/retry actions.
            if (request.Sequence <= session.Sequence || request.Sequence <= 0)
                return (new Reply(false, "replay_rejected"), false);
            session.Sequence = request.Sequence;
            session.LastSeen = time.GetTimestamp();
            Reply End(bool success, string code, object? data = null)
            {
                Record(code, device.Id, state.Instances.Any(i => i.InstanceId == request.InstanceId) ? request.InstanceId : null);
                return new(success, code, data);
            }
            if (request.Operation == "session.close")
            {
                sessions.Remove(Protocol.Hash(sessionToken!));
                return (End(true, "session_closed"), false);
            }
            if (request.Operation == "instances.list")
                return (End(true, "instances", state.Instances.Where(i => device.Instances.Contains(i.InstanceId)).ToArray()), false);
            if (request.Operation == "session.ping") return (End(true, "alive", new { state.HostId, ProtocolVersion = Protocol.Version }), false);
            if (request.Operation is not ("srank.status" or "screenshot" or "srank.skip" or "srank.reset.request" or "srank.reset.confirm"))
                return (End(false, "operation_not_allowed"), false);
            if (!device.Instances.Contains(request.InstanceId) || !state.Instances.Any(i => i.InstanceId == request.InstanceId))
                return (End(false, "instance_not_authorized"), false);
            if (request.Operation is "srank.skip" or "srank.reset.request" or "srank.reset.confirm" && !device.Control)
                return (End(false, "control_not_authorized"), false);
            if (request.Operation is "srank.skip" or "srank.reset.confirm")
            {
                if (!mutationTimes.TryGetValue(device.Id, out var recent)) mutationTimes[device.Id] = recent = new();
                while (recent.TryPeek(out var stamp) && time.GetElapsedTime(stamp) >= TimeSpan.FromSeconds(30)) recent.Dequeue();
                if (recent.Count >= 5) return (End(false, "cooldown"), false);
                recent.Enqueue(time.GetTimestamp()); // Per-device budget survives session reconnects.
            }
            string? expectedGameEpoch = null;
            if (gameBridge is not null && request.Operation == "srank.reset.request")
                return (End(true, "internal_game_intent", new GameIntent(device.Id, state.Epoch,
                    state.Bindings.FirstOrDefault(b => b.InstanceId == request.InstanceId), request.Operation, null, session)), false);
            if (request.Operation == "srank.reset.request")
            {
                var nonce = Protocol.Secret();
                session.ResetHash = Protocol.Hash(nonce);
                session.ResetInstance = request.InstanceId;
                session.ResetExpires = Now.AddSeconds(30);
                session.ResetStarted = time.GetTimestamp();
                return (End(true, "confirmation_required", new { Confirmation = nonce, Expires = session.ResetExpires }), false);
            }
            if (request.Operation == "srank.reset.confirm")
            {
                var valid = session.ResetHash is not null && session.ResetInstance == request.InstanceId &&
                    time.GetElapsedTime(session.ResetStarted) < TimeSpan.FromSeconds(30) && Protocol.Matches(session.ResetHash, request.Confirmation);
                expectedGameEpoch = session.ResetGameEpoch;
                session.ResetHash = null;
                session.ResetGameEpoch = null;
                session.ResetInstance = null;
                if (!valid) return (End(false, "confirmation_rejected"), false);
            }
            if (gameBridge is not null)
                return (End(true, "internal_game_intent", new GameIntent(device.Id, state.Epoch,
                    state.Bindings.FirstOrDefault(b => b.InstanceId == request.InstanceId), request.Operation, expectedGameEpoch, session)), false);
            return (End(false, "bridge_unavailable"), false);
          });
          if (reply.Data is not GameIntent intent) return reply;
          if (intent.Binding is null || gameBridge is null) return new(false, "bridge_unavailable");
          bool StillAllowed() => !cancellation.IsCancellationRequested &&
            time.GetElapsedTime(intent.Session.Started) < SessionLife && store.Access(state =>
          {
              var device = state.Devices.FirstOrDefault(d => d.Id == intent.DeviceId && !d.Revoked);
              return (state.Enabled && state.Epoch == intent.Epoch && device is not null &&
                  device.Instances.Contains(intent.Binding.InstanceId) && state.Bindings.Contains(intent.Binding) &&
                  (intent.Operation is "srank.status" or "screenshot" || device.Control), false);
          });
          if (!StillAllowed()) return new(false, "unauthorized");
          if (intent.Operation == "srank.reset.request")
          {
              intent.Session.ResetHash = null;
              var snapshot = gameBridge.Read(intent.Binding);
              if (snapshot is null) return new(false, "bridge_unavailable");
              if (!snapshot.ControlAllowed) return new(false, "control_not_authorized");
              if (!StillAllowed()) return new(false, "unauthorized");
              var nonce = Protocol.Secret();
              intent.Session.ResetHash = Protocol.Hash(nonce);
              intent.Session.ResetInstance = intent.Binding.InstanceId;
              intent.Session.ResetGameEpoch = snapshot.GameEpoch;
              intent.Session.ResetStarted = time.GetTimestamp();
              intent.Session.ResetExpires = Now.AddSeconds(30);
              Record("confirmation_required", intent.DeviceId, intent.Binding.InstanceId);
              return new(true, "confirmation_required", new { Confirmation = nonce, Expires = intent.Session.ResetExpires });
          }
          var result = intent.Operation == "srank.status" ? gameBridge.Status(intent.Binding)
              : gameBridge.Execute(intent.Binding, intent.Operation, intent.ExpectedGameEpoch, StillAllowed, cancellation);
          if (!StillAllowed()) return new(false, "unauthorized"); // Also suppress state/frames after revocation.
          Record(result.Code, intent.DeviceId, intent.Binding.InstanceId);
          return result;
        }
    }
}
