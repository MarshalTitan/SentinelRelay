# Sentinel Remote implementation and acceptance plan

## Current desktop scope (0.3.0 candidate)

The user clarified the intended product: mirror the Windows desktop on the phone; manually open apps/files, operate XIVLauncher and select the intended character; then combine that view with joysticks/buttons for occasional movement, target and menu recovery. This explicitly replaces the original game-window-only restriction. The primary-desktop view, touch/Unicode keyboard control and combined rescue gamepad are implemented in the 0.3.0 draft candidates. Desktop access has separate local per-phone permission and does not depend on a running game. See [desktop mirror setup and field gate](DESKTOP_MIRROR.md). Native PC/GPU/DPI/monitor-off behavior and the real login-to-game handoff remain acceptance checks, not claims inferred from protocol tests.


Phase 0 is documented in the architecture audit. The Phase 1 host and Milestone A Android/bridge software are implemented as candidates. Milestone A has partial real-device evidence recorded in [Desktop setup](DESKTOP_SETUP.md); the full [consolidated checklist](MILESTONE_A.md) is still open. Skip + Keep Queue queue preservation is explicitly deferred because consecutive S Rank spawns are unavailable; it does not block independent development. The original staged work table below describes each gate, not a claim that every later phase is implemented.

| Stage | Software work | Acceptance gate |
|---|---|---|
| Foundation — current candidate | Local registry/grants, one-time pairing, revocation, disabled default/emergency CLI, private Serve boundary, versioned strict JSON, expiring sessions, replay checks, reset intent and independent CI/artifact | Automated Windows/Linux authority/persistence/HTTP checks and unchanged Relay package/regressions. Real HTTPS/tailnet proving remains required with Android. |
| A: Android/status + local bridge | Create Flutter app/protocol client; secure storage, pairing UI, offline handling; opt-in named-pipe Relay bridge, PID/start-time/character binding, heartbeat projection; reuse existing status/skip/reset/screenshot executor; Profiles remains unknown until explicit IPC exists | One consolidated Android + PC session: pair, see both correct clients independently, confirm status and existing SRank commands, reset confirmation, revoke/disable, reconnect and no cross-routing. Test separate PCs and same-PC clients if applicable. |
| B: Manual input | Select maintained/licensed signed virtual-controller backend; establish device/process isolation; exclusive leases, monotonic watchdog, release/neutral on loss, local tray emergency button; touch sticks/buttons | Real devices/game: only selected character responds, camera/buttons work, other game and physical controller unaffected, silent network loss/local revoke neutralize input promptly. No production input until this passes. If isolation cannot be guaranteed, stop for a product decision. |
| C: Game view | Verified per-window Windows capture, 5 FPS rescue frames, byte/dimension/backpressure limits, no desktop fallback; measure foreground/background/minimized behavior; later hardware video only after need/license review | Correct game only; acceptable rescue latency, stale-frame indication; switch cannot leak/cross-control; capture denial/blank/minimized does not fall back to desktop. |
| D: Recovery | Process lifetime monitor independent of plugin, last-known state; configured XIVLauncher-only launch and explicitly confirmed game logout; state-aware lobby/character recovery, bounded incident evidence, secure manual authentication only if unavoidable | Disconnect/exited process distinguished correctly; local launcher/account flow; correct known character; fresh Relay epoch/status; no automatic skip/reset replay. Unknown UI halts assistance; no character modification/deletion tested. |
| iOS | Reuse Flutter protocol/UI; platform secure storage, lifecycle, media and touch verification; signed installation/distribution | After Android/PC input/view proof. User's Apple setup and private distribution approach must be confirmed separately. |

## Milestone A implementation status

1. `MarshalTitan/SentinelRemote` now exists and is accessible, with the Android Flutter application. PC code remains in SentinelRelay.
2. Implemented: Flutter Android client, pinned SDK and locked dependencies, HTTPS-only transport, OS-backed credentials, memory-only sessions/confirmation, per-PC/client selection, background cancellation and no automatic mutation retry.
3. Implemented: current-user-only local pipe, kernel server-PID verification, explicit local process lifetime/character binding, new game epochs, framework-only fixed SRank dispatch and screenshot capture with a second identity check. Real pipe disconnect/expiry and bridge routing/epoch tests pass on Windows and Linux.
4. Implemented: observed process/Relay health, character/world/territory/map, SRank, heartbeat and nullable vnavmesh readiness. Last-known state is explicitly marked. Profiles stays unknown; no config scraping is used.
5. Local GUI/tray and CLI emergency disable/revoke, per-user installation, optional Windows startup, and verified same-character automatic rebinding are implemented. Reported field checks include normal Start launch, retained pairing, tray continuation, game exit/restart reconnection, and screenshots over mobile data. Windows reboot/startup, two-client isolation and emergency/revoke field acceptance remain open.
6. Matched Android debug APK, Windows plugin and self-contained host candidates are available through the [validated download/checklist page](MILESTONE_A.md). Stable private Android release signing remains a later packaging task. The current production Discord catalog is unchanged pending supervised acceptance.

## Automated verification scope

Current tests cover disabled default; pair once/expiry; invalid credentials; device grant filtering; wrong-client/view-only mutation denial; unknown operations; request/session protocol mismatch; sequential and concurrent replays; reset instance/device binding/expiry/consumption; reconnect invalidation; idle/absolute timeout; local revoke/disable; safe status/audit; bounded audit; real state restart/consumption/revocation and corruption; real Kestrel identity/origin/schema/query/admin-route/authentication/replay/body/rate-limit behavior. Existing Relay policy tests and plugin build run independently.

Bridge/process/binding checks and the backend-independent controller lease/watchdog now have automated coverage. Future gates add a real controller backend and its independent failure watchdog, live authorization wiring, single-client enforcement, window ownership/capture limits, reconnect state machines and secret-redacted local incidents. No destructive account/character operation belongs in automated or manual testing.

CI cannot certify Tailscale deployment, Android secure storage/lifecycle, game controller routing, native capture, launcher account behavior or physical character choice. Those are explicitly supervised gates; no labels or simulated status substitute for them.

## Distribution research

Android: Flutter supports APK output/private installation. A stable private signing key is needed for update continuity; keep it out of source and public artifacts. Store distribution is unnecessary for the first two-device proving build. See [official Android release guide](https://docs.flutter.dev/deployment/android).

iOS: building/signing requires the Apple/Xcode toolchain. Apple's free Personal Team is for personal device testing and provisioning expires after seven days, requiring rebuilding/reinstallation; this is a poor default for unattended rescue. A developer-program/TestFlight path is a later option, with its own signing/account/beta constraints. Do not promise a permanently installable free IPA or let Apple setup block Android. Recheck current requirements before Phase 9.

- [Apple developer account and Personal Team](https://developer.apple.com/help/account/basics/about-your-developer-account)
- [TestFlight overview](https://developer.apple.com/help/app-store-connect/test-a-beta-version/testflight-overview)

## Current input and recovery candidates

The draft gamepad integration now connects Android touch controls to the supervised VIIPER worker. Windows 10 native helper checks passed, and the user confirmed actual movement and camera control. PC 0.2.3 allows background/minimized play while retaining live character/process/epoch, single-FFXIV, Windows desktop/session, authorization and watchdog checks. Minimized/monitor-off behavior and the complete interruption/release/coexistence matrix still require field acceptance; Windows Server native enumeration CI remains unresolved.

The first gamepad version is limited to exactly one running FFXIV client per PC, as approved by the user. See [manual input research and integration gate](MILESTONE_B.md) and [current preview setup and recovery scope](GAMEPAD_PREVIEW.md).

A separate authenticated locally permitted launcher action now opens a locally selected/hash-approved XIVLauncher without requiring FFXIV to be running. A saved-process window restore action is also implemented. Android Preview 0.2.1 exposes these buttons. The phone-to-PC connection is independent of an active gamepad lease.

Now implemented as a candidate: manual desktop view and touch/keyboard interaction before FFXIV starts, including launcher/lobby/character-selection screens, plus the combined gamepad view. Still unimplemented: automatic state-aware login assistance, confirmed automatic logout, high-rate hardware video, Profiles IPC, incident collection/export, stable Android preview signing and iOS distribution. Missing bridge identity must not authorize unidentified lobby/game control. The intended workflow is phone desktop access -> manually open XIVLauncher or another app -> user authentication -> view/select intended character -> verified in-game rescue controls. Software for this manual workflow is in the 0.3.0 draft candidates; do not call it accepted until proven on devices.
