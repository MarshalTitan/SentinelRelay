using Dalamud.Plugin;
using SentinelRelay.Core;

namespace SentinelRelay.Services;

/// <summary>No hard runtime dependency; fixed primitive-string v1 calls, never reflection or chat.</summary>
internal sealed class SRankRemoteClient(IDalamudPluginInterface pluginInterface)
{
    public SRankRemoteResponse Invoke(SRankRemoteDecision decision, string expectedCharacterKey)
    {
        try
        {
            var endpoint = decision switch
            {
                SRankRemoteDecision.Status => "SRankSentinel.Remote.v1.Status",
                SRankRemoteDecision.Skip => "SRankSentinel.Remote.v1.SkipCurrentKeepQueue",
                SRankRemoteDecision.Reset => "SRankSentinel.Remote.v1.StopResetThroughUldah",
                _ => null,
            };
            if (endpoint is null) return SRankRemoteResponse.Unavailable;
            return SRankRemoteResponse.Decode(pluginInterface.GetIpcSubscriber<string, string>(endpoint)
                .InvokeFunc(expectedCharacterKey));
        }
        catch { return SRankRemoteResponse.Unavailable; }
    }
}
