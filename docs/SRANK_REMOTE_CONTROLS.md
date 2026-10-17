# SRank remote controls

Versions: SRank **0.7.62.0** / Relay **0.5.0.7**. The user authorized normal live-updater
delivery on 2026-10-17 UTC with remote controls default OFF. **The user reports remote controls working; status and reset-confirmation replies are evidenced.**
Separate skip/queue, expiry, isolation and permission-OFF scenarios are not individually evidenced.
Baselines inspected: SRank main f853eead173ed0668641617d43cdfba0a97115a6, published 0.7.56.0;
Relay main d9e7f6fc1662f2e26275888e5a31ea4b9574ca3e, published 0.5.0.5.
Core latest 0.4.1 was inspected; both consumers retain exact compatible 0.3.1 UI pins.
No navigation proving code, Core change or mandatory runtime coupling.

## Fixed contract and permissions

Ordinary Discord **text messages**, not registered Discord slash/application commands:
`/srank status`, `/srank skip`, `/srank reset`, `/srank reset confirm`.
Unknown commands, extra parameters, multiline input and arbitrary commands do not execute.
No backend, companion app, remote desktop, UI click simulation or chat-command forwarding.

Relay adds **Allow SRankSentinel remote controls**, per character, default OFF.
It is independent of **Enable Discord → FFXIV Replies**, outbound chat destinations, and screenshots.
Use the existing protected bot credential, exact relay channel and authorized user,
plus the character-specific webhook for compact no-timestamp/no-mention responses.
Require the real currently loaded character; wrong users/channels/characters, bots,
webhooks, stale/duplicate messages, paused/disabled profiles and old reader generations
never execute or reveal private state.

The reader persists its high-water checkpoint before any action. A separate in-memory
consumed-message guard prevents duplicates inside/replayed batches. Reconnect priming
discards messages sent offline. Generation checks reject queued work from old readers;
the current player and permission are rechecked immediately before synchronous IPC.
No action is retried because IPC or response delivery failed.

`reset` creates pending intent only. `reset confirm` requires the same character,
channel and authorized user within 30 seconds of the original Discord timestamp.
It is one-shot and cleared on pause, profile/connection/credential/webhook changes,
logout, reload/unload and observed reader interruption. Delayed polling does not
renew an expired request. No confirmation or token is persisted.
An accepted reset means the existing recovery action was **requested**, not that
Ul'dah travel has already finished.

SRank exposes exactly three string→JSON functions:
- `SRankSentinel.Remote.v1.Status`
- `SRankSentinel.Remote.v1.SkipCurrentKeepQueue`
- `SRankSentinel.Remote.v1.StopResetThroughUldah`

Each takes the expected local character key, checks the framework thread/current
character, and returns schema `sentinel.srank.remote.v1`, Version, Success,
Reason, and Status. The private character binding is omitted from JSON.
The IPC client uses fixed names only; it handles absent/unloaded/old providers,
malformed responses and native exceptions without arbitrary method invocation.

SRank rejects mutations while disabled, initializing/loading, without the correct
character, or already resetting. Skip also requires an active hunt. Status is
read-only. Local UI and IPC use the exact same extracted existing recovery methods:
skip retains queued alerts; reset clears queued alerts; neither implicitly enables
Sentinel. Original ManualSkip evidence/return semantics remain unchanged.

Logs: typed RemoteControlRequested/Rejected/Confirmed/Executed/Failed with correlation
ID, operation, configured character, resulting state and fixed reason. No bot token,
webhook, Discord IDs, raw command text or full status text is logged.

## Wrothy first — supervised test

Use Dalamud's normal plugin updater with the permanent Sentinel repository:
`https://raw.githubusercontent.com/MarshalTitan/Sentinel/main/repo.json`.
Keep dev copies disabled; update/reload the live plugins and confirm SRank **0.7.62.0**
and Relay **0.5.0.7**. Do not delete config. Do not enable Elektra yet.

1. Log into Wrothy Minioa. In `/srelay`, retain the existing Wrothy webhook, bot
   credential, channel and authorized user. Replies & Controls → Remote Sentinel
   Controls: enable **Allow SRankSentinel remote controls**, then **Save Reply
   Permissions**. Wait for the Discord reader to connect. Chat replies need not be on.
2. Send ordinary text `/srank status` in #relay-wrothy. Version/enabled/state/status/
   hunt/queue must match the SRank UI. A missing provider must report unavailable.
3. In a safe supervised hunt where skip is appropriate, note the queue; send
   `/srank skip`. Expect exactly the existing **SKIP CURRENT + KEEP QUEUE** behavior,
   recovery requested through Ul'dah and the remaining queue preserved.
4. In another safe situation, send `/srank reset`: no game action yet. Send
   `/srank reset confirm` within 30s: expect exactly **STOP + RESET THROUGH UL'DAH**
   (queue cleared, normal enabled state retained). A late/second confirm must not act.
5. Keep Elektra's new permission OFF. Wrothy-channel messages must not control her
   client. Turn Wrothy's permission OFF/save; `/srank status` must no longer return
   private state or execute recovery. Test Elektra separately only after Wrothy passes.

Return PASS/FAIL for status, skip/queue, reset/confirmation, isolation and permission
OFF. On failure only: timestamp, plugin versions, command and concise sanitized
response/last RemoteControl log lines. Do not send tokens, webhook URLs or config.
`/screenshot` is optional and not a prerequisite.

Rollback: turn the permission OFF/save; normal hunting and Relay remain available.
For a binary rollback, unload the new copies and use preserved prior SRank **0.7.56.0**
and Relay **0.5.0.5** releases. Configuration is additive; no cleanup or migration is
needed. Publication alone does not prove native acceptance; the scoped returned evidence is recorded below.

## Territory labels — Relay 0.5.0.7

Status and recovery replies show the localized territory/map name and numeric ID, resolved
from the TerritoryType row matching SRank's status snapshot. For example:
`Territory: <map name> (1191)`. Missing rows/sheets/names retain the numeric ID.
Resolution failure does not suppress the recovery result or retry an action.
The fixed v1 IPC contract and SRank 0.7.62.0 require no change.

User evidence at 2026-10-16 23:39 America/Toronto: “Everything is working great.”
The attached status reply shows SRank 0.7.62.0, Idle, enabled, queue 0, territory 250;
reset creates confirmation and a confirmed reset returns ResetToUldah/queue 0.
This is scoped functional acceptance, not proof of every negative authorization case.

After updating Relay through the normal Sentinel updater, send one new
`/srank status` in the existing authorized Discord channel. Expect the current game's
localized map name plus its ID. No repeat reset/skip or configuration changes are needed.
Report only a wrong/missing name with territory ID, game language and Relay version.
Rollback: disable the remote-control permission/save or restore Relay 0.5.0.6;
retain settings.
