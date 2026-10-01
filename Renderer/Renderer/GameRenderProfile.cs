using ValveResourceFormat.IO;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// Rendering behaviour that differs between games, resolved once from the game's <c>gameinfo.gi</c>.
/// </summary>
public sealed class GameRenderProfile
{
    /// <summary>Gets whether lights use the barn door brightness from before the photometric light model.</summary>
    public bool UsesLegacyBarnBrightness { get; init; }

    /// <summary>Gets whether <c>info_visibility_box</c> has the camera-inside cull mode 2.</summary>
    public bool HasVisibilityBoxCameraInsideMode { get; init; }

    /// <summary>
    /// Resolves the profile for a game.
    /// </summary>
    /// <param name="gameInfo">The game's info, or <see langword="null"/> when it is unknown.</param>
    /// <returns>The profile for the game.</returns>
    public static GameRenderProfile FromGameInfo(GameInfo? gameInfo)
    {
        var name = gameInfo?.Name;

        return new GameRenderProfile
        {
            UsesLegacyBarnBrightness = name == "Aperture Desk Job",
            HasVisibilityBoxCameraInsideMode = name is null or "Counter-Strike 2",
        };
    }
}
