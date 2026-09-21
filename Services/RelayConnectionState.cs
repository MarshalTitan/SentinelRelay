namespace SentinelRelay.Services;

public enum RelayConnectionState
{
    Disabled,
    Connecting,
    ConnectedUnlinked,
    Authenticated,
    Reconnecting,
    Offline,
    Disposed,
}

