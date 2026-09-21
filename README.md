# Sentinel Relay

Sentinel Relay is a privacy-first **FFXIV → Discord** chat relay for Dalamud. It sends only the chat types a player explicitly enables, directly from the game client to that character's private Discord webhook.

**No hosted backend is required.** There is no Railway service, monthly fee, Docker container, Node process, port forwarding, Discord bot token, pairing code, or Discord-to-FFXIV control path.

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

Wrothy Minioa and Elektra Minoa are not hard-coded. Dalamud identifies the active character by content ID, and each character profile independently retains its protected webhook, filters, formatting, keywords, and pause state.

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
- There is no HTTP listener, WebSocket server, Discord Gateway client, remote command parser, or Discord-to-game feature.

See [SECURITY.md](SECURITY.md) for the complete review.

## Setup

The current `0.1.0.0` release is an explicitly testing-only beta. It is hosted online; testers do not need a compiler, development-plugin folder, backend, or always-on service.

1. Add `https://raw.githubusercontent.com/MarshalTitan/Sentinel/main/repo.json` under **Dalamud Settings → Experimental → Custom Plugin Repositories** and save.
2. Open `/xlplugins`, find **Sentinel Relay** in the available/testing section, and choose **Install**.
3. Create private Discord channels such as `#relay-wrothy` and `#relay-elektra`.
4. Create a separate incoming webhook in each channel by following [DISCORD_SETUP.md](DISCORD_SETUP.md).
5. Log in as the intended character and run `/srelay`.
6. Open **Discord Webhook**, paste that character's webhook URL, select **Save Webhook**, and then **Test Webhook**.
7. Open **Chat Filters** and enable only the desired chat types.
8. Repeat on the other character/client with its own webhook.

The successful test message is:

```text
Sentinel Relay connected successfully for Wrothy Minioa.
```

## Keyword alerts

Keyword matching remains entirely local. Rules support case-insensitive contains matching, optional whole-word matching, and per-rule chat-channel selection.

A match can be highlighted in the relay channel and optionally ping one explicitly configured Discord user ID. Normal relayed text cannot create mentions. Incoming webhooks cannot send Discord DMs, so DM alerts are not part of this no-backend design.

## Commands

| Command | Action |
|---|---|
| `/srelay` | Open or close the settings window |
| `/srelay status` | Show character, webhook/configuration state, enabled chats, and queue length |
| `/srelay pause` | Immediately stop new FFXIV → Discord delivery and clear the pending queue |
| `/srelay resume` | Resume delivery for enabled chats |
| `/srelay debug` | Show non-secret queue and delivery diagnostics |

There are no Discord slash or prefix commands. A shared Discord Gateway bot inside two simultaneous game clients would require distributing a bot token and could process commands twice. The existing Sentinel Relay bot may remain in the server, but this plugin does not require or connect to it.

## Building

Sentinel Relay targets Dalamud API 15 and .NET 10.

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
- [Wrothy/Elektra live-test checklist](LIVE_TEST_CHECKLIST.md)

Sentinel Relay is intended for the private/custom `MarshalTitan/Sentinel` Dalamud catalog. It is not affiliated with or endorsed by Square Enix, Discord, Dalamud, or XIVLauncher.
