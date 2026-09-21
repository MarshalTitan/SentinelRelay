# Sentinel Relay

Sentinel Relay is a privacy-first **FFXIV → Discord** chat relay for Dalamud. It sends only the chat types a player explicitly enables, directly from the game client to that character's private Discord webhook.

Sentinel Relay sends directly to Discord and does not require a separately hosted relay service.

The `0.2.0.0` development line also contains an **optional experimental** reply reader. While FFXIV is running, it polls one configured private Discord channel and recognizes only an authorized ordinary text message beginning with `/fc `. It does not register a Discord slash command and does not change the webhook relay's independent operation.

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

When experimental replies are enabled, the same profile also owns its protected bot credential, exact Discord channel ID, exact authorized Discord user ID, persistent message checkpoint, and outbound allowlist. Those values never route across character profiles.

## Supported chat types

- Say, Yell, Shout, Free Company
- Party and Cross-world Party
- Alliance and PvP Team
- Linkshell 1–8 and Cross-world Linkshell 1–8
- Incoming Tell and Outgoing Tell as separate choices
- Novice Network
- Standard Emotes and Custom Emotes

Incoming chat comes from Dalamud's structured `IChatGui.ChatMessage` event and `XivChatType`; Sentinel Relay does not scrape the visible chat window. `SeString` content is converted into safe, readable text.

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
- Experimental replies are off by default and understand only the explicitly implemented `/fc` mapping.
- Bot-authored, webhook-authored, wrong-channel, wrong-user, stale, duplicate, and cross-character messages are rejected locally.

See [SECURITY.md](SECURITY.md) for the complete review.

## Setup

The stable `0.1.0.1` one-way release remains available from the live Sentinel catalog. The experimental reply build is distributed separately until an actual second FFXIV client confirms that `/fc` produces server-visible Free Company chat. Neither build requires a hosted relay service.

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
| `/srelay pause` | Stop webhook delivery and experimental replies, and clear both pending queues |
| `/srelay resume` | Resume enabled webhook delivery and restart enabled reply polling at a fresh checkpoint |
| `/srelay debug` | Show non-secret queue and delivery diagnostics |

There are no registered Discord application commands. In the experimental build, `/fc hello` is an ordinary text message read through Discord's authenticated REST API. Each client polls only its own configured channel, and all routing and author checks must pass before the fixed Free Company mapping can run. The stable FFXIV → Discord webhook relay does not depend on the bot reader.

## Experimental `/fc` replies

Do not enable this until the channel-specific bot permissions and IDs are configured as described in [DISCORD_SETUP.md](DISCORD_SETUP.md).

1. Run `/srelay` and open **Experimental Replies**.
2. Paste the bot token, Relay Channel ID, and one Authorized Discord User ID.
3. Check **Allow /fc (Free Company) replies**.
4. Check **Enable Discord → FFXIV Replies**, save, and run **Test Discord Reader**.
5. Post the ordinary Discord message `/fc hi` in that exact channel from that exact user.
6. Verify on a second FFXIV client that the active character really sent `hi` in Free Company chat.

Starting, resuming, reconnecting, or switching characters first advances to Discord's newest current message. Commands written while the game/reader was offline are never executed later. The processing checkpoint is persisted before a game send is queued, favoring a dropped command over an accidental replay.

## Building

Sentinel Relay targets Dalamud API 15 and .NET 10. The outbound experiment uses the current FFXIVClientStructs chat-shell interface and therefore still requires an in-game proof on every relevant game/API update.

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
