namespace SentinelRelay.Core;

public sealed class BoundedOrderedQueue<T>(int capacity)
{
    private readonly object gate = new();
    private readonly Queue<T> items = new();

    public int Capacity { get; } = capacity > 0
        ? capacity
        : throw new ArgumentOutOfRangeException(nameof(capacity));

    public int Count
    {
        get
        {
            lock (gate)
                return items.Count;
        }
    }

    public bool TryEnqueue(T item)
    {
        lock (gate)
        {
            if (items.Count >= Capacity)
                return false;
            items.Enqueue(item);
            return true;
        }
    }

    public bool TryDequeue(out T item)
    {
        lock (gate)
        {
            if (items.TryDequeue(out var value))
            {
                item = value;
                return true;
            }

            item = default!;
            return false;
        }
    }

    public void Clear()
    {
        lock (gate)
            items.Clear();
    }
}
