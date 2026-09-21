# Sentinel Relay live-test checklist

Target release: `0.1.0.0`  
Required testers: Wrothy Minioa and Elektra Minoa

Do not publish the catalog entry until every release-blocking item passes. Record the Dalamud API, FFXIV patch, plugin commit, tester, and date.

## Install the pre-release build

The catalog is intentionally not updated until this checklist passes:

1. Open the successful **Build** workflow run for the candidate commit.
2. Download the `SentinelRelay-0.1.0.0` artifact.
3. Extract the downloaded artifact; it contains `latest.zip`.
4. Extract `latest.zip` into a stable folder such as `C:\DalamudDevPlugins\SentinelRelay`.
5. Confirm the folder contains `SentinelRelay.dll`, `SentinelRelay.json`, `SentinelRelay.deps.json`, and `assets\icon.png`.
6. In FFXIV, run `/xlsettings` → **Experimental** → **Dev Plugin Locations** and select `SentinelRelay.dll`.
7. Save, run `/xlplugins`, open **Dev Tools** → **Installed Dev Plugins**, and enable **Sentinel Relay**.
8. Run `/srelay` and confirm version `0.1.0.0`.

Remove this development-plugin entry before installing the eventual catalog copy.

## Preconditions

- [ ] `#relay-wrothy` and `#relay-elektra` are private channels.
- [ ] Each channel has its own webhook; the two URLs are not the same.
- [ ] No webhook URL is committed, posted, logged, or visible in screenshots.
- [ ] No Railway/Docker/Node/WebSocket service is running or required.
- [ ] The Discord bot may be offline; webhook tests still work.
- [ ] All chat filters begin off for a fresh character profile.

## A — Wrothy webhook and routing

1. Log in as **Wrothy Minioa**.
2. Run `/srelay` and verify Wrothy appears in the header.
3. Paste only the `#relay-wrothy` webhook, save, and test.
4. Confirm this appears in `#relay-wrothy`:

   ```text
   Sentinel Relay connected successfully for Wrothy Minioa.
   ```

5. Confirm nothing appears in `#relay-elektra`.

- [ ] PASS

## B — Wrothy chat relay

1. Enable Say, Free Company, and Shout only.
2. Send unique messages in this order:

   ```text
   Say: Sentinel Relay Wrothy Say B1
   FC: Sentinel Relay Wrothy FC B2
   Shout: Sentinel Relay Wrothy Shout B3
   ```

3. Confirm three ordered messages in `#relay-wrothy` with correct labels, sender, readable text, and optional world.
4. Confirm none appears in `#relay-elektra`.

- [ ] PASS

## C — Elektra webhook and routing

1. Log in as **Elektra Minoa** and wait for the header to change.
2. Confirm Elektra initially shows her own webhook state and filters—not Wrothy's values.
3. Paste only the `#relay-elektra` webhook, save, and test.
4. Confirm the Elektra success message appears only in `#relay-elektra`.

- [ ] PASS

## D — Elektra independent filters

1. Enable Party and Yell for Elektra, leaving FC and Shout disabled.
2. Send a Party message and a Yell; confirm both reach `#relay-elektra`.
3. Send an FC message and a Shout; confirm neither reaches Discord.
4. Switch back to Wrothy and confirm Wrothy still has Say/FC/Shout enabled and her own webhook.

- [ ] PASS

## E — Sensitive channel opt-in

For a character that can access each chat type:

- [ ] Incoming Tell works only when Incoming Tell is enabled.
- [ ] Outgoing Tell works only when Outgoing Tell is enabled.
- [ ] Party and Cross-world Party are independently selectable.
- [ ] Alliance and PvP Team remain off unless explicitly enabled.
- [ ] LS 1–8 mappings match their selected numbers.
- [ ] CWLS 1–8 mappings match their selected numbers.
- [ ] Novice Network remains off unless explicitly enabled.
- [ ] Standard and Custom Emotes are independently selectable.

## F — Local filtering proof

1. On Wrothy, disable Shout and keep Say enabled.
2. Send `Sentinel Relay disabled Shout F1` in Shout.
3. Send `Sentinel Relay enabled Say F2` in Say.
4. Confirm only F2 reaches Discord.
5. Check `/srelay debug`; no queue activity should result from F1.

- [ ] PASS

## G — Pause/resume

1. Run `/srelay pause`.
2. Send an otherwise enabled message; confirm Discord receives nothing.
3. Run `/srelay status`; confirm **paused**.
4. Run `/srelay resume`, send fresh text, and confirm delivery resumes.

- [ ] PASS

## H — Keyword highlight and optional ping

1. In Wrothy's **Keywords** tab, save Wrothy's numeric Discord User ID.
2. Add keyword `Wrothy`, contains mode, Free Company, **Ping configured user** enabled.
3. From another character, send `Wrothy are you coming?` in FC.
4. Confirm one relayed chat item includes a keyword alert and pings only Wrothy.
5. Add a second matching keyword and repeat; confirm one bundled alert, not one ping per rule.
6. Test literal `@everyone`, `@here`, `<@user>`, `<@&role>`, and `<#channel>` text; confirm none creates an unintended mention.

- [ ] PASS

## I — Formatting, sanitization, and duplicates

- [ ] Compact text mode displays a clean `【CHANNEL】 Sender: message` form.
- [ ] Embed mode displays channel/sender, message, color, and timestamp cleanly on desktop/mobile.
- [ ] Unicode survives in readable form.
- [ ] Item, map, and player links become harmless readable plain text.
- [ ] Control characters do not create malformed Discord output.
- [ ] One duplicated chat event is not posted twice.
- [ ] Distinct messages remain in original order.

## J — Outage and rate behavior

1. Temporarily block/disconnect Internet access or wait for a safe Discord maintenance test window.
2. Confirm FFXIV remains responsive and Sentinel Relay reports an error without freezing.
3. Restore connectivity and use **Test Webhook**.
4. Confirm the queue does not dump messages older than two minutes.
5. During a normal chat burst, confirm message order is maintained and Discord is not hammered with rapid retries.

- [ ] PASS

## K — Restart and persistence

1. Record both characters' webhook state, filters, formatting, keyword rules, and pause state.
2. Restart/reload Dalamud and, if practical, restart FFXIV.
3. Log into Wrothy and verify her settings and webhook test.
4. Log into Elektra and verify her independent settings and webhook test.
5. Confirm neither saved webhook URL is visible in UI, status, debug, or logs.

- [ ] PASS

## L — Scope-removal checks

- [ ] No Discord → FFXIV command exists.
- [ ] No `/srelay link` or `/srelay unlink` exists.
- [ ] No pairing code appears.
- [ ] No bot token is requested.
- [ ] No Railway, Docker, Node, SQLite, Tailscale, port-forwarding, or local service instruction remains.
- [ ] Normal delivery works while the old Sentinel Relay bot is offline.

## Final sign-off

- [ ] Wrothy tester: ____________________ Date: __________
- [ ] Elektra tester: ___________________ Date: __________
- [ ] Candidate commit: __________________________________
- [ ] Release workflow run: _______________________________
- [ ] Catalog URL/version verified from a clean Dalamud install

Failures in routing isolation, disabled-filter privacy, pause behavior, webhook masking, mention safety, or no-backend operation block release.
