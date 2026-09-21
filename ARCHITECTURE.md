# Sentinel Relay architecture

## Scope

Sentinel Relay is a one-way, direct-webhook Dalamud plugin:

```text
FFXIV chat → local Sentinel Relay policy → Discord incoming webhook
```

Discord cannot connect back to the plugin. There is no generic or whitelisted game-command path because there is no inbound Discord path at all.

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
10. Successful delivery timestamps and sanitized errors are returned to the game thread through a small action queue.

No HTTP, cryptography, disk, or retry delay runs inside the FFXIV chat callback.

## Per-character routing

Dalamud's `IPlayerState.ContentId` becomes a key such as `cid:0011223344556677`. Each key owns a `CharacterProfile` containing:

- display name and home world;
- DPAPI-protected webhook URL;
- enabled chat types;
- pause state;
- compact-text/embed and world-display preferences;
- local keyword rules and optional Discord mention user ID; and
- last successful webhook test/delivery timestamp.

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

Embed mode uses a small channel-specific color, sender/channel title, message description, and UTC timestamp. Long content is split on Unicode rune boundaries so surrogate pairs are not broken.

Every payload has an empty `allowed_mentions.parse` array. Raw FFXIV text also has mention-like syntax neutralized. A keyword rule may separately authorize exactly one validated Discord user ID; that ID is the only entry in `allowed_mentions.users` for that alert.

## Discord commands decision

Discord slash commands are intentionally absent. Webhooks are write-only and cannot receive interactions. Adding a Gateway bot inside the plugin would require placing a shared bot token on both PCs, and two simultaneous clients could process the same interaction. That would add risk and unreliable routing without improving the core relay.

All configuration lives in the `/srelay` UI. The unrelated pre-existing Discord bot is neither required nor contacted.

## Reference and licensing

`reiichi001/Dalamud.DiscordBridge` was reviewed as behavioral proof for structured chat capture, mapping, queueing, duplicate suppression, and Discord delivery. That project is AGPL-3.0. Sentinel Relay remains MIT and uses an independently written implementation; no substantial source was copied.
