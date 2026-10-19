# Milestone A — one supervised proving session

For the newer **Windows installer and setup window** (Relay 0.6.0.1 candidate), see [Desktop setup](DESKTOP_SETUP.md). The instructions below document the original 0.6.0.0 console candidate and remain the full game/phone acceptance checklist.

Candidate pairing: Sentinel Remote Android 0.1.0 and Sentinel Relay **0.6.0.0 candidate**, plus the host from the same Relay source commit. These are candidates until the checklist below passes. The published Discord plugin remains independent; do not replace the production catalog with an unverified package.

## Validated downloads

Open each successful build page and download the named artifact at the bottom under **Artifacts**. Sign into the GitHub account if downloading requires it.

| Install on | Build page | Artifact |
|---|---|---|
| Android phone | [Android APK](https://github.com/MarshalTitan/SentinelRemote/actions/runs/39194150121) | `SentinelRemote-Android-proving` |
| FFXIV PC | [Relay plugin candidate](https://github.com/MarshalTitan/SentinelRelay/actions/runs/39194267538) | `SentinelRelay-0.6.0.0` |
| FFXIV PC | [Standalone PC host](https://github.com/MarshalTitan/SentinelRelay/actions/runs/39194267510) | `SentinelRemoteHost-foundation-candidate` |

Verified source: Android `84a496be101f4b999a1414f41a50977622b3693f`; Relay/host `8a5445bcfd4c6c56b64751232a6fc7dddf37fe79`. These exact heads passed Android analysis, seven app tests and APK packaging; Windows/Linux 80 remote/bridge checks and 57 Relay regressions; and normal plugin/release-policy validation. Android PR #1 and Relay PR #14 are merged. These checks do not establish real-device acceptance. The production Relay release remains 0.5.0.7.

## Downloads and installation

1. From SentinelRemote's successful **Android proving build**, download `SentinelRemote-Android-proving`, extract it and install `SentinelRemote-0.1.0-proving.apk` on Android. This is a signed debug APK for private proving. Allow installation from the chosen file manager/browser when Android requests it. Later CI debug builds may require uninstall/reinstall and fresh pairing because the signing key is temporary.
2. From the matching SentinelRelay **Build** run, download `SentinelRelay-0.6.0.0`. Extract the candidate to a separate folder. In Dalamud's developer plugin settings, point to the extracted `SentinelRelay.dll`. Disable the regular Relay instance before enabling the candidate so only one Relay is loaded. Existing per-character configuration is preserved; all new mobile permissions start off. Restore the regular plugin after proving if needed; do not delete its configuration.
3. From the same commit's **Remote foundation** run, download `SentinelRemoteHost-foundation-candidate` and extract it. Run the host as the same ordinary Windows user running FFXIV, with no elevation. Keep the candidate host in its own folder.
4. Install/sign in to the official Tailscale client on each intended PC and Android phone. Enable HTTPS and restrict tailnet access to the intended accounts/devices. This is private Serve access; do not configure Funnel or router port forwarding.

## Configure each game client locally

Log into the intended character using the candidate Relay. Run:

```text
/srelay mobile on
/srelay mobile control on
/srelay mobile screenshots on
```

These are independent per-character opt-ins; view alone needs only the first. Note the **game PID** printed by the command. Changing any mobile permission retires the previous game epoch. `/srelay mobile off` disables all three. Relay's existing pause also stops the bridge.

In PowerShell opened in the extracted host folder:

```powershell
./SentinelRelay.RemoteHost.exe add-instance 'Wrothy Minioa'
./SentinelRelay.RemoteHost.exe status
./SentinelRelay.RemoteHost.exe bind INSTANCE_ID GAME_PID
./SentinelRelay.RemoteHost.exe enable
./SentinelRelay.RemoteHost.exe serve YOUR-PC.YOUR-TAILNET.ts.net YOUR-TAILSCALE-LOGIN
```

Use the actual instance ID returned by `add-instance` and the actual game PID printed in FFXIV. Labels are configurable; the binder verifies the process executable, process start time, kernel pipe owner and current character. A slot cannot be bound to the same live process as another slot. It never chooses the first FFXIV window.

In a second local console:

```powershell
tailscale serve --bg 32179
tailscale serve status
./SentinelRelay.RemoteHost.exe pair 'My Android phone' control INSTANCE_ID
```

Enter the shown **HTTPS tailnet URL**, a PC label and the one-time invitation in Android's **Pair a PC** dialog. The invitation expires in two minutes. Never paste it into Discord, chat logs or GitHub. A second phone receives a separate invitation. For two accounts, the final host `serve` argument accepts exact comma-separated tailnet login names without spaces.

For a second game on the same PC, register and bind a second slot, then grant the phone both instance IDs separated by a comma. For a second PC, repeat PC setup there and add that PC in Android. A phone can retain both PC pairings. Rebinding any slot invalidates host sessions; a refresh safely creates a new session.

## Consolidated checks

| Check | Expected |
|---|---|
| Pair and choose each PC/client | Correct actual character/world/map; separate clients never exchange status or actions. |
| Status | FFXIV process and Relay availability, SRank state, heartbeat, territory/map and read-only vnavmesh readiness where supported. Profiles remains Unknown. Missing IPC is Unknown rather than ready. |
| Screenshot | Only the selected FFXIV client area; a local capture notice; no desktop fallback. Minimized/blank captures fail safely. Requests are spaced 15 seconds apart. |
| Skip | Only the selected character uses the already supported Skip Current + Keep Queue operation. Observe queue preservation in game. |
| Reset | First request asks for confirmation. Cancel does nothing; confirm within 30 seconds invokes existing Stop + Reset Through Ul'dah once. Confirm after expiry/reconnect/permission change fails. |
| Wrong-client safety | Switch selected clients before a fresh operation; verify only that character responds. Log out/switch a game character: the old binding must stop working. |
| Phone interruption | Background/lock the phone or interrupt its network. Old sessions/queued requests are dropped; returning refreshes status and never repeats a skip/reset automatically. |
| Local permission changes | `mobile control off` prevents skip/reset. `mobile screenshots off` prevents capture. `mobile off` or Relay pause makes the bridge unavailable. |
| Host emergency disable | Run `SentinelRelay.RemoteHost.exe disable`; existing phone sessions stop. Reenable and refresh; old session tokens cannot resume. |
| Revoke | Find the device ID with local `status`, then `revoke DEVICE_ID`; refresh must be denied. A new pairing requires a fresh local invitation. |
| Process loss | Close FFXIV normally once. Host remains alive and shows process exited plus last-known state. It must not claim a crash cause or launch/reconnect. |

Stop if any request acts on the wrong character or captures another window. Do not test account/character deletion or other destructive operations. Record candidate versions, which topology was tested (separate PCs/same-PC clients), check results and sanitized error codes only. No secrets, raw authority state or login information in reports.

## Current limitations

This build is status and bounded rescue commands, not manual game control or unattended login recovery. There are no sticks/buttons, live video, launcher actions, Profiles IPC, incident uploads, automatic host startup or tray emergency button. Keep the host console open. The CLI provides local emergency disable; a visible tray/watchdog stage is required before real input is added.

After a game process restarts, its PID/start-time binding is deliberately stale. Log into the intended character and run local `bind INSTANCE_ID NEW_GAME_PID` again. Automatic verified rebinding/launcher recovery belongs to a later stage. If a command reply is lost, its outcome can be uncertain: inspect current status in game before issuing another mutation; never assume failure means it did not execute.
