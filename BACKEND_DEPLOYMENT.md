# Backend deployment

## Recommended host

Use one small Railway service built from this repository's `Dockerfile`, plus one persistent volume mounted at `/data`. Railway terminates HTTPS/TLS, supports long-lived WebSockets, restarts failed containers, and can redeploy from GitHub. That is sufficient for the initial two-character workload; no Redis, managed database, load balancer, or worker fleet is needed.

As of September 2026, Railway's Hobby plan has a $5 monthly minimum that includes $5 of usage. Actual pricing can change, so inspect <https://railway.com/pricing> before subscribing. A tiny always-on Node service plus a minimal volume should be inexpensive, but do not assume it will be permanently free. No purchase is made by this project.

## What the service does

- Maintains Discord's Gateway connection and registers slash commands.
- Accepts authenticated `wss://` connections initiated outward from each Dalamud client.
- Routes one installation only to its own Discord user/channel.
- Stores link/routing metadata in SQLite at `/data/sentinel-relay.db`.
- Does not persist chat messages.

## Prerequisites you must supply

- The published `MarshalTitan/SentinelRelay` GitHub repository
- A Railway account and billing choice
- `DISCORD_TOKEN`, `DISCORD_CLIENT_ID`, and the testing server's `DISCORD_GUILD_ID` from [DISCORD_SETUP.md](DISCORD_SETUP.md)

## Deploy on Railway

1. Sign in at <https://railway.com/> and select **New Project**.
2. Choose **Deploy from GitHub repo**.
3. Authorize Railway to access `MarshalTitan/SentinelRelay` if prompted, then select that repository.
4. Railway detects `railway.toml` and the root `Dockerfile`. The first deployment may fail readiness until Discord secrets are added; this is expected.
5. Open the new service → **Variables** and add:

   ```text
   NODE_ENV=production
   PORT=8080
   DATABASE_PATH=/data/sentinel-relay.db
   DISCORD_TOKEN=YOUR_REAL_BOT_TOKEN
   DISCORD_CLIENT_ID=YOUR_APPLICATION_ID
   DISCORD_GUILD_ID=YOUR_SERVER_ID
   RAILWAY_RUN_UID=0
   ```

6. Add `ALLOWED_INSTALLATION_IDS` only after the plugin has displayed the installation IDs under `/srelay` → **Debug**. It is a comma-separated allowlist. Leave it blank during first pairing; after Wrothy and Elektra are linked, set exactly their two IDs and restart to close enrollment. An incorrect value blocks a client before it can request a code.
7. On the project canvas, right-click and choose **New Volume** (or use the command palette).
8. Attach the volume to the Sentinel Relay service and set its mount path to exactly `/data`.
9. `RAILWAY_RUN_UID=0` is required because Railway mounts volumes as root while the portable image normally runs as the unprivileged `sentinel` user. This Railway-specific runtime override keeps the image non-root on other hosts. The service does not execute user-supplied programs or expose a shell/API.
10. Open **Settings** → **Networking** and select **Generate Domain**.
11. Copy the resulting HTTPS address, such as `https://sentinel-relay-production.up.railway.app`.
12. Add `PUBLIC_BASE_URL` with that exact HTTPS address, without a trailing path, and redeploy.
13. Open `https://YOUR-DOMAIN/ready` in a browser. A healthy deployment returns JSON similar to:

   ```json
   {"status":"ok","version":"0.1.0","discord":"connected"}
   ```

14. In each plugin, set the service URL to the same domain with the WebSocket scheme and path:

   ```text
   wss://YOUR-DOMAIN/v1/plugin
   ```

Railway supplies the TLS certificate. Never use `ws://` over the Internet. The plugin accepts unencrypted `ws://` only for loopback development.

## Secrets and configuration

| Variable | Secret? | Purpose |
|---|:---:|---|
| `DISCORD_TOKEN` | Yes | Authenticates the bot to Discord; server only |
| `DISCORD_CLIENT_ID` | No | Discord Application ID |
| `DISCORD_GUILD_ID` | No | Registers commands immediately in one private server |
| `DATABASE_PATH` | No | Must point inside the persistent volume |
| `PUBLIC_BASE_URL` | No | Documents/validates the service's HTTPS identity |
| `ALLOWED_INSTALLATION_IDS` | No, but private | Optional comma-separated deployment allowlist |
| `PORT` | No | HTTP/WebSocket listen port; Railway normally injects this |

Keep real values only in Railway's variable store. Do not create or commit `service/.env`; the committed `service/.env.example` contains placeholders only. No Discord OAuth client secret or signing key is required by this Gateway-based design.

## Updating and restarting

### Update

1. Merge or push the intended release commit to the repository's deployment branch.
2. Railway auto-deploys the new commit if GitHub autodeploy is enabled. Otherwise open the service's deployment menu and choose **Redeploy**.
3. Watch build/deploy logs for `Sentinel Relay Discord bot connected` and `Sentinel Relay service listening`.
4. Check `/ready` before testing clients.
5. Dalamud clients reconnect automatically with exponential backoff.

### Restart without updating

Open the current deployment's menu and choose **Restart**. Do not remove the volume.

## Logs and privacy

Normal logs contain service lifecycle, installation IDs, command names, Discord user IDs involved in errors, and sanitized error metadata. They intentionally redact tokens and message fields. Chat bodies are not logged by default.

Do not enable platform request-body logging or add debug logging of WebSocket frames in production. Use `/srelay debug`, `/relay status`, `/health`, and `/ready` for diagnostics.

## Backups

The only persistent file is `/data/sentinel-relay.db`. It contains installation IDs, character/world labels, Discord routing IDs, usernames, pause state, token hashes, and timestamps—no chat history and no plaintext client tokens.

Before a major update:

1. Pause both relays or stop the service for a consistent SQLite copy.
2. Use Railway's volume backup/snapshot feature, or download `sentinel-relay.db` using the Railway volume browser/CLI.
3. Store the backup as private security-sensitive metadata.
4. Resume/restart the service.

If the database is lost, create a fresh volume/database and relink each character. No FFXIV or Discord content history is lost because none is stored.

## Local Docker smoke test

Copy the placeholder file only on your own machine and insert test secrets:

```bash
cp service/.env.example service/.env
docker compose up --build
```

The compose port is bound only to loopback. Check `http://127.0.0.1:8080/ready`; for a local plugin connection use `ws://127.0.0.1:8080/v1/plugin`.

Stop with `docker compose down`. Do not run `docker compose down -v` unless you deliberately want to delete all stored links.

## Capacity and future growth

The V1 process is intentionally single-instance because live WebSocket sessions and delivery acknowledgements are kept in memory. SQLite is appropriate for two users and modest expansion. Before horizontal scaling or broad public use, move session coordination/rate limiting to shared infrastructure and migrate links to a managed relational database. Do not start multiple replicas against the same SQLite volume.
