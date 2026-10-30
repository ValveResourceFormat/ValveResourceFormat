namespace ValveResourceFormat.IO
{
    /// <summary>
    /// The game a <see cref="GameInfo"/> belongs to, named after the game's mod folder.
    /// </summary>
    /// <remarks>
    /// Think twice before using this. Files should be read and drawn the same way for every game, driven by what the
    /// files themselves and the keys of <c>gameinfo.gi</c> say. Switching on the game is a last resort, only for
    /// differences that nothing in the data reveals, and any game not listed here, such as a mod or a new release,
    /// gets <see cref="Unknown"/> and must still behave sensibly.
    /// </remarks>
    public enum Game
    {
        /// <summary>A game that is not detected, or no game at all.</summary>
        Unknown = 0,

        /// <summary>Counter-Strike 2, mod folder <c>csgo</c>.</summary>
        Csgo,

        /// <summary>Dota 2, mod folder <c>dota</c>.</summary>
        Dota,

        /// <summary>Deadlock, mod folder <c>citadel</c>.</summary>
        Citadel,

        /// <summary>Half-Life: Alyx, mod folder <c>hlvr</c>.</summary>
        Hlvr,

        /// <summary>The game of mod folder <c>hlx</c>.</summary>
        Hlx,

        /// <summary>Aperture Desk Job, mod folder <c>steampal</c>.</summary>
        Steampal,

        /// <summary>SteamVR Home and Destinations, mod folder <c>steamtours</c>.</summary>
        Steamtours,

        /// <summary>Aperture Robot Repair and The Lab, mod folder <c>vr</c>.</summary>
        Vr,
    }
}
