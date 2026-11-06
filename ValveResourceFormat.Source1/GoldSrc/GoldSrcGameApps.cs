namespace ValveResourceFormat.GoldSrc;

/// <summary>
/// The Steam app ids of the GoldSrc games, by the folder of their mod. A GoldSrc install does not name its app
/// anywhere, so the folder is all there is to tell the games apart.
/// </summary>
public static class GoldSrcGameApps
{
    private static readonly Dictionary<string, int> Apps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cstrike"] = 10,
        ["tfc"] = 20,
        ["dod"] = 30,
        ["dmc"] = 40,
        ["gearbox"] = 50,
        ["ricochet"] = 60,
        ["valve"] = 70,
        ["czero"] = 80,
        ["czeror"] = 100,
        ["bshift"] = 130,
    };

    /// <summary>Gets the Steam app id of the game of a mod folder, or 0 for a folder that is not a known game.</summary>
    public static int GetAppId(string modDirectory) => Apps.GetValueOrDefault(modDirectory);
}
