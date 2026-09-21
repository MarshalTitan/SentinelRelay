# Sentinel Relay architecture

## Scope

Sentinel Relay's primary path is a direct-webhook Dalamud plugin:

```text
FFXIV chat → local Sentinel Relay policy → Discord incoming webhook
```

The live `0.2.0.0` release adds a separately optional, experimental REST-polling path:

```text
one private Discord channel → authenticated REST poll → local authorization → fixed /freecompany submission
```

There is still no hosted Sentinel backend, listening port, WebSocket, Discord Gateway client, or arbitrary command executor. The webhook sender and experimental reader are independent.

## Runtime flow

1. Dalamud publishes a structured `IChatGui.ChatMessage` event.
2. `ChatChannelMapper` maps the supported `XivChatType` to a stable `RelayChatType`.
3. `ChatCaptureService` extracts player/world information where available and converts `SeString.TextValue` into normalized plain text.
4. `Plugin.OnChatCaptured` verifies the live content ID still matches the loaded character profile; a transition mismatch is dropped rather than risk cross-character routing.
5. Paused, disabled, unconfigured, empty, unsupported, or immediate duplicate messages stop locally.
6. `KeywordMatcher` evaluates configured rules locally for enabled chat only.
7. `WebhookMessageFormatter` creates compact text or embed payloads, splits oversized content, and disables all untrusted mentions.
8. `WebhookRelayClient` adds the immutable payload and that profile's already validated webhook endpoint to a bounded FIFO queue.
9. One background worker posts messages to Discord through `WebhookHttpSender` in order.
10. Successful delivery times and sanitized errors are returned to the game thread through a small action queue.

No HTTP, cryptography, disk, or retry delay runs inside the FFXIV chat callback.

## Experimental reply flow

1. A character profile explicitly enables replies and `/fc`, and stores a DPAPI-protected bot token plus exact channel/user IDs.
2. `DiscordReplyReader` establishes a new-message checkpoint from Discord's current newest message before it begins polling.
3. `DiscordRestMessageClient` performs outbound `GET /channels/{id}/messages` requests on a background task and honors Discord `429` delays.
4. New messages are sorted by snowflake and their high-water mark is persisted before any game action is queued.
5. `DiscordReplyPolicy` rejects disabled, paused, wrong-character, wrong-channel, wrong-user, bot, webhook, stale, duplicate, empty, unknown, and non-allowlisted inputs.
6. `DiscordReplyCommandParser` recognizes only the literal ordinary-message prefix `/fc `.
7. A five-item queue and local five-per-30-second rate limit protect the client. Sends are spaced by at least 1.5 seconds.
8. On Dalamud's framework thread, `GameChatSender` constructs `/freecompany {sanitized message}` from a fixed mapping and passes it through FFXIV's chat shell.
9. The resulting real outgoing FC event follows the normal webhook path back to Discord once. Its `webhook_id` causes the reader to ignore it.

The checkpoint advances before submission. A crash can therefore drop an individual command, but cannot cause that command to execute twice after restart. Starting, resuming, reconnecting, or switching characters primes to the newest message, so commands written while offline never execute later.

## Per-character routing

Dalamud's `IPlayerState.ContentId` becomes a key such as `cid:0011223344556677`. Each key owns a `CharacterProfile` containing:

- display name and home world;
- DPAPI-protected webhook URL;
- enabled chat types;
- pause state;
- compact-text/embed and world-display preferences;
- local keyword rules and optional Discord mention user ID; and
- last successful webhook test/delivery timestamp.

Experimental reply fields are also per character: DPAPI-protected bot token, enabled state, exact relay channel ID, exact authorized user ID, persistent snowflake checkpoint, `/fc` allowlist, and last reader success time.

The framework checks for a content-ID change every 250 ms. When it changes, Sentinel Relay cancels/clears pending delivery, clears the duplicate cache, loads only the new profile, decrypts only its webhook into memory, and updates the UI. A queued item also carries the endpoint selected at capture time; it is never rerouted through another profile. A chat event that lands inside the transition window is discarded, never sent through the previous route.

## Webhook validation

The plugin accepts only absolute HTTPS URLs whose host is a recognized Discord host and whose path matches Discord's incoming-webhook structure:

```text
/api/webhooks/{numeric webhook id}/{webhook token}
```

The request adds `wait=true` so Discord confirms creation rather than returning before the message is saved.

## Queue and failure policy

- FIFO capacity: 100 deliveries
- Stale cutoff: two minutes
- Worker count: one, preserving order
- HTTP timeout: 30 seconds
- `429`: honor Discord retry timing, bounded to 60 seconds per wait
- transient network/5xx responses: bounded exponential retry
- permanent 4xx response: fail clearly without an infinite loop
- full queue: reject the newest item and increment the non-secret drop counter
- redirects: disabled so the webhook credential remains on the validated Discord origin
- pause or character switch: cancel active delivery and clear pending work

## Message presentation

Compact mode:

```text
【FC】 Player Name @ World: Anyone want roulettes?
```

Embed mode uses a small channel-specific color, sender/channel title, and message description. It deliberately omits an embed timestamp and footer so Discord's native message timestamp is the only timestamp shown. Long content is split on Unicode rune boundaries so surrogate pairs are not broken.

Every payload has an empty `allowed_mentions.parse` array. Raw FFXIV text also has mention-like syntax neutralized. A keyword rule may separately authorize exactly one validated Discord user ID; that ID is the only entry in `allowed_mentions.users` for that alert.

## Discord command decision

Registered Discord slash commands are intentionally absent. `/fc hi` is an ordinary channel message, not an application command. A Gateway connection is unnecessary: each FFXIV client performs authenticated REST reads against only its configured channel and accepts only its configured user ID.

All configuration lives in the `/srelay` UI. The bot remains unnecessary for FFXIV → Discord webhook delivery and is contacted only when that character explicitly enables the experimental reader.

## Reference and licensing

`reiichi001/Dalamud.DiscordBridge` was reviewed as behavioral proof for structured chat capture, mapping, queueing, duplicate suppression, and Discord delivery. That project is AGPL-3.0. Sentinel Relay remains MIT and uses an independently written implementation; no substantial source was copied.
