using SentinelRelay.Bridge;
namespace SentinelRelay.Remote;

public sealed record GameBinding(string InstanceId, int ProcessId, long StartedUtcTicks, string Executable, string CharacterKey);
public sealed record RemoteGameStatus(string ProcessStatus, string RelayStatus, string? Character = null, string? World = null,
    uint? Territory = null, string? Map = null, DateTimeOffset? HeartbeatUtc = null,
    [property: System.Text.Json.Serialization.JsonPropertyName("srank")] System.Text.Json.JsonElement? SRank = null,
    string? Profile = null, bool? Vnavmesh = null, bool? ControlAllowed = null, bool? ScreenshotAllowed = null);
public interface IGameBridge
{
    BridgeSnapshot? Read(GameBinding binding);
    Reply Status(GameBinding binding);
    Reply Execute(GameBinding binding, string operation, string? expectedGameEpoch, Func<bool> authorized, CancellationToken cancellation);
}
