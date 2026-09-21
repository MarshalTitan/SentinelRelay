namespace SentinelRelay.Core;

public sealed class SlidingWindowRateLimiter(int limit, TimeSpan window)
{
    private readonly Queue<DateTime> attempts = new();

    public bool TryAcquire(DateTime utcNow, out TimeSpan retryAfter)
    {
        while (attempts.TryPeek(out var oldest) && utcNow - oldest >= window)
            attempts.Dequeue();

        if (attempts.Count >= limit)
        {
            retryAfter = window - (utcNow - attempts.Peek());
            return false;
        }

        attempts.Enqueue(utcNow);
        retryAfter = TimeSpan.Zero;
        return true;
    }

    public void Clear() => attempts.Clear();
}
