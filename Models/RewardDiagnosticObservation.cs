namespace SentinelRelay.Models;

public sealed record RewardDiagnosticObservation(
    DateTime TimestampUtc,
    string LogKindName,
    int LogKindValue,
    bool CandidateLogKind,
    string MatchKind,
    string Message,
    IReadOnlyList<uint> NearbyLogMessageIds);
