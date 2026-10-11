using System.Linq;
using System.Threading.Tasks;
using ValveResourceFormat.IO;

namespace GUI.Utils;

/// <summary>
/// Installed Steam apps, found once in the background as the app starts.
/// </summary>
static class InstalledSteamGames
{
    private static readonly Task<List<GameFolderLocator.SteamLibraryGameInfo>> FindTask =
        Task.Run(static () => GameFolderLocator.FindAllSteamGames().OrderBy(static game => game.AppID).ToList());

    /// <summary>
    /// Installed apps ordered by app id, or empty until <see cref="FindAsync"/> completes,
    /// so the UI never waits on reading Steam libraries from slow drives.
    /// </summary>
    public static IReadOnlyList<GameFolderLocator.SteamLibraryGameInfo> Games => FindTask.IsCompletedSuccessfully ? FindTask.Result : [];

    /// <summary>
    /// Starts finding the installed apps on first use, and completes when they are found.
    /// </summary>
    public static Task<List<GameFolderLocator.SteamLibraryGameInfo>> FindAsync() => FindTask;
}
