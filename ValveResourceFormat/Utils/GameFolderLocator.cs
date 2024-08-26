using Microsoft.Win32;
using System.Globalization;
using System.IO;
using System.Linq;
using ValveKeyValue;

namespace ValveResourceFormat.Utils
{
    public static class GameFolderLocator
    {
        public static string GetSteamPath()
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    using var key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Valve\\Steam") ??
                                    Registry.LocalMachine.OpenSubKey("SOFTWARE\\Valve\\Steam");

                    if (key?.GetValue("SteamPath") is string steamPath)
                    {
                        return steamPath;
                    }
                }
                else if (OperatingSystem.IsLinux())
                {
                    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    var paths = new[] { ".steam", ".steam/steam", ".steam/root", ".local/share/Steam" };

                    return paths
                        .Select(path => Path.Join(home, path))
                        .FirstOrDefault(steamPath => Directory.Exists(Path.Join(steamPath, "appcache")));
                }
                else if (OperatingSystem.IsMacOS())
                {
                    var home = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    return Path.Join(home, "Steam");
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        public static string[] FindSteamLibraryFolderPaths()
        {
            var steam = GetSteamPath();

            var libraryfolders = Path.Join(steam, "steamapps", "libraryfolders.vdf");

            if (string.IsNullOrEmpty(steam) || !File.Exists(libraryfolders))
            {
                return [];
            }

            var kvDeserializer = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);

            KVObject libraryFoldersKv;

            using (var libraryFoldersStream = File.OpenRead(libraryfolders))
            {
                libraryFoldersKv = kvDeserializer.Deserialize(libraryFoldersStream, KVSerializerOptions.DefaultOptions);
            }

            var steamPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { steam };

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

        public static SteamLibraryGameInfo[] FindAllSteamGames()
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

            return [.. gameInfos];
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
            var gamePath = Path.Combine(steamPath, "common", installDir);

            return new SteamLibraryGameInfo(appID, appName, steamPath, gamePath);
        }
    }
}
