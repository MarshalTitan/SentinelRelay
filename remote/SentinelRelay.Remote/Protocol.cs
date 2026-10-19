using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SentinelRelay.Remote;

public static class Protocol
{
    public const int Version = 1;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };
    public static string Secret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static bool IsSecret(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    public static bool Matches(string hash, string? secret) => IsSecret(secret) &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hash), Encoding.ASCII.GetBytes(Hash(secret!)));
}

public sealed record Enrollment(int Version, string Invitation);
public sealed record SessionRequest(int Version, string DeviceId);
public sealed record RemoteRequest(int Version, long Sequence, string Operation, string? InstanceId = null, string? Confirmation = null);
public sealed record Reply(bool Success, string Code, object? Data = null);
public sealed record DeviceCredential(string DeviceId, string DeviceToken);
public sealed record SessionCredential(string SessionToken, DateTimeOffset Expires, int ProtocolVersion = Protocol.Version);
public sealed record InstanceInfo(string InstanceId, string Label, string RelayStatus = "bridge_unavailable");

public sealed class RemoteState
{
    public int Version { get; set; } = Protocol.Version;
    public string HostId { get; set; } = Guid.NewGuid().ToString("N");
    public string Epoch { get; set; } = Guid.NewGuid().ToString("N");
    public bool Enabled { get; set; }
    public List<InstanceInfo> Instances { get; set; } = [];
    public List<Device> Devices { get; set; } = [];
    public List<Invitation> Invitations { get; set; } = [];
}
public sealed record Device(string Id, string Label, string TokenHash, string[] Instances, bool Control, bool Revoked = false);
public sealed record Invitation(string Hash, DateTimeOffset Expires, string Label, string[] Instances, bool Control);
public sealed record AuditEvent(DateTimeOffset At, string Code, string? DeviceId = null, string? InstanceId = null);

public interface IStateStore
{
    T Access<T>(Func<RemoteState, (T Result, bool Save)> action);
}
