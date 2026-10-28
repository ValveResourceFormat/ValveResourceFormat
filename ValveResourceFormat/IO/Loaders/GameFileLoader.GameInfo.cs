//#define DEBUG_FILE_LOAD

using System.Globalization;
using System.IO;
using System.IO.Enumeration;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.Blocks;

namespace ValveResourceFormat.IO
{
    public partial class GameFileLoader
    {
        // Search paths a game mounts only in some modes, such as for a language other than English
        private static readonly HashSet<string> ConditionalGameSearchPathKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "Game_LowViolence",
            "Game_UILanguage",
            "Game_AudioLanguage",
        };

        // The engine mounts Game_LowViolence search paths only for low violence clients
        private static bool MountLowViolence => false;

        private GameInfo? FoundGameInfo;

        /// <summary>
        /// Gets the <c>gameinfo.gi</c> of the first game found, else the one of <see cref="GameInfoFallback"/>, else
        /// <see cref="IO.GameInfo.Empty"/>.
        /// </summary>
        public GameInfo GameInfo => FoundGameInfo ?? GameInfoFallback?.GameInfo ?? GameInfo.Empty;

        /// <summary>
        /// Gets a loader whose game gives the settings read from <c>gameinfo.gi</c> while this one has found no game of
        /// its own, such as the loader of the file this one's file was opened from.
        /// </summary>
        protected virtual GameFileLoader? GameInfoFallback => null;

        private GameInfo? ReadGameInfo(string gameinfoPath)
        {
            try
            {
                return GameInfo.Read(gameinfoPath);
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Failed to read \"{GameInfo}\"", gameinfoPath);
                return null;
            }
        }

        private void LoadGameInfo(HashSet<string> folders, string gameinfoPath)
        {
            if (ReadGameInfo(gameinfoPath) is { } gameInfo)
            {
                HandleGameInfo(folders, gameInfo);
            }
        }

        private void HandleGameInfo(HashSet<string> folders, GameInfo gameInfo)
        {
            var gameinfoPath = gameInfo.FilePath;
            Logger.LogInformation("Found \"{GameName}\" from \"{GameInfo}\"", gameInfo.Name, gameinfoPath);

            FoundGameInfo ??= gameInfo;

            // Only games that opt in mount the dependencies listed in an addon's addoninfo.txt
            if (gameInfo.AllowAddonDependencies)
            {
                AddonDependenciesPending = true;

                // Workshop dependencies of a local addon are installed next to the game, in steamapps/workshop/content/appid
                if (WorkshopContentFolder == null
                    && gameInfo.SteamAppId != 0
                    && FindSteamAppsFolder(gameinfoPath) is { } steamApps)
                {
                    var steamAppId = gameInfo.SteamAppId.ToString(CultureInfo.InvariantCulture);
                    WorkshopContentFolder = Path.Join(steamApps, "workshop", "content", steamAppId);
                }
            }

            foreach (var (key, searchPath) in gameInfo.SearchPaths)
            {
                var path = Path.Combine(gameInfo.GameRoot, searchPath);

                if (key.Equals("Game", StringComparison.OrdinalIgnoreCase)
                    || (MountLowViolence && key.Equals("Game_LowViolence", StringComparison.OrdinalIgnoreCase)))
                {
                    folders.Add(path);
                }
                else if (key.Equals("OfficialAddonRoot", StringComparison.OrdinalIgnoreCase))
                {
                    CurrentGameOfficialAddonsPaths.Add(path);
                }
                else if (key.Equals("AddonRoot", StringComparison.OrdinalIgnoreCase))
                {
                    CurrentGameAddonsPaths.Add(path);
                }
            }

            // The engine searches core after the gameinfo search paths, whether the gameinfo lists it or not
            var core = Path.Combine(gameInfo.GameRoot, "core");

            if (Directory.Exists(core))
            {
                folders.Add(core);
            }
        }

        /// <summary>
        /// Finds and loads search paths from gameinfo.gi files.
        /// </summary>
        public void FindAndLoadSearchPaths(string? modIdentifierPath = null)
        {
            modIdentifierPath ??= GetModIdentifierFile() ?? FindGameInfoMountingCurrentFolder();

            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (modIdentifierPath == "<VRF_WORKSHOP>")
            {
                FindGameFoldersForWorkshopFile(folders);
            }
            else
            {
                if (modIdentifierPath == null)
                {
                    return;
                }

                var rootFolder = Path.GetDirectoryName(modIdentifierPath);
                var assumedGameRoot = Path.GetDirectoryName(rootFolder)!;

                if (Path.GetFileName(modIdentifierPath) == GameinfoGi)
                {
                    LoadGameInfo(folders, modIdentifierPath);
                }
                else
                {
                    FindGameFoldersForWorkshopFile(folders);

                    if (assumedGameRoot.EndsWith(AddonsSuffix, StringComparison.OrdinalIgnoreCase))
                    {
                        var mainGameDir = assumedGameRoot[..^AddonsSuffix.Length];
                        var mainGameInfo = Path.Join(mainGameDir, GameinfoGi);

                        if (File.Exists(mainGameInfo))
                        {
                            LoadGameInfo(folders, mainGameInfo);
                        }
                        else if (Directory.Exists(mainGameDir))
                        {
                            folders.Add(mainGameDir);
                        }
                    }

                    PreferredAddonFolderOnDisk = rootFolder;
                }
            }

            FindAndLoadVpksInFolders(folders);

            if (PreferredAddonFolderOnDisk != null)
            {
                AddAddonFolderToSearch(PreferredAddonFolderOnDisk, 0);
            }
        }

        // A source file sits under "content", beside the "game" folder holding what it compiles to, with the
        // same folders below both. The game side is the one with the gameinfo and the compiled files.
        private static string? GetGamePathForContentFile(string? fileName)
        {
            var directory = Path.GetDirectoryName(fileName);

            while (!string.IsNullOrEmpty(directory))
            {
                var parent = Path.GetDirectoryName(directory);

                if (parent != null && Path.GetFileName(directory).Equals("content", StringComparison.OrdinalIgnoreCase))
                {
                    var gameDirectory = Path.Join(parent, "game");

                    return Directory.Exists(gameDirectory)
                        ? Path.Join(gameDirectory, fileName![(directory.Length + 1)..])
                        : fileName;
                }

                directory = parent;
            }

            return fileName;
        }

        private string? GetModIdentifierFile()
        {
            var directory = GameSearchFileName!;
            string? childDirectory = null;
            var i = 10;
            var isLastWorkshop = false;

            // Check for slash here to support paths on linux under wine
            if (!Path.IsPathFullyQualified(directory) && !directory.StartsWith('/'))
            {
#if DEBUG_FILE_LOAD
                Logger.LogDebug("Not a fully qualified path \"{Directory}\", not checking for mod", directory);
#endif

                return null;
            }

            while (i-- > 0)
            {
                directory = Path.GetDirectoryName(directory);

                if (directory == null)
                {
                    return null;
                }

#if DEBUG_FILE_LOAD
                Logger.LogDebug("Scanning \"{Directory}\"", directory);
#endif

                if (directory.EndsWith(AddonsSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    var mainGameDir = directory[..^AddonsSuffix.Length];
                    var mainGameInfo = Path.Join(mainGameDir, GameinfoGi);

                    if (File.Exists(mainGameInfo))
                    {
                        // Loose compiled files of the addon the opened file is in (e.g. csgo_addons/<addon>/materials).
                        // An addon packed as csgo_addons/vpks/<addon>.vpk is the opened package itself, not a folder.
                        if (!string.Equals(Path.GetFileName(childDirectory), "vpks", StringComparison.OrdinalIgnoreCase))
                        {
                            PreferredAddonFolderOnDisk ??= childDirectory;
                        }

                        return mainGameInfo;
                    }
                }

                var currentDirectory = Path.GetFileName(directory);

                if (currentDirectory == "steamapps")
                {
                    if (isLastWorkshop) // Found /steamapps/workshop/ folder
                    {
                        return "<VRF_WORKSHOP>";
                    }

                    return null;
                }

                isLastWorkshop = currentDirectory == "workshop";

                foreach (var modIdentifier in ModIdentifiers)
                {
                    var path = Path.Combine(directory, modIdentifier);
                    if (File.Exists(path))
                    {
                        return path;
                    }
                }

                childDirectory = directory;
            }

            return null;
        }

        /// <summary>
        /// Finds the <c>gameinfo.gi</c> of a sibling mod that lists one of the folders the current file is in as a
        /// search path or addon root, for mod folders that have no mod identifier of their own.
        /// </summary>
        private string? FindGameInfoMountingCurrentFolder()
        {
            var childDirectory = GameSearchFileName!;

            if (!Path.IsPathFullyQualified(childDirectory) && !childDirectory.StartsWith('/'))
            {
                return null;
            }

            childDirectory = Path.GetDirectoryName(childDirectory);

            for (var i = 0; i < 10 && childDirectory != null; i++)
            {
                var directory = Path.GetDirectoryName(childDirectory);

                if (directory == null || Path.GetFileName(directory) == "steamapps")
                {
                    return null;
                }

                var childName = Path.GetFileName(childDirectory);
                childDirectory = directory;

                IEnumerable<string> modFolders;

                try
                {
                    modFolders = Directory.EnumerateDirectories(directory);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var modFolder in modFolders)
                {
                    var gameInfo = Path.Join(modFolder, GameinfoGi);

                    if (File.Exists(gameInfo) && GameInfoMountsFolder(gameInfo, childName))
                    {
                        return gameInfo;
                    }
                }
            }

            return null;
        }

        private bool GameInfoMountsFolder(string gameinfoPath, string folderName)
        {
            if (ReadGameInfo(gameinfoPath) is not { } gameInfo)
            {
                return false;
            }

            foreach (var (_, searchPath) in gameInfo.SearchPaths)
            {
                if (string.Equals(searchPath, folderName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsGameSearchPath(GameInfo gameInfo, string folder)
        {
            foreach (var (key, searchPath) in gameInfo.SearchPaths)
            {
                if (key.Equals("Game", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(Path.Combine(gameInfo.GameRoot, searchPath), folder, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void FindGameFoldersForWorkshopFile(HashSet<string> folders)
        {
            // If we're loading a file from steamapps/workshop folder, attempt to discover gameinfos and load vpks for the game
            const string STEAMAPPS_WORKSHOP_CONTENT = "steamapps/workshop/content";
            var filePath = CurrentFileName!.Replace('\\', '/');
            var contentIndex = filePath.IndexOf(STEAMAPPS_WORKSHOP_CONTENT, StringComparison.OrdinalIgnoreCase);

            if (contentIndex == -1)
            {
                return;
            }

            // Extract the appid from path
            var contentIndexEnd = contentIndex + STEAMAPPS_WORKSHOP_CONTENT.Length + 1;
            var slashAfterAppId = filePath.IndexOf('/', contentIndexEnd);

            if (slashAfterAppId == -1)
            {
                return;
            }

            var appIdString = filePath[contentIndexEnd..slashAfterAppId];

            if (!int.TryParse(appIdString, NumberStyles.None, CultureInfo.InvariantCulture, out var appId))
            {
                return;
            }

#if DEBUG_FILE_LOAD
            Logger.LogDebug("Parsed appid {AppId} for workshop file {FilePath}", appId, filePath);
#endif

            var steamPath = filePath[..(contentIndex + "steamapps".Length)];

            WorkshopContentFolder = filePath[..slashAfterAppId];

            // The app is usually installed in the same library as its workshop content, but it can be moved to another one
            if ((GameFolderLocator.FindSteamGameInLibrary(steamPath, appId) ?? GameFolderLocator.FindSteamGameByAppId(appId)) is not { GamePath: var gamePath })
            {
                return;
            }

            FindGameFoldersInSteamApp(folders, gamePath);
        }

        /// <summary>
        /// Mounts the installed Steam game named by the content search paths a resource was compiled with, such as
        /// <c>csgo_addons/example</c>, when no game was found otherwise.
        /// </summary>
        /// <remarks>
        /// Call it for each opened file before loading its dependencies. It is passed on to <see cref="GameInfoFallback"/>,
        /// so the package the file was opened from is still searched before the game.
        /// </remarks>
        /// <param name="resource">The opened resource.</param>
        public void FindGameFromResource(Resource resource)
        {
            ArgumentNullException.ThrowIfNull(resource);

            if (GameInfoFallback is { } fallback)
            {
                fallback.FindGameFromResource(resource);
                return;
            }

            // Files are opened in parallel and must wait for a game that is being mounted
            lock (AddonDependenciesLock)
            {
                if (FoundGameInfo != null || ResourceGame != Game.Unknown || resource.EditInfo is not { } editInfo)
                {
                    return;
                }

                foreach (var searchPath in EnumerateContentSearchPaths(editInfo))
                {
                    var game = GameInfo.GetGameOfMod(GetModOfContentSearchPath(searchPath));

                    if (game == Game.Unknown)
                    {
                        continue;
                    }

                    Logger.LogInformation("Found {Game} from content search path \"{SearchPath}\" of \"{File}\"", game, searchPath, resource.FileName);

                    ResourceGame = game;
                    MountSteamGame(game);
                    return;
                }
            }
        }

        private static IEnumerable<string> EnumerateContentSearchPaths(ResourceEditInfo editInfo)
        {
            foreach (var dependency in editInfo.InputDependencies)
            {
                yield return dependency.ContentSearchPath;
            }

            foreach (var dependency in editInfo.AdditionalInputDependencies)
            {
                yield return dependency.ContentSearchPath;
            }

            foreach (var relatedFile in editInfo.AdditionalRelatedFiles)
            {
                yield return relatedFile.ContentSearchPath;
            }
        }

        // "csgo_addons/example" is an addon of csgo
        private static string GetModOfContentSearchPath(string searchPath)
        {
            var separator = searchPath.IndexOfAny(['/', '\\']);
            var mod = separator < 0 ? searchPath : searchPath[..separator];

            if (mod.EndsWith(AddonsSuffix, StringComparison.OrdinalIgnoreCase))
            {
                mod = mod[..^AddonsSuffix.Length];
            }

            return mod;
        }

        private void MountSteamGame(Game game)
        {
            foreach (var steamAppId in GameInfo.GetSteamAppIds(game))
            {
                if (GameFolderLocator.FindSteamGameByAppId(steamAppId) is not { GamePath: var gamePath })
                {
                    continue;
                }

                var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                FindGameFoldersInSteamApp(folders, gamePath);
                FindAndLoadVpksInFolders(folders);
                return;
            }

            Logger.LogInformation("No Steam app of {Game} is installed", game);
        }

        private void FindGameFoldersInSteamApp(HashSet<string> folders, string gamePath)
        {
            // Find all the gameinfo.gi files, open them to get game paths
            var gameInfos = new FileSystemEnumerable<string>(
                gamePath,
                (ref entry) => entry.ToSpecifiedFullPath(),
                new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    MaxRecursionDepth = 5,
                })
            {
                ShouldIncludePredicate = static (ref entry) => !entry.IsDirectory && entry.FileName.Equals(GameinfoGi, StringComparison.Ordinal)
            };

            List<GameInfo> mods = [];
            List<(string GameRoot, string FolderName)> conditionalFolders = [];

            foreach (var gameInfoPath in gameInfos)
            {
                var modFolder = Path.GetDirectoryName(gameInfoPath)!;

                if (Path.GetFileName(modFolder) == "core")
                {
                    // Skip loading core gameinfo directly, let it be discovered by any of the other mod folders
                    // This is needed to prevent core being found first and having highest priority
                    continue;
                }

                if (ReadGameInfo(gameInfoPath) is not { } gameInfo)
                {
                    continue;
                }

                foreach (var (key, searchPath) in gameInfo.SearchPaths)
                {
                    if (ConditionalGameSearchPathKeys.Contains(key))
                    {
                        conditionalFolders.Add((gameInfo.GameRoot, searchPath.Replace("*LANGUAGE*", "*", StringComparison.OrdinalIgnoreCase)));
                    }
                }

                // A gameinfo that does not list its own folder, such as coreH or csgoH, is not a mod of its own
                if (IsGameSearchPath(gameInfo, modFolder))
                {
                    mods.Add(gameInfo);
                }
            }

            foreach (var gameInfo in mods)
            {
                // Low violence and language folders have a gameinfo of their own, but are only mounted through the main game's
                var modName = Path.GetFileName(Path.GetDirectoryName(gameInfo.FilePath));

                if (!conditionalFolders.Exists(folder => string.Equals(folder.GameRoot, gameInfo.GameRoot, StringComparison.OrdinalIgnoreCase)
                    && FileSystemName.MatchesSimpleExpression(folder.FolderName, modName)))
                {
                    HandleGameInfo(folders, gameInfo);
                }
            }
        }

        private static string? FindSteamAppsFolder(string path)
        {
            var folder = Path.GetDirectoryName(path);

            while (folder != null && !Path.GetFileName(folder).Equals("steamapps", StringComparison.OrdinalIgnoreCase))
            {
                folder = Path.GetDirectoryName(folder);
            }

            return folder;
        }
    }
}
