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
8. Open **Chat Filters** and intentionally enable the desired channels. All filters begin off. **Rewards / Hunt Results** is an optional inbound-only system feed and cannot send anything back into FFXIV.

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

## 7. Optional Discord replies

The direct FFXIV → Discord webhook works without a bot. The steps below are needed only for Discord → FFXIV chat replies and the experimental `/screenshot` control.

This feature does **not** register Discord slash commands. `/fc hi`, `/party hi`, `/screenshot`, and the other supported prefixes are ordinary messages posted in the private text channel. Do not configure BotGhost or another bot to own them for Sentinel Relay.

### A. Restrict the existing bot to the relay channel

The existing Sentinel Relay Discord application may be used. In each intended relay channel:

1. Open **Edit Channel → Permissions**.
2. Add the **Sentinel Relay** bot or its bot role.
3. Allow **View Channel**.
4. Allow **Read Message History**.
5. Do not grant **Administrator** for this feature.
6. Do not grant Send Messages, Manage Messages, Manage Channels, or other permissions unless a separate feature genuinely needs them.
7. Ensure the bot cannot view the other character's relay channel unless that other client is intentionally configured for it.

The reader uses Discord REST only; it does not open a Gateway session. Two game clients can therefore poll their separate channel IDs without duplicate Gateway command handling.

### B. Enable the one required privileged intent

Discord otherwise omits ordinary message content from bot API responses.

1. Go to <https://discord.com/developers/applications>.
2. Open the existing **Sentinel Relay** application.
3. Select **Bot** in the left sidebar.
4. Find **Privileged Gateway Intents**.
5. Enable **Message Content Intent**.
6. Save changes if Discord shows a save button.

Presence Intent and Server Members Intent are not required. Although the reader does not use a Gateway connection, Discord applies the message-content access rule to the bot's message data.

### C. Copy the bot token securely

1. On the application's **Bot** page, select **Reset Token** only if the current token is unavailable or may have leaked.
2. Copy the token once.
3. Never paste it into GitHub, a Discord message, a screenshot, a support ticket, `repo.json`, or a log.
4. Treat it as more sensitive than a webhook URL: the token represents the bot wherever it has permissions.
5. If it leaks, return to the Bot page, reset it immediately, and replace the saved credential on each intended client.

Sentinel Relay masks the token and stores it with Windows DPAPI under the current Windows account. It cannot recover or display the plaintext token later.

### D. Copy the channel and user IDs

1. In Discord, open **User Settings → Advanced** and enable **Developer Mode**.
2. Right-click the intended private relay channel and select **Copy Channel ID**.
3. Right-click the one Discord account allowed to control that character and select **Copy User ID**.
4. Do not use channel names, display names, or usernames; the plugin requires the numeric IDs.

### E. Configure one character profile

While the intended FFXIV character is logged in:

1. Run `/srelay`.
2. Confirm the active character shown in the header.
3. Open **Discord Replies**.
4. Paste the Discord bot token.
5. Paste that character's **Relay Channel ID**.
6. Paste the single **Authorized Discord User ID**.
7. Select only the outbound chat destinations this character may use. These permissions are separate from the FFXIV → Discord **Chat Filters**.
8. To test window capture, separately check **Allow authorized /screenshot window capture**. Leave it off if this character should never be remotely captured.
9. Check **Enable Discord → FFXIV Replies**. This master reader switch is also required for `/screenshot`.
10. Select **Save Reply Settings**.
11. Select **Test Discord Reader** and wait for the success notice in FFXIV chat.

Saving or starting the reader establishes a checkpoint at the newest current Discord message. Old commands are not executed.

### F. Prove the real round trip

1. Keep the configured character logged in and confirm the reader shows **Connected**.
2. From the authorized Discord account, post exactly:

   ```text
   /fc hi
   ```

3. On a second FFXIV character/account in the same Free Company, verify that `hi` appears as a real Free Company message from the configured character.
4. The normal webhook relay should then post that real outgoing FC event back into Discord once. Its webhook-authored message is ignored by the reader.

`IChatGui.Print()` output is not proof. A second client/account must see the server-side FC message.

Configure a second character separately with its own channel ID and authorized user ID. Never copy one character's channel ID into the other character's profile.

### G. Supported ordinary-message commands

- `/say message` or `/s message`
- `/yell message` or `/y message`
- `/shout message` or `/sh message`
- `/fc message`
- `/party message` or `/p message` — also uses Cross-world Party when that is the active party type
- `/alliance message` or `/a message`
- `/pvpteam message`
- `/novice message` or `/n message`
- `/ls1 message` through `/ls8 message`
- `/cwls1 message` through `/cwls8 message`
- `/r message` — replies to the latest incoming Tell

For `/r`, the active character must have received a Tell during the current plugin session within the previous 30 minutes, and **Allow /r** must be enabled. Sentinel Relay sends only the fixed FFXIV `/reply` operation; Discord cannot provide a Tell target or execute `/tell`, `/logout`, macros, plugins, or other arbitrary commands.

### H. Experimental `/screenshot` control

This is a Sentinel Relay control, not an FFXIV command and not a registered Discord slash command.

1. Keep the intended character logged in and restore the FFXIV window if it is minimized.
2. Confirm **Enable Discord → FFXIV Replies** and **Allow authorized /screenshot window capture** are both saved for that character.
3. From the exact authorized account in the exact configured relay channel, post:

   ```text
   /screenshot
   ```

4. FFXIV prints a local notice when the authorized request is accepted.
5. Discord should receive `【SCREENSHOT】 Character Name` followed by a PNG of that FFXIV client only.
6. Wait at least 15 seconds before requesting another screenshot.

No new Discord permission is required beyond the reader's existing **View Channel**, **Read Message History**, and Message Content Intent. The image is uploaded by the character-specific webhook, not by a bot Send Messages permission. The plugin captures and encodes in memory and does not create a persistent image file.

Minimized FFXIV windows are deliberately rejected. If a restored game returns a blank-frame error, try borderless-windowed or windowed mode and report the graphics mode and GPU/driver details; Sentinel Relay will never substitute a whole-desktop capture.

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
- **Reader test returns HTTP 401:** the bot token is invalid or was reset. Replace it in the character profile.
- **Reader test returns HTTP 403:** the bot lacks View Channel or Read Message History in that exact channel.
- **Reader connects but a command is ignored:** verify Message Content Intent, the exact numeric channel/user IDs, the master reply toggle, that command's destination permission, the active FFXIV character, and the literal command prefix. For `/r`, receive a fresh Tell first.
- **`/screenshot` is ignored:** verify its separate opt-in, the master reader, exact user/channel IDs, configured webhook, 15-second cooldown, and that the game is not minimized.

See [TROUBLESHOOTING.md](TROUBLESHOOTING.md) for diagnostic steps.
