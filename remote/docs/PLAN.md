# Sentinel Remote implementation and acceptance plan

Phase 0 is documented in the architecture audit. The Phase 1 host and Milestone A Android/bridge software are implemented as candidates. Milestone A remains unproven on real devices until the [consolidated checklist](MILESTONE_A.md) passes. The original staged work table below describes each gate, not a claim that every later phase is implemented.

| Stage | Software work | Acceptance gate |
|---|---|---|
| Foundation — current candidate | Local registry/grants, one-time pairing, revocation, disabled default/emergency CLI, private Serve boundary, versioned strict JSON, expiring sessions, replay checks, reset intent and independent CI/artifact | Automated Windows/Linux authority/persistence/HTTP checks and unchanged Relay package/regressions. Real HTTPS/tailnet proving remains required with Android. |
| A: Android/status + local bridge | Create Flutter app/protocol client; secure storage, pairing UI, offline handling; opt-in named-pipe Relay bridge, PID/start-time/character binding, heartbeat projection; reuse existing status/skip/reset/screenshot executor; Profiles remains unknown until explicit IPC exists | One consolidated Android + PC session: pair, see both correct clients independently, confirm status and existing SRank commands, reset confirmation, revoke/disable, reconnect and no cross-routing. Test separate PCs and same-PC clients if applicable. |
| B: Manual input | Select maintained/licensed signed virtual-controller backend; establish device/process isolation; exclusive leases, monotonic watchdog, release/neutral on loss, local tray emergency button; touch sticks/buttons | Real devices/game: only selected character responds, camera/buttons work, other game and physical controller unaffected, silent network loss/local revoke neutralize input promptly. No production input until this passes. If isolation cannot be guaranteed, stop for a product decision. |
| C: Game view | Verified per-window Windows capture, 5 FPS rescue frames, byte/dimension/backpressure limits, no desktop fallback; measure foreground/background/minimized behavior; later hardware video only after need/license review | Correct game only; acceptable rescue latency, stale-frame indication; switch cannot leak/cross-control; capture denial/blank/minimized does not fall back to desktop. |
| D: Recovery | Process lifetime monitor independent of plugin, last-known state; configured launcher-only action; state-aware lobby/character recovery, bounded incident evidence, secure manual authentication only if unavoidable | Disconnect/exited process distinguished correctly; local launcher/account flow; correct known character; fresh Relay epoch/status; no automatic skip/reset replay. Unknown UI halts assistance; no character modification/deletion tested. |
| iOS | Reuse Flutter protocol/UI; platform secure storage, lifecycle, media and touch verification; signed installation/distribution | After Android/PC input/view proof. User's Apple setup and private distribution approach must be confirmed separately. |

## Milestone A implementation status

1. `MarshalTitan/SentinelRemote` now exists and is accessible, with the Android Flutter application. PC code remains in SentinelRelay.
2. Implemented: Flutter Android client, pinned SDK and locked dependencies, HTTPS-only transport, OS-backed credentials, memory-only sessions/confirmation, per-PC/client selection, background cancellation and no automatic mutation retry.
3. Implemented: current-user-only local pipe, kernel server-PID verification, explicit local process lifetime/character binding, new game epochs, framework-only fixed SRank dispatch and screenshot capture with a second identity check. Real pipe disconnect/expiry and bridge routing/epoch tests pass on Windows and Linux.
4. Implemented: observed process/Relay health, character/world/territory/map, SRank, heartbeat and nullable vnavmesh readiness. Last-known state is explicitly marked. Profiles stays unknown; no config scraping is used.
5. Local CLI emergency disable/revoke is implemented. A user-session tray and startup opt-in remain deferred, and are required before enabling manual input in Milestone B. No administration privilege is required for the current host.
6. Matched Android debug APK, Windows plugin and self-contained host candidates are available through the [validated download/checklist page](MILESTONE_A.md). Stable private Android release signing remains a later packaging task. The current production Discord catalog is unchanged pending supervised acceptance.

## Automated verification scope

Current tests cover disabled default; pair once/expiry; invalid credentials; device grant filtering; wrong-client/view-only mutation denial; unknown operations; request/session protocol mismatch; sequential and concurrent replays; reset instance/device binding/expiry/consumption; reconnect invalidation; idle/absolute timeout; local revoke/disable; safe status/audit; bounded audit; real state restart/consumption/revocation and corruption; real Kestrel identity/origin/schema/query/admin-route/authentication/replay/body/rate-limit behavior. Existing Relay policy tests and plugin build run independently.

Future gates add host/plugin transport authentication, binding ambiguity, process loss/reuse, heartbeat freshness, controller ownership/release/watchdog, window ownership/capture limits, reconnect state machines and secret-redacted local incidents. No destructive account/character operation belongs in automated or manual testing.

CI cannot certify Tailscale deployment, Android secure storage/lifecycle, game controller routing, native capture, launcher account behavior or physical character choice. Those are explicitly supervised gates; no labels or simulated status substitute for them.

## Distribution research

Android: Flutter supports APK output/private installation. A stable private signing key is needed for update continuity; keep it out of source and public artifacts. Store distribution is unnecessary for the first two-device proving build. See [official Android release guide](https://docs.flutter.dev/deployment/android).

iOS: building/signing requires the Apple/Xcode toolchain. Apple's free Personal Team is for personal device testing and provisioning expires after seven days, requiring rebuilding/reinstallation; this is a poor default for unattended rescue. A developer-program/TestFlight path is a later option, with its own signing/account/beta constraints. Do not promise a permanently installable free IPA or let Apple setup block Android. Recheck current requirements before Phase 9.

- [Apple developer account and Personal Team](https://developer.apple.com/help/account/basics/about-your-developer-account)
- [TestFlight overview](https://developer.apple.com/help/app-store-connect/test-a-beta-version/testflight-overview)

## Known unimplemented capabilities

Android pairing/status/rescue UI, private local bridge, process lifetime/character binding, optional vnavmesh readiness and last-known status are implemented candidates. Automatic startup, tray emergency UI, Profiles IPC, automatic rebinding, input ownership/watchdog, driver, streaming, launcher launch, manual authentication, lobby/character assistant and incident collection/export remain unimplemented. CI alone does not establish real-device acceptance.
