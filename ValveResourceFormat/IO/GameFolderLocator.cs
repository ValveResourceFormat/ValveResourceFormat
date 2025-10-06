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
        /// <param name="AppName">Name of the path.</param>
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
                        var paths = new[] { ".steam", ".steam/steam", ".steam/root", ".local/share/Steam" };

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

            var steamPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { steamPath };

            foreach (var child in libraryFoldersKv.Children)
            {
                var steamAppsPath = Path.GetFullPath(Path.Join(child["path"].ToString(CultureInfo.InvariantCulture), "steamapps"));

                if (Directory.Exists(steamAppsPath))
                {
                    steamPaths.Add(steamAppsPath);
                }
            }

            return [.. steamPaths];
        }

        /// <summary>
        /// Find all installed games in in all Steam library folders.
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
            var appID = appManifestKv["appid"].ToInt32(CultureInfo.InvariantCulture);
            var appName = appManifestKv["name"].ToString(CultureInfo.InvariantCulture);
            var installDir = appManifestKv["installdir"].ToString(CultureInfo.InvariantCulture);

            // Intentionally append separator to the end to avoid issues when one game is a prefix of another game,
            // e.g. "Artifact" and "Artifact 2.0"
            var gamePath = Path.Combine(steamPath, "common", string.Concat(installDir, Path.DirectorySeparatorChar));

            return new SteamLibraryGameInfo(appID, appName, steamPath, gamePath);
        }
    }
}
