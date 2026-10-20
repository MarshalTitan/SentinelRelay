# Phone gamepad preview

**Current 0.3.0 desktop preview:** the user clarified full desktop mirroring and manual app/login/character-selection control, with the gamepad over the same view after login. Use [Desktop mirror setup](DESKTOP_MIRROR.md) for this candidate. The 0.2.x history below describes the earlier gamepad-only/restricted-launcher step and its remaining work at that time.


This work follows the [Windows 10 native backend pass](GAMEPAD_CHECK_RESULT_2026-10-19.md). It is a supervised candidate, not production acceptance.

## Setup

1. Exit Sentinel Remote from its tray menu and install the 0.2.3 Windows setup over the existing PC app. Saved host/pairing/character settings remain in place.
2. Open Sentinel Remote, connect securely, and click **Prepare gamepad support**. It downloads/verifies the same pinned signed VIIPER component as Gamepad Check. The separately installed official USBip driver remains required; a PC that passed Gamepad Check already has the prerequisite.
3. Install **Sentinel Remote Preview** on Android. This uses a separate application ID so the current status/rescue app and pairing remain installed. Create a new pairing invitation in the PC GUI with rescue controls enabled and pair the preview.
4. Select the preview phone on the PC and click **Allow selected phone gamepad**. This is a separate explicit manual-input opt-in; it clears when the host restarts or **Stop phone gamepad** is pressed.
5. Keep exactly one FFXIV process running, with the selected character logged in, Relay mobile/control enabled, and FFXIV's gamepad mode enabled locally. Close other games. Background/minimized control is allowed in PC 0.2.3; enable **Keep gamepad enabled when client is inactive** in FFXIV's Gamepad Settings. Keep Windows awake and unlocked; the monitor may be off.
6. On Android open **Gamepad**, then **Start gamepad**. Move/Camera sticks and Xbox-labelled buttons send complete states. **Stop gamepad**, leaving the page, app background/lock, loss of connection, expired/replaced session, changed process/character/epoch, second FFXIV process or loss of the unlocked Windows desktop ends control. Reacquire explicitly with all fingers released.

No PowerShell or additional Relay DLL update is required by this integration. Closing the PC window keeps its tray host running; the footer/tray show active phone gamepad control. The tray **Stop phone gamepad** clears manual permissions. **Disable all remote access** still stops every remote capability.

The preview APK uses CI debug signing for supervised testing. Its signing key is not yet stable across rebuilds; do not promise seamless preview updates. The original Android app uses its existing separate identity and is unaffected.

## Startup result

If Start briefly enables and then stops, finish the attempt before opening the PC window. Click **Copy gamepad result** in the PC's Phone gamepad section and paste it into the setup conversation. It includes only app version, Windows version, phase, allowlisted outcome code and accepted-frame count; no credentials, addresses, character IDs or raw backend messages.

PC 0.2.3 allows opening other windows and minimizing FFXIV without ending control. Windows lock, secure desktop, another Windows session or a disconnected session still ends input. Do not press Stop or Allow again before copying a failed attempt, since that records a new local action.

This PC-only update works with Android Preview 0.2.0. Keep the phone app installed and paired. The PC host restart clears manual permission, so click Prepare and Allow once after updating. PC 0.2.2 corrected the session-touch/watchdog lock race. The user has now confirmed actual movement and camera control on Windows 10. Minimized and monitor-off behavior still require the new 0.2.3 field test.

## Transport and release

The authenticated private Serve boundary remains unchanged. A single WebSocket accepts one strict hello and then responds to server-issued challenges at at most 20 states/sec. Each text frame is at most 512 bytes, must match the current positive sequence/random challenge, and must arrive within 250 ms measured on the host's monotonic clock. There is no input push queue or automatic retry. HTTP rescue/status rate limits remain 120 requests/minute.

The host repeats live session/device/grant/binding checks and requires separate local per-phone permission. A recent authenticated game snapshot and exact process lifetime/executable/character/game epoch are required. All FFXIV processes count, including clients outside saved bindings. Observation failures veto input. Window focus is no longer required. The host verifies that the game shares the host's active Windows session and that the ordinary Default desktop is the input desktop. This does not make virtual controllers per-process devices.

The existing independent lease and native worker watchdogs remain active. Failure removes the worker's entire native device. Host process loss closes stdin; worker loss closes its kill-on-close backend job. New acquisition never restores old touches.

## Supervised field gate

Verify movement, camera, face buttons, independent triggers and neutral release; then Stop/page exit/background/lock/network removal and silent network loss, local Stop/revoke/disable, worker/host exit, game restart/character change, second-game launch, minimized/background play, and Windows lock. Separately verify monitor-off play with Windows kept awake. Confirm correct character and physical-controller coexistence, and measure native release latency. Do not test near combat or dangerous edges.

Reported field evidence now includes in-game movement and camera control. Buttons/triggers, native release timing and the complete interruption matrix remain open. The standalone helper pass alone does not establish this integration's FFXIV behavior. Windows Server CI native enumeration remains unresolved and is recorded separately.

## Launcher and window recovery candidate

PC 0.2.3 adds fixed `launcher.open` and `game.restore` protocol actions. These require an authenticated, unrevoked control phone, enabled host authority, fresh session/sequence, an additional local recovery permission, and a three-actions-per-30-seconds device budget which survives session reconnects. The launcher action is independent of a live game, bridge or gamepad lease. Restore additionally requires the phone's saved instance grant and verifies process lifetime/executable/window ownership.

In the PC GUI choose **Choose XIVLauncher**, select the installed **XIVLauncher.exe**, then select a phone and click **Allow selected phone recovery**. The executable path and SHA-256 approval are saved locally. A changed launcher binary requires local reselection. Phone requests cannot specify paths/arguments. Launch uses CreateProcess in the existing ordinary user session with no shell or elevation; it refuses if a game is already running and does not start a second launcher. Restore requests SW_RESTORE for the verified game window; it does not force foreground focus. Lock/disconnected Windows sessions deny both actions.

Android Preview 0.2.1 adds **Open XIVLauncher** above the character controls, available even when no game is running, and **Restore game window** for the selected saved instance. Android Preview 0.2.0 remains compatible for minimized in-game control but has no recovery buttons. Preview CI signing is still ephemeral: updating the preview APK may require uninstalling only Sentinel Remote Preview and pairing it again. The original status/rescue app remains separate.

This is the first bounded recovery step, not a complete phone desktop or login flow. Starting XIVLauncher does not submit passwords/MFA, show its window on the phone, or navigate title/character-selection menus. The gamepad remains bound to an opted-in logged-in character; missing Relay is never treated as proof that the game is in the lobby. Add explicit pre-login process/window authorization and view/controls before claiming end-to-end launcher-to-character recovery. No credentials are stored by Sentinel.

Also requested: **Log out of FFXIV**, separate from terminating the process. It still needs selected-client/identity checks, explicit confirmation and verified menu handling. No forced process kill is presented as logout.
