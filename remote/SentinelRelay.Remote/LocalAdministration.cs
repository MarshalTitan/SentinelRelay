namespace SentinelRelay.Remote;

/// <summary>Local-only capabilities. Never exposed by the HTTP router.</summary>
public sealed class LocalAdministration(IStateStore store, TimeProvider? clock = null)
{
    public void SetEnabled(bool enabled) => store.Access(state =>
    {
        state.Enabled = enabled;
        state.Epoch = Guid.NewGuid().ToString("N");
        state.Invitations.Clear();
        return (true, true);
    });
    public InstanceInfo AddInstance(string label) => store.Access(state =>
    {
        ValidateLabel(label);
        if (state.Instances.Count >= 16) throw new InvalidOperationException("instance_limit");
        var instance = new InstanceInfo(Guid.NewGuid().ToString("N"), label);
        state.Instances.Add(instance);
        return (instance, true);
    });
    public string Invite(string label, string[] instances, bool control) => store.Access(state =>
    {
        ValidateLabel(label);
        if (!state.Enabled) throw new InvalidOperationException("remote_disabled");
        if (instances.Length == 0 || instances.Distinct().Count() != instances.Length || instances.Any(id => !state.Instances.Any(i => i.InstanceId == id)))
            throw new InvalidOperationException("invalid_instance_grant");
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        state.Invitations.RemoveAll(i => now >= i.Expires);
        if (state.Invitations.Count >= 8) throw new InvalidOperationException("invitation_limit");
        var secret = Protocol.Secret();
        state.Invitations.Add(new(Protocol.Hash(secret), now.AddMinutes(2), label, instances.ToArray(), control));
        return (secret, true);
    });
    public bool Revoke(string id) => store.Access(state =>
    {
        var index = state.Devices.FindIndex(d => d.Id == id);
        if (index < 0) return (false, false);
        state.Devices[index] = state.Devices[index] with { Revoked = true };
        return (true, true);
    });
    public object Status() => store.Access(state => ((object)new
    {
        state.Enabled, state.HostId, state.Instances,
        Devices = state.Devices.Select(d => new { d.Id, d.Label, d.Instances, d.Control, d.Revoked }).ToArray(),
    }, false));
    public void Bind(GameBinding binding) => store.Access(state =>
    {
        if (!state.Instances.Any(i => i.InstanceId == binding.InstanceId)) throw new InvalidOperationException("unknown_instance");
        if (state.Bindings.Any(b => b.InstanceId != binding.InstanceId && b.ProcessId == binding.ProcessId && b.StartedUtcTicks == binding.StartedUtcTicks))
            throw new InvalidOperationException("process_already_bound");
        state.Bindings.RemoveAll(b => b.InstanceId == binding.InstanceId);
        state.Bindings.Add(binding);
        state.Epoch = Guid.NewGuid().ToString("N"); // Rebinding invalidates all old sessions/reset intent.
        return (true, true);
    });
    public LocalHostSnapshot Snapshot() => store.Access(state => (new LocalHostSnapshot(
        state.Enabled, state.Instances.ToArray(), state.Bindings.ToArray(),
        state.Devices.Select(d => new LocalDevice(d.Id, d.Label, d.Control, d.Revoked)).ToArray()), false));

    public InstanceInfo ConnectGame(GameBinding candidate, string label) => store.Access(state =>
    {
        ValidateLabel(label);
        if (string.IsNullOrWhiteSpace(candidate.CharacterKey)) throw new InvalidOperationException("character_missing");
        var matching = state.Bindings.Where(b => b.CharacterKey == candidate.CharacterKey).ToArray();
        if (matching.Length > 1) throw new InvalidOperationException("character_ambiguous");
        InstanceInfo instance;
        if (matching.Length == 1)
            instance = state.Instances.Single(i => i.InstanceId == matching[0].InstanceId);
        else
        {
            if (state.Instances.Count >= 16) throw new InvalidOperationException("instance_limit");
            instance = new(Guid.NewGuid().ToString("N"), label);
            state.Instances.Add(instance);
        }
        if (state.Bindings.Any(b => b.InstanceId != instance.InstanceId && b.ProcessId == candidate.ProcessId && b.StartedUtcTicks == candidate.StartedUtcTicks))
            throw new InvalidOperationException("process_already_bound");
        state.Bindings.RemoveAll(b => b.InstanceId == instance.InstanceId);
        state.Bindings.Add(candidate with { InstanceId = instance.InstanceId });
        state.Epoch = Guid.NewGuid().ToString("N");
        return (instance, true);
    });

    public int ReconnectKnown(IReadOnlyList<GameBinding> discovered) => store.Access(state =>
    {
        var replacements = DesktopSetupPolicy.Rebindings(state.Bindings, discovered);
        foreach (var binding in replacements)
        {
            state.Bindings.RemoveAll(b => b.InstanceId == binding.InstanceId);
            state.Bindings.Add(binding);
        }
        if (replacements.Length > 0) state.Epoch = Guid.NewGuid().ToString("N");
        return (replacements.Length, replacements.Length > 0);
    });

    private static void ValidateLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label) || label.Length > 64 || label.Any(char.IsControl))
            throw new InvalidOperationException("invalid_label");
    }
}

public sealed record LocalDevice(string Id, string Label, bool Control, bool Revoked);
public sealed record LocalHostSnapshot(bool Enabled, InstanceInfo[] Instances, GameBinding[] Bindings, LocalDevice[] Devices);
