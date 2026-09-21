import fs from "node:fs";
import http from "node:http";
import os from "node:os";
import path from "node:path";
import { once } from "node:events";
import pino from "pino";
import { WebSocket } from "ws";
import { afterEach, describe, expect, it } from "vitest";
import { RelayDatabase } from "../src/database.js";
import { PluginHub } from "../src/plugin-hub.js";

const temporaryDirectories: string[] = [];

afterEach(() => {
  for (const directory of temporaryDirectories.splice(0)) fs.rmSync(directory, { recursive: true, force: true });
});

describe("plugin WebSocket hub", () => {
  it("pairs once and authenticates a reconnect with the issued token", async () => {
    const directory = fs.mkdtempSync(path.join(os.tmpdir(), "sentinel-relay-hub-test-"));
    temporaryDirectories.push(directory);
    const database = new RelayDatabase(path.join(directory, "relay.db"));
    const hub = new PluginHub(database, pino({ level: "silent" }), false, new Set());
    const server = http.createServer((_request, response) => {
      response.writeHead(404).end();
    });
    hub.attach(server);
    server.listen(0, "127.0.0.1");
    await once(server, "listening");
    const address = server.address();
    if (!address || typeof address === "string") throw new Error("Test server did not expose a TCP port.");
    const endpoint = `ws://127.0.0.1:${address.port}/v1/plugin`;

    const installationId = "11111111-1111-4111-8111-111111111111";
    const hello = {
      installationId,
      protocolVersion: 1,
      pluginVersion: "0.1.0.0",
      characterName: "Wrothy Minioa",
      homeWorld: "Example",
      characterKey: "cid:0000000000000001@example",
      paused: false,
    };

    let first: WebSocket | null = null;
    let second: WebSocket | null = null;
    try {
      first = new WebSocket(endpoint);
      await once(first, "open");
      const helloAckPromise = nextEnvelope(first);
      first.send(JSON.stringify({ type: "hello", requestId: "hello-1", payload: hello }));
      expect((await helloAckPromise).payload).toMatchObject({ linked: false });

      const codePromise = nextEnvelope(first);
      first.send(JSON.stringify({ type: "pair.request", requestId: "pair-1", payload: {} }));
      const codeEnvelope = await codePromise;
      expect(codeEnvelope.type).toBe("pair.code");
      const code = (codeEnvelope.payload as { code: string }).code;

      const completedPromise = nextEnvelope(first);
      const claim = hub.claimPairingCode(code, "100", "wrothy");
      expect(claim.ok).toBe(true);
      const completed = await completedPromise;
      expect(completed.type).toBe("pair.completed");
      const token = (completed.payload as { clientToken: string }).clientToken;
      expect(token.length).toBeGreaterThanOrEqual(40);
      expect(hub.claimPairingCode(code, "100", "wrothy").ok).toBe(false);

      first.close();
      await once(first, "close");
      first = null;

      second = new WebSocket(endpoint);
      await once(second, "open");
      const reconnectAckPromise = nextEnvelope(second);
      second.send(JSON.stringify({
        type: "hello",
        requestId: "hello-2",
        payload: { ...hello, clientToken: token },
      }));
      const reconnectAck = await reconnectAckPromise;
      expect(reconnectAck.type).toBe("hello.ack");
      expect(reconnectAck.payload).toMatchObject({ linked: true, discordUsername: "wrothy" });
      expect(hub.isOnline(installationId)).toBe(true);
    }
    finally {
      first?.close();
      second?.close();
      hub.close();
      await new Promise<void>((resolve) => server.close(() => resolve()));
      database.close();
    }
  });
});

function nextEnvelope(socket: WebSocket): Promise<{ type: string; payload: unknown }> {
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => reject(new Error("Timed out waiting for WebSocket message.")), 2_000);
    socket.once("message", (data) => {
      clearTimeout(timeout);
      try {
        resolve(JSON.parse(data.toString("utf8")) as { type: string; payload: unknown });
      }
      catch (error) {
        reject(error);
      }
    });
  });
}
