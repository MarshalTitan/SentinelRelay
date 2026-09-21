import { createHash, randomBytes, timingSafeEqual } from "node:crypto";

const pairingAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

export function sha256(value: string): string {
  return createHash("sha256").update(value, "utf8").digest("hex");
}

export function tokenMatches(token: string, expectedHash: string): boolean {
  const actual = Buffer.from(sha256(token), "hex");
  const expected = Buffer.from(expectedHash, "hex");
  return actual.length === expected.length && timingSafeEqual(actual, expected);
}

export function createClientToken(): string {
  return randomBytes(32).toString("base64url");
}

export function createPairingCode(): string {
  const bytes = randomBytes(8);
  const characters = Array.from(bytes, (value) => pairingAlphabet[value % pairingAlphabet.length]);
  return `${characters.slice(0, 4).join("")}-${characters.slice(4).join("")}`;
}

export function normalizePairingCode(value: string): string {
  const compact = value.toUpperCase().replace(/[^A-Z0-9]/g, "");
  return compact.length === 8 ? `${compact.slice(0, 4)}-${compact.slice(4)}` : compact;
}

export function sanitizeDiscordMessage(value: string): string {
  let result = "";
  let previousSpace = false;
  for (const character of value.normalize("NFC")) {
    const codePoint = character.codePointAt(0) ?? 0;
    const isControl = (codePoint >= 0 && codePoint < 32) || (codePoint >= 127 && codePoint <= 159);
    if (isControl || /\s/u.test(character)) {
      if (result.length > 0 && !previousSpace) result += " ";
      previousSpace = true;
      continue;
    }
    result += character;
    previousSpace = false;
  }
  return result.trim();
}

export function validateOutboundMessage(value: string): string | null {
  const text = sanitizeDiscordMessage(value);
  if (!text) return "Message cannot be empty.";
  if ([...text].length > 180) return "Message exceeds 180 characters.";
  if (Buffer.byteLength(text, "utf8") > 400) return "Message exceeds 400 UTF-8 bytes.";
  return null;
}

