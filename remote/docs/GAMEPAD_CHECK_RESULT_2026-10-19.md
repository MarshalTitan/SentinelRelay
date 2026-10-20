# Windows gamepad backend field result — 2026-10-19

User-reported result from Sentinel Remote Gamepad Check 0.1.0, downloaded from the [validated candidate run](https://github.com/MarshalTitan/SentinelRelay/actions/runs/39205234441), source commit `85c81a313204d636676af922b86310c05bbaedab`.

- Windows: Microsoft Windows 10.0.19045.
- Normal-session token type: 1 (ordinary, non-elevated session).
- Overall copied result: **PASS**.
- A free XInput slot was available; preexisting slots were excluded.

For each failure case below, the helper reported all of these checks passed: IPv4 loopback-only backend, real XInput enumeration, exact submitted buttons/both sticks/independent triggers, complete neutral frame, held input before failure, backend exit, native held-controller disconnection, preexisting physical slots still connected, and no orphan backend API.

| Failure case | Reported result |
|---|---|
| Worker stdin EOF | PASS |
| Worker silence with stdin open | PASS |
| Worker force-kill | PASS |
| Stale worker packet | PASS |
| Replayed worker packet | PASS |

This establishes native backend readback, neutralization and failure cleanup on this Windows 10 PC under an ordinary session. The copied report contains no per-case timing measurements. The preexisting-slot checks do not establish that a physical controller was attached or that in-game controller coexistence works.

The separate Windows Server 2025 CI native-enumeration failure remains accurately recorded; this field result does not turn that CI job green or prove Windows 11 compatibility. Driver-install/reboot behavior was not separately reported.

Still required: host authorization and explicit manual-input permission, single-client enforcement, active-control indication/emergency stop, bounded phone transport, Android lifecycle/touch handling, and FFXIV movement/camera/coexistence/full release field acceptance. No phone gamepad controls are enabled by this result.

The S Rank Skip + Keep Queue preservation observation remains deferred; it is unrelated to this backend proof.
