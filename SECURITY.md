# Security and privacy

## Security properties

### No inbound control path

Sentinel Relay is FFXIV → Discord only. It does not:

- connect to a Discord Gateway;
- poll Discord;
- accept WebSocket or HTTP connections;
- execute FFXIV commands;
- expose slash commands;
- load macros; or
- send text from Discord into the game.

Removing the inbound path also removes the earlier architecture's remote-command, pairing, replay-token, and cross-user authorization risks.

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

### Mention safety

FFXIV text is untrusted. Sentinel Relay removes control/private-use characters, normalizes whitespace, neutralizes `@everyone`, `@here`, user/role mention syntax, and channel mention syntax, and uses Discord's `allowed_mentions` field.

Keyword alerts are the sole exception: a rule may explicitly allow one locally configured numeric Discord user ID. Even then, role and mass mentions remain disabled.

### Network and rate-limit safety

The plugin opens no listening port. It makes outbound HTTPS POST requests directly to Discord. One background worker owns a bounded FIFO queue, so network delay never blocks the FFXIV chat/framework thread.

Automatic HTTP redirects are disabled, keeping the webhook credential on the already validated Discord origin. Pausing, removing/replacing a webhook, or changing characters cancels active delivery and clears pending work on a best-effort basis.

The sender honors `Retry-After`, `X-RateLimit-Reset-After`, or Discord's JSON `retry_after` value on HTTP 429. Network failures and 5xx responses use bounded retries. Permanent errors are not retried forever. Messages older than two minutes are discarded.

## Data handling

Sent to Discord for each enabled message:

- selected chat-type label;
- sanitized sender name;
- sender world when available and enabled;
- sanitized message text; and
- timestamp in embed mode.

Stored locally per character:

- character content-ID-derived key, name, and world;
- DPAPI-protected webhook URL;
- enabled filters and formatting choices;
- pause state;
- keyword rules and optional Discord user ID; and
- last success timestamp.

Not stored or operated by Sentinel Relay:

- chat history database;
- Discord bot token;
- Discord OAuth secret;
- Railway or hosting credential;
- backend authentication token; or
- remotely supplied FFXIV command.

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
- message body;
- sender text;
- keyword text; or
- decrypted configuration.

`/srelay debug` shows only state, queue length, drop count, active character, configured/not-configured status, timestamps, and the last sanitized error.

## Known limitations

- Anyone who obtains a webhook URL can post into that Discord channel until the webhook is deleted.
- Incoming webhooks cannot send private Discord DMs; keyword alerts are channel highlights/optional channel pings.
- Webhook delivery is unavailable when Discord or the local Internet connection is unavailable.
- A two-minute queue cutoff intentionally favors privacy/current context over later bulk delivery.
- A chat event during the brief content-ID transition window is deliberately dropped instead of risking delivery through the previous character's route.
- Immediate identical sender/channel/message events inside the duplicate window may be treated as duplicates.
- `SeString.TextValue` produces a safe plain-text approximation; interactive item/map/player links are not recreated as Discord links.
- The plugin cannot verify who can read a Discord channel. Channel permissions remain the server owner's responsibility.

## Dependency and licensing review

The production plugin uses the .NET/Dalamud runtime and no Discord SDK package. `reiichi001/Dalamud.DiscordBridge` was reviewed only as an AGPL-3.0 architectural reference. Sentinel Relay's direct-webhook implementation is independently written and remains MIT licensed.
