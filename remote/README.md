# Sentinel Remote PC foundation

Status: Milestone A software candidate. The Android app and opt-in local game bridge now provide status, screenshots and existing SRank rescue operations. This optional host remains independent of Discord. Manual input, live video and launcher recovery are later stages. Start with the [single installation and acceptance checklist](docs/MILESTONE_A.md); the development notes below retain the original foundation setup.

- [Architecture and live-source audit](docs/ARCHITECTURE.md)
- [Security and protocol](docs/SECURITY.md)
- [Implementation and acceptance plan](docs/PLAN.md)

## Build and validate

Use .NET SDK 10. No third-party NuGet packages are required by these projects.

```powershell
dotnet run --project remote/SentinelRelay.Remote.Tests -c Release
dotnet run --project tests/SentinelRelay.Core.Tests -c Release
dotnet publish remote/SentinelRelay.RemoteHost -c Release -r win-x64 --self-contained true -o remote-candidate
```

The `Remote foundation` GitHub workflow runs authority, real HTTP, persistence, and existing Relay regression checks on Windows and Linux. Its Windows artifact is a self-contained host candidate, not a Dalamud plugin update. The regular plugin build separately verifies its package. No version bump or catalog update is needed for this foundation.

## PC setup for a developer/proving session

Do not run the host as Administrator or LocalSystem. Run under the Windows user running FFXIV. Phase 1 runs in a console; a tray app and explicit startup registration will follow before unattended use. This console host survives game exit, but does not restart itself after a Windows logout/reboot.

Install/sign into the official Tailscale client on the PC and proving phone. Use **Serve**, restricted to the intended tailnet account, with HTTPS enabled. Do not configure Funnel, LAN listeners, router port forwarding, or a public reverse proxy. Installing/configuring Tailscale and any phone app is a later user/device step; this source change installs nothing on either PC.

Using the published candidate folder:

```powershell
./SentinelRelay.RemoteHost.exe add-instance 'First FFXIV installation'
./SentinelRelay.RemoteHost.exe add-instance 'Second FFXIV installation'
./SentinelRelay.RemoteHost.exe enable
./SentinelRelay.RemoteHost.exe status
./SentinelRelay.RemoteHost.exe serve YOUR-PC.YOUR-TAILNET.ts.net YOUR-TAILSCALE-LOGIN
```

In a separate local console:

```powershell
tailscale serve --bg 32179
tailscale serve status
./SentinelRelay.RemoteHost.exe pair 'My Android phone' view INSTANCE_ID
```

`INSTANCE_ID` is the opaque ID returned by `add-instance`/`status`, not a PID, HWND, character name or window index. Multiple grants use a comma-separated list. Choose `control` locally only if required. The 256-bit invitation appears once locally and expires in two minutes; transfer it directly to the intended app, never to Discord or a support log. QR scanning is planned for the Android client. These rows currently indicate `bridge_unavailable`; a label is not proof that a game process is online.

The host accepts only `127.0.0.1:32179`, the exact configured tailnet Host header, and the exact configured Tailscale login header. Tailscale Serve terminates HTTPS on the same PC and forwards to loopback. Keep the phone's normal certificate validation enabled. The app must use the **HTTPS tailnet URL**, never the loopback HTTP URL or a certificate-bypass mode. This requires a real-device transport check; CI exercises the proxy boundary, not a deployed tailnet. Unrestricted tailnet defaults should be narrowed to the intended devices/users before proving.

For two distinct tailnet accounts, the final `serve` argument accepts a comma-separated list of exact authorized login values, with no spaces. Each phone still needs its own local invitation and device grants.

## Local emergency control and revocation

```powershell
./SentinelRelay.RemoteHost.exe disable
./SentinelRelay.RemoteHost.exe revoke DEVICE_ID
./SentinelRelay.RemoteHost.exe status
```

Disable clears pending invitations and changes the authorization epoch. Every subsequent request rereads that state, including requests on existing connections; reenabling never resurrects old sessions. Stopping the console also closes access. If a local command encounters the very brief exclusive state transaction, it fails closed; retry it or stop the host. No remote endpoint can enable the host, grant permissions, create invitations, or revoke another device. No input can be held in Phase 1 because no input path exists. A visible tray emergency button and input watchdog are required before Phase 3.

## Android and iOS

The Android app now lives in `MarshalTitan/SentinelRemote`; use its matched signed debug proving APK with the Relay 0.6.0.0 candidate and host. A stable private release signing key remains a later packaging task. iOS stays deferred until Android/control/view are proven. See the milestone checklist and distribution plan.

## Reconnect and troubleshooting

- A dropped session needs a new authenticated session and a fresh sequence starting at 1. Do not automatically retry a game mutation after a lost response. Read current status first. Pairing survives host restart; sessions and reset intent do not.
- `403` before JSON: check the HTTPS tailnet URL, exact hostname/login, Serve configuration and Origin. Never fix it by broadening the listener or removing identity checks.
- `401`: invitation expired/used, device revoked, expired/idle session, or incorrect credential. A new invitation requires local PC access.
- `400`: protocol/schema/sequence/confirmation error. Unknown JSON fields and null required values are rejected.
- `429`: request budget exhausted. Back off; do not run a rapid retry loop.
- `503 state_unavailable`: state is locked, unreadable or corrupt. Stop the host and inspect locally; there is no permissive fallback. Preserve the file for local repair; do not post it publicly.
- `503 bridge_unavailable`: the slot is unbound, the character changed, the plugin is unavailable or mobile access is disabled. Check the local binding and Relay opt-in. Commands are never automatically retried.
- Process lifetime, Relay availability, character/world/map, SRank and optional vnavmesh readiness are observed through the bound bridge. Profiles stays unknown. Missing Relay preserves clearly marked last-known state; disappearance is not a crash diagnosis. A registry label alone is never health evidence.

State is stored under the current user's local application data, `Sentinel/Remote`, with owner-only permissions. It contains hashes and local grants, not Discord tokens, Square Enix passwords, OTP values or authenticator secrets. No diagnostic upload runs automatically.
