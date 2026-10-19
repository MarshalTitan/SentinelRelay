# Sentinel Remote - Windows setup candidate

This candidate replaces terminal setup with a per-user Windows installer and a desktop/tray app. Android 0.1.0 keeps its existing pairing protocol. iPhone distribution is a separate stage.

## Install and connect

1. Update Relay to candidate **0.6.0.1**. On each intended character, open Relay's **General** page and click **Enable all mobile features**. Alternatively run `/srelay mobile all on` once. Permissions save per character across restarts. Old configurations remain off until locally enabled. Discord permissions are separate.
2. Download the **SentinelRemote-Windows-Setup-candidate** artifact from the matching Remote foundation run. Extract it, then run **SentinelRemote-0.1.2-Windows-Setup.exe** normally. It installs for the current Windows user and includes the required runtime.
3. If a previous PowerShell host is running, close that host window first. Existing character slots, phone pairings, and grants are reused.
4. Open **Sentinel Remote** from Start. Install/sign into official Tailscale on PC and phone using the intended account. Click **Connect securely**. A browser may open for the initial HTTPS/Serve approval; approve it, then click Connect securely again.
5. Log into FFXIV, click **Refresh games**, select the actual character/world, then **Connect selected character**. The app verifies the process, named pipe owner, and character.
6. Check the approved characters to grant this phone. Enter a phone name, choose whether it may use rescue controls, and click **Show pairing invitation**. In Android's Pair a PC screen, enter the displayed HTTPS address and invitation within two minutes. Keep the invitation private.
7. Optionally enable **Start with Windows**. Closing the window keeps it in the tray. Right-click the tray icon to reopen, disable all remote access, or exit.

## Reconnection and emergency controls

The app remembers approved character bindings. On refresh it reconnects a restarted process only when exactly one opted-in game reports the same character identity from the same executable path. Ambiguous identities, changed paths, different characters, and unavailable/paused plugins are not auto-approved. Existing phone instance grants survive a verified reconnect; old sessions and reset confirmations are invalidated.

**Disable all remote access** disables the authority and clears invitations immediately; it also turns off automatic resume. Discovery cannot reenable it. **Revoke selected phone** revokes one device's credential. Relay's **Disable all mobile features** or `/srelay mobile off` stops access for that character.

Tailscale remains a separate official client with its own sign-in. This app uses private Serve, never Funnel, router forwarding, or a public/LAN listener. It does not overwrite another Serve route or an existing Funnel configuration. The first connection and the Windows-startup checkbox require local interaction. No game login credentials are stored.

## Candidate checks

Automated checks cover identity discovery, private Serve route recognition, refusal of existing routes/Funnel, exact-character reconnection, ambiguity rejection, epoch invalidation, persisted emergency disable, Windows packaging, installer install/uninstall, and form rendering at wide/narrow sizes. Existing Remote and Relay regressions also run.

Before production, verify on a real PC: ordinary-user install, browser consent, game selection, existing pairing reuse, new pairing, game restart reconnect, two clients remaining isolated, Windows startup, tray disable, device revoke, and uninstall. The installer is not code-signed. A Windows signing identity and a stable Android release signing key are future distribution work.

## Reported Milestone A results

The user verified Android pairing and actual character status, selected-game screenshot capture, and Stop + Reset returning the character to Ul'dah. Skip + Keep Queue and the rest of the acceptance matrix remain unverified. Desktop install/reconnection still require field acceptance. These reports do not mark the complete Milestone A matrix as passed.

## Windows startup correction in desktop 0.1.1

Desktop 0.1.0 incorrectly rejected administrator-enabled Windows sessions even when opened normally from Start. Desktop 0.1.1 uses the Windows token elevation type instead of administrator role membership. Standard and no-linked-token sessions run in their normal asInvoker context. An elevated split token relaunches this same executable under the same user's verified linked standard token; no other account is used and relaunch cannot loop. Service accounts remain rejected. No Windows security setting is changed and the app never requests elevation.

Installer checks now exercise the installed executable's startup/token path before rendering, rather than bypassing startup for smoke tests. Relay 0.6.0.1 and Android 0.1.0 are unchanged by this desktop-only fix. Install the new desktop candidate over 0.1.0; saved pairings and bindings are retained.

## SRank status wire correction in desktop 0.1.2

The host previously serialized its C# SRank property as `sRank`, while the shipped Android 0.1.0 app reads `srank`. This caused Android to show S Rank Unknown even with an available SRank plugin. The host now explicitly names that public field `srank`; the inner state, hunt, status text, and queue values are preserved. An unavailable SRank value remains null. Profiles remains unsupported/Unknown.

This is a PC-host-only correction. Install desktop 0.1.2 over the existing desktop app; Relay 0.6.0.1, Android 0.1.0, saved pairings, and character bindings are unchanged. Field testing confirmed desktop 0.1.1 opens normally, retains the existing pairing, connects Android, and keeps running in the tray after closing its window. These reports do not mark the complete acceptance matrix as passed.
