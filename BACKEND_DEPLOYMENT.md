# No backend deployment required

Sentinel Relay no longer has or requires a backend service.

## Production architecture

```text
FFXIV / Dalamud
  → Sentinel Relay local filter and queue
  → HTTPS request to Discord's incoming-webhook API
  → private Discord relay channel
```

The optional reply path also runs inside the same Dalamud plugin:

```text
private Discord relay channel
  → outbound HTTPS REST polling by Sentinel Relay
  → local authorization and fixed chat-destination allowlist
  → active FFXIV character
```

There is nothing to deploy to Railway, Render, Fly.io, a VPS, a home server, Docker, Node.js, SQLite, Tailscale, or a router.

## Cost

The Sentinel Relay architecture itself has no hosting charge. It uses:

- the user's existing FFXIV/XIVLauncher/Dalamud installation;
- the Sentinel Relay plugin; and
- Discord's built-in incoming webhooks.

No purchase or hosting signup is required.

## Network behavior

- The plugin makes outbound HTTPS requests only when an enabled FFXIV chat message or explicit webhook test must be delivered.
- If Discord replies are enabled, the plugin also makes small outbound Discord message-history requests about every two seconds while that character is logged in.
- It accepts only Discord webhook URLs on Discord-owned HTTPS hosts.
- It never opens a listening port.
- It does not expose the PC to inbound Internet connections.
- It does not need port forwarding or a fixed public IP address.
- Discord responses, including `429` rate limits, are handled by the background delivery worker.

## Removed components

The earlier prototype's following components have been retired from the production repository:

- Node/TypeScript relay service
- Discord Gateway bot dependency (the optional reader uses REST, not Gateway)
- WebSocket client/server protocol
- Railway configuration
- Docker image and Compose configuration
- SQLite link/routing database
- pairing codes and client authentication tokens
- hosted Discord-to-FFXIV command routing

## Updates

Plugin updates are delivered through the normal Sentinel Dalamud custom repository. There is no server process to update or restart.

If Discord is temporarily unavailable, FFXIV remains unaffected. The plugin retries current transient/rate-limited requests in order, maintains a bounded queue, and drops stale messages rather than dumping hours of old chat later.

For channel/webhook creation, follow [DISCORD_SETUP.md](DISCORD_SETUP.md).
