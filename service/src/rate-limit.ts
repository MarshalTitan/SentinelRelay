export class SlidingWindowRateLimiter {
  private readonly attempts = new Map<string, number[]>();

  public constructor(
    private readonly limit: number,
    private readonly windowMs: number,
  ) {}

  public acquire(key: string, now = Date.now()): { allowed: true } | { allowed: false; retryAfterMs: number } {
    const cutoff = now - this.windowMs;
    const recent = (this.attempts.get(key) ?? []).filter((timestamp) => timestamp > cutoff);
    if (recent.length >= this.limit) {
      this.attempts.set(key, recent);
      return { allowed: false, retryAfterMs: Math.max(1, recent[0]! + this.windowMs - now) };
    }
    recent.push(now);
    this.attempts.set(key, recent);
    return { allowed: true };
  }

  public clear(key: string): void {
    this.attempts.delete(key);
  }
}

export class ExpiringDeduplicator {
  private readonly entries = new Map<string, number>();

  public constructor(private readonly retentionMs: number, private readonly capacity = 2048) {}

  public accept(key: string, now = Date.now()): boolean {
    for (const [entry, expiresAt] of this.entries) {
      if (expiresAt <= now) this.entries.delete(entry);
    }
    if (this.entries.has(key)) return false;
    if (this.entries.size >= this.capacity) {
      const oldest = this.entries.keys().next().value as string | undefined;
      if (oldest) this.entries.delete(oldest);
    }
    this.entries.set(key, now + this.retentionMs);
    return true;
  }
}

