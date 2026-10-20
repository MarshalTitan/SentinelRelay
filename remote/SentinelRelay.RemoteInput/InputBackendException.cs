namespace SentinelRelay.RemoteInput;

public sealed class InputBackendException(string code) : IOException(code)
{
    public string Code { get; } = code;
}
