using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using ValveKeyValue;

namespace ValveResourceFormat.IO
{
    /// <summary>
    /// Provides methods for locating Steam game installations and libraries.
    /// </summary>
    public static class GameFolderLocator
    {
        /// <summary>
        /// Represents an installed Steam app (game, tool, application, etc.)
        /// </summary>
        /// <param name="AppID">AppID of the app.</param>
        /// <param name="AppName">Name of the app.</param>
        /// <param name="SteamPath">Path to the root of the Steam library where this app is installed. ("C:/Steam/steamapps")</param>
        /// <param name="GamePath">Full path to the installation directory of the app. ("C:/Steam/steamapps/common/dota 2 beta")</param>
        public record struct SteamLibraryGameInfo(int AppID, string AppName, string SteamPath, string GamePath);

        private static string? steamPath;

        /// <summary>
        /// Path to the root of Steam installation. <c>null</c> if not found.
        /// </summary>
        public static string? SteamPath
        {
            get
            {
                if (steamPath != null)
                {
                    return steamPath;
                }

                try
                {
                    if (OperatingSystem.IsWindows())
                    {
                        using var key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Valve\\Steam") ??
                                        Registry.LocalMachine.OpenSubKey("SOFTWARE\\Valve\\Steam");

                        if (key?.GetValue("SteamPath") is string steamPathTemp)
                        {
                            steamPath = steamPathTemp;
                        }
                    }
                    else if (OperatingSystem.IsLinux())
                    {
                        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                        string[] paths =
                        [
                            ".steam",
                            ".steam/steam",
                            ".steam/root",
                            ".local/share/Steam",
                            ".var/app/com.valvesoftware.Steam/.local/share/Steam",
                        ];

                        steamPath = paths
                            .Select(path => Path.Join(home, path))
                            .FirstOrDefault(steamPath => Directory.Exists(Path.Join(steamPath, "appcache")));
                    }
                    else if (OperatingSystem.IsMacOS())
                    {
                        var home = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                        steamPath = Path.Join(home, "Steam");
                    }
                }
                catch
                {
                }

                return steamPath;
            }
        }

        /// <summary>
        /// Find all the Steam installation library paths.
        /// </summary>
        /// <returns>A list of Steam library paths.</returns>
        public static List<string> FindSteamLibraryFolderPaths()
        {
            var libraryfolders = Path.Join(SteamPath, "steamapps", "libraryfolders.vdf");

            if (steamPath == null || !File.Exists(libraryfolders))
            {
                return [];
            }

            var kvDeserializer = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);

            KVObject libraryFoldersKv;

            using (var libraryFoldersStream = File.OpenRead(libraryfolders))
            {
                libraryFoldersKv = kvDeserializer.Deserialize(libraryFoldersStream, KVSerializerOptions.DefaultOptions);
            }

            var steamPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.GetFullPath(Path.Join(steamPath, "steamapps")) };

            foreach (var child in libraryFoldersKv.Children)
            {
                var steamAppsPath = Path.GetFullPath(Path.Join((string)child.Value["path"], "steamapps"));

                if (Directory.Exists(steamAppsPath))
                {
                    steamPaths.Add(steamAppsPath);
                }
            }

            return [.. steamPaths];
        }

        /// <summary>
        /// Find all installed games in all Steam library folders.
        /// </summary>
        /// <returns>A list of all installed Steam games.</returns>
        public static List<SteamLibraryGameInfo> FindAllSteamGames()
        {
            var gameInfos = new List<SteamLibraryGameInfo>();

            var steamPaths = FindSteamLibraryFolderPaths();

            foreach (var steamPath in steamPaths)
            {
                var manifests = Directory.GetFiles(steamPath, "appmanifest_*.acf");

                foreach (var appManifestPath in manifests)
                {
                    var gameInfo = GetGameInfoFromAppManifestFile(steamPath, appManifestPath);
                    if (gameInfo.HasValue)
                    {
                        gameInfos.Add(gameInfo.Value);
                    }
                }
            }

            return gameInfos;
        }

        /// <summary>
        /// Optimized way to find Steam game by app id. Opens only 1 file for the specified app, instead of
        /// opening new file for every installed game.
        /// </summary>
        public static SteamLibraryGameInfo? FindSteamGameByAppId(int appId)
        {
            var steamPaths = FindSteamLibraryFolderPaths();

            foreach (var steamPath in steamPaths)
            {
                var appManifestPath = Path.Combine(steamPath, $"appmanifest_{appId}.acf");

                var gameInfo = GetGameInfoFromAppManifestFile(steamPath, appManifestPath);
                if (!gameInfo.HasValue)
                {
                    continue;
                }

                if (gameInfo.Value.AppID != appId)
                {
                    continue;
                }

                return gameInfo.Value;
            }

            return null;
        }

        /// <summary>
        /// Prefix of portable paths relative to the installation folder of a Steam app, such as <c>steam:730/game/csgo/pak01_dir.vpk</c>.
        /// </summary>
        public const string SteamAppPathPrefix = "steam:";

        private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        /// <summary>
        /// Whether the path is relative to a Steam app, see <see cref="SteamAppPathPrefix"/>.
        /// </summary>
        public static bool IsSteamAppPath(string path) => path.StartsWith(SteamAppPathPrefix, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Resolves a <c>steam:&lt;appid&gt;/&lt;path&gt;</c> path to the full path inside of the installation folder of that Steam app,
        /// other paths are returned unchanged.
        /// </summary>
        /// <param name="path">Path to resolve.</param>
        /// <param name="resolvedPath">The resolved path, or <paramref name="path"/> if it is not relative to a Steam app or could not be resolved.</param>
        /// <param name="error">Why the path could not be resolved, such as the app not being installed.</param>
        public static bool TryResolveSteamAppPath(string path, out string resolvedPath, [NotNullWhen(false)] out string? error)
        {
            resolvedPath = path;
            error = null;

            if (!IsSteamAppPath(path))
            {
                return true;
            }

            var rest = path.AsSpan(SteamAppPathPrefix.Length);
            var separator = rest.IndexOfAny('/', '\\');
            var appIdText = separator < 0 ? rest : rest[..separator];
            var relativePath = separator < 0 ? [] : rest[(separator + 1)..];

            if (!int.TryParse(appIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var appId))
            {
                error = $"\"{path}\" does not start with a Steam app id, for example \"{SteamAppPathPrefix}730/game/csgo/pak01_dir.vpk\".";
                return false;
            }

            var game = FindSteamGameByAppId(appId);

            if (game == null)
            {
                // Source 2 apps keep their content in a "game" folder
                var installedApps = FindAllSteamGames()
                    .Where(static game => Directory.Exists(Path.Join(game.GamePath, "game")))
                    .OrderBy(static game => game.AppID)
                    .Select(static game => $"{game.AppID} ({game.AppName})");

                error = $"Steam app {appId} is not installed. Installed Source 2 apps: {string.Join(", ", installedApps)}.";
                return false;
            }

            var fullPath = Path.GetFullPath(Path.Join(game.Value.GamePath, relativePath));

            if (!IsInsideFolder(fullPath, game.Value.GamePath))
            {
                error = $"\"{path}\" points outside of the folder of Steam app {appId}.";
                return false;
            }

            resolvedPath = fullPath;
            return true;
        }

        /// <summary>
        /// Converts a full path inside of an installed Steam app to a portable <c>steam:&lt;appid&gt;/&lt;path&gt;</c> path,
        /// which resolves back to the same file on any computer that has the app installed.
        /// </summary>
        /// <param name="fullPath">Path to convert.</param>
        /// <param name="games">Installed apps, as returned by <see cref="FindAllSteamGames"/>. Found when not given.</param>
        /// <returns>The portable path, or <c>null</c> if the path is not inside of an installed app.</returns>
        public static string? GetSteamAppPath(string fullPath, IReadOnlyList<SteamLibraryGameInfo>? games = null)
        {
            games ??= FindAllSteamGames();
            fullPath = Path.GetFullPath(fullPath);

            // Resolving only finds the first manifest of an app, so a stale one in another library can not be linked to
            var game = FindSteamGameContainingPath(fullPath, games.DistinctBy(static installed => installed.AppID));

            if (game == null)
            {
                return null;
            }

            var relativePath = fullPath.AsSpan(Math.Min(game.Value.GamePath.Length, fullPath.Length));

            return $"{SteamAppPathPrefix}{game.Value.AppID}/{relativePath}".Replace('\\', '/');
        }

        /// <summary>
        /// Finds the installed Steam app whose installation folder contains the path.
        /// </summary>
        /// <param name="fullPath">Full path to a file or folder.</param>
        /// <param name="games">Installed apps, as returned by <see cref="FindAllSteamGames"/>.</param>
        /// <returns>The app, the one with the lowest app id if several share the folder, or <c>null</c>.</returns>
        public static SteamLibraryGameInfo? FindSteamGameContainingPath(string fullPath, IEnumerable<SteamLibraryGameInfo> games)
        {
            SteamLibraryGameInfo? match = null;

            foreach (var game in games)
            {
                if (IsInsideFolder(fullPath, game.GamePath) && (match == null || game.AppID < match.Value.AppID))
                {
                    match = game;
                }
            }

            return match;
        }

        /// <param name="path">Full path.</param>
        /// <param name="folder">Full path of the folder, ending with a separator.</param>
        private static bool IsInsideFolder(string path, string folder)
        {
            return path.StartsWith(folder, PathComparison)
                || path.AsSpan().Equals(Path.TrimEndingDirectorySeparator(folder.AsSpan()), PathComparison);
        }

        private static SteamLibraryGameInfo? GetGameInfoFromAppManifestFile(string steamPath, string appManifestPath)
        {
            try
            {
                using var appManifestStream = File.OpenRead(appManifestPath);
                var kvDeserializer = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);
                var appManifestKv = kvDeserializer.Deserialize(appManifestStream, KVSerializerOptions.DefaultOptions);

                var gameInfo = ToGameInfo(steamPath, appManifestKv);

                if (!Directory.Exists(gameInfo.GamePath))
                {
                    return null;
                }

                return gameInfo;
            }
            catch
            {
                // Ignore games that failed to parse
            }

            return null;
        }

        private static SteamLibraryGameInfo ToGameInfo(string steamPath, KVObject appManifestKv)
        {
            var appID = (int)appManifestKv["appid"];
            var appName = (string)appManifestKv["name"];
            var installDir = (string)appManifestKv["installdir"];

            // Intentionally append separator to the end to avoid issues when one game is a prefix of another game,
            // e.g. "Artifact" and "Artifact 2.0"
            var gamePath = Path.Combine(steamPath, "common", string.Concat(installDir, Path.DirectorySeparatorChar));

            return new SteamLibraryGameInfo(appID, appName, steamPath, gamePath);
        }
    }
}
