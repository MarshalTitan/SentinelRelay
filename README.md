# Sentinel Relay

Sentinel Relay is a privacy-first **FFXIV ↔ Discord** chat relay for Dalamud. It sends only the chat types a player explicitly enables to that character's private Discord webhook. Its optional reply reader accepts only individually allowed chat destinations from one authorized Discord user.

Sentinel Relay sends directly to Discord and does not require a separately hosted relay service.

The `0.4.0.0` revision adds an opt-in experimental remote **FFXIV-window screenshot** control while preserving the direct webhook relay, fixed chat-reply allowlist, and inbound-only Rewards / Hunt Results feed.

> Enabling a chat type causes its sender and text to leave the local PC and be delivered to Discord. Other people represented in that chat may not expect off-platform forwarding. Every filter starts off; enable only what you need and keep relay channels private.

## How it works

```text
Dalamud structured chat event
  → local per-character filter
  → safe text conversion and mention neutralization
  → bounded background queue
  → character's Discord webhook
  → private relay channel
```

Dalamud identifies the active character by content ID. Each character profile independently retains its protected webhook, filters, formatting, keywords, and pause state.

When Discord replies are enabled, the same profile also owns its protected bot credential, exact Discord channel ID, exact authorized Discord user ID, persistent message checkpoint, and outbound allowlist. Those values never route across character profiles.

## Supported chat types

- Say, Yell, Shout, Free Company
- Party and Cross-world Party
- Alliance and PvP Team
- Linkshell 1–8 and Cross-world Linkshell 1–8
- Incoming Tell and Outgoing Tell as separate choices
- Novice Network
- Standard Emotes and Custom Emotes
- Rewards / Hunt Results (inbound-only, default off)

Incoming chat comes from Dalamud's structured `IChatGui.ChatMessage` event and `XivChatType`; Sentinel Relay does not scrape the visible chat window. The reward diagnostic also observes the API 15 `IChatGui.LogMessage` event's numeric IDs without formatting or forwarding arbitrary system messages. `SeString` content is converted into safe, readable text.

The reward filter currently recognizes the English client lines for hunt contribution, `You obtain ...`, and `You cannot carry any more ...` only when they originate from the narrow API 15 system candidates `SystemMessage`, `SystemError`, `ErrorMessage`, `LootNotice`, or `Progress`. This prevents player chat containing similar text from bypassing its own disabled filter. Consecutive lines are kept in order and grouped after a short quiet window.

## Safety and privacy

- All chat filters—including Free Company—default off.
- Disabled chat is discarded locally before any network request.
- Discord webhook URLs are stored per character and protected with Windows DPAPI.
- The UI never reveals a saved URL, and status/debug output never prints it.
- Only HTTPS Discord webhook URLs are accepted.
- Networking runs on a background worker; the FFXIV framework/chat thread never waits on Discord.
- The ordered queue is capped at 100 deliveries and discards messages older than two minutes.
- Discord `429` rate limits and transient server failures are retried in order with bounded delays.
- Arbitrary FFXIV text cannot ping `@everyone`, `@here`, users, roles, or channels.
- There is no HTTP listener, WebSocket server, Discord Gateway client, or generic remote command executor.
- Discord replies are off by default. Every outbound destination has a separate opt-in permission and fixed FFXIV chat mapping.
- Rewards / Hunt Results is never an outbound destination and has no Discord reply command.
- `/r` is accepted only for 30 minutes after the active character receives a Tell during the current session; Sentinel Relay never accepts an arbitrary `/tell` target.
- Bot-authored, webhook-authored, wrong-channel, wrong-user, stale, duplicate, and cross-character messages are rejected locally.
- Remote screenshots are off by default, accept only the exact `/screenshot` control, capture only the current FFXIV process's client area, and have a 15-second cooldown.
- Screenshot pixels are downscaled and PNG-encoded in memory; Sentinel Relay does not create a persistent screenshot file or capture the whole desktop.

See [SECURITY.md](SECURITY.md) for the complete review.

## Setup

Version `0.4.0.0` is distributed through the live Sentinel catalog. Dalamud downloads and updates the plugin; there is no ZIP to extract, standalone program to launch, or hosted relay service to operate. The optional reply/control reader remains off until configured per character.

1. Add `https://raw.githubusercontent.com/MarshalTitan/Sentinel/main/repo.json` under **Dalamud Settings → Experimental → Custom Plugin Repositories** and save.
2. Open `/xlplugins`, find **Sentinel Relay** under available plugins, and choose **Install**.
3. Create one private Discord relay channel per FFXIV character.
4. Create a separate incoming webhook in each channel by following [DISCORD_SETUP.md](DISCORD_SETUP.md).
5. Log in as the intended character and run `/srelay`.
6. Open **Discord Webhook**, paste that character's webhook URL, select **Save Webhook**, and then **Test Webhook**.
7. Open **Chat Filters** and enable only the desired chat types.
8. Repeat on the other character/client with its own webhook.

The successful test message is:

```text
Sentinel Relay connected successfully for Example Character.
```

## Keyword alerts

Keyword matching remains entirely local. Rules support case-insensitive contains matching, optional whole-word matching, and per-rule chat-channel selection.

A match can be highlighted in the relay channel and optionally ping one explicitly configured Discord user ID. Normal relayed text cannot create mentions. Incoming webhooks cannot send Discord DMs, so DM alerts are not part of this no-backend design.

## Commands

| Command | Action |
|---|---|
| `/srelay` | Open or close the settings window |
| `/srelay status` | Show character, webhook/configuration state, enabled chats, and queue length |
| `/srelay pause` | Stop webhook delivery and Discord replies, and clear both pending queues |
| `/srelay resume` | Resume enabled webhook delivery and restart enabled reply polling at a fresh checkpoint |
| `/srelay debug` | Show non-secret queue and delivery diagnostics |

There are no registered Discord application commands. `/fc hello`, `/party hello`, `/screenshot`, and the other supported prefixes are ordinary text messages read through Discord's authenticated REST API. Each client polls only its configured channel, and all routing, author, freshness, and feature-permission checks must pass. The FFXIV → Discord webhook relay does not depend on the bot reader.

## Discord replies

Do not enable this until the channel-specific bot permissions and IDs are configured as described in [DISCORD_SETUP.md](DISCORD_SETUP.md).

1. Run `/srelay` and open **Discord Replies**.
2. Paste the bot token, Relay Channel ID, and one Authorized Discord User ID.
3. Enable only the desired reply destinations. These are deliberately separate from the inbound **Chat Filters**.
4. Check **Enable Discord → FFXIV Replies**, save, and run **Test Discord Reader**.
5. Post the ordinary Discord message `/fc hi` in that exact channel from that exact user.
6. Verify on a second FFXIV client that the active character really sent `hi` in Free Company chat.

Supported ordinary-message prefixes:

- `/say` or `/s`, `/yell` or `/y`, `/shout` or `/sh`, and `/fc`
- `/party` or `/p`, `/alliance` or `/a`, `/pvpteam`, and `/novice` or `/n`
- `/ls1` through `/ls8`
- `/cwls1` through `/cwls8`
- `/r` to reply to the latest incoming Tell seen during the current session

`/party` uses the game's Party channel and therefore also reaches a cross-world party when that is the active party type. Tell targeting is intentionally limited to `/r`; arbitrary `/tell name message` input is not accepted.

Starting, resuming, reconnecting, or switching characters first advances to Discord's newest current message. Commands written while the game/reader was offline are never executed later. The processing checkpoint is persisted before a game send is queued, favoring a dropped command over an accidental replay.

## Experimental remote screenshot

`/screenshot` is a Sentinel Relay control command, not an FFXIV chat command. In `/srelay` → **Discord Replies**, enable **Allow authorized /screenshot window capture**, keep the master reader enabled, and save. A fresh ordinary Discord message containing exactly `/screenshot` then:

1. passes the same active-character, channel, user, bot/webhook, freshness, and replay checks as a chat reply;
2. selects only the current FFXIV process's verified game window;
3. captures the game client area into memory on a background task;
4. resizes it to at most 1280×720 and encodes an in-memory PNG; and
5. uploads it through that character's configured webhook as `【SCREENSHOT】 Character Name`.

The command never enters `DiscordReplyCommandParser` or `GameChatSender`, cannot select another process or filesystem path, and cannot capture the desktop. A local FFXIV notice appears whenever a request is accepted. Requests have a 15-second cooldown. Minimized windows are deliberately rejected because Windows does not provide a current capturable window frame in that state; restore the game and try again. Background and borderless/windowed behavior require live verification on each PC and graphics-driver configuration.

## Building

Sentinel Relay targets Dalamud API 15 and .NET 10. Outbound chat uses the current FFXIVClientStructs chat-shell interface and therefore still requires an in-game proof on every relevant game/API update.

```powershell
$env:DALAMUD_HOME = "C:\path\to\Dalamud\dev"
dotnet restore SentinelRelay.csproj --locked-mode
dotnet run --project tests/SentinelRelay.Core.Tests/SentinelRelay.Core.Tests.csproj -c Release
dotnet build SentinelRelay.csproj -c Release --no-restore
```

The installable package is written to `bin/Release/SentinelRelay/latest.zip`. GitHub Actions builds, tests, validates, and uploads the same package.

## Documentation

- [Discord webhook setup](DISCORD_SETUP.md)
- [No-backend deployment explanation](BACKEND_DEPLOYMENT.md)
- [Architecture](ARCHITECTURE.md)
- [Security and privacy](SECURITY.md)
- [Troubleshooting](TROUBLESHOOTING.md)
- [Live-test checklist](LIVE_TEST_CHECKLIST.md)

Sentinel Relay is intended for the private/custom `MarshalTitan/Sentinel` Dalamud catalog. It is not affiliated with or endorsed by Square Enix, Discord, Dalamud, or XIVLauncher.
