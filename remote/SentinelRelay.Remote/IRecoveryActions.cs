namespace SentinelRelay.Remote;

// Only these fixed actions are admitted by RemoteAuthority. No remote path/arguments.
public interface IRecoveryActions
{
    Reply Execute(string deviceId, string operation, GameBinding? binding);
}
