import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { afterEach, describe, expect, it } from "vitest";
import { RelayDatabase } from "../src/database.js";

const temporaryDirectories: string[] = [];

afterEach(() => {
  for (const directory of temporaryDirectories.splice(0)) fs.rmSync(directory, { recursive: true, force: true });
});

function database(): RelayDatabase {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), "sentinel-relay-test-"));
  temporaryDirectories.push(directory);
  return new RelayDatabase(path.join(directory, "relay.db"));
}

describe("relay database", () => {
  it("stores only a token hash and verifies the original token", () => {
    const db = database();
    const link = db.createLink({
      installationId: "11111111-1111-4111-8111-111111111111",
      token: "secret-token-that-is-long-enough",
      characterKey: "cid:0000000000000001",
      characterName: "Wrothy Minioa",
      homeWorld: "Example",
      discordUserId: "100",
      discordUsername: "wrothy",
    });
    expect(link.tokenHash).not.toContain("secret-token");
    expect(db.verifyInstallation(link.installationId, "secret-token-that-is-long-enough")?.discordUserId).toBe("100");
    expect(db.verifyInstallation(link.installationId, "wrong-token")).toBeNull();
    db.close();
  });

  it("keeps Wrothy and Elektra routing isolated", () => {
    const db = database();
    const wrothy = db.createLink({
      installationId: "11111111-1111-4111-8111-111111111111",
      token: "wrothy-token-that-is-long-enough",
      characterKey: "cid:1",
      characterName: "Wrothy Minioa",
      homeWorld: "Example",
      discordUserId: "100",
      discordUsername: "wrothy",
    });
    const elektra = db.createLink({
      installationId: "22222222-2222-4222-8222-222222222222",
      token: "elektra-token-that-is-long-enough",
      characterKey: "cid:2",
      characterName: "Elektra Minoa",
      homeWorld: "Example",
      discordUserId: "200",
      discordUsername: "elektra",
    });
    db.setChannel(wrothy.installationId, "guild", "wrothy-channel", "relay-wrothy");
    db.setChannel(elektra.installationId, "guild", "elektra-channel", "relay-elektra");
    expect(db.findByDiscordUser("100")?.channelId).toBe("wrothy-channel");
    expect(db.findByDiscordUser("200")?.channelId).toBe("elektra-channel");
    expect(db.unlinkByDiscordUser("100")?.characterName).toBe("Wrothy Minioa");
    expect(db.findByDiscordUser("200")?.characterName).toBe("Elektra Minoa");
    db.close();
  });

  it("persists pause and channel configuration", () => {
    const db = database();
    const link = db.createLink({
      installationId: "33333333-3333-4333-8333-333333333333",
      token: "another-token-that-is-long-enough",
      characterKey: "cid:3",
      characterName: "Test Character",
      homeWorld: "Example",
      discordUserId: "300",
      discordUsername: "test",
    });
    db.setPaused(link.installationId, true);
    db.setChannel(link.installationId, "guild", "channel", "relay-test");
    const updated = db.findByInstallation(link.installationId)!;
    expect(updated.paused).toBe(true);
    expect(updated.channelName).toBe("relay-test");
    db.close();
  });
});

