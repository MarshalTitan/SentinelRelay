namespace SentinelRelay.Remote;

public static class LocalEmergencyStop
{
    /// <summary>Always stop the owned listener, even when an exclusive state transaction or disk write fails.</summary>
    public static async Task<bool> Run(Func<Task> stop, params Action[] persist)
    {
        var saved = true;
        try
        {
            foreach (var write in persist)
            {
                try { write(); }
                catch { saved = false; }
            }
        }
        finally { await stop(); }
        return saved;
    }
}
