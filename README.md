# Sentinel Relay

Sentinel Relay is a privacy-first, two-way bridge between selected FFXIV chat channels and one private Discord channel per character. It consists of a Dalamud plugin plus a small self-hosted Discord bot/WebSocket service.

The plugin filters chat locally. Every channel starts disabled, and disabled chat never leaves the game client. A linked Discord user can send only text to an explicit channel whitelist—Discord is never given arbitrary FFXIV command execution.

> **Important:** Enabling a chat type transmits that chat's sender and text to your relay service and Discord. Everyone represented in that chat may not expect off-platform forwarding. Enable only the channels you need and protect each relay channel.

## Current status

Version `0.1.0.0` targets Dalamud API 15 and .NET 10. Automated core, protocol, database, sanitizer, matching, replay, and rate-limit tests pass. A live FFXIV/Dalamud and Discord test is still required before treating the release as production-ready; use [LIVE_TEST_CHECKLIST.md](LIVE_TEST_CHECKLIST.md).

## Supported channels

| Channel | FFXIV → Discord | Discord → FFXIV |
|---|:---:|:---:|
| Say, Yell, Shout | Yes | Yes |
| Free Company, Party, Alliance, PvP Team | Yes | Yes |
| LS 1–8, CWLS 1–8 | Yes | Yes |
| Tell | Yes, off by default | No (V1 safety deferral) |
| Novice Network | Yes, off by default | No |
| Standard and custom emotes | Yes, off by default | No |

Incoming chat uses Dalamud's structured `IChatGui.ChatMessage` event and `XivChatType`, not chat-window scraping. FFXIV `SeString` content is converted to readable plain text before transmission.

## Safety model

- All inbound filters, including Free Company, are off by default.
- Profiles, filters, keywords, link credentials, and pause state are per FFXIV character.
- Pairing codes are cryptographically random, one-use, expire after ten minutes, and work only while the generating client remains connected.
- The long-lived client token is stored as a hash on the server and protected with Windows DPAPI in Dalamud configuration.
- Only the linked Discord user can operate a character, and outbound commands must be used in that character's configured relay channel.
- Outbound packets contain a channel enum and text. The plugin maps that enum to its own fixed allowlist; no raw Discord command reaches FFXIV.
- Both service and client enforce length, freshness, replay, rate, and queue limits.
- A Discord send is reported successful only after the plugin observes the real matching FFXIV chat echo from the active character.
- Pausing immediately blocks both directions while leaving status connectivity available.
- The server stores links/routing metadata but no chat history. Message bodies are not logged by default.

See [SECURITY.md](SECURITY.md) for the complete review and limitations.

## Requirements

- Windows, FFXIV, XIVLauncher/Dalamud, and the Sentinel custom repository
- A Discord server you control
- A Discord application/bot created for Sentinel Relay
- An HTTPS host with WebSocket support and a small persistent volume

The recommended two-user deployment is Railway with the included Dockerfile. No inbound port or router forwarding is required on either FFXIV PC.

## Install and configure

1. Follow [DISCORD_SETUP.md](DISCORD_SETUP.md) to create and invite the Discord bot.
2. Follow [BACKEND_DEPLOYMENT.md](BACKEND_DEPLOYMENT.md) to deploy the service and attach `/data` as persistent storage.
3. Add the Sentinel custom repository URL in Dalamud and install **Sentinel Relay** once version `0.1.0.0` is published to the catalog.
4. In FFXIV, run `/srelay`, open **Advanced**, enter `wss://YOUR-HOST/v1/plugin`, then select **Save and Reconnect**.
5. Enable only the desired channels in **Chat Filters**.
6. Run `/srelay link`; in Discord run `/relay link code:CODE`.
7. In the intended private Discord text channel, run `/relay channel channel:#that-channel`.
8. Repeat from the other character/client for a fully isolated second link.

## Commands

### Dalamud

| Command | Action |
|---|---|
| `/srelay` | Open or close the settings window |
| `/srelay status` | Print current character, service, pause, link, and destination state locally |
| `/srelay link` | Request a temporary link code |
| `/srelay pause` | Stop both relay directions immediately |
| `/srelay resume` | Resume both directions |
| `/srelay unlink` | Revoke the current character's link |
| `/srelay debug` | Print non-secret diagnostic state locally |

### Discord

`/relay link`, `/relay channel`, `/relay status`, and `/relay unlink` manage the link. The following commands send text through the active linked FFXIV character:

`/fc`, `/say`, `/yell`, `/shout`, `/party`, `/alliance`, `/pvpteam`, `/ls channel:1..8`, and `/cwls channel:1..8`.

Each command uses Discord's `message` option and is ephemeral until the game confirms delivery. Tell is intentionally unavailable in V1.

## Keyword alerts

Open `/srelay` → **Keywords**. Add a case-insensitive contains or whole-word rule, choose the channels for that rule, then choose Discord DM, relay-channel mention, or both. **Use My Character Name** offers the character's first name as a starting keyword. Multiple matches in one FFXIV message are bundled into one alert; duplicate DM events are suppressed.

## Building

The plugin follows the current Sentinel convention: `Dalamud.NET.Sdk/15.0.0`, .NET 10, a four-part plugin version, and `latest.zip` as the install package.

```powershell
$env:DALAMUD_HOME = "C:\path\to\Dalamud\dev"
dotnet restore SentinelRelay.csproj
dotnet run --project tests/SentinelRelay.Core.Tests/SentinelRelay.Core.Tests.csproj -c Release
dotnet build SentinelRelay.csproj -c Release --no-restore
```

Service:

```bash
cd service
npm ci
npm run check
npm test
npm run build
```

The plugin package is written to `bin/Release/SentinelRelay/latest.zip`. Release automation and package validation live under `.github`.

## Documentation

- [Architecture](ARCHITECTURE.md)
- [Discord setup](DISCORD_SETUP.md)
- [Backend deployment](BACKEND_DEPLOYMENT.md)
- [Security and privacy](SECURITY.md)
- [Troubleshooting](TROUBLESHOOTING.md)
- [Wrothy/Elektra live-test checklist](LIVE_TEST_CHECKLIST.md)

## Distribution note

Sentinel Relay is designed for the private/custom `MarshalTitan/Sentinel` catalog. Its remotely initiated outbound chat behavior is unlikely to satisfy the official Dalamud repository rule against plugins interacting with game servers automatically. It should not be submitted to the official repository without prior approval and a fresh policy review. This does not change the custom-repository feature set.

Sentinel Relay is not affiliated with or endorsed by Square Enix, Discord, Dalamud, or XIVLauncher. Use third-party tools in accordance with the terms and risk tolerance applicable to you.
