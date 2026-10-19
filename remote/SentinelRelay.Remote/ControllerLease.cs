namespace SentinelRelay.Remote;

/// <summary>A complete gamepad state, never a queued key/button transition.</summary>
public readonly record struct ControllerFrame(double LeftX, double LeftY, double RightX, double RightY,
    double LeftTrigger, double RightTrigger, ushort Buttons)
{
    // D-pad, menu/view, sticks, shoulders and four face buttons. Reserved bits are denied.
    public const ushort AllowedButtons = 0xF3FF;
    public static ControllerFrame Neutral => default;
    public bool IsValid => Axis(LeftX) && Axis(LeftY) && Axis(RightX) && Axis(RightY) &&
        Trigger(LeftTrigger) && Trigger(RightTrigger) && (Buttons & ~AllowedButtons) == 0;
    private static bool Axis(double value) => double.IsFinite(value) && value is >= -1 and <= 1;
    private static bool Trigger(double value) => double.IsFinite(value) && value is >= 0 and <= 1;
}

/// <summary>Constructed locally after authorization; session identity must change on reconnect.</summary>
public sealed record ControllerOwner(string DeviceId, string SessionId, string HostEpoch,
    GameBinding Binding, string GameEpoch);

/// <summary>
/// Future external-host adapter only. Calls must be bounded, synchronous and non-reentrant.
/// Neutralize must release every axis/button and detach as appropriate; never send keyboard/mouse input.
/// A native backend also needs an independent worker/process-loss watchdog.
/// </summary>
public interface IControllerOutput
{
    void Send(ControllerFrame frame);
    void Neutralize();
}

public enum ControllerResult
{
    Acquired, Applied, Released, Busy, NotOwner, ReplayRejected, InvalidFrame, TargetUnavailable, Unavailable
}

public sealed record ControllerAcquisition(ControllerResult Result, Guid? LeaseId = null);

/// <summary>
/// Backend-independent Milestone B preparation, deliberately not wired to HTTP or an OS input device.
/// One lease per output. All authorization/identity/isolation checks must be repeated by stillAllowed.
/// </summary>
public sealed class ControllerLease : IDisposable
{
    public static readonly TimeSpan FrameTimeout = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan WatchdogPeriod = TimeSpan.FromMilliseconds(50);
    private readonly object gate = new();
    private readonly IControllerOutput output;
    private readonly Func<ControllerOwner, bool> stillAllowed;
    private readonly TimeProvider time;
    private readonly ITimer watchdog;
    private ActiveLease? active;
    private bool stopped;
    private bool disposed;
    public bool Faulted { get { lock (gate) return faulted; } }
    private bool faulted;

    private sealed class ActiveLease(Guid id, ControllerOwner owner, long now)
    {
        public Guid Id = id;
        public ControllerOwner Owner = owner;
        public long LastFrame = now;
        public long Sequence;
    }

    public ControllerLease(IControllerOutput output, Func<ControllerOwner, bool> stillAllowed, TimeProvider? time = null)
    {
        this.output = output ?? throw new ArgumentNullException(nameof(output));
        this.stillAllowed = stillAllowed ?? throw new ArgumentNullException(nameof(stillAllowed));
        this.time = time ?? TimeProvider.System;
        watchdog = this.time.CreateTimer(_ => Watchdog(), null, WatchdogPeriod, WatchdogPeriod);
    }

    private bool Allowed(ControllerOwner owner)
    {
        if (string.IsNullOrWhiteSpace(owner.DeviceId) || string.IsNullOrWhiteSpace(owner.SessionId) ||
            string.IsNullOrWhiteSpace(owner.HostEpoch) || string.IsNullOrWhiteSpace(owner.GameEpoch) ||
            owner.Binding is not { ProcessId: > 0, StartedUtcTicks: > 0 } binding ||
            string.IsNullOrWhiteSpace(binding.InstanceId) || string.IsNullOrWhiteSpace(binding.Executable) ||
            string.IsNullOrWhiteSpace(binding.CharacterKey)) return false;
        try { return stillAllowed(owner); }
        catch { return false; } // Authorization or observation failure must release, never extend a lease.
    }

    private bool Expired(ActiveLease lease) => time.GetElapsedTime(lease.LastFrame) >= FrameTimeout;
    private bool Neutralize()
    {
        active = null; // An exception cannot leave a logically owned controller.
        try { output.Neutralize(); return true; }
        catch { faulted = true; return false; }
    }

    private void Watchdog()
    {
        lock (gate)
        {
            if (disposed || active is null) return;
            if (Expired(active) || !Allowed(active.Owner)) Neutralize();
        }
    }

    public ControllerAcquisition Acquire(ControllerOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        lock (gate)
        {
            if (disposed || stopped || faulted) return new(ControllerResult.Unavailable);
            if (active is not null)
            {
                if (Expired(active) || !Allowed(active.Owner)) Neutralize();
                else return new(ControllerResult.Busy); // Even the same caller must release before reacquiring.
            }
            if (faulted) return new(ControllerResult.Unavailable);
            if (!Allowed(owner)) return new(ControllerResult.TargetUnavailable);
            if (!Neutralize()) return new(ControllerResult.Unavailable);
            // Recheck after touching the backend; no non-neutral output is sent during acquire.
            if (!Allowed(owner)) return new(ControllerResult.TargetUnavailable);
            var id = Guid.NewGuid();
            active = new(id, owner, time.GetTimestamp());
            return new(ControllerResult.Acquired, id);
        }
    }

    public ControllerResult Submit(Guid leaseId, ControllerOwner owner, long sequence, ControllerFrame frame)
    {
        lock (gate)
        {
            if (disposed || stopped || faulted) return ControllerResult.Unavailable;
            if (active is null || active.Id != leaseId || active.Owner != owner) return ControllerResult.NotOwner;
            if (Expired(active) || !Allowed(owner))
            {
                Neutralize();
                return ControllerResult.TargetUnavailable;
            }
            if (sequence <= 0 || sequence <= active.Sequence) return ControllerResult.ReplayRejected;
            active.Sequence = sequence; // Consume malformed frames too; never replay uncertain output.
            if (!frame.IsValid)
            {
                Neutralize();
                return ControllerResult.InvalidFrame;
            }
            try { output.Send(frame); }
            catch
            {
                faulted = true;
                Neutralize();
                return ControllerResult.Unavailable;
            }
            active.LastFrame = time.GetTimestamp();
            // A bounded adapter may observe local disable/identity loss while sending.
            if (!Allowed(owner)) { Neutralize(); return ControllerResult.TargetUnavailable; }
            return ControllerResult.Applied;
        }
    }

    public ControllerResult Release(Guid leaseId, ControllerOwner owner)
    {
        lock (gate)
        {
            if (disposed || stopped || faulted) return ControllerResult.Unavailable;
            if (active is null || active.Id != leaseId || active.Owner != owner) return ControllerResult.NotOwner;
            return Neutralize() ? ControllerResult.Released : ControllerResult.Unavailable;
        }
    }

    public void RevokeDevice(string deviceId)
    {
        lock (gate)
        {
            if (!disposed && active?.Owner.DeviceId == deviceId) Neutralize();
        }
    }

    /// <summary>Latched off. Recovery requires a new coordinator and a fresh explicit acquire.</summary>
    public void EmergencyStop()
    {
        lock (gate)
        {
            if (disposed) return;
            stopped = true;
            Neutralize();
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            stopped = true;
            Neutralize();
        }
        watchdog.Dispose();
    }
}
