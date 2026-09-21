# Security and privacy review

Review date: 2026-09-21  
Target release: `0.1.0.0`

## Security guarantees in V1

### Bot token

- Exists only as the hosted `DISCORD_TOKEN` environment secret.
- Is absent from the DLL, repository, package, local Dalamud configuration, Docker image, and `.env.example`.
- Logger redaction includes token-like and message fields.
- No Discord OAuth client secret is required.

If the token is exposed, reset it in Discord Developer Portal, replace the hosted variable, and restart immediately.

### Client authentication

- Character name/world alone never authenticates a client.
- Pairing codes use 40 bits of cryptographically random entropy, omit ambiguous characters, expire after ten minutes, are one-use, and are bound to the live generating socket.
- Pair requests are limited to three per installation per ten minutes.
- Successful pairing issues a random 256-bit client token.
- The server stores only a SHA-256 token hash and compares it in constant time.
- The plugin protects the plaintext token with Windows DPAPI for the current Windows user before saving Dalamud configuration.
- A credential is bound to installation ID and the detected character key. Switching characters cannot silently continue under another identity.
- `/srelay unlink` or `/relay unlink` deletes the server link and revokes the live session.

### Transport security

- Production service accepts WebSocket upgrades only when the direct socket is TLS or the trusted hosting proxy reports HTTPS.
- The plugin requires `wss://` except for loopback development.
- Standard Windows/.NET certificate validation protects the client connection.
- The client always connects outward; no home port forwarding or public PC listener exists.

### Outbound authorization and arbitrary-command prevention

- Only the Discord user ID in the link record may request a send.
- Slash commands must be issued in the exact linked guild and configured character relay channel.
- The service emits a typed `chatType` plus sanitized `message`, never a raw FFXIV command.
- Service and plugin independently enforce the same enum allowlist.
- The game client constructs the prefix from plugin code; Discord cannot select `/logout`, `/teleport`, `/target`, `/xl...`, macros, plugin commands, or any unknown slash command.
- Newlines/control characters are collapsed, so the text cannot inject a second command.
- Tell has no V1 outbound implementation.
- Success is withheld until a matching outgoing structured chat event is observed from the active character.

### Replay, spam, and queue controls

- Unique 128-bit event IDs and five-minute replay caches reject duplicate packets.
- Backend outbound limit: five messages per linked Discord user per 30 seconds.
- Plugin outbound limit: five messages per 30 seconds plus at least 1.5 seconds between sends.
- Discord message size: at most 180 Unicode characters and 400 UTF-8 bytes.
- Service packet expiry: 20 seconds. Client queues discard stale work.
- Local outbound queue is capped at five; general WebSocket queue is capped at 64 short-lived events.
- Server frames are capped at 64 KiB.
- Discord waits for an acknowledgement; disconnects and timeouts produce a visible error.

### Loop prevention

For each Discord-originated send, the plugin retains the event ID, channel, normalized message signature, active-character identity, and timestamp. The first matching outgoing FFXIV chat echo confirms the send and is suppressed from normal FFXIV → Discord forwarding. The bot then posts exactly one outbound echo. Replay caches and deadlines provide secondary protection.

### Routing isolation

- Database primary key: installation ID.
- Database uniqueness: Discord user ID.
- One live WebSocket per installation; a reconnect replaces the earlier socket.
- Delivery always begins from the authenticated session's own link record.
- Discord commands verify linked user, guild, and channel.
- Wrothy/Elektra names are never used as authorization data and are not hard-coded.

### Chat privacy and retention

- Every incoming channel is disabled by default, including Free Company.
- Filtering and keyword matching happen on the PC before transmission.
- Disabled channels are not sent to the backend for filtering.
- The UI names the privacy consequence where filters are enabled.
- The backend forwards and discards messages; it has no chat-message table.
- Keyword text/rules stay in local Dalamud configuration.
- Normal logs do not include chat bodies. Platform-level body/frame logging should remain disabled.

### Discord permission scope

The bot requests only the standard `Guilds` intent. No privileged Presence, Server Members, or Message Content intent is enabled. Install scopes are `bot` and `applications.commands`; bot permissions are View Channels, Send Messages, and Embed Links. Administrator and moderation permissions are unnecessary.

## Sanitization

- Incoming FFXIV `SeString` values are reduced to readable `TextValue`; control and noncharacter code points are removed, whitespace is normalized, and text is capped before protocol transmission.
- Player payloads are used only to improve sender/world labels.
- Discord outbound text is Unicode NFC-normalized, control characters/newlines are collapsed, and character/byte limits are checked on both ends.
- Discord embeds use Discord's structured API. User text is a description value, not executable Markdown or an allowed mention; channel pings use an explicit linked-user allowlist only.

## Data inventory

| Location | Stored data | Not stored |
|---|---|---|
| Dalamud config | Service URL; per-character ID/name/world, filters, keywords, pause; DPAPI token blob | Bot token; plaintext client token; chat history |
| SQLite | Client token hash, installation/character key, Discord user/guild/channel routing, timestamps, pause | Chat bodies, keyword rules, bot token, plaintext client token |
| Process memory | Live sessions, pending pairing codes, short replay/rate caches, in-flight message bodies | Durable chat history |
| Discord | Relay embeds and keyword DMs sent to Discord | Controlled by Discord retention, not this service |

Discord itself retains delivered messages according to Discord/server/user behavior. Deleting backend data does not delete already delivered Discord messages.

## Known limitations and accepted risks

1. **FFXIV client internals:** Real outbound chat uses an unsafe `FFXIVClientStructs` game function because Dalamud has no stable high-level network-visible send service. A game patch can break or change it. Complete manual server-visible testing is mandatory on every affected update.
2. **Third-party tool risk:** Use of Dalamud and remote outbound game chat may violate Square Enix rules or user expectations. Users accept that risk. This release is intended for a private custom catalog.
3. **Official Dalamud repository eligibility:** Remotely initiated sends likely conflict with the official rule against plugins interacting with game servers automatically. Do not submit without advance approval/current policy review.
4. **Host trust:** The host sees enabled plaintext chat transiently to deliver it. TLS protects transit but does not make the server end-to-end encrypted from the host.
5. **Compromised PC/account:** Malware under the same Windows user could access the running plugin or invoke DPAPI; a stolen linked Discord account can send within the whitelist. Use MFA, private channels, and `/srelay pause`/unlink during an incident.
6. **Channel-membership visibility:** Discord channel permission does not grant send-through authority, but it does grant visibility into relayed chat. Configure private overwrites carefully.
7. **Single-process V1:** SQLite and in-memory session routing are not horizontally scalable. Run exactly one backend replica.
8. **No server-side keyword backup:** Keyword rules are deliberately local and must be recreated if Dalamud configuration is lost.
9. **No Tell outbound:** Tell requires target/world semantics and additional privacy/abuse controls; it is intentionally deferred.
10. **No audit-content history:** Privacy-first no-content logging limits forensic reconstruction. Metadata and Discord's own channel history remain available.

## Operational checklist

- [ ] Discord bot token is only a hosted secret and absent from git history.
- [ ] Production URL is `wss://` with a valid certificate.
- [ ] SQLite volume is private, persistent, and backed up before upgrades.
- [ ] Bot has no privileged intents or Administrator permission.
- [ ] Wrothy and Elektra use distinct Discord accounts, character profiles, and relay channels.
- [ ] `ALLOWED_INSTALLATION_IDS` contains only the two enrolled installation IDs after setup.
- [ ] All unneeded FFXIV filters remain off.
- [ ] Cross-user and wrong-channel tests fail visibly.
- [ ] `/srelay pause` blocks both directions.
- [ ] Live external observers confirm `/fc` and `/say` are network-visible.
- [ ] No message body appears in backend logs.

Report vulnerabilities privately to the repository owner. Do not include tokens, private chat, or reusable pairing codes in a report.
