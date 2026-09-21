import { describe, expect, it } from "vitest";
import { inboundChatSchema, outboundChatTypes } from "../src/protocol.js";

describe("relay protocol", () => {
  it("never exposes Tell, emotes, or Novice Network outbound", () => {
    expect(outboundChatTypes).not.toContain("Tell");
    expect(outboundChatTypes).not.toContain("StandardEmote");
    expect(outboundChatTypes).not.toContain("CustomEmote");
    expect(outboundChatTypes).not.toContain("NoviceNetwork");
  });

  it("accepts a bounded structured inbound chat event", () => {
    const parsed = inboundChatSchema.parse({
      eventId: "a".repeat(32),
      chatType: "FreeCompany",
      channelLabel: "FC",
      sender: "Player Name",
      senderWorld: "Example",
      message: "Maps tonight?",
      timestampUtc: new Date().toISOString(),
      keywordMatches: [{ keyword: "maps", alertMethod: "DirectMessage" }],
    });
    expect(parsed.chatType).toBe("FreeCompany");
  });
});

