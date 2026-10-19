# Milestone B: manual gamepad control

## Scope and current state

The user approved exactly one running FFXIV client per PC for the first manual-control version. Status and existing bounded rescue commands retain their multi-client design. If any second FFXIV client appears, even unbound or not opted into Relay, manual input must release and stay unavailable. Unknown process visibility also vetoes control. This is a product limitation, not proof that a virtual device is process-isolated.

Implemented preparation: `ControllerLease` and a separate experimental VIIPER worker/adapter under `SentinelRelay.RemoteInput`. The shipped host, installer and Android do not invoke or bundle this worker. There is no phone input endpoint or touch-control UI yet. Existing Android 0.1.0 and desktop 0.1.2 remain the usable candidates; no reinstall is needed for this preparation.

- Exactly one owner per output coordinator, bound to device/session, host epoch, saved process lifetime/character binding and current game epoch.
- Full gamepad states only, bounded axes/triggers and allowlisted buttons; no keyboard/mouse/chat/path fields.
- Strict positive rising sequence; rejected/replayed frames cannot extend ownership. No frame queue/retry.
- Complete neutralization on explicit release, matching-device revoke, observation/authorization failure, expiry, shutdown or emergency stop. A fresh lease is required after release.
- Monotonic 500 ms frame expiry, checked independently every 50 ms and before accepting a frame. Expiry cannot be undone by a late frame or wall-clock adjustment.
- Backend exceptions latch a fault; emergency stop latches off. A new coordinator is required before another explicit acquisition.

These are tests against a fake output, not native gamepad acceptance. The authorization callback must be bounded/non-reentrant and validate live authority plus every identity/isolation veto. The adapter must be bounded/non-reentrant; a blocked/crashed coordinator cannot run its own timer. An independent worker/OS device lifetime release mechanism is mandatory before live output. One host must own the one output coordinator; creating additional coordinators must not create competing devices.

## External worker proof

The candidate backend is upstream **VIIPER 0.9.0**, a separately running GPL-3.0 server, with **usbip-win2 0.9.8.1 x64**, its separately installed signed driver. Our adapter implements the documented gamepad TCP protocol independently; it does not link GPL libVIIPER, copy its server source, install drivers, or modify machine certificate stores. The backend binary is hash-pinned. CI also checks Authenticode on the exact Windows backend and driver-installer release assets.

The worker owns a fresh backend with both USB/IP and management listeners explicitly bound to `127.0.0.1`. It verifies kernel socket ownership against its live child before management requests and stream connection. It does not adopt another user's VIIPER service. Environment/configuration are isolated; update notifications, tray and raw logs are disabled. Backend output is drained without recording or forwarding credentials, frame contents or arbitrary logs.

The local worker pipe carries only length-prefixed, strict-schema complete gamepad states. It rejects version mismatch, reserved/system buttons, invalid axes/triggers, nonpositive/replayed sequences, future timestamps and packets already 250 ms old. It has no general device/profile/command argument on the pipe. A dedicated watchdog thread stops its backend after 500 ms without a valid frame and exits the worker; cancellation alone cannot reliably unblock console reads. A Windows kill-on-close job also owns the backend so a force-killed worker cannot leave its virtual device server alive. This failure path operates independently of HTTP and capture.

The proof workflow distinguishes fake/wire tests, actual-server protocol tests without USB attachment, and **native Windows XInput** readback/failure tests with a driver on disposable CI. Native proof must observe both sticks, both triggers and buttons, a complete neutral frame, then held-device removal after EOF, open-pipe silence, worker force-kill, stale packets and replay. The installer requests a reboot by design; CI readback before that reboot does not establish reboot/install acceptance. A user-facing installation must honor the driver's reboot requirement. No simulated/protocol-only pass may be described as native input acceptance.

No third-party binaries are included in Sentinel artifacts in this proof. Before bundling VIIPER, include GPL license/notices and a durable complete corresponding-source archive for the exact executable alongside its distribution. Keep the MIT TCP adapter separate from GPL libVIIPER. Before bundling or launching any driver installer, separately verify its exact hash, catalog/publisher signature and explicit local install choice. Never import a test certificate or disable Secure Boot/test-signing.

The host still must integrate target/grant/permission/single-client checks before creating any input worker, local active-control indication, emergency/revoke/session/lifecycle release and a separately bounded phone transport. CI does not certify FFXIV mapping, physical-controller coexistence or field release latency.

## Backend research

Official sources checked 2026-10-19; no third-party controller source or binary is bundled and no driver is installed.

| Option | Finding | Gate |
|---|---|---|
| VIIPER 0.9.0 + usbip-win2 0.9.8.1 | Actively developed upstream standalone server; MIT TCP-client route, GPL-3.0 server. Official Windows releases are signed; usbip-win2 supplies the signed kernel transport. Both listener addresses and cleanup behavior are configurable. | Selected for the isolated proof above. Pin releases/hashes, verify signatures, preserve license/source obligations, prove native failure removal and ordinary-user behavior. Native/FFXIV acceptance remains a gate before enabling phone input. |
| HIDMaestro 1.12 | Active MIT UMDF2 SDK, but its documented installation creates/imports a locally trusted self-signed certificate and requires administrator access. | Do not silently change certificate trust stores; it does not meet the existing no-security-setting-change setup requirement. |
| LizardByte libvirtualhid | MIT client library, separately licensed production driver/broker and paid machine activation. | A possible alternative requiring a separate paid-product/terms decision. Do not bundle its restricted driver/broker or assume the MIT client license grants redistribution of those components. |
| Microsoft InputInjector virtual gamepad | Windows-provided API; creation requires restricted `inputInjectionBrokered` package capability. Microsoft's desktop input guidance categorizes InputInjector as UWP-only, while capability guidance describes same-App-Container delivery. Gamepad methods have no per-PID target argument. | Not a supported drop-in backend for the current WinForms host. A separate feasibility design would have to resolve desktop/game delivery and ordinary-user distribution before relying on this route; MSIX identity alone must not be assumed sufficient. No developer mode, elevation or OS security changes. |
| ViGEmBus | Official repository is archived and identifies the project as retired. | Do not add it automatically as a maintained default. |
| Nefarius VirtualPad | Official documentation describes a commercial framework supplied to business partners. | Obtain appropriate terms only after a separate product/licensing decision; no purchase/contact is authorized by this research. |
| Brunner vJoy 2.2.2.0 | Official release has an EV-signed installer/driver and Microsoft attestation for Windows 10/11. MIT source; README explicitly describes limited maintenance resources. A signed release alone does not establish an actively maintained suitable backend. | Do not auto-install or silently waive the maintained-backend gate. FFXIV joystick mapping, supported Windows versions, compatibility and failure release remain unproven. Never follow old test-signing/Secure Boot workarounds. |
| HidHide | Device visibility helper, not an input backend. Documented app access lists identify executable paths, not a particular PID. | Cannot distinguish two FFXIV processes launched from the same executable. Do not hide a user's physical controller or claim process isolation using this alone. |

A reported InputInjector initialization hang/crash on Windows 11 25H2 exists in Microsoft's issue tracker. Issue #10860 is closed, but inspected comments do not identify a shipped fix; the author subsequently attached a reproduction. This is a compatibility test requirement, not proof that all installations fail.

## Integration gate before a phone-control candidate

1. Complete the VIIPER worker's native proof and ordinary-user/game compatibility gates, then package its separate signed driver installation and exact corresponding-source distribution correctly. Existing alternatives remain documented if the proof fails. Do not silently substitute archived ViGEm, import a self-signed driver certificate, or implement a new kernel driver. No automatic installation or consent/security-setting changes.
2. Put native output in the external interactive-user host/worker, never Dalamud. Prove forced worker/host termination cannot leave held controls. Native watchdog remains independent of HTTP and game capture.
3. Wire a separate explicit acquire/frame/release protocol to live session/grant/permission/process checks. A new manual-input opt-in is required; today's rescue-control permission must not silently grant analog movement. Show a local active-control indication and integrate tray disable, revoke, session close, app background, target switch and reconnect release.
4. Enforce exactly one locally observed FFXIV process, including clients outside saved bindings. Validate saved executable/PID/start time/current character/game epoch; unknown/stale observation vetoes input. Stop if a second process appears. Focus alone is not an isolation guarantee.
5. Introduce a separately bounded frame transport. Current 120 requests/minute HTTP budget is for rescue/status and cannot sustain gamepad frames; do not simply remove existing abuse limits.
6. Add Android sticks/buttons only after the backend/transport gates. Use full neutral frames, cancel touches on interruption and release on lifecycle/selection/network loss; never automatically reacquire while a touch is held. Stable private Android signing is still needed for reliable updates.

The consolidated real-device session must verify selected character movement/camera/buttons; physical-controller coexistence; loss of network including silent loss; lock/background; explicit release and local emergency/revoke; game exit/identity change; second-game launch; target change/reconnect; worker/host termination and native backend failure. Measure actual release latency. No production manual controls until this passes. If the backend feasibility experiment cannot meet these constraints, return with a concrete product decision rather than substituting global keyboard input.

## Primary references

- [VIIPER 0.9.0 release and signed binaries](https://github.com/Alia5/VIIPER/releases/tag/v0.9.0)
- [VIIPER standalone distribution and license boundary](https://github.com/Alia5/VIIPER/blob/v0.9.0/docs/getting-started/installation.md)
- [VIIPER fixed Xbox 360 wire contract](https://github.com/Alia5/VIIPER/blob/v0.9.0/docs/devices/xbox360.md)
- [VIIPER server listener/configuration flags](https://github.com/Alia5/VIIPER/blob/v0.9.0/docs/cli/server.md)
- [usbip-win2 0.9.8.1 release](https://github.com/vadimgrn/usbip-win2/releases/tag/v.0.9.8.1)
- [HIDMaestro installation/certificate requirements](https://github.com/hifihedgehog/HIDMaestro)
- [libvirtualhid Windows deployment/license requirements](https://github.com/LizardByte/libvirtualhid/blob/master/docs/windows-driver.md)
- [libvirtualhid license split](https://github.com/LizardByte/libvirtualhid/blob/master/LICENSE.md)

- [Microsoft InputInjector creation/capability](https://learn.microsoft.com/en-us/uwp/api/windows.ui.input.preview.injection.inputinjector.trycreate)
- [Microsoft desktop input support restrictions](https://learn.microsoft.com/en-us/windows/apps/develop/input/)
- [Microsoft package capability and App-Container restrictions](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/app-capability-declarations)
- [Microsoft virtual gamepad initialization](https://learn.microsoft.com/en-us/uwp/api/windows.ui.input.preview.injection.inputinjector.initializegamepadinjection)
- [Microsoft issue #10860 and comments](https://github.com/microsoft/microsoft-ui-xaml/issues/10860)
- [ViGEmBus retirement](https://github.com/nefarius/ViGEmBus)
- [VirtualPad](https://docs.nefarius.at/projects/VirtualPad/)
- [Brunner vJoy signed release](https://github.com/BrunnerInnovation/vJoy/releases/tag/v2.2.2.0)
- [Brunner vJoy maintenance statement and MIT license](https://github.com/BrunnerInnovation/vJoy)
- [HidHide API and app access lists](https://docs.nefarius.at/projects/HidHide/API-Documentation/)
