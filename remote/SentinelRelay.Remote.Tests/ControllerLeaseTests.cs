using SentinelRelay.Remote;

static class ControllerLeaseTests
{
    public static void Run(Action<bool, string> check)
    {
        var binding = new GameBinding("slot", 42, 123, "ffxiv_dx11.exe", "cid:A");
        var owner = new ControllerOwner("phone", "session", "host-epoch", binding, "game-epoch");
        var other = owner with { DeviceId = "other-phone", SessionId = "other-session" };
        var held = new ControllerFrame(1, -1, 0.5, -0.5, 1, 0.25, 0x1000);
        var clock = new LeaseClock();
        var output = new FakeControllerOutput();
        var allowed = true;
        using var lease = new ControllerLease(output, _ => allowed, clock);
        check(lease.Acquire(owner with { SessionId = "" }).Result == ControllerResult.TargetUnavailable &&
            lease.Acquire(owner with { Binding = binding with { ProcessId = 0 } }).Result == ControllerResult.TargetUnavailable,
            "controller acquire requires complete local identity");
        var id = lease.Acquire(owner).LeaseId!.Value;
        check(output.Last == ControllerFrame.Neutral && output.Sends == 0, "controller acquire starts neutral");
        check(lease.Acquire(other).Result == ControllerResult.Busy && lease.Acquire(owner).Result == ControllerResult.Busy,
            "one exclusive lease; reconnect cannot silently take ownership");
        check(lease.Submit(id, other, 1, held) == ControllerResult.NotOwner && output.Sends == 0,
            "another session cannot use a lease");
        check(lease.Submit(id, owner, 1, held) == ControllerResult.Applied && output.Last == held,
            "valid complete controller frame applied");
        clock.Advance(TimeSpan.FromMilliseconds(450));
        check(lease.Submit(id, owner, 1, held) == ControllerResult.ReplayRejected, "duplicate controller sequence rejected");
        clock.MoveWallClock(TimeSpan.FromDays(-1));
        clock.Advance(TimeSpan.FromMilliseconds(50));
        check(output.Last == ControllerFrame.Neutral && lease.Submit(id, owner, 2, held) == ControllerResult.NotOwner,
            "silent loss releases at timeout; replay and wall-clock rollback cannot keep input alive");

        id = lease.Acquire(owner).LeaseId!.Value;
        lease.Submit(id, owner, 1, held);
        var before = output.Neutralizations;
        clock.Advance(TimeSpan.FromMilliseconds(400));
        check(output.Last == held && output.Neutralizations == before, "watchdog retains a fresh authorized frame");
        check(lease.Submit(id, owner, 2, ControllerFrame.Neutral) == ControllerResult.Applied,
            "full neutral frame releases all held controls");
        clock.Advance(TimeSpan.FromMilliseconds(400));
        check(lease.Submit(id, owner, 3, held) == ControllerResult.Applied, "fresh frames renew timeout");
        allowed = false;
        clock.Advance(ControllerLease.WatchdogPeriod);
        check(output.Last == ControllerFrame.Neutral && lease.Acquire(owner).Result == ControllerResult.TargetUnavailable,
            "independent watchdog releases permission or target loss without a new request");
        allowed = true;

        id = lease.Acquire(owner).LeaseId!.Value;
        lease.Submit(id, owner, 1, held);
        lease.RevokeDevice(other.DeviceId);
        check(output.Last == held, "unrelated revocation cannot change ownership");
        lease.RevokeDevice(owner.DeviceId);
        check(output.Last == ControllerFrame.Neutral && lease.Submit(id, owner, 2, held) == ControllerResult.NotOwner,
            "device revoke releases and retires lease");

        id = lease.Acquire(owner).LeaseId!.Value;
        lease.Submit(id, owner, 1, held);
        clock.AdvanceWithoutTick(ControllerLease.FrameTimeout);
        check(lease.Submit(id, owner, 2, held) == ControllerResult.TargetUnavailable && output.Last == ControllerFrame.Neutral,
            "late frame cannot revive expired lease before timer callback");

        id = lease.Acquire(owner).LeaseId!.Value;
        check(lease.Release(id, other) == ControllerResult.NotOwner && lease.Release(id, owner) == ControllerResult.Released,
            "only exact owner can explicitly release");
        var next = lease.Acquire(other).LeaseId!.Value;
        check(lease.Submit(id, owner, 99, held) == ControllerResult.NotOwner && lease.Submit(next, other, 1, held) == ControllerResult.Applied,
            "client switch uses fresh lease and old frames cannot cross-route");
        lease.Release(next, other);

        var invalid = new[] { held with { LeftX = double.NaN }, held with { RightY = double.PositiveInfinity },
            held with { LeftTrigger = -0.01 }, held with { RightTrigger = 1.01 }, held with { LeftY = -1.01 },
            held with { Buttons = 0x0400 } };
        foreach (var frame in invalid)
        {
            id = lease.Acquire(owner).LeaseId!.Value;
            lease.Submit(id, owner, 1, held);
            check(lease.Submit(id, owner, 2, frame) == ControllerResult.InvalidFrame && output.Last == ControllerFrame.Neutral &&
                lease.Submit(id, owner, 3, held) == ControllerResult.NotOwner, "invalid controller frame retires lease and neutralizes");
        }
        id = lease.Acquire(owner).LeaseId!.Value;
        check(lease.Submit(id, owner, 0, held) == ControllerResult.ReplayRejected &&
            lease.Submit(id, owner, -1, held) == ControllerResult.ReplayRejected, "controller sequence must be positive");
        lease.EmergencyStop();
        check(output.Last == ControllerFrame.Neutral && lease.Acquire(owner).Result == ControllerResult.Unavailable,
            "local emergency stop latches input off");

        // The guard must incorporate every authority, game identity and single-client veto.
        var current = owner;
        var clientCount = 1;
        var guardThrows = false;
        var guardedOutput = new FakeControllerOutput();
        using var guarded = new ControllerLease(guardedOutput, candidate =>
        {
            if (guardThrows) throw new IOException("observation unavailable");
            return clientCount == 1 && candidate == current;
        }, clock);
        foreach (var changed in new[] { owner with { SessionId = "new-session" }, owner with { HostEpoch = "new-host" },
            owner with { GameEpoch = "new-game" }, owner with { Binding = binding with { ProcessId = 43 } },
            owner with { Binding = binding with { StartedUtcTicks = 124 } },
            owner with { Binding = binding with { CharacterKey = "cid:B" } } })
        {
            current = owner;
            id = guarded.Acquire(owner).LeaseId!.Value;
            guarded.Submit(id, owner, 1, held);
            current = changed;
            clock.Advance(ControllerLease.WatchdogPeriod);
            check(guardedOutput.Last == ControllerFrame.Neutral && guarded.Submit(id, owner, 2, held) == ControllerResult.NotOwner,
                "session/host/game/process/character identity change releases controller");
        }
        current = owner;
        id = guarded.Acquire(owner).LeaseId!.Value;
        guarded.Submit(id, owner, 1, held);
        clientCount = 2;
        clock.Advance(ControllerLease.WatchdogPeriod);
        check(guardedOutput.Last == ControllerFrame.Neutral && guarded.Acquire(owner).Result == ControllerResult.TargetUnavailable,
            "opening second game vetoes and releases first-version input");
        clientCount = 1;
        id = guarded.Acquire(owner).LeaseId!.Value;
        guarded.Submit(id, owner, 1, held);
        guardThrows = true;
        clock.Advance(ControllerLease.WatchdogPeriod);
        check(guardedOutput.Last == ControllerFrame.Neutral, "observation failure releases controller");

        var broken = new FakeControllerOutput();
        using var failing = new ControllerLease(broken, _ => true, clock);
        id = failing.Acquire(owner).LeaseId!.Value;
        broken.FailSend = true;
        check(failing.Submit(id, owner, 1, held) == ControllerResult.Unavailable && failing.Faulted &&
            broken.Last == ControllerFrame.Neutral && failing.Acquire(owner).Result == ControllerResult.Unavailable,
            "backend send failure neutralizes and latches fault");

        broken = new FakeControllerOutput { FailNeutral = true };
        using var cannotRelease = new ControllerLease(broken, _ => true, clock);
        check(cannotRelease.Acquire(owner).Result == ControllerResult.Unavailable && cannotRelease.Faulted,
            "failed neutral baseline prevents acquisition");

        var disposableOutput = new FakeControllerOutput();
        var disposable = new ControllerLease(disposableOutput, _ => true, clock);
        id = disposable.Acquire(owner).LeaseId!.Value;
        disposable.Submit(id, owner, 1, held);
        disposable.Dispose();
        disposable.Dispose();
        clock.Advance(TimeSpan.FromSeconds(1));
        check(disposableOutput.Last == ControllerFrame.Neutral && disposable.Acquire(owner).Result == ControllerResult.Unavailable,
            "shutdown is idempotent and neutralizes held controls");

        // Contending acquire calls never produce two owners.
        using var concurrent = new ControllerLease(new FakeControllerOutput(), _ => true, clock);
        var results = new ControllerAcquisition[16];
        Parallel.For(0, results.Length, i => results[i] = concurrent.Acquire(owner with { SessionId = i.ToString() }));
        check(results.Count(r => r.Result == ControllerResult.Acquired) == 1 &&
            results.Count(r => r.Result == ControllerResult.Busy) == 15, "concurrent acquire has exactly one winner");
    }

    private sealed class FakeControllerOutput : IControllerOutput
    {
        public ControllerFrame Last;
        public int Sends;
        public int Neutralizations;
        public bool FailSend;
        public bool FailNeutral;
        public void Send(ControllerFrame frame) { Last = frame; Sends++; if (FailSend) throw new IOException(); }
        public void Neutralize() { Neutralizations++; if (FailNeutral) throw new IOException(); Last = ControllerFrame.Neutral; }
    }

    private sealed class LeaseClock : TimeProvider
    {
        private long timestamp;
        private DateTimeOffset wall = DateTimeOffset.UnixEpoch;
        private readonly List<ManualTimer> timers = [];
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => timestamp;
        public override DateTimeOffset GetUtcNow() => wall;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state);
            timers.Add(timer);
            return timer;
        }
        public void Advance(TimeSpan amount)
        {
            timestamp += amount.Ticks;
            wall += amount;
            foreach (var timer in timers.ToArray()) timer.Fire();
        }
        public void AdvanceWithoutTick(TimeSpan amount) { timestamp += amount.Ticks; wall += amount; }
        public void MoveWallClock(TimeSpan amount) => wall += amount;
        private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
        {
            private bool disposed;
            public void Fire() { if (!disposed) callback(state); }
            public bool Change(TimeSpan dueTime, TimeSpan period) => !disposed;
            public void Dispose() => disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
