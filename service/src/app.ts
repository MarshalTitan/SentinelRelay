import http from "node:http";
import pino from "pino";
import { loadConfig } from "./config.js";
import { RelayDatabase } from "./database.js";
import { DiscordRelayBot } from "./discord-bot.js";
import { PluginHub } from "./plugin-hub.js";

const config = loadConfig();
const logger = pino({
  level: config.nodeEnv === "production" ? "info" : "debug",
  redact: {
    paths: ["token", "clientToken", "req.headers.authorization", "discordToken", "*.message"],
    censor: "[REDACTED]",
  },
});

const database = new RelayDatabase(config.databasePath);
const hub = new PluginHub(
  database,
  logger,
  config.nodeEnv === "production",
  config.allowedInstallationIds,
);

let bot: DiscordRelayBot | null = null;
if (config.discordConfigured) {
  bot = new DiscordRelayBot(
    database,
    hub,
    logger,
    config.discordToken!,
    config.discordClientId!,
    config.discordGuildId,
  );
  hub.setInboundHandler((link, payload) => bot!.deliverInbound(link, payload));
  hub.setStatusHandler((link, online) => bot!.handleStatus(link, online));
  await bot.start();
} else {
  logger.warn("Discord is not configured; development health checks are available but relay delivery is disabled");
}

const server = http.createServer((request, response) => {
  const path = new URL(request.url ?? "/", "http://relay.invalid").pathname;
  if (request.method === "GET" && (path === "/health" || path === "/ready")) {
    const ready = !config.discordConfigured || Boolean(bot?.isReady());
    response.writeHead(path === "/ready" && !ready ? 503 : 200, { "content-type": "application/json; charset=utf-8" });
    response.end(JSON.stringify({
      status: ready ? "ok" : "not-ready",
      version: process.env.npm_package_version ?? "0.1.0",
      discord: bot?.isReady() ? "connected" : config.discordConfigured ? "connecting" : "disabled",
    }));
    return;
  }
  response.writeHead(404, { "content-type": "application/json; charset=utf-8" });
  response.end(JSON.stringify({ error: "not_found" }));
});

hub.attach(server);
server.listen(config.port, "0.0.0.0", () => {
  logger.info({ port: config.port, websocketPath: "/v1/plugin" }, "Sentinel Relay service listening");
});

let shuttingDown = false;
async function shutdown(signal: string): Promise<void> {
  if (shuttingDown) return;
  shuttingDown = true;
  logger.info({ signal }, "Sentinel Relay shutting down");
  hub.close();
  await bot?.stop();
  await new Promise<void>((resolve) => server.close(() => resolve()));
  database.close();
}

for (const signal of ["SIGINT", "SIGTERM"] as const) {
  process.on(signal, () => {
    void shutdown(signal).finally(() => process.exit(0));
  });
}

