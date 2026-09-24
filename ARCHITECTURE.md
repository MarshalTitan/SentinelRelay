# Sentinel Relay architecture

## Scope

Sentinel Relay's primary path is a direct-webhook Dalamud plugin:

```text
FFXIV chat → local Sentinel Relay policy → Discord incoming webhook
```

The `0.4.0.0` revision includes a separately optional REST-polling path:

```text
one private Discord channel → authenticated REST poll → local authorization → fixed chat submission
```

There is still no hosted Sentinel backend, listening port, WebSocket, Discord Gateway client, or arbitrary command executor. The webhook sender and reply reader are independent.

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

## Reward / hunt-result flow

1. `ChatCaptureService` checks sanitized chat text for the narrow hunt-credit, obtained-reward, and capped-currency patterns.
2. A line is classified as `RewardsHuntResults` only when its API 15 `XivChatType` RowId is one of `SystemMessage` (57), `SystemError` (58), `ErrorMessage` (60), `LootNotice` (62), or `Progress` (64). Player chat can never impersonate this inbound channel.
3. The per-character Rewards / Hunt Results filter must be enabled; it defaults off and is absent from every outbound allowlist and parser.
4. Consecutive lines are accumulated for a 1.25-second quiet window, capped at 20 lines/four seconds, then emitted in original order as one compact `【REWARD】` embed. An intervening ordinary chat message flushes the pending reward batch first.
5. The embed has no sender header, timestamp, or timestamp footer. Discord's native message timestamp remains available.
6. Optional per-character diagnostics observe raw `IChatGui.LogMessage` IDs in memory and correlate them with reward-pattern `ChatMessage` observations. They never invoke `FormatLogMessageForDebugging`, never write message bodies to logs, and clear on character switch or reload.

## Discord reply flow

1. A character profile explicitly enables replies and one or more outbound destinations, and stores a DPAPI-protected bot token plus exact channel/user IDs.
2. `DiscordReplyReader` establishes a new-message checkpoint from Discord's current newest message before it begins polling.
3. `DiscordRestMessageClient` performs outbound `GET /channels/{id}/messages` requests on a background task and honors Discord `429` delays.
4. New messages are sorted by snowflake and their high-water mark is persisted before any game action is queued.
5. `DiscordReplyPolicy` rejects disabled, paused, wrong-character, wrong-channel, wrong-user, bot, webhook, stale, duplicate, empty, unknown, and non-allowlisted inputs.
6. `DiscordReplyCommandParser` recognizes only fixed ordinary-message prefixes for Say, Yell, Shout, FC, Party, Alliance, PvP Team, Novice Network, LS1–8, CWLS1–8, and recent Tell reply.
7. A five-item queue and local five-per-30-second rate limit protect the client. Sends are spaced by at least 1.5 seconds.
8. On Dalamud's framework thread, `GameChatSender` constructs the appropriate fixed chat prefix plus the sanitized message and passes it through FFXIV's chat shell.
9. `/r` maps only to FFXIV's `/reply` and requires an incoming Tell observed for the active character within the previous 30 minutes. Arbitrary `/tell` targets are never accepted.
10. The resulting real outgoing game event follows the normal webhook path back to Discord once. Its `webhook_id` causes the reader to ignore it.

The checkpoint advances before submission. A crash can therefore drop an individual command, but cannot cause that command to execute twice after restart. Starting, resuming, reconnecting, or switching characters primes to the newest message, so commands written while offline never execute later.

## Remote screenshot control flow

1. `DiscordControlCommandParser` recognizes only a message whose trimmed content is exactly `/screenshot`. It is separate from the FFXIV chat-command parser.
2. `RemoteScreenshotPolicy` applies the same enabled, pause, active-character, channel, user, bot/webhook, snowflake, freshness, and replay checks as chat replies, plus the per-character screenshot toggle and a 15-second cooldown.
3. The processing checkpoint is persisted before capture begins, so a restart cannot repeat an old capture.
4. `GameWindowCapture` accepts only a visible top-level window owned by the current FFXIV process. It captures that HWND's client area into a memory DIB; it never selects another process, screen region, or desktop framebuffer.
5. Capture, resize, PNG encoding, and HTTPS upload run outside the framework thread. The output is at most 1280×720 and 7.5 MB, and no persistent file is created.
6. `ScreenshotWebhookSender` uploads a mention-safe `【SCREENSHOT】 Character Name` message and PNG attachment through the active character's already-validated webhook, with redirects disabled and bounded `429`/5xx retries.
7. Character switch, pause, or plugin shutdown cancels the active upload. A completion notice and sanitized status are returned to the framework thread.

Windows window capture cannot obtain a current frame from a minimized window, so minimized requests fail closed. A blank-frame check also rejects unusable Direct3D captures rather than uploading an empty image or falling back to the desktop.

## Per-character routing

Dalamud's `IPlayerState.ContentId` becomes a key such as `cid:0011223344556677`. Each key owns a `CharacterProfile` containing:

- display name and home world;
- DPAPI-protected webhook URL;
- enabled chat types;
- pause state;
- compact-text/embed and world-display preferences;
- local keyword rules and optional Discord mention user ID; and
- last successful webhook test/delivery timestamp.

Reply/control fields are also per character: DPAPI-protected bot token, enabled state, exact relay channel ID, exact authorized user ID, persistent snowflake checkpoint, outbound destination allowlist, remote-screenshot opt-in, and last reader/screenshot success times.

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

Reward batches always use the compact embed presentation regardless of the ordinary-chat formatting preference, preserving one reward line per display line.

Every payload has an empty `allowed_mentions.parse` array. Raw FFXIV text also has mention-like syntax neutralized. A keyword rule may separately authorize exactly one validated Discord user ID; that ID is the only entry in `allowed_mentions.users` for that alert.

## Discord command decision

Registered Discord slash commands are intentionally absent. `/fc hi`, `/party hi`, `/r hi`, `/screenshot`, and the other supported prefixes are ordinary channel messages, not application commands. A Gateway connection is unnecessary: each FFXIV client performs authenticated REST reads against only its configured channel and accepts only its configured user ID.

All configuration lives in the `/srelay` UI. The bot remains unnecessary for FFXIV → Discord webhook delivery and is contacted only when that character explicitly enables Discord replies.

## Reference and licensing

`reiichi001/Dalamud.DiscordBridge` was reviewed as behavioral proof for structured chat capture, mapping, queueing, duplicate suppression, and Discord delivery. That project is AGPL-3.0. Sentinel Relay remains MIT and uses an independently written implementation; no substantial source was copied.
