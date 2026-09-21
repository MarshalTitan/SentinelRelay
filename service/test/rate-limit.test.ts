import { describe, expect, it } from "vitest";
import { ExpiringDeduplicator, SlidingWindowRateLimiter } from "../src/rate-limit.js";

describe("rate and replay protection", () => {
  it("limits each identity independently", () => {
    const limiter = new SlidingWindowRateLimiter(2, 1000);
    expect(limiter.acquire("wrothy", 0).allowed).toBe(true);
    expect(limiter.acquire("wrothy", 1).allowed).toBe(true);
    expect(limiter.acquire("wrothy", 2).allowed).toBe(false);
    expect(limiter.acquire("elektra", 2).allowed).toBe(true);
    expect(limiter.acquire("wrothy", 1001).allowed).toBe(true);
  });

  it("rejects replayed IDs until retention expires", () => {
    const guard = new ExpiringDeduplicator(1000);
    expect(guard.accept("event", 0)).toBe(true);
    expect(guard.accept("event", 999)).toBe(false);
    expect(guard.accept("event", 1000)).toBe(true);
  });
});

