using Lumina.Excel.Sheets;
using SentinelRelay.Core;
using SentinelRelay.Models;
using SentinelRelay.Services;

namespace SentinelRelay;

public sealed partial class Plugin
{
    private readonly SRankRemoteControlSession srankRemoteSession = new();

    private void HandleSRankRemote(CharacterProfile profile, DiscordChannelMessage source, string checkpoint, int readerGeneration)
    {
        var operationId = Guid.NewGuid();
        void Diagnostic(string stage, string reason, string state = "unavailable")
            => Log.Information("{Event} id={OperationId} operation={Operation} character={Character} state={State} reason={Reason}",
                stage, operationId,
                SRankRemoteCommandParser.TryParse(source.Content, out var parsed) ? parsed.ToString() : "Unknown",
                MessageSanitizer.SanitizePlainText(profile.CharacterName), state, reason);
        Diagnostic("RemoteControlRequested", "Received");

        // Re-read the real player at execution; activeIdentity may be up to 250ms old.
        var identity = characterContext.Current;
        if (disposed || !Framework.IsInFrameworkUpdateThread || identity is null ||
            activeProfile != profile || identity.CharacterKey != profile.CharacterKey)
        {
            srankRemoteSession.Clear();
            Diagnostic("RemoteControlRejected", "WrongCharacterOrContext");
            return;
        }
        if (activeWebhookEndpoint is null)
        {
            srankRemoteSession.Clear();
            Diagnostic("RemoteControlRejected", "ResponseWebhookUnavailable");
            return; // No mutation without a configured result channel.
        }

        var authorization = srankRemoteSession.Accept(profile, identity, source, checkpoint, DateTime.UtcNow, readerGeneration);
        if (!authorization.Accepted)
        {
            Diagnostic("RemoteControlRejected", authorization.Reason);
            if (authorization.SourceAuthorized)
                webhookRelay.TryEnqueue(identity.CharacterKey, activeWebhookEndpoint,
                    [SRankRemoteResponseFormatter.Notice("S Rank Sentinel: " + authorization.Reason + ". Use /srank reset then /srank reset confirm within 30 seconds.")]);
            return; // Untrusted/disabled/replayed inputs never receive private state.
        }
        if (authorization.Decision == SRankRemoteDecision.ConfirmationRequired)
        {
            var notice = SRankRemoteResponseFormatter.Notice(
                "Reset requested. Send `/srank reset confirm` within 30 seconds to perform STOP + RESET THROUGH UL'DAH.");
            if (!webhookRelay.TryEnqueue(identity.CharacterKey, activeWebhookEndpoint, [notice]))
            {
                srankRemoteSession.Clear();
                Diagnostic("RemoteControlFailed", "ConfirmationDeliveryQueueFull");
            }
            return;
        }
        if (authorization.Decision == SRankRemoteDecision.Reset)
            Diagnostic("RemoteControlConfirmed", "ResetConfirmed");
        if (!profile.RemoteSRankControlsEnabled || profile.Paused ||
            !discordReplyReader.IsCurrentGeneration(readerGeneration) ||
            characterContext.Current?.CharacterKey != identity.CharacterKey ||
            !DiscordReplyPolicy.TryAuthorizeSource(profile, characterContext.Current, source,
                checkpoint, DateTime.UtcNow, out _, requireReplies: false))
        {
            srankRemoteSession.Clear();
            Diagnostic("RemoteControlRejected", "AuthorizationChangedBeforeExecution");
            return;
        }
        var result = new SRankRemoteClient(PluginInterface).Invoke(authorization.Decision, identity.CharacterKey);
        Diagnostic(result.Success ? "RemoteControlExecuted" : "RemoteControlFailed",
            result.Reason, result.Status?.State ?? "unavailable");
        if (!webhookRelay.TryEnqueue(identity.CharacterKey, activeWebhookEndpoint,
                [SRankRemoteResponseFormatter.Format(identity.CharacterName, authorization.Decision, result,
                    result.Status is { } status ? ResolveSRankTerritoryName(status.Territory) : null)]))
            Diagnostic("RemoteControlFailed", "ResponseQueueFull");
        // Never repeat IPC when result delivery fails: requests are at-most-once, not guaranteed delivery.
    }

    private static string? ResolveSRankTerritoryName(uint territory)
    {
        try
        {
            var sheet = DataManager.GetExcelSheet<TerritoryType>();
            return sheet.TryGetRow(territory, out var row) && row.PlaceName.IsValid
                ? row.PlaceName.Value.Name.ToString() : null;
        }
        catch
        {
            // A missing row/sheet must not hide an accepted recovery result or retry IPC.
            return null;
        }
    }
}
