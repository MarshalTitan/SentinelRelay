import fs from "node:fs";
import path from "node:path";
import Database from "better-sqlite3";
import { sha256, tokenMatches } from "./security.js";

export interface LinkRecord {
  installationId: string;
  tokenHash: string;
  characterKey: string;
  characterName: string;
  homeWorld: string;
  discordUserId: string;
  discordUsername: string;
  guildId: string | null;
  channelId: string | null;
  channelName: string | null;
  paused: boolean;
  createdAt: string;
  updatedAt: string;
  lastSeenAt: string | null;
}

type LinkRow = {
  installation_id: string;
  token_hash: string;
  character_key: string;
  character_name: string;
  home_world: string;
  discord_user_id: string;
  discord_username: string;
  guild_id: string | null;
  channel_id: string | null;
  channel_name: string | null;
  paused: number;
  created_at: string;
  updated_at: string;
  last_seen_at: string | null;
};

export class RelayDatabase {
  private readonly database: Database.Database;

  public constructor(filename: string) {
    fs.mkdirSync(path.dirname(path.resolve(filename)), { recursive: true });
    this.database = new Database(filename);
    this.database.pragma("journal_mode = WAL");
    this.database.pragma("foreign_keys = ON");
    this.database.pragma("busy_timeout = 5000");
    this.migrate();
  }

  public close(): void {
    this.database.close();
  }

  public findByInstallation(installationId: string): LinkRecord | null {
    const row = this.database.prepare("SELECT * FROM links WHERE installation_id = ?").get(installationId) as LinkRow | undefined;
    return row ? mapRow(row) : null;
  }

  public findByDiscordUser(discordUserId: string): LinkRecord | null {
    const row = this.database.prepare("SELECT * FROM links WHERE discord_user_id = ?").get(discordUserId) as LinkRow | undefined;
    return row ? mapRow(row) : null;
  }

  public verifyInstallation(installationId: string, token: string): LinkRecord | null {
    const link = this.findByInstallation(installationId);
    return link && tokenMatches(token, link.tokenHash) ? link : null;
  }

  public createLink(input: {
    installationId: string;
    token: string;
    characterKey: string;
    characterName: string;
    homeWorld: string;
    discordUserId: string;
    discordUsername: string;
  }): LinkRecord {
    const now = new Date().toISOString();
    this.database.prepare(`
      INSERT INTO links (
        installation_id, token_hash, character_key, character_name, home_world,
        discord_user_id, discord_username, created_at, updated_at, last_seen_at
      ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
      ON CONFLICT(installation_id) DO UPDATE SET
        token_hash = excluded.token_hash,
        character_key = excluded.character_key,
        character_name = excluded.character_name,
        home_world = excluded.home_world,
        discord_user_id = excluded.discord_user_id,
        discord_username = excluded.discord_username,
        updated_at = excluded.updated_at,
        last_seen_at = excluded.last_seen_at
    `).run(
      input.installationId,
      sha256(input.token),
      input.characterKey,
      input.characterName,
      input.homeWorld,
      input.discordUserId,
      input.discordUsername,
      now,
      now,
      now,
    );
    return this.findByInstallation(input.installationId)!;
  }

  public touchInstallation(installationId: string, characterName: string, homeWorld: string, paused: boolean): void {
    const now = new Date().toISOString();
    this.database.prepare(`
      UPDATE links SET character_name = ?, home_world = ?, paused = ?, updated_at = ?, last_seen_at = ?
      WHERE installation_id = ?
    `).run(characterName, homeWorld, paused ? 1 : 0, now, now, installationId);
  }

  public setChannel(installationId: string, guildId: string, channelId: string, channelName: string): LinkRecord {
    this.database.prepare(`
      UPDATE links SET guild_id = ?, channel_id = ?, channel_name = ?, updated_at = ? WHERE installation_id = ?
    `).run(guildId, channelId, channelName, new Date().toISOString(), installationId);
    return this.findByInstallation(installationId)!;
  }

  public setPaused(installationId: string, paused: boolean): void {
    this.database.prepare("UPDATE links SET paused = ?, updated_at = ? WHERE installation_id = ?")
      .run(paused ? 1 : 0, new Date().toISOString(), installationId);
  }

  public unlinkByInstallation(installationId: string): LinkRecord | null {
    const existing = this.findByInstallation(installationId);
    if (existing) this.database.prepare("DELETE FROM links WHERE installation_id = ?").run(installationId);
    return existing;
  }

  public unlinkByDiscordUser(discordUserId: string): LinkRecord | null {
    const existing = this.findByDiscordUser(discordUserId);
    if (existing) this.database.prepare("DELETE FROM links WHERE discord_user_id = ?").run(discordUserId);
    return existing;
  }

  private migrate(): void {
    this.database.exec(`
      CREATE TABLE IF NOT EXISTS links (
        installation_id TEXT PRIMARY KEY,
        token_hash TEXT NOT NULL,
        character_key TEXT NOT NULL,
        character_name TEXT NOT NULL,
        home_world TEXT NOT NULL,
        discord_user_id TEXT NOT NULL UNIQUE,
        discord_username TEXT NOT NULL,
        guild_id TEXT,
        channel_id TEXT,
        channel_name TEXT,
        paused INTEGER NOT NULL DEFAULT 0 CHECK (paused IN (0, 1)),
        created_at TEXT NOT NULL,
        updated_at TEXT NOT NULL,
        last_seen_at TEXT
      );
      CREATE INDEX IF NOT EXISTS idx_links_channel ON links(guild_id, channel_id);
    `);
  }
}

function mapRow(row: LinkRow): LinkRecord {
  return {
    installationId: row.installation_id,
    tokenHash: row.token_hash,
    characterKey: row.character_key,
    characterName: row.character_name,
    homeWorld: row.home_world,
    discordUserId: row.discord_user_id,
    discordUsername: row.discord_username,
    guildId: row.guild_id,
    channelId: row.channel_id,
    channelName: row.channel_name,
    paused: row.paused === 1,
    createdAt: row.created_at,
    updatedAt: row.updated_at,
    lastSeenAt: row.last_seen_at,
  };
}

