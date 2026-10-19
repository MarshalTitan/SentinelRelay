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
    private static void ValidateLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label) || label.Length > 64 || label.Any(char.IsControl))
            throw new InvalidOperationException("invalid_label");
    }
}
