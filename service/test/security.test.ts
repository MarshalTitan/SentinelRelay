import { describe, expect, it } from "vitest";
import {
  createClientToken,
  createPairingCode,
  normalizePairingCode,
  sanitizeDiscordMessage,
  sha256,
  tokenMatches,
  validateOutboundMessage,
} from "../src/security.js";

describe("security helpers", () => {
  it("creates unambiguous one-time pairing codes", () => {
    const code = createPairingCode();
    expect(code).toMatch(/^[ABCDEFGHJKLMNPQRSTUVWXYZ23456789]{4}-[ABCDEFGHJKLMNPQRSTUVWXYZ23456789]{4}$/);
    expect(normalizePairingCode(code.toLowerCase())).toBe(code);
  });

  it("creates and verifies high-entropy client tokens", () => {
    const token = createClientToken();
    expect(token.length).toBeGreaterThanOrEqual(40);
    expect(tokenMatches(token, sha256(token))).toBe(true);
    expect(tokenMatches(createClientToken(), sha256(token))).toBe(false);
  });

  it("removes line breaks and controls from Discord text", () => {
    expect(sanitizeDiscordMessage(" hello\r\nworld\0  again ")).toBe("hello world again");
  });

  it("enforces game-safe character and UTF-8 limits", () => {
    expect(validateOutboundMessage("hello")).toBeNull();
    expect(validateOutboundMessage(" ")).toMatch(/empty/i);
    expect(validateOutboundMessage("a".repeat(181))).toMatch(/180/);
    expect(validateOutboundMessage("😀".repeat(101))).toMatch(/400/);
  });
});

