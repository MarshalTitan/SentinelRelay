# Security and privacy

## Security properties

### Narrow Discord reply path

The FFXIV → Discord webhook path remains independent. The optional reply path:

- makes outbound authenticated Discord REST reads only;
- connects to no Discord Gateway and accepts no inbound network connection;
- recognizes no registered application/slash command;
- accepts one exact channel ID and one exact user ID per active character profile;
- ignores messages authored by bots or webhooks;
- uses a persistent Discord snowflake checkpoint and a two-minute freshness window;
- exposes only locally enabled chat destinations with fixed parser and FFXIV-prefix mappings;
- maps `/r` only to FFXIV's `/reply`, and only for 30 minutes after the active character receives a Tell;
- rejects arbitrary `/tell` targets as well as every non-allowlisted slash command;
- has no generic "execute this slash command" operation;
- never loads macros; and
- is disabled by default.

The checkpoint is saved before game submission. This deliberately favors dropping a command during a crash over replaying it. Every reader start/reconnect first advances to the newest current Discord message, so messages posted while the reader is offline are not executed later.

### Remote screenshot boundary

Remote screenshots are disabled by default and require the master Discord reader plus a separate per-character opt-in. The exact `/screenshot` message is authorized with the same character key, channel ID, user ID, author-type, freshness, checkpoint, pause, and replay checks as chat replies. A 15-second local cooldown and single in-flight capture limit prevent continuous capture.

`/screenshot` never enters the FFXIV chat sender. The capture implementation:

- resolves only a visible top-level window owned by the current FFXIV process;
- never accepts a process, monitor, desktop region, window handle, or filesystem path from Discord;
- captures only that window's client area into an in-memory bitmap;
- refuses minimized and blank frames rather than falling back to desktop capture;
- downsizes to at most 1280×720 and caps encoded data at 7.5 MB;
- encodes and uploads without writing a persistent screenshot file;
- disables HTTP redirects and uses only the active character's validated webhook; and
- cancels in-flight work on pause, character switch, or shutdown.

The plugin prints an in-game notice when an authorized capture is accepted, so remote capture is not silent. The screenshot can contain anything visible inside the FFXIV render output, including chat, UI, names, plugins, and overlays rendered into the game window. Users should enable this experimental feature only in a private relay channel they trust.

### Local privacy enforcement

Every chat filter defaults off. The plugin tests the active character's filter before formatting, queueing, or making an HTTP request. Disabled chat does not leave the FFXIV process.

Tell, Party, Cross-world Party, Alliance, linkshell, CWLS, PvP Team, and Novice Network filters require deliberate individual selection. **Enable common chats** does not enable these sensitive channels.

### Webhook credential protection

A Discord webhook URL contains a secret token. Sentinel Relay:

- never hard-codes webhook URLs;
- never commits them to GitHub or includes them in a release;
- protects each saved URL with Windows DPAPI, scoped to the current Windows user;
- holds only the active character's decrypted URL in memory;
- masks it in the UI;
- omits it from status, debug output, and logs; and
- accepts only HTTPS URLs on recognized Discord hosts with a valid webhook path.

DPAPI protects the local file at rest but cannot protect secrets from malware or another process already running as the same Windows user.

### Discord bot credential protection

The reply reader stores its bot token per character using Windows DPAPI with entropy separate from webhook protection. The token is masked after entry and excluded from status, debug output, errors, and logs.

A bot token represents the bot anywhere it has permissions and is more powerful than one channel's webhook. Use a dedicated bot with only **View Channel** and **Read Message History** access to the intended private relay channel. Do not grant Administrator. Reset the token in the Discord Developer Portal immediately if it may have leaked, then replace it on each intended client.

### Mention safety

FFXIV text is untrusted. Sentinel Relay removes control/private-use characters, normalizes whitespace, neutralizes `@everyone`, `@here`, user/role mention syntax, and channel mention syntax, and uses Discord's `allowed_mentions` field.

Keyword alerts are the sole exception: a rule may explicitly allow one locally configured numeric Discord user ID. Even then, role and mass mentions remain disabled.

### Network and rate-limit safety

The plugin opens no listening port. The stable sender makes outbound HTTPS POST requests directly to Discord; the optional reader makes outbound HTTPS GET requests. Background workers own all network waits, so Discord never blocks the FFXIV chat/framework thread.

Automatic HTTP redirects are disabled, keeping the webhook credential on the already validated Discord origin. Pausing, removing/replacing a webhook, or changing characters cancels active delivery and clears pending work on a best-effort basis.

The sender honors `Retry-After`, `X-RateLimit-Reset-After`, or Discord's JSON `retry_after` value on HTTP 429. Network failures and 5xx responses use bounded retries. Permanent errors are not retried forever. Messages older than two minutes are discarded.

## Data handling

Sent to Discord for each enabled message:

- selected chat-type label;
- sanitized sender name;
- sender world when available and enabled;
- sanitized message text.

Stored locally per character:

- character content-ID-derived key, name, and world;
- DPAPI-protected webhook URL;
- optional DPAPI-protected Discord bot token;
- optional exact relay channel ID and authorized Discord user ID;
- reply enabled state, outbound destination allowlist, remote-screenshot opt-in, and last processed message snowflake;
- enabled filters and formatting choices;
- pause state;
- keyword rules and optional Discord user ID; and
- last success timestamp.

Not stored or operated by Sentinel Relay:

- chat history database;
- Discord OAuth secret;
- Railway or hosting credential;
- backend authentication token; or
- arbitrary remotely supplied FFXIV command or macro.

Discord receives delivered content under the server/account's normal Discord data and retention rules. Deleting a message in FFXIV does not remove its already delivered Discord copy.

## Secret rotation

If a webhook URL may have leaked:

1. Delete the webhook in Discord.
2. Create a replacement in the same intended channel.
3. Use `/srelay` → **Discord Webhook** → **Replace Webhook**.
4. Test it.

Deleting the webhook invalidates the old URL. Each character uses a separate URL, so rotate only the affected character unless both were exposed.

## Logs and diagnostics

Normal logs contain lifecycle information and sanitized HTTP/status errors. They do not include:

- webhook URL or token;
- Discord bot token;
- message body;
- sender text;
- keyword text; or
- decrypted configuration.

`/srelay debug` shows only state, queue length, drop count, active character, configured/not-configured status, timestamps, the last sanitized error, and the latest reward LogKind summary. It never prints a reward body.

The Debug tab has a separate per-character, default-off reward diagnostic. When enabled, it holds at most 20 recognized reward-pattern lines plus nearby numeric `LogMessage` IDs in memory. It does not collect player chat, does not persist observations, does not write bodies to logs, and clears on character switch, disable, or reload.

## Known limitations

- Anyone who obtains a webhook URL can post into that Discord channel until the webhook is deleted.
- Incoming webhooks cannot send private Discord DMs; keyword alerts are channel highlights/optional channel pings.
- Webhook delivery is unavailable when Discord or the local Internet connection is unavailable.
- A two-minute queue cutoff intentionally favors privacy/current context over later bulk delivery.
- A chat event during the brief content-ID transition window is deliberately dropped instead of risking delivery through the previous character's route.
- Immediate identical sender/channel/message events inside the duplicate window may be treated as duplicates.
- `SeString.TextValue` produces a safe plain-text approximation; interactive item/map/player links are not recreated as Discord links.
- The plugin cannot verify who can read a Discord channel. Channel permissions remain the server owner's responsibility.
- Discord message-content access requires the Message Content privileged intent on the bot application.
- The reverse path uses an internal FFXIV chat-shell interface. Automated tests can verify policy and construction, but only a second live FFXIV client can prove a server-visible FC send after a game/API update.
- REST polling is intentionally near-real-time rather than instantaneous and functions only while the configured FFXIV client/plugin is running.
- Reward recognition currently targets the English client phrases documented in the live-test checklist. Other client languages require separately verified patterns.
- Remote screenshots also function only while that character's FFXIV client and plugin are running; GitHub or Discord cannot capture an offline client.
- Minimized windows are intentionally unsupported. Restored Direct3D window capture can still return blank on some graphics modes/drivers; the plugin rejects that frame and never falls back to desktop capture.
- A successful screenshot includes all visible content rendered inside the FFXIV client area. Sentinel Relay cannot selectively redact chat, names, Dalamud overlays, or game UI from the captured pixels.

## Dependency and licensing review

The production plugin uses the .NET/Dalamud runtime and no Discord SDK package. `reiichi001/Dalamud.DiscordBridge` was reviewed only as an AGPL-3.0 architectural reference. Sentinel Relay's direct-webhook implementation is independently written and remains MIT licensed.
