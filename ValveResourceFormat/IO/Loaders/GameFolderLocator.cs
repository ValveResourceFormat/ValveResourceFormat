using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
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
        private static bool steamPathSearched;

        /// <summary>
        /// Path to the root of Steam installation. <c>null</c> if not found.
        /// </summary>
        public static string? SteamPath
        {
            get
            {
                if (steamPathSearched)
                {
                    return steamPath;
                }

                steamPathSearched = true;

                try
                {
                    string? foundPath = null;

                    if (OperatingSystem.IsWindows())
                    {
                        foundPath = GetRegistryPath(Registry.CurrentUser, "SOFTWARE\\Valve\\Steam", "SteamPath")
                            ?? GetRegistryPath(Registry.LocalMachine, "SOFTWARE\\WOW6432Node\\Valve\\Steam", "InstallPath")
                            ?? GetRegistryPath(Registry.LocalMachine, "SOFTWARE\\Valve\\Steam", "InstallPath");
                    }
                    else if (OperatingSystem.IsLinux())
                    {
                        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } xdgDataHome
                            ? xdgDataHome
                            : Path.Join(home, ".local", "share");

                        string[] paths =
                        [
                            Path.Join(home, ".steam", "root"),
                            Path.Join(home, ".steam", "steam"),
                            Path.Join(dataHome, "Steam"),
                            Path.Join(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"),
                            Path.Join(home, "snap", "steam", "common", ".local", "share", "Steam"),
                        ];

                        foundPath = paths.FirstOrDefault(path => Directory.Exists(Path.Join(path, "appcache")));
                    }
                    else if (OperatingSystem.IsMacOS())
                    {
                        var home = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                        foundPath = Path.Join(home, "Steam");
                    }

                    if (foundPath != null)
                    {
                        steamPath = NormalizeSteamPath(foundPath);
                    }
                }
                catch
                {
                }

                return steamPath;
            }
        }

        /// <summary>
        /// Resolves symbolic links, such as ~/.steam/steam on Linux, and takes the casing that Steam lists its own library with,
        /// because the registry has the path lowercased.
        /// </summary>
        private static string NormalizeSteamPath(string path)
        {
            path = Path.TrimEndingDirectorySeparator(ResolveFolderLink(Path.GetFullPath(path)));

            if (ReadSteamKeyValues(Path.Join(path, "steamapps", "libraryfolders.vdf")) is { } libraryFolders
                && GetChild(libraryFolders, "0") is { } mainLibrary
                && GetChildValue(mainLibrary, "path") is { Length: > 0 } listedPath)
            {
                listedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(listedPath));

                if (listedPath.Equals(path, PathComparison))
                {
                    return listedPath;
                }
            }

            return path;
        }

        /// <summary>
        /// Find all the Steam installation library paths.
        /// </summary>
        /// <returns>A list of Steam library paths.</returns>
        public static List<string> FindSteamLibraryFolderPaths()
        {
            if (SteamPath is not { } root)
            {
                return [];
            }

            var steamAppsPaths = new List<string>();
            var seenPaths = new HashSet<string>(StringComparer.FromComparison(PathComparison));

            void AddLibrary(string libraryPath)
            {
                var fullPath = Path.GetFullPath(libraryPath);

                if (!Directory.Exists(fullPath))
                {
                    return;
                }

                // A library reached through a symbolic link would otherwise be listed twice
                var steamAppsPath = Path.Join(ResolveFolderLink(fullPath), "steamapps");

                if (Directory.Exists(steamAppsPath) && seenPaths.Add(steamAppsPath))
                {
                    steamAppsPaths.Add(steamAppsPath);
                }
            }

            AddLibrary(root);

            var libraryFolders = ReadSteamKeyValues(Path.Join(root, "steamapps", "libraryfolders.vdf"))
                ?? ReadSteamKeyValues(Path.Join(root, "config", "libraryfolders.vdf"));

            if (libraryFolders != null)
            {
                foreach (var (_, library) in libraryFolders.Children)
                {
                    if (GetChildValue(library, "path") is { Length: > 0 } path)
                    {
                        AddLibrary(path);
                    }
                }
            }

            return steamAppsPaths;
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
            foreach (var steamPath in FindSteamLibraryFolderPaths())
            {
                if (FindSteamGameInLibrary(steamPath, appId) is { } gameInfo)
                {
                    return gameInfo;
                }
            }

            return null;
        }

        /// <summary>
        /// Finds an installed Steam app in the given library.
        /// </summary>
        /// <param name="steamAppsPath">Path to the "steamapps" folder of the library.</param>
        /// <param name="appId">App to find.</param>
        internal static SteamLibraryGameInfo? FindSteamGameInLibrary(string steamAppsPath, int appId)
        {
            return GetGameInfoFromAppManifestFile(steamAppsPath, Path.Join(steamAppsPath, $"appmanifest_{appId}.acf"));
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
            var appManifest = ReadSteamKeyValues(appManifestPath);

            if (appManifest == null
                || !int.TryParse(GetChildValue(appManifest, "appid"), NumberStyles.None, CultureInfo.InvariantCulture, out var appId)
                || GetChildValue(appManifest, "installdir") is not { Length: > 0 } installDir)
            {
                return null;
            }

            // The separator at the end avoids matching another app whose folder starts with the same name, such as "Artifact" and "Artifact 2.0"
            var gamePath = Path.GetFullPath(Path.Combine(steamPath, "common", installDir)) + Path.DirectorySeparatorChar;

            if (!Directory.Exists(gamePath))
            {
                return null;
            }

            return new SteamLibraryGameInfo(appId, GetChildValue(appManifest, "name") ?? string.Empty, steamPath, gamePath);
        }

        /// <summary>
        /// Reads a KeyValues file written by Steam, such as an app manifest.
        /// </summary>
        /// <returns>The root object, or <c>null</c> if the file does not exist or failed to parse.</returns>
        internal static KVObject? ReadSteamKeyValues(string path)
        {
            try
            {
                using var stream = File.OpenRead(path);

                return KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream, new KVSerializerOptions
                {
                    HasEscapeSequences = true,
                });
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or KeyValueException)
            {
                return null;
            }
        }

        /// <summary>
        /// Gets a child with a case-insensitive name, as Steam does when reading its files.
        /// </summary>
        internal static KVObject? GetChild(KVObject parent, string name)
        {
            foreach (var (childName, child) in parent.Children)
            {
                if (childName.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return child;
                }
            }

            return null;
        }

        private static string? GetChildValue(KVObject parent, string name)
        {
            return GetChild(parent, name) is { ValueType: not KVValueType.Collection } child ? (string)child : null;
        }

        [SupportedOSPlatform("windows")]
        private static string? GetRegistryPath(RegistryKey root, string keyName, string valueName)
        {
            using var key = root.OpenSubKey(keyName);

            return key?.GetValue(valueName) is string { Length: > 0 } path ? path : null;
        }

        /// <summary>
        /// Resolves a folder that is a symbolic link, so that the same folder reached through a link is recognized.
        /// </summary>
        private static string ResolveFolderLink(string path)
        {
            try
            {
                return Directory.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? path;
            }
            catch (IOException)
            {
                return path;
            }
        }
    }
}
