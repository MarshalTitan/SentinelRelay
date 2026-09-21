using Dalamud.Plugin.Services;
using SentinelRelay.Models;

namespace SentinelRelay.Services;

public sealed class CharacterContextService(IPlayerState playerState)
{
    public CharacterIdentity? Current
    {
        get
        {
            if (!playerState.IsLoaded || playerState.ContentId == 0 || string.IsNullOrWhiteSpace(playerState.CharacterName))
                return null;

            string homeWorld;
            try
            {
                homeWorld = playerState.HomeWorld.Value.Name.ToString();
            }
            catch
            {
                homeWorld = "Unknown";
            }

            var contentId = playerState.ContentId;
            return new CharacterIdentity(
                $"cid:{contentId:X16}",
                playerState.CharacterName,
                homeWorld,
                contentId);
        }
    }
}

