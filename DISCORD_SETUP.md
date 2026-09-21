# Discord setup for Sentinel Relay

Sentinel Relay uses Discord's built-in incoming webhooks. A webhook URL is a secret credential that can post into its assigned channel. Never paste one into GitHub, a public Discord message, a screenshot, a support ticket, or a shared log.

## Before you start

You need Discord's **Manage Webhooks** permission. Create one private relay channel and one webhook for each FFXIV character. For example:

- `#relay-character-one`
- `#relay-character-two`

Keep the channels private because every enabled FFXIV chat message is delivered there.

## 1. Create the first private channel

1. In Discord, create a category named **Sentinel Relay** if desired.
2. Edit the category or channel permissions.
3. Deny **View Channel** to `@everyone`.
4. Create a text channel such as `relay-character-one`.
5. Permit only its intended Discord user and trusted administrators to **View Channel** and **Read Message History**.

## 2. Create its webhook

1. Right-click the channel and select **Edit Channel**.
2. Open **Integrations → Webhooks**.
3. Select **New Webhook** or **Create Webhook**.
4. Name it **Sentinel Relay**.
5. Confirm the destination is the correct private channel.
6. Optionally upload `assets/icon.png` from this repository as its avatar.
7. Select **Copy Webhook URL**.
8. Do not post or save the URL anywhere public.

## 3. Connect the first character

Do this while the intended FFXIV character is active:

1. Run `/srelay`.
2. Confirm the correct character appears in the window header.
3. Open **Discord Webhook**.
4. Paste the URL into **Webhook URL**.
5. Select **Save Webhook**.
6. Select **Test Webhook**.
7. Confirm the success message appears only in the intended Discord channel.
8. Open **Chat Filters** and intentionally enable the desired channels. All filters begin off.

The saved URL is masked and protected in the local Dalamud configuration using Windows DPAPI. `/srelay status` and `/srelay debug` report only whether it is configured.

## 4. Configure the second character

1. Create a second private Discord text channel.
2. Create a new webhook assigned specifically to that second channel.
3. Copy the second webhook URL.
4. Log into the second FFXIV character and run `/srelay`.
5. Confirm the second character appears in the header before pasting anything.
6. Save and test the second webhook.
7. Configure the second character's filters independently.

Never reuse one character's webhook for another. Separate URLs keep the destinations independent even when multiple characters use the same Windows/Dalamud installation.

## 5. Configure keyword highlights and pings

Sentinel Relay can add a visible keyword-alert line and optionally ping one explicitly configured Discord account.

1. In Discord, open **User Settings → Advanced**.
2. Enable **Developer Mode**.
3. Right-click your Discord name/avatar and select **Copy User ID**.
4. In FFXIV, run `/srelay` and open **Keywords**.
5. Paste the numeric ID into **Discord User ID** and select **Save User ID**.
6. Enter a keyword, choose contains or whole-word matching, select the channels, and choose whether it should ping the configured user.
7. Select **Add Keyword**.

Only this explicit feature can produce a mention. Text copied from FFXIV—including literal `@everyone`, `@here`, user mentions, role mentions, and channel mentions—is neutralized and sent with Discord's `allowed_mentions` protection.

## 6. Test normal chat delivery

For each character:

1. Enable **Say** only.
2. Send `Sentinel Relay Say test` in FFXIV.
3. Confirm it appears only in that character's relay channel.
4. Enable **Free Company** and test again.
5. Disable **Shout**, send a Shout, and verify nothing appears in Discord.
6. Run `/srelay pause`, send an enabled chat message, and verify nothing appears.
7. Run `/srelay resume` and repeat.

Use [LIVE_TEST_CHECKLIST.md](LIVE_TEST_CHECKLIST.md) after updates or configuration changes.

## If a webhook URL leaks

Treat a leaked URL like a leaked password:

1. Open the affected channel's **Integrations → Webhooks**.
2. Delete the affected webhook. Editing its name is not enough.
3. Create a new webhook in that same channel.
4. In FFXIV, run `/srelay` and open **Discord Webhook**.
5. Paste the new URL and select **Replace Webhook**.
6. Test it.

The deleted URL stops working. Other characters' separate webhooks do not need to change.

## Common problems

- **No Integrations/Webhooks option:** your Discord role lacks **Manage Webhooks**, or the channel type does not support the expected incoming-webhook flow.
- **Test returns HTTP 401/404:** the webhook was deleted, regenerated, copied incompletely, or belongs to an unavailable channel. Replace it with a freshly copied URL.
- **Test appears in the wrong channel:** remove the saved webhook from `/srelay`, create a new webhook in the correct channel, and paste it while the correct FFXIV character is active.
- **Normal chat does not appear:** the test must pass, the relay must be resumed, and that exact chat type must be enabled.
- **Keyword highlight appears but no ping:** save the correct Discord User ID and enable **Ping configured user** on the keyword rule.

See [TROUBLESHOOTING.md](TROUBLESHOOTING.md) for diagnostic steps.
