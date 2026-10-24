using System.IO;

namespace ValveResourceFormat.IO
{
    /// <summary>
    /// What the <c>gameinfo.gi</c> of the first game a <see cref="GameFileLoader"/> found says about the game.
    /// </summary>
    public sealed class GameInfoSettings
    {
        // Counter-Strike 2 mounts its content as the CS:GO SDK, not under its own app id
        private const int CounterStrike2SteamAppId = 710;

        /// <summary>
        /// Gets the Steam app id the game mounts its content as, which its <c>gameinfo.gi</c> gives as
        /// <c>FileSystem/SteamAppId</c>, or zero when no game gives one.
        /// </summary>
        public int SteamAppId { get; internal set; }

        /// <summary>Gets whether the game is Counter-Strike 2.</summary>
        public bool IsCounterStrike2 => SteamAppId == CounterStrike2SteamAppId;

        /// <summary>
        /// Gets whether the game fogs particles whose renderer leaves fog to the game, which its <c>gameinfo.gi</c>
        /// gives as <c>Particles/ParticlesFoggedByDefault</c>.
        /// </summary>
        public bool ParticlesFoggedByDefault { get; internal set; }

        /// <summary>
        /// Gets whether the game switches particle shader features at run time rather than through static combos,
        /// which its <c>gameinfo.gi</c> gives as <c>Particles/EnableParticleShaderFeatureBranching</c>.
        /// </summary>
        public bool ParticleShaderFeatureBranching { get; internal set; }

        /// <summary>Gets the path of the <c>gameinfo.gi</c> of the game, or <see langword="null"/> where no game was found.</summary>
        internal string? GameInfoPath { get; init; }

        private int? CachedInstalledSteamAppId;

        /// <summary>
        /// Gets the installed Steam app whose folder the game is in, such as 570 for Dota 2, or zero when no game was
        /// found or it is not in the folder of an installed app. Where several apps share the folder, it is the one
        /// with the lowest app id.
        /// </summary>
        public int InstalledSteamAppId => GameInfoPath == null ? 0 : CachedInstalledSteamAppId ??= FindInstalledSteamAppId(GameInfoPath);

        private static int FindInstalledSteamAppId(string gameInfoPath)
            => GameFolderLocator.FindSteamGameContainingPath(Path.GetFullPath(gameInfoPath), GameFolderLocator.FindAllSteamGames()) is { } game ? game.AppID : 0;
    }
}
