# Sentinel Relay live-test checklist

Stable fallback: `0.1.0.1`
Experimental build: `0.2.0.0` development artifact (not the live catalog)
Test with two independent FFXIV character profiles and two private Discord channels. Record the Dalamud API, FFXIV patch, plugin commit, tester, and date.

## Install the live build

1. In FFXIV, run `/xlsettings` and open **Experimental**.
2. Add this URL under **Custom Plugin Repositories**:

   ```text
   https://raw.githubusercontent.com/MarshalTitan/Sentinel/main/repo.json
   ```

3. Save and close settings.
4. Run `/xlplugins`, find **Sentinel Relay** under available plugins, and choose **Install**.
5. For stable one-way checks, confirm version `0.1.0.1`. For the separately installed reply artifact, confirm version `0.2.0.0`. Verify the active character shown in the header.

The plugin runs inside each active FFXIV client and sends directly to Discord. There is no separate PC-hosted relay process.

## Preconditions

- [ ] Two private Discord relay channels exist.
- [ ] Each channel has its own webhook; the two URLs are different.
- [ ] No webhook URL is committed, posted, logged, or visible in screenshots.
- [ ] All chat filters begin off for a fresh character profile.

## A — First character webhook and routing

1. Log into the first character.
2. Run `/srelay` and verify the correct character appears in the header.
3. Paste only that character's webhook, save it, and run **Test Webhook**.
4. Confirm the success message appears only in the first relay channel.
5. Confirm nothing appears in the second relay channel.

- [ ] PASS

## B — First character chat relay

1. Enable Say, Free Company, and Shout only.
2. Send unique messages in this order:

   ```text
   Sentinel Relay Say B1
   Sentinel Relay FC B2
   Sentinel Relay Shout B3
   ```

3. Confirm three ordered messages in the first relay channel with correct labels, sender, readable text, and optional world.
4. Confirm none appears in the second relay channel.

- [ ] PASS

## C — Second character isolation

1. Log into the second character and wait for the `/srelay` header to change.
2. Confirm the profile initially shows its own webhook state and filters, not the first profile's values.
3. Paste only the second webhook, save it, and test it.
4. Confirm the success message appears only in the second relay channel.
5. Enable Party and Yell, leaving Free Company and Shout disabled.
6. Confirm Party and Yell reach only the second relay channel.
7. Confirm Free Company and Shout do not leave the PC.
8. Switch back to the first character and verify its original webhook and filters remain intact.

- [ ] PASS

## D — Sensitive channel opt-in

For a character that can access each chat type:

- [ ] Incoming Tell works only when Incoming Tell is enabled.
- [ ] Outgoing Tell works only when Outgoing Tell is enabled.
- [ ] Party and Cross-world Party are independently selectable.
- [ ] Alliance and PvP Team remain off unless explicitly enabled.
- [ ] LS 1–8 mappings match their selected numbers.
- [ ] CWLS 1–8 mappings match their selected numbers.
- [ ] Novice Network remains off unless explicitly enabled.
- [ ] Standard and Custom Emotes are independently selectable.

## E — Local filtering proof

1. Disable Shout and keep Say enabled.
2. Send `Sentinel Relay disabled Shout E1` in Shout.
3. Send `Sentinel Relay enabled Say E2` in Say.
4. Confirm only E2 reaches Discord.
5. Check `/srelay debug`; no queue activity should result from E1.

- [ ] PASS

## F — Pause and resume

1. Run `/srelay pause`.
2. Send an otherwise enabled message; confirm Discord receives nothing.
3. Run `/srelay status`; confirm **paused**.
4. Run `/srelay resume`, send fresh text, and confirm delivery resumes.

- [ ] PASS

## G — Keyword highlight and optional ping

1. In **Keywords**, save the intended numeric Discord User ID.
2. Add keyword `ready`, contains mode, Free Company, and enable **Ping configured user**.
3. From another character, send `Are you ready?` in Free Company chat.
4. Confirm one relayed chat item includes a keyword alert and pings only the configured user.
5. Add a second matching keyword and repeat; confirm one bundled alert rather than one ping per rule.
6. Test literal `@everyone`, `@here`, `<@user>`, `<@&role>`, and `<#channel>` text; confirm none creates an unintended mention.

- [ ] PASS

## H — Formatting, sanitization, and duplicates

- [ ] Compact text mode displays a clean `【CHANNEL】 Sender: message` form.
- [ ] Embed mode displays channel/sender, message, and color cleanly on desktop and mobile.
- [ ] No embed timestamp or timestamp footer is present; only Discord's native message timestamp remains.
- [ ] Unicode survives in readable form.
- [ ] Item, map, and player links become harmless readable plain text.
- [ ] Control characters do not create malformed Discord output.
- [ ] One duplicated chat event is not posted twice.
- [ ] Distinct messages remain in original order.

## I — Outage and rate behavior

1. Temporarily disconnect Internet access.
2. Confirm FFXIV remains responsive and Sentinel Relay reports an error without freezing.
3. Restore connectivity and use **Test Webhook**.
4. Confirm the queue does not dump messages older than two minutes.
5. During a normal chat burst, confirm message order is maintained and Discord is not hammered with rapid retries.

- [ ] PASS

## J — Restart and persistence

1. Record both character profiles' webhook state, filters, formatting, keyword rules, and pause state.
2. Restart or reload Dalamud and, if practical, restart FFXIV.
3. Verify each character reloads only its own saved configuration.
4. Confirm neither saved webhook URL is visible in UI, status, debug, or logs.

- [ ] PASS

## K — Experimental reader setup (first profile only)

Do not enable the second profile yet.

1. Give the Sentinel Relay bot only **View Channel** and **Read Message History** in the first private relay channel.
2. Enable the bot application's **Message Content Intent**; do not enable Presence or Server Members intents for this feature.
3. In `/srelay` → **Experimental Replies**, paste the protected bot token, first channel ID, and first authorized user ID.
4. Enable **Allow /fc** and **Discord → FFXIV Replies**, then save.
5. Select **Test Discord Reader** and confirm success.
6. Confirm the reader reports **Connected** and the checkpoint reports **established**.

- [ ] PASS

## L — Real `/fc` proof

1. Keep the first configured character logged in.
2. From the exact authorized Discord account in the exact first relay channel, post the ordinary message:

   ```text
   /fc hi
   ```

3. On a second FFXIV account/client in the same Free Company, confirm a real FC message `hi` from the configured character.
4. Confirm the natural outgoing-game webhook relay appears once in Discord.
5. Confirm the webhook post does not trigger another game send.

Local plugin output is not proof. The second FFXIV client must see the server-side message.

- [ ] PASS

## M — Authorization and replay rejection

- [ ] The same `/fc hi` message ID executes at most once.
- [ ] An `/fc` message posted while FFXIV/replies are off does not execute after restart/resume.
- [ ] A different Discord user is ignored.
- [ ] The same authorized user in a different channel is ignored.
- [ ] `/say hi`, `/logout`, and empty `/fc` are ignored.
- [ ] Bot and webhook posts are ignored.
- [ ] Pausing stops both directions and clears the outgoing queue.
- [ ] A burst above the local limit is dropped rather than spammed into FFXIV.

## N — Second profile isolation

Only after section L passes for the first profile:

1. Configure the second profile with its own relay channel ID and authorized Discord user ID.
2. Test `/fc second profile isolation` from the second authorized account/channel.
3. Confirm only the second FFXIV character sends it.
4. Post `/fc wrong route` in each wrong channel/user combination and confirm neither profile sends it.

- [ ] PASS

## Final sign-off

- [ ] First tester: ____________________ Date: __________
- [ ] Second tester: ___________________ Date: __________
- [ ] Release commit: ___________________________________
- [ ] Release workflow run: ______________________________
- [ ] Catalog URL/version verified from a clean Dalamud install

Failures in routing isolation, disabled-filter privacy, pause behavior, webhook/bot-token masking, mention safety, checkpointing, or real server-side FC delivery require a follow-up fix. Until the `/fc` proof passes, keep `0.1.0.1` as the live catalog fallback.
