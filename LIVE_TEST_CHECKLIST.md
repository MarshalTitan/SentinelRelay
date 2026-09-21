# Sentinel Relay live-test checklist

Target release: `0.1.0.0`  
Required testers: Wrothy Minioa, Elektra Minoa, and at least one independent FFXIV observer for network-visible outbound verification.

Do not publish the catalog entry as production-ready until every release-blocking item passes. Record date, Dalamud API, FFXIV patch, plugin commit, service commit, and tester initials.

## Install the pre-release build

The catalog is intentionally not updated until the real outbound-chat checks pass. Install the validated CI package as a development plugin on each test PC:

1. Open the successful **Build** workflow run for the release commit and download the `SentinelRelay-0.1.0.0` artifact.
2. Extract the downloaded artifact. It contains `latest.zip`; extract that archive into a stable local folder such as `C:\DalamudDevPlugins\SentinelRelay`.
3. Confirm the folder contains `SentinelRelay.dll`, `SentinelRelay.json`, `SentinelRelay.deps.json`, and `assets\icon.png` together.
4. In FFXIV, run `/xlsettings`, open **Experimental**, and add the full path to `SentinelRelay.dll` under **Dev Plugin Locations**. Dalamud also accepts the containing folder, but selecting the DLL avoids loading unrelated DLLs.
5. Select **Save and Close**, run `/xlplugins`, open **Dev Tools** → **Installed Dev Plugins**, and enable **Sentinel Relay**.
6. Run `/srelay` and verify version `0.1.0.0` before pairing.

After the signed-off release is available through the Sentinel catalog, disable/remove this dev-plugin entry before installing the catalog copy so two copies cannot load.

## Preconditions

- [ ] Backend `/ready` returns HTTP 200 with Discord `connected`.
- [ ] Plugin and backend both report version `0.1.0`/`0.1.0.0` as appropriate.
- [ ] Wrothy and Elektra use separate Discord accounts.
- [ ] `#relay-wrothy` and `#relay-elektra` have private, non-overlapping member permissions.
- [ ] Sentinel Relay bot has only View Channels, Send Messages, and Embed Links.
- [ ] All privileged Discord intents are off.
- [ ] Backend logs do not print secrets or chat bodies.

## A — Wrothy FFXIV → Discord

1. Log in as **Wrothy Minioa** and run `/srelay status`.
2. Enable Say, Free Company, and Shout; leave other filters off.
3. Send, in order:

   ```text
   Say: Sentinel Relay Wrothy Say test A1
   FC: Sentinel Relay Wrothy FC test A2
   Shout: Sentinel Relay Wrothy Shout test A3
   ```

4. Verify three ordered embeds in `#relay-wrothy` with correct labels, sender, readable text, and timestamps.
5. Verify none appears in `#relay-elektra`.

- [ ] PASS

## B — Elektra FFXIV → Discord

1. Log in as **Elektra Minoa** in the independent client/profile.
2. Enable Say, Free Company, and Shout.
3. Send equivalent B1/B2/B3 messages.
4. Verify they appear only in `#relay-elektra`, never `#relay-wrothy`.

- [ ] PASS

## C — Local filtering

1. On Wrothy, disable Shout and keep Say enabled.
2. Send `Sentinel Relay disabled Shout test C1` in Shout.
3. Send `Sentinel Relay enabled Say control C2` in Say.
4. Verify C1 never reaches the backend/Discord and C2 reaches `#relay-wrothy`.

- [ ] PASS

## D — Keyword alert

1. On Wrothy, add keyword `Wrothy`, contains mode, Discord DM, monitoring enabled FC/Say/Shout.
2. From another character, say in an enabled channel: `Wrothy are you coming?`
3. Verify one relay-channel embed and exactly one DM containing keyword, channel, sender, and context.
4. Add a second matching keyword temporarily and repeat; verify one bundled DM, not one DM per keyword.

- [ ] PASS

## E — Discord → real Free Company chat

1. Keep an independent FC member/client watching FC chat.
2. In `#relay-wrothy`, from Wrothy's linked Discord account, run:

   ```text
   /fc message:Sentinel Relay FC test E1
   ```

3. Verify the command receives an ephemeral success only after FFXIV echo.
4. Verify the independent FC member genuinely sees E1 sent by Wrothy Minioa.
5. Verify `#relay-wrothy` shows exactly one outbound confirmation and no loop.

- [ ] PASS — independent observer confirmed

## F — Discord → real Say chat

1. Keep another nearby FFXIV character/client watching Say.
2. In `#relay-wrothy`, run:

   ```text
   /say message:Sentinel Relay Say test F1
   ```

3. Verify the nearby observer genuinely sees F1 from Wrothy.
4. Verify one Discord confirmation and no relay-back duplicate.

- [ ] PASS — independent observer confirmed

## Initial channel verification extension

Repeat inbound and outbound visibility for these minimum V1 channels:

- [ ] Yell inbound/outbound
- [ ] Shout inbound/outbound
- [ ] Party inbound/outbound with another party member
- [ ] Free Company inbound/outbound
- [ ] Say inbound/outbound

Then test memberships that exist on each character:

- [ ] LS 1 using `/ls channel:1 message:Sentinel Relay LS1 test`
- [ ] At least one non-1 LS mapping if configured
- [ ] CWLS 1 using `/cwls channel:1 message:Sentinel Relay CWLS1 test`
- [ ] At least one non-1 CWLS mapping if configured
- [ ] Alliance and PvP Team in valid game contexts, or mark deferred with reason

Tell outbound is not implemented and is not a release criterion.

## G — User and route isolation

1. From Wrothy's Discord account, run an outbound command in `#relay-elektra`; expect rejection.
2. From Elektra's account, run one in `#relay-wrothy`; expect rejection.
3. From an unlinked third account with channel visibility, attempt `/say`; expect `not linked`.
4. Confirm no rejected text reaches either FFXIV client.

- [ ] PASS

## H — Character/client offline

1. Close Wrothy's FFXIV client and wait at least 30 seconds.
2. Confirm `#relay-wrothy` receives one offline status.
3. Attempt `/fc message:Offline test H1`.
4. Verify the bot says Wrothy is offline and does not queue H1.
5. Keep Elektra online and confirm her relay remains functional.

- [ ] PASS

## I — Backend outage

1. Pause or stop the hosted backend for two minutes.
2. Verify both FFXIV clients remain responsive/stable and show Offline/Reconnecting.
3. Send several FFXIV test lines during the outage.
4. Restart the backend.
5. Verify clients reconnect with backoff and no old chat burst appears in Discord.
6. Verify current new chat relays normally.

- [ ] PASS

## J — Reload and persistence

1. Record each character's enabled filters, keyword, pause setting, Discord link, and channel.
2. Restart/reload Dalamud and, if practical, restart FFXIV.
3. Verify per-character local configuration survives.
4. Verify the DPAPI-protected credential reconnects without relinking.
5. Restart the backend without deleting `/data`; verify both links/channels survive.

- [ ] PASS

## Additional safety tests

- [ ] `/srelay pause` immediately blocks both directions and is visually obvious.
- [ ] `/srelay resume` restores both directions without relinking.
- [ ] A 181-character Discord message is rejected.
- [ ] Newline/control-character input cannot inject a second FFXIV command.
- [ ] Six rapid outbound commands trigger rate/queue rejection without a delayed flood.
- [ ] Repeating/replaying an event ID does not duplicate a game send.
- [ ] `/logout`, `/teleport`, `/target`, `/xlplugins`, and arbitrary command names do not exist in Discord and cannot be encoded as a `chatType`.
- [ ] Unicode, item link text, map link text, player links, standard emotes, and custom emotes arrive as safe/readable text or a harmless plain-text approximation.
- [ ] Wrong/expired pairing code fails; used pairing code cannot be reused.
- [ ] `/relay unlink` revokes the plugin session; relinking issues a new credential.
- [ ] Service logs contain no bot token, client token, keyword rule, or message body.

## Final sign-off

- [ ] Wrothy tester sign-off: ____________________ Date: __________
- [ ] Elektra tester sign-off: ___________________ Date: __________
- [ ] Independent FC/Say observer: ______________ Date: __________
- [ ] Release commit/tag recorded: _______________________________
- [ ] Catalog URL/version verified from a clean Dalamud install

Any failure in E, F, G, pause behavior, arbitrary-command prevention, credential isolation, or cross-channel routing blocks release.
