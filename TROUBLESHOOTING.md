# Troubleshooting Sentinel Relay

## Quick diagnostic order

1. Run `/srelay status` and confirm the expected character name.
2. Confirm **Discord webhook: configured** and **relay: active**.
3. Open `/srelay` → **Discord Webhook** and select **Test Webhook**.
4. Confirm the test appears in the intended Discord channel.
5. Open **Chat Filters** and confirm the exact FFXIV chat type is enabled.
6. Run `/srelay debug` and inspect queue, drop count, last success, and last error.

The saved webhook URL is never displayed. Do not paste it into a log or support message.

## Test webhook fails

### URL rejected before saving

Sentinel Relay accepts only a complete Discord HTTPS incoming-webhook URL. Copy it again from the channel's **Edit Channel → Integrations → Webhooks** screen. A normal Discord channel URL is not a webhook.

### HTTP 401 or 404

The webhook was deleted, regenerated, copied incompletely, or is no longer accessible. Delete/replace the saved webhook in `/srelay`, create a fresh Discord webhook, and paste the new URL.

### HTTP 403

The webhook/channel no longer permits the operation. Confirm the webhook still exists in the intended channel and that the channel has not been removed or converted to an incompatible type.

### Network error or timeout

Confirm the PC can reach Discord over HTTPS. VPN, firewall, DNS, security software, or a Discord outage may interfere. FFXIV remains unaffected; Sentinel Relay never blocks the game while waiting.

## Test works, but normal chat does not

- Verify the exact channel is checked. Party and Cross-world Party are separate; Incoming Tell and Outgoing Tell are separate.
- Verify the relay is not paused.
- Verify you are testing the same logged-in character shown in the window header.
- All filters default off, including Free Company.
- An immediate identical repeat may be suppressed as a duplicate; change the text during testing.

## Message went to the wrong Discord channel

1. Pause the relay.
2. Confirm the active FFXIV character shown in `/srelay`.
3. Remove the saved webhook for that character.
4. In Discord, open the intended relay channel and copy its own webhook URL.
5. Save and test it.
6. Resume.

Each character requires a separate webhook. Webhook URLs must not be swapped or reused between profiles.

## Hunt rewards do not appear

1. Open **Chat Filters** and enable **Rewards / Hunt Results** for the active character. It defaults off and is not included by **Enable common chats**.
2. Confirm the webhook is configured and the relay is not paused.
3. Open **Debug** and enable **Record reward XivChatType / LogKind diagnostics** before the next S-rank.
4. After rewards appear in FFXIV, inspect the observations for the exact `XivChatType` name/value and nearby raw `LogMessage` IDs.
5. If the line is marked **diagnostic only**, record that observation for a follow-up mapping update; Sentinel Relay deliberately refuses to forward reward-looking text from an unverified LogKind.

Diagnostics are in memory only and disappear on character switch or plugin reload. The current text rules target the English client.

## Queue full or dropped messages increase

Discord may be unavailable or heavily rate limiting. The queue is deliberately capped at 100 and messages older than two minutes are discarded. Sentinel Relay will not accumulate hours of private chat and later dump it into Discord.

Wait for Discord connectivity to recover, then use **Test Webhook**. There is no backend service to restart.

## Keyword alert does not ping

- Confirm the keyword itself is matching and its chat type is selected for the rule.
- Confirm **Ping configured user** is enabled on that rule.
- Copy your numeric Discord User ID with Developer Mode, not your username/display name.
- Save the ID in the same FFXIV character profile.
- The user must be able to view the relay channel.

Webhook keyword alerts cannot send DMs. They highlight and optionally ping inside the relay channel.

## `@everyone` or a copied Discord mention does not ping

This is intentional. Arbitrary FFXIV text is sent with all mentions disabled and visible mention syntax neutralized. Only the explicit keyword-alert user ID can create a ping.

## Character settings appear missing

Settings are per FFXIV content ID. Confirm the intended character is fully logged in and `IPlayerState` has loaded. One character's filters and webhook must not appear while another character is active.

If the protected URL becomes unreadable after moving a Dalamud configuration file to another Windows account/PC, paste that character's webhook again. Windows DPAPI intentionally ties protection to the original Windows user.

## Discord reader test fails

- **HTTP 401:** the bot token is invalid or has been reset. Paste the current token in **Discord Replies** and save.
- **HTTP 403:** the bot lacks **View Channel** or **Read Message History** in the configured channel.
- **HTTP 404:** the Relay Channel ID is wrong or the channel is unavailable to the bot.
- **Reader connected but content is ignored:** enable **Message Content Intent** on the application Bot page, verify the exact Authorized Discord User ID, enable the master reply switch, and enable that command's outbound destination.

The webhook relay still works if the bot reader is disabled or broken. The bot is not required for FFXIV → Discord.

## `/fc hi` does nothing

1. Confirm the intended FFXIV character is logged in and `/srelay` shows its profile.
2. Confirm the reader state is **Connected**, not merely configured.
3. Confirm the message was posted as ordinary text beginning exactly with `/fc `.
4. Confirm it came from the configured user in the configured channel.
5. Confirm **Allow /fc** and **Enable Discord → FFXIV Replies** are both saved.
6. Confirm the relay is not paused and the local rate limit has not been reached.

Do not use `/fc message: hi`, quote syntax, BotGhost, or a registered slash command. A local FFXIV notice is not proof of delivery; check from a second account/client in the same Free Company.

## An old `/fc` message did not send after restart

This is intentional. Starting, resuming, reconnecting, or switching characters advances to the newest current Discord message before polling. Commands written while the reader was offline are never replayed later.

## `/r message` does nothing

1. Enable **Allow /r (Reply to the latest incoming Tell)** under **Discord Replies**.
2. Enable the master **Discord → FFXIV Replies** switch and save.
3. Receive a new incoming Tell on the active character while Sentinel Relay is loaded.
4. Post `/r message` within 30 minutes from the exact authorized Discord user and channel.
5. Check `/srelay debug`; `recentTellTarget` should report `available`.

Sentinel Relay intentionally clears the Tell-reply target when changing characters or reloading the plugin. It never accepts an arbitrary Discord-supplied `/tell` target.

## Safe diagnostic information to share

- plugin version;
- active character name/world;
- enabled chat labels;
- webhook configured/not configured;
- delivery state;
- queue length and drop count;
- last success time; and
- last sanitized error.

Never share the webhook URL, bot token, a screenshot containing either, or a local protected-secret value.
