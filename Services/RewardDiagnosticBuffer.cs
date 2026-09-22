using SentinelRelay.Core;
using SentinelRelay.Models;

namespace SentinelRelay.Services;

public sealed class RewardDiagnosticBuffer(int capacity = 20)
{
    private static readonly TimeSpan CorrelationWindow = TimeSpan.FromSeconds(2);
    private readonly object gate = new();
    private readonly int capacity = Math.Max(1, capacity);
    private readonly List<(DateTime TimestampUtc, uint Id)> rawLogMessages = [];
    private readonly List<RewardDiagnosticObservation> observations = [];

    public void RecordLogMessage(uint id, DateTime utcNow)
    {
        lock (gate)
        {
            rawLogMessages.Add((utcNow, id));
            TrimRaw(utcNow);
            if (rawLogMessages.Count > 128)
                rawLogMessages.RemoveRange(0, rawLogMessages.Count - 128);
            for (var index = 0; index < observations.Count; index++)
            {
                var observation = observations[index];
                if (Math.Abs((utcNow - observation.TimestampUtc).TotalSeconds) > CorrelationWindow.TotalSeconds
                    || observation.NearbyLogMessageIds.Contains(id))
                    continue;
                observations[index] = observation with
                {
                    NearbyLogMessageIds = observation.NearbyLogMessageIds
                        .Append(id)
                        .TakeLast(16)
                        .ToArray(),
                };
            }
        }
    }

    public void RecordChatMessage(
        string logKindName,
        int logKindValue,
        bool candidateLogKind,
        RewardLineKind matchKind,
        string message,
        DateTime utcNow)
    {
        lock (gate)
        {
            TrimRaw(utcNow);
            var nearbyIds = rawLogMessages
                .Where(item => Math.Abs((utcNow - item.TimestampUtc).TotalSeconds) <= CorrelationWindow.TotalSeconds)
                .Select(item => item.Id)
                .Distinct()
                .TakeLast(16)
                .ToArray();
            observations.Add(new RewardDiagnosticObservation(
                utcNow,
                logKindName,
                logKindValue,
                candidateLogKind,
                matchKind.ToString(),
                MessageSanitizer.SanitizePlainText(message),
                nearbyIds));
            if (observations.Count > capacity)
                observations.RemoveRange(0, observations.Count - capacity);
        }
    }

    public IReadOnlyList<RewardDiagnosticObservation> Snapshot()
    {
        lock (gate)
            return observations.ToArray();
    }

    public void Clear()
    {
        lock (gate)
        {
            rawLogMessages.Clear();
            observations.Clear();
        }
    }

    private void TrimRaw(DateTime utcNow) => rawLogMessages.RemoveAll(
        item => utcNow - item.TimestampUtc > CorrelationWindow);
}
