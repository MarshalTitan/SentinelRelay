# Discord setup for Sentinel Relay

This guide assumes no prior Discord Developer Portal experience. Do not paste the bot token into FFXIV, Dalamud, GitHub, chat, or any checked-in file. It belongs only in the hosted service's secret-variable screen.

## 1. Create the application

1. Sign in at <https://discord.com/developers/applications> with the Discord account that owns or manages your server.
2. Select **New Application**.
3. Enter **Sentinel Relay**, accept Discord's terms, and select **Create**.
4. On **General Information**:
   - Set the name to **Sentinel Relay**.
   - Upload `assets/icon.png` from this repository as the app icon.
   - Optionally use: `Private two-way FFXIV chat relay for the Sentinel Dalamud ecosystem.`
   - Select **Save Changes**.
5. Copy **Application ID**. This non-secret number becomes `DISCORD_CLIENT_ID` during backend deployment.

Sentinel Relay receives slash commands over Discord's Gateway connection. Leave **Interactions Endpoint URL** blank. The app does not use a public HTTP interactions endpoint or a Discord public key.

## 2. Configure the bot user and token

1. Select **Bot** in the left sidebar. New Discord apps normally already have a bot user.
2. Confirm the bot username is **Sentinel Relay** and upload the same icon if necessary.
3. Under **Privileged Gateway Intents**, leave all three switches **OFF**:
   - Presence Intent: not required
   - Server Members Intent: not required
   - Message Content Intent: not required
4. Under **Token**, select **Reset Token**. Discord may ask for your password or multi-factor authentication.
5. Select **Copy** immediately and place the token in your password manager until the backend is configured. Discord does not show it again.
6. Never send this token to another person. If it is exposed, return to **Bot** → **Reset Token**, update the hosted secret, and restart the service.

The code requests only the standard `Guilds` intent. It does not read normal Discord messages; outbound FFXIV messages arrive only through slash commands.

## 3. Configure installation scopes and permissions

1. Select **Installation** in the left sidebar.
2. Under **Installation Contexts**, enable **Guild Install**. **User Install** is not needed.
3. Under **Install Link**, select **Discord Provided Link**.
4. Under **Default Install Settings** → **Guild Install**, add these OAuth2 scopes:
   - `applications.commands`
   - `bot`
5. Under bot permissions, select only:
   - **View Channels**
   - **Send Messages**
   - **Embed Links**
6. Save changes.

Do not grant Administrator, Manage Server, Manage Channels, Manage Messages, Mention Everyone, or Read Message History. They are not required. Server members who use commands need **Use Application Commands** through their own role or channel permissions; that is a normal member permission, not a bot permission.

## 4. Install the bot into the Discord server

1. Still on **Installation**, copy the **Install Link**.
2. Open the link in a browser.
3. Select **Add to server**, choose your intended server, and select **Continue**.
4. Confirm only the permissions listed above, then select **Authorize**.
5. Verify **Sentinel Relay** appears in the server member list. It will remain offline until the backend starts.

You must have the server's **Manage Server** permission to install an app.

## 5. Create the two private relay channels

The exact names are configurable. This example uses `#relay-wrothy` and `#relay-elektra`.

1. In Discord, create a category named **Sentinel Relay**.
2. On the category permissions, deny **View Channel** to `@everyone`.
3. Create the text channel `#relay-wrothy` inside that category.
4. Add Wrothy's Discord account (or a Wrothy-only role) and allow:
   - View Channel
   - Send Messages
   - Use Application Commands
5. Add the **Sentinel Relay** bot and allow:
   - View Channel
   - Send Messages
   - Embed Links
6. Do not add Elektra's account unless both humans intentionally share visibility. Visibility alone still does not grant character control, but private channels reduce exposure.
7. Create `#relay-elektra` and repeat with Elektra's Discord account. Do not add Wrothy's account unless intentionally desired.

Sentinel Relay also enforces routing server-side: the linked Discord account, linked installation, Discord server, and configured channel must all match before an outbound request is accepted.

## 6. Add the backend secrets

Follow [BACKEND_DEPLOYMENT.md](BACKEND_DEPLOYMENT.md). The relevant values are:

```text
DISCORD_TOKEN=the bot token copied from the Bot page
DISCORD_CLIENT_ID=the Application ID from General Information
DISCORD_GUILD_ID=the numeric ID of your Discord server during initial testing
```

To copy the guild/server ID:

1. In the normal Discord app, open **User Settings** → **Advanced**.
2. Enable **Developer Mode**.
3. Right-click the server icon and select **Copy Server ID**.

`DISCORD_GUILD_ID` makes command registration appear in that server immediately. Keep it for this private two-user deployment. If it is removed, the service registers global commands instead, which can take longer to appear.

Restart/redeploy the service after changing secrets. The bot should become online and these commands should appear: `/relay`, `/fc`, `/say`, `/yell`, `/shout`, `/party`, `/alliance`, `/pvpteam`, `/ls`, and `/cwls`.

## 7. Link Wrothy Minioa

Do this while **Wrothy Minioa** is the active logged-in character on the correct PC/client.

1. In FFXIV, run `/srelay`.
2. In **Advanced**, enter the deployed WebSocket URL exactly: `wss://YOUR-HOST/v1/plugin`.
3. Select **Save and Reconnect** and wait for **ConnectedUnlinked**.
4. Run `/srelay link` or select **Generate Link Code**.
5. Copy the displayed temporary code. It expires after ten minutes and becomes invalid if that client disconnects.
6. From Wrothy's Discord account, run `/relay link code:THE-CODE`.
7. In `#relay-wrothy`, run `/relay channel channel:#relay-wrothy`.
8. Run `/relay status`. It should show **Wrothy Minioa**, online, running, and `#relay-wrothy`.

The names are detected from Dalamud; they are not hard-coded.

## 8. Link Elektra Minoa

Do this from Elektra's own FFXIV session/profile. If both characters use one PC, fully switch to Elektra and wait for the Sentinel Relay status header to show **Elektra Minoa** before linking.

1. Configure the same `wss://YOUR-HOST/v1/plugin` address.
2. Run `/srelay link`.
3. From Elektra's Discord account, run `/relay link code:THE-NEW-CODE`.
4. In `#relay-elektra`, run `/relay channel channel:#relay-elektra`.
5. Run `/relay status` and verify Elektra's character and channel.

Each character profile has a separate installation identifier, protected token, filters, keyword rules, pause state, Discord user, and destination.

After both characters are linked, copy each **Installation ID** from `/srelay` → **Debug**. In the hosted service variables set `ALLOWED_INSTALLATION_IDS` to the two comma-separated IDs and restart, for example:

```text
ALLOWED_INSTALLATION_IDS=11111111-1111-4111-8111-111111111111,22222222-2222-4222-8222-222222222222
```

Use the real IDs shown by the plugin, not those examples. Confirm both reconnect. This closes initial enrollment so an unrelated installation cannot request a pairing code from this deployment.

## 9. Configure chat filters and keyword DMs

For each FFXIV character:

1. Run `/srelay` → **Chat Filters**.
2. Read the privacy warning and enable only the desired channels. Every channel is initially off.
3. Open **Keywords**.
4. Enter `Wrothy` for Wrothy or `Elektra` for Elektra—or select **Use My Character Name**.
5. Leave **Whole word** off for a case-insensitive contains match, or enable it for a token-boundary match.
6. Select the enabled channels that this rule should watch.
7. Choose **Discord DM**, **Relay-channel mention**, or **DM and mention**.
8. Select **Add Keyword**.

If a keyword message matches several rules, Sentinel Relay posts one relay embed and bundles the keyword names into one DM. Repeated identical sender/channel/message alerts are suppressed for 60 seconds while ordinary relay embeds still flow. For DMs to work, the linked user must allow direct messages from server members/apps; if a DM fails, the relay-channel message still posts.

## 10. Test the two directions

### FFXIV → Discord

1. Enable **Say** and **Free Company** for one character.
2. Send `Sentinel Relay incoming test` in Say.
3. Send `Sentinel Relay FC incoming test` in Free Company.
4. Confirm both appear only in that character's configured Discord channel.
5. Disable **Shout**, send a Shout, and confirm it does not appear.

### Discord → FFXIV

Use the linked account inside that character's configured relay channel:

```text
/fc message:Sentinel Relay FC test
/say message:Sentinel Relay Say test
```

The bot first replies ephemerally. It reports success only after the plugin observes FFXIV's real outgoing chat echo. Confirm with another FC member/client and another nearby character; a line printed only on the sending client is not sufficient.

Complete [LIVE_TEST_CHECKLIST.md](LIVE_TEST_CHECKLIST.md) before relying on the system.

## 11. Troubleshoot Discord setup

- **Bot is offline:** the backend is stopped, repeatedly crashing, or has an invalid token. Check hosted logs and `/ready`.
- **Commands do not appear:** confirm `DISCORD_CLIENT_ID` and `DISCORD_GUILD_ID`, restart the service, and verify the bot was installed with `applications.commands`.
- **Missing Access / Missing Permissions:** add View Channel, Send Messages, and Embed Links for the bot in that exact relay channel.
- **Only one user can link:** expected if both attempts came from the same Discord account. V1 permits one character link per Discord user so cross-character control cannot happen accidentally.
- **Keyword channel post works but DM does not:** enable DMs for that server/user and ensure the bot has not been blocked.
- **A token may have leaked:** reset it in the Developer Portal immediately, replace `DISCORD_TOKEN`, and redeploy.
- **Character shows offline:** the plugin is not authenticated to the same backend, FFXIV is closed, the character switched, or its WebSocket is reconnecting.

See [TROUBLESHOOTING.md](TROUBLESHOOTING.md) for service and plugin diagnostics.

## 12. Update or restart

- To restart only, use the host's **Restart** action. Do not delete the persistent `/data` volume.
- To update from GitHub, deploy the new commit and watch `/ready` plus service logs until Discord reconnects.
- A normal restart retains all links because the SQLite database lives at `/data/sentinel-relay.db`.
- Before a risky update, make a copy/snapshot of the `/data` volume. Chat history is not stored there—only link and routing metadata.
