# Phone gamepad preview

This work follows the [Windows 10 native backend pass](GAMEPAD_CHECK_RESULT_2026-10-19.md). It is a supervised candidate, not production acceptance.

## Setup

1. Exit Sentinel Remote from its tray menu and install the 0.2.0 Windows setup over the existing PC app. Saved host/pairing/character settings remain in place.
2. Open Sentinel Remote, connect securely, and click **Prepare gamepad support**. It downloads/verifies the same pinned signed VIIPER component as Gamepad Check. The separately installed official USBip driver remains required; a PC that passed Gamepad Check already has the prerequisite.
3. Install **Sentinel Remote Preview** on Android. This uses a separate application ID so the current status/rescue app and pairing remain installed. Create a new pairing invitation in the PC GUI with rescue controls enabled and pair the preview.
4. Select the preview phone on the PC and click **Allow selected phone gamepad**. This is a separate explicit manual-input opt-in; it clears when the host restarts or **Stop phone gamepad** is pressed.
5. Keep exactly one FFXIV process running, with the selected character logged in, Relay mobile/control enabled, and FFXIV in the foreground. Close other games. Enable FFXIV's gamepad mode locally.
6. On Android open **Gamepad**, then **Start gamepad**. Move/Camera sticks and Xbox-labelled buttons send complete states. **Stop gamepad**, leaving the page, app background/lock, loss of connection, expired/replaced session, changed process/character/epoch, second FFXIV process or foreground loss ends control. Reacquire explicitly with all fingers released.

No PowerShell or additional Relay DLL update is required by this integration. Closing the PC window keeps its tray host running; the footer/tray show active phone gamepad control. The tray **Stop phone gamepad** clears manual permissions. **Disable all remote access** still stops every remote capability.

The preview APK uses CI debug signing for supervised testing. Its signing key is not yet stable across rebuilds; do not promise seamless preview updates. The original Android app uses its existing separate identity and is unaffected.

## Transport and release

The authenticated private Serve boundary remains unchanged. A single WebSocket accepts one strict hello and then responds to server-issued challenges at at most 20 states/sec. Each text frame is at most 512 bytes, must match the current positive sequence/random challenge, and must arrive within 250 ms measured on the host's monotonic clock. There is no input push queue or automatic retry. HTTP rescue/status rate limits remain 120 requests/minute.

The host repeats live session/device/grant/binding checks and requires separate local per-phone permission. A recent authenticated game snapshot and exact process lifetime/executable/character/game epoch are required. All FFXIV processes count, including clients outside saved bindings. Observation failures veto input. Foreground checking is an additional restriction; it is not a claim that virtual controllers are per-process devices.

The existing independent lease and native worker watchdogs remain active. Failure removes the worker's entire native device. Host process loss closes stdin; worker loss closes its kill-on-close backend job. New acquisition never restores old touches.

## Supervised field gate

Verify movement, camera, face buttons, independent triggers and neutral release; then Stop/page exit/background/lock/network removal and silent network loss, local Stop/revoke/disable, worker/host exit, game restart/character change, second-game launch, and foreground loss. Confirm correct character and physical-controller coexistence, and measure native release latency. Do not test near combat or dangerous edges.

The standalone helper pass does not establish this integration's FFXIV behavior. Windows Server CI native enumeration remains unresolved and is recorded separately.

## Recovery scope requested by the user

A later recovery phase must provide **Open XIVLauncher** for a locally selected, validated executable in the ordinary Windows user session, with no arbitrary phone-supplied path/arguments. Secure authentication/MFA remains an explicit user interaction.

Also requested: **Log out of FFXIV**, separate from terminating the game process. It needs selected-client/identity checks, explicit confirmation and verified game/menu handling. Neither launcher launch nor logout is implemented in this gamepad candidate. Never equate a forced process kill with a successful logout.
