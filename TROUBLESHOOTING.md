# Troubleshooting

Start with `/srelay` → **Debug** and `/relay status`. Neither view exposes credentials.

## Status meanings

| Plugin status | Meaning | Next check |
|---|---|---|
| `Disabled` | No valid service URL is configured | Set `wss://HOST/v1/plugin` in Advanced |
| `Connecting` | First WebSocket connection is in progress | Wait a few seconds, then check `/ready` |
| `ConnectedUnlinked` | Service is reachable; this character has no valid link | Run `/srelay link` |
| `Authenticated` | Character credential was accepted | Configure the Discord channel and filters |
| `Reconnecting` | Backoff/retry is active | Check service logs/TLS/network |
| `Offline` | Last connection attempt failed | Read **Last error** in Debug |
| `PAUSED` | Both message directions are blocked | Run `/srelay resume` when safe |

## Bot is offline

1. Open `https://YOUR-HOST/health`. If it is unavailable, the service/container is down.
2. Open `/ready`. `discord:"connecting"` or HTTP 503 points to Discord authentication/connectivity.
3. Check deployment logs for `DISCORD_TOKEN and DISCORD_CLIENT_ID are required`, `invalid token`, or database open errors.
4. Verify the token was copied without quotes/newlines and was not reset after deployment.
5. Verify the volume is mounted at `/data`; for Railway volume permissions, set `RAILWAY_RUN_UID=0` as described in [BACKEND_DEPLOYMENT.md](BACKEND_DEPLOYMENT.md).
6. Restart the service. If the token may be exposed, reset it instead of retrying the old value.

## Slash commands are missing

1. Confirm `DISCORD_CLIENT_ID` is the Application ID—not server ID, public key, or bot user ID.
2. Confirm `DISCORD_GUILD_ID` is the intended server ID.
3. Confirm the app was installed into that server with the `applications.commands` and `bot` scopes.
4. Restart the service and look for `Discord commands registered` with `commandCount: 10` and scope `guild`.
5. Reload Discord (`Ctrl+R`) and type `/relay`.

## `/relay link` says invalid or expired

- Codes expire after ten minutes, work once, and stop working when the generating FFXIV WebSocket disconnects.
- Generate a new code from the character currently displayed in the Sentinel Relay header.
- Do not reuse Wrothy's code for Elektra.
- Type the eight characters; the hyphen is optional and matching is case-insensitive.
- Repeated generation is rate-limited. Wait for the displayed retry interval.

## `/relay channel` fails

- Run it after linking, from the linked Discord account.
- Choose a normal server text channel, not a thread, forum, DM, announcement channel, or voice channel.
- The linked user needs View Channel and Send Messages.
- The bot needs View Channel, Send Messages, and Embed Links in that exact channel.

## FFXIV → Discord does not relay

1. Confirm the plugin shows `Authenticated` and not `PAUSED`.
2. Confirm the exact chat type is enabled for the active character under **Chat Filters**. Every type starts off.
3. Confirm `/relay status` shows the correct character and destination.
4. Check **Last FFXIV → Discord** in Debug. If it never advances, the channel is disabled or the current Dalamud chat type did not map.
5. If it advances but Discord is empty, check service logs for missing channel/permissions without exposing chat content.
6. Test Say, then FC; do not diagnose LS/CWLS first.
7. Verify the message is a real chat message, not a combat/system log category.

## Keyword DM does not arrive

1. Confirm the ordinary relay embed arrives first.
2. Confirm the keyword rule includes that channel and the channel itself is enabled.
3. Contains matching is case-insensitive; whole-word mode requires non-letter/digit boundaries.
4. Confirm the rule's method includes **Discord DM**.
5. Allow direct messages for the server/user and ensure the bot is not blocked.
6. One message with multiple matches creates one bundled alert; an identical sender/channel/message alert is suppressed for 60 seconds.

## Discord → FFXIV says character offline

- FFXIV/Dalamud must be running with the linked character active.
- Check for `Authenticated`; a different character profile does not borrow the first profile's credential.
- Confirm the plugin points to the same deployed service as Discord.
- Wait through reconnect backoff or select **Save and Reconnect**.
- If the client was closed, offline is the correct safe response; messages are not queued for later.

## Discord → FFXIV says use another channel

Outbound commands are accepted only in the configured channel and server. Run `/relay status`, go to its destination, or rerun `/relay channel channel:#correct-channel` from the linked account.

## Bot says sent, but nobody sees the FFXIV message

This must be treated as a release blocker.

The service reports success only after a matching local structured chat echo, but a live second client/member is still the authoritative network-visible test. Check:

- the character really belongs to the FC/party/linkshell;
- the command is permitted in the current game context;
- another player/client can see it;
- the FFXIV update has not changed `RaptureShellModule.ExecuteCommandInner` behavior;
- Dalamud/FFXIV logs for client-structure exceptions.

Never replace this path with `IChatGui.Print`; that would be a local fake. Disable/pause the relay and hold the release if external visibility fails.

## Message times out or is rate-limited

- Wait at least 30 seconds before retrying a burst.
- Limits are five Discord-originated sends per 30 seconds and 1.5 seconds minimum spacing locally.
- Messages expire after 20 seconds and are not delivered after a long outage.
- The local queue holds at most five. Restarting will intentionally discard it.
- Message text must be non-empty, at most 180 Unicode characters, and at most 400 UTF-8 bytes.

## Duplicate messages or a suspected loop

1. Pause immediately with `/srelay pause`.
2. Save a screenshot of both the FFXIV and Discord timestamps, but redact private chat.
3. Note the plugin/service versions, character, channel type, and whether the duplicate was the bot's normal `Sent from Discord through Sentinel Relay` confirmation.
4. A Discord-originated message should appear in Discord exactly once as that confirmation. More copies indicate correlation failure.
5. Restart the plugin and service, retest in Say with another observer, and keep the release paused if duplication persists.

## Character switched but status is wrong

The plugin rechecks the active character every two seconds. Wait briefly. If it remains wrong:

1. Run `/srelay status`.
2. Log fully to character selection and back in.
3. Reload the plugin through Dalamud.
4. Do not link or send until the header shows the correct character.

Switching clears pending outbound work and disconnects the old profile before activating the new one.

## TLS/WebSocket failures

- Production plugin URL must be `wss://`, not `https://` or `ws://`.
- Include `/v1/plugin` exactly.
- Open `https://HOST/ready` to validate the certificate/domain.
- Do not put a CDN/proxy in front unless it preserves WebSocket upgrades and `X-Forwarded-Proto: https`.
- Correct the host's clock and the Windows PC's clock; message freshness checks reject large skew.

## Safe diagnostic information

Useful to share:

- plugin version and backend version
- connection state and reconnect count
- active character/world
- installation ID (private but not a credential)
- selected routing channel name/ID
- queue length and pending confirmation ID
- last inbound/outbound timestamps
- sanitized error text

Never share the Discord bot token, DPAPI token blob, plaintext client token, real chat content, or an unexpired pairing code.
