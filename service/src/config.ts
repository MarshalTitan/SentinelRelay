import { z } from "zod";

const environmentSchema = z.object({
  NODE_ENV: z.enum(["development", "test", "production"]).default("development"),
  PORT: z.coerce.number().int().min(1).max(65535).default(8080),
  DATABASE_PATH: z.string().min(1).default("./data/sentinel-relay.db"),
  PUBLIC_BASE_URL: z.string().url().optional(),
  DISCORD_TOKEN: z.string().min(20).optional(),
  DISCORD_CLIENT_ID: z.string().regex(/^\d+$/).optional(),
  DISCORD_GUILD_ID: z.string().regex(/^\d+$/).optional(),
  ALLOWED_INSTALLATION_IDS: z.string().optional(),
});

export type AppConfig = ReturnType<typeof loadConfig>;

export function loadConfig(environment: NodeJS.ProcessEnv = process.env) {
  const parsed = environmentSchema.parse(environment);
  const discordConfigured = Boolean(parsed.DISCORD_TOKEN && parsed.DISCORD_CLIENT_ID);
  if (parsed.NODE_ENV === "production" && !discordConfigured) {
    throw new Error("DISCORD_TOKEN and DISCORD_CLIENT_ID are required in production.");
  }
  if (parsed.PUBLIC_BASE_URL && parsed.NODE_ENV === "production" && !parsed.PUBLIC_BASE_URL.startsWith("https://")) {
    throw new Error("PUBLIC_BASE_URL must use HTTPS in production.");
  }

  return {
    nodeEnv: parsed.NODE_ENV,
    port: parsed.PORT,
    databasePath: parsed.DATABASE_PATH,
    publicBaseUrl: parsed.PUBLIC_BASE_URL,
    discordToken: parsed.DISCORD_TOKEN,
    discordClientId: parsed.DISCORD_CLIENT_ID,
    discordGuildId: parsed.DISCORD_GUILD_ID,
    discordConfigured,
    allowedInstallationIds: new Set(
      (parsed.ALLOWED_INSTALLATION_IDS ?? "")
        .split(",")
        .map((value) => value.trim().toLowerCase())
        .filter(Boolean),
    ),
  } as const;
}

