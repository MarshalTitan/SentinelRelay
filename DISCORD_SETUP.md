# Discord setup for Sentinel Relay

Sentinel Relay uses Discord's built-in incoming webhooks. You do **not** need a Discord Developer Portal application, bot token, privileged intent, OAuth installation, hosted service, or running Discord bot.

A webhook URL is a secret credential that can post into its channel. Never paste it into GitHub, a public Discord message, a screenshot, a support ticket, or a shared log.

## Before you start

You need permission to manage the two relay channels and their webhooks. Discord calls this permission **Manage Webhooks**. Keep both channels private because enabled FFXIV chat will appear there.

Recommended layout:

- `#relay-wrothy` — visible only to Wrothy and chosen server administrators
- `#relay-elektra` — visible only to Elektra and chosen server administrators

Channel visibility does not control the FFXIV client; this revision is one-way only. Privacy permissions still prevent unrelated members from reading relayed chat.

## 1. Create `#relay-wrothy`

1. In Discord, create a category named **Sentinel Relay** if one does not already exist.
2. Edit the category permissions.
3. Deny **View Channel** to `@everyone`.
4. Create a text channel named `relay-wrothy` inside the category.
5. Add Wrothy's Discord account or a Wrothy-only role.
6. Allow that account/role to **View Channel** and **Read Message History**.
7. Add only the administrators who should be able to see relayed FFXIV chat.

## 2. Create Wrothy's webhook

Discord may expose webhook management from the channel or server settings. Use either route:

- Right-click `#relay-wrothy` → **Edit Channel** → **Integrations** → **Webhooks**, or
- Server name → **Server Settings** → **Integrations** → **Webhooks**, then choose `#relay-wrothy`.

Then:

1. Select **New Webhook** or **Create Webhook**.
2. Name it **Sentinel Relay — Wrothy**.
3. Confirm its destination channel is exactly `#relay-wrothy`.
4. Optionally upload `assets/icon.png` from this repository as its avatar.
5. Select **Copy Webhook URL**.
6. Do not post or save the copied URL anywhere public.

Discord's official documentation describes webhooks as generated URLs that post into a selected channel. Sentinel Relay uses that URL directly over HTTPS; there is no intermediary service.

## 3. Connect Wrothy Minioa

Do this while **Wrothy Minioa** is the active character:

1. In FFXIV, run `/srelay`.
2. Confirm the header says **Wrothy Minioa**.
3. Open **Discord Webhook**.
4. Paste the URL copied from `#relay-wrothy` into **Webhook URL**.
5. Select **Save Webhook**.
6. Select **Test Webhook**.
7. Confirm `#relay-wrothy` receives:

   ```text
   Sentinel Relay connected successfully for Wrothy Minioa.
   ```

8. Open **Chat Filters** and intentionally enable the desired channels. All filters begin off.

The saved URL is masked and protected in the local Dalamud configuration using Windows DPAPI. `/srelay status` and `/srelay debug` report only **configured** or **not configured**.

## 4. Create `#relay-elektra`

1. Create a second private text channel named `relay-elektra`.
2. Permit Elektra's Discord account or role to view it.
3. Do not grant Wrothy visibility unless sharing is intentional.
4. Open `#relay-elektra` → **Edit Channel** → **Integrations** → **Webhooks**.
5. Create a new webhook named **Sentinel Relay — Elektra**.
6. Confirm its destination is exactly `#relay-elektra`.
7. Copy this second webhook URL.

Never reuse Wrothy's webhook for Elektra. A separate URL is what guarantees the destinations remain independent even if both characters use the same Windows/Dalamud installation.

## 5. Connect Elektra Minoa

Do this while **Elektra Minoa** is the active character:

1. Run `/srelay`.
2. Confirm the header says **Elektra Minoa** before pasting anything.
3. Open **Discord Webhook**.
4. Paste Elektra's `#relay-elektra` webhook URL.
5. Select **Save Webhook**, then **Test Webhook**.
6. Confirm the success message appears only in `#relay-elektra`.
7. Configure Elektra's chat filters independently.

The plugin keys profiles to Dalamud's character content ID, not the displayed name alone.

## 6. Configure keyword highlights and pings

Webhooks cannot open a DM conversation with a Discord user. Sentinel Relay instead supports:

- a visible keyword-alert line in the private relay channel; and
- an optional ping of one explicitly configured Discord account.

To enable the optional ping:

1. In Discord, open **User Settings** → **Advanced**.
2. Enable **Developer Mode**.
3. Right-click your own Discord name/avatar and select **Copy User ID**.
4. In FFXIV, run `/srelay` → **Keywords**.
5. Paste the 17–20 digit number into **Discord User ID** and select **Save User ID**.
6. Enter a keyword such as `Wrothy`, choose contains or whole-word matching, choose the channels, and decide whether the rule should ping the configured user.
7. Select **Add Keyword**.

Only this explicit feature can produce a mention. Text copied from FFXIV—including literal `@everyone`, `@here`, user mentions, role mentions, and channel mentions—is neutralized and sent with Discord's `allowed_mentions` protection.

## 7. Test normal chat delivery

For each character:

1. Enable **Say** only.
2. Send `Sentinel Relay Say test` in FFXIV.
3. Confirm it appears only in that character's relay channel.
4. Enable **Free Company** and test again.
5. Disable **Shout**, send a Shout, and verify nothing appears in Discord.
6. Run `/srelay pause`, send an enabled chat message, and verify nothing appears.
7. Run `/srelay resume` and repeat.

Complete [LIVE_TEST_CHECKLIST.md](LIVE_TEST_CHECKLIST.md) before publishing the release.

## Existing Sentinel Relay bot

The Discord application/bot created for the earlier design is not used by this architecture. It may remain in the server, be disabled, or be removed; it has no effect on webhook delivery. Do not paste its bot token into Sentinel Relay.

## If a webhook URL leaks

Treat a leaked URL like a leaked password:

1. Open the affected channel's **Integrations** → **Webhooks**.
2. Delete the affected webhook. Editing its name is not enough.
3. Create a new webhook in that same channel.
4. In FFXIV, run `/srelay` → **Discord Webhook**.
5. Paste the new URL and select **Replace Webhook**.
6. Test it.

The deleted URL stops working. The other character's separate webhook does not need to change.

## Common problems

- **No Integrations/Webhooks option:** your Discord role lacks Manage Webhooks, or you are using a channel type that does not support the expected incoming-webhook flow.
- **Test returns HTTP 401/404:** the webhook was deleted, regenerated, copied incompletely, or belongs to an unavailable channel. Replace it with a freshly copied URL.
- **Test appears in the wrong channel:** delete that saved webhook from `/srelay`, create a new webhook in the correct channel, and paste it while the correct FFXIV character is active.
- **Normal chat does not appear:** the test must pass, the relay must be resumed, and that exact chat type must be enabled.
- **Keyword highlight appears but no ping:** save the correct Discord User ID and enable **Ping configured user** on the keyword rule.
- **Discord bot is offline:** irrelevant to Sentinel Relay webhook operation.

See [TROUBLESHOOTING.md](TROUBLESHOOTING.md) for diagnostic steps.
