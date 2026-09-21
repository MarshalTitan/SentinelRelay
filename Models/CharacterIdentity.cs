namespace SentinelRelay.Models;

public sealed record CharacterIdentity(
    string CharacterKey,
    string CharacterName,
    string HomeWorld,
    ulong ContentId);

