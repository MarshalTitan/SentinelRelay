# Milestone B: manual gamepad control

## Scope and current state

The user approved exactly one running FFXIV client per PC for the first manual-control version. Status and existing bounded rescue commands retain their multi-client design. If any second FFXIV client appears, even unbound or not opted into Relay, manual input must release and stay unavailable. Unknown process visibility also vetoes control. This is a product limitation, not proof that a virtual device is process-isolated.

Implemented preparation: `ControllerLease`, an external-host library core with no native backend, network endpoint, installer change or Android control UI. It cannot move a real character. Existing Android 0.1.0 and desktop 0.1.2 remain the usable candidates; no reinstall is needed for this preparation.

- Exactly one owner per output coordinator, bound to device/session, host epoch, saved process lifetime/character binding and current game epoch.
- Full gamepad states only, bounded axes/triggers and allowlisted buttons; no keyboard/mouse/chat/path fields.
- Strict positive rising sequence; rejected/replayed frames cannot extend ownership. No frame queue/retry.
- Complete neutralization on explicit release, matching-device revoke, observation/authorization failure, expiry, shutdown or emergency stop. A fresh lease is required after release.
- Monotonic 500 ms frame expiry, checked independently every 50 ms and before accepting a frame. Expiry cannot be undone by a late frame or wall-clock adjustment.
- Backend exceptions latch a fault; emergency stop latches off. A new coordinator is required before another explicit acquisition.

These are tests against a fake output, not native gamepad acceptance. The authorization callback must be bounded/non-reentrant and validate live authority plus every identity/isolation veto. The adapter must be bounded/non-reentrant; a blocked/crashed coordinator cannot run its own timer. An independent worker/OS device lifetime release mechanism is mandatory before live output. One host must own the one output coordinator; creating additional coordinators must not create competing devices.

## Backend research

Official sources checked 2026-10-19; no third-party controller source or binary is bundled and no driver is installed.

| Option | Finding | Gate |
|---|---|---|
| Microsoft InputInjector virtual gamepad | Windows-provided API; the documented creation path requires restricted `inputInjectionBrokered` package capability. Gamepad methods publish input to the system, with no per-PID target argument. | Preferred next feasibility experiment, not selected for production. Prove ordinary-user packaging/capability and supported Windows compatibility without developer mode, elevation or OS security changes; prove device disposal/worker crash releases, FFXIV compatibility, latency and physical-controller behavior. |
| ViGEmBus | Official repository is archived and identifies the project as retired. | Do not add it automatically as a maintained default. |
| Nefarius VirtualPad | Official documentation describes a commercial framework supplied to business partners. | Obtain appropriate terms only after a separate product/licensing decision; no purchase/contact is authorized by this research. |
| Brunner vJoy 2.2.2.0 | Official release has an EV-signed installer/driver and Microsoft attestation for Windows 10/11. MIT source; README explicitly describes limited maintenance resources. A signed release alone does not establish an actively maintained suitable backend. | Do not auto-install or silently waive the maintained-backend gate. FFXIV joystick mapping, supported Windows versions, compatibility and failure release remain unproven. Never follow old test-signing/Secure Boot workarounds. |
| HidHide | Device visibility helper, not an input backend. Documented app access lists identify executable paths, not a particular PID. | Cannot distinguish two FFXIV processes launched from the same executable. Do not hide a user's physical controller or claim process isolation using this alone. |

A reported InputInjector initialization hang/crash on Windows 11 25H2 exists in Microsoft's issue tracker. Issue #10860 is closed, but inspected comments do not identify a shipped fix; the author subsequently attached a reproduction. This is a compatibility test requirement, not proof that all installations fail.

## Integration gate before a phone-control candidate

1. Prove a legally usable maintained backend and ordinary-user distribution. Prefer a driverless Microsoft prototype if its documented deployment requirements can be met. No automatic installation or consent/security-setting changes.
2. Put native output in the external interactive-user host/worker, never Dalamud. Prove forced worker/host termination cannot leave held controls. Native watchdog remains independent of HTTP and game capture.
3. Wire a separate explicit acquire/frame/release protocol to live session/grant/permission/process checks. A new manual-input opt-in is required; today's rescue-control permission must not silently grant analog movement. Show a local active-control indication and integrate tray disable, revoke, session close, app background, target switch and reconnect release.
4. Enforce exactly one locally observed FFXIV process, including clients outside saved bindings. Validate saved executable/PID/start time/current character/game epoch; unknown/stale observation vetoes input. Stop if a second process appears. Focus alone is not an isolation guarantee.
5. Introduce a separately bounded frame transport. Current 120 requests/minute HTTP budget is for rescue/status and cannot sustain gamepad frames; do not simply remove existing abuse limits.
6. Add Android sticks/buttons only after the backend/transport gates. Use full neutral frames, cancel touches on interruption and release on lifecycle/selection/network loss; never automatically reacquire while a touch is held. Stable private Android signing is still needed for reliable updates.

The consolidated real-device session must verify selected character movement/camera/buttons; physical-controller coexistence; loss of network including silent loss; lock/background; explicit release and local emergency/revoke; game exit/identity change; second-game launch; target change/reconnect; worker/host termination and native backend failure. Measure actual release latency. No production manual controls until this passes. If the backend feasibility experiment cannot meet these constraints, return with a concrete product decision rather than substituting global keyboard input.

## Primary references

- [Microsoft InputInjector creation/capability](https://learn.microsoft.com/en-us/uwp/api/windows.ui.input.preview.injection.inputinjector.trycreate)
- [Microsoft virtual gamepad initialization](https://learn.microsoft.com/en-us/uwp/api/windows.ui.input.preview.injection.inputinjector.initializegamepadinjection)
- [Microsoft issue #10860 and comments](https://github.com/microsoft/microsoft-ui-xaml/issues/10860)
- [ViGEmBus retirement](https://github.com/nefarius/ViGEmBus)
- [VirtualPad](https://docs.nefarius.at/projects/VirtualPad/)
- [Brunner vJoy signed release](https://github.com/BrunnerInnovation/vJoy/releases/tag/v2.2.2.0)
- [Brunner vJoy maintenance statement and MIT license](https://github.com/BrunnerInnovation/vJoy)
- [HidHide API and app access lists](https://docs.nefarius.at/projects/HidHide/API-Documentation/)
