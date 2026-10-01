//#define DEBUG_FILE_LOAD

using System.IO;
using System.IO.Enumeration;
using ValveKeyValue;
using KVObject = ValveKeyValue.KVObject;

namespace ValveResourceFormat.IO
{
    public partial class GameFileLoader
    {
        private void HandleGameInfo(HashSet<string> folders, string gameinfoPath)
        {
            if (TryReadGameInfo(gameinfoPath) is { } gameInfo)
            {
                HandleGameInfo(folders, gameinfoPath, gameInfo);
            }
        }

        private static GameInfo? TryReadGameInfo(string gameinfoPath)
        {
            try
            {
                return GameInfo.Read(gameinfoPath);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e);
                return null;
            }
        }

        private void HandleGameInfo(HashSet<string> folders, string gameinfoPath, GameInfo gameInfo)
        {
            Console.WriteLine($"Found \"{gameInfo.Name}\" from \"{gameinfoPath}\"");

            // The walk starts at the file being opened, so the first one found is the mod it belongs to.
            GameInfo ??= gameInfo;

            // Only games that opt in mount the dependencies listed in an addon's addoninfo.txt
            if (gameInfo.AllowAddonDependencies)
            {
                AddonDependenciesPending = true;

                // Workshop dependencies of a local addon are installed next to the game, in steamapps/workshop/content/appid
                if (WorkshopContentFolder == null
                    && gameInfo.SteamAppId is { } steamAppId
                    && FindSteamAppsFolder(gameinfoPath) is { } steamApps)
                {
                    WorkshopContentFolder = Path.Join(steamApps, "workshop", "content", steamAppId);
                }
            }

            foreach (var (key, searchPath) in gameInfo.SearchPaths)
            {
                if (key.Equals("Game", StringComparison.OrdinalIgnoreCase) || key.Equals("Game_NonTools", StringComparison.OrdinalIgnoreCase))
                {
                    folders.Add(Path.Combine(gameInfo.GameRoot, searchPath));
                }
                else if (key == "OfficialAddonRoot")
                {
                    CurrentGameOfficialAddonsPaths.Add(Path.Combine(gameInfo.GameRoot, searchPath));
                }
                else if (key == "AddonRoot")
                {
                    CurrentGameAddonsPaths.Add(Path.Combine(gameInfo.GameRoot, searchPath));
                }
            }
        }

        /// <summary>
        /// Finds and loads search paths from gameinfo.gi files.
        /// </summary>
        public void FindAndLoadSearchPaths(string? modIdentifierPath = null)
        {
            modIdentifierPath ??= GetModIdentifierFile() ?? FindGameInfoMountingCurrentFolder();

            HashSet<string> folders;

            if (modIdentifierPath == "<VRF_WORKSHOP>")
            {
                folders = FindGameFoldersForWorkshopFile();
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
                    folders = [];

                    HandleGameInfo(folders, modIdentifierPath);
                }
                else
                {
                    folders = FindGameFoldersForWorkshopFile();

                    if (assumedGameRoot.EndsWith(AddonsSuffix, StringComparison.InvariantCultureIgnoreCase))
                    {
                        var mainGameDir = assumedGameRoot[..^AddonsSuffix.Length];
                        var mainGameInfo = Path.Join(mainGameDir, GameinfoGi);

                        if (File.Exists(mainGameInfo))
                        {
                            HandleGameInfo(folders, mainGameInfo);
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
        }

        private string? GetModIdentifierFile()
        {
            var directory = CurrentFileName!;
            string? childDirectory = null;
            var i = 10;
            var isLastWorkshop = false;

            // Check for slash here to support paths on linux under wine
            if (!Path.IsPathFullyQualified(directory) && !directory.StartsWith('/'))
            {
#if DEBUG_FILE_LOAD
                Console.WriteLine($"Not a fully qualified path \"{directory}\", not checking for mod");
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
                Console.WriteLine($"Scanning \"{directory}\"");
#endif

                if (directory.EndsWith(AddonsSuffix, StringComparison.InvariantCultureIgnoreCase))
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
            var childDirectory = CurrentFileName!;

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

        private static bool GameInfoMountsFolder(string gameinfoPath, string folderName)
        {
            try
            {
                return GameInfo.Read(gameinfoPath).MountsFolder(folderName);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or KeyValueException)
            {
                return false;
            }
        }

        private HashSet<string> FindGameFoldersForWorkshopFile()
        {
            // If we're loading a file from steamapps/workshop folder, attempt to discover gameinfos and load vpks for the game
            const string STEAMAPPS_WORKSHOP_CONTENT = "steamapps/workshop/content";
            var filePath = CurrentFileName!.Replace('\\', '/');
            var contentIndex = filePath.IndexOf(STEAMAPPS_WORKSHOP_CONTENT, StringComparison.InvariantCultureIgnoreCase);

            if (contentIndex == -1)
            {
                return [];
            }

            // Extract the appid from path
            var contentIndexEnd = contentIndex + STEAMAPPS_WORKSHOP_CONTENT.Length + 1;
            var slashAfterAppId = filePath.IndexOf('/', contentIndexEnd);

            if (slashAfterAppId == -1)
            {
                return [];
            }

            var appIdString = filePath[contentIndexEnd..slashAfterAppId];

            if (!uint.TryParse(appIdString, out var appId))
            {
                return [];
            }

#if DEBUG_FILE_LOAD
            Console.WriteLine($"Parsed appid {appId} for workshop file {filePath}");
#endif

            var steamPath = filePath[..(contentIndex + "steamapps/".Length)];
            var appManifestPath = Path.Join(steamPath, $"appmanifest_{appId}.acf");

            WorkshopContentFolder = filePath[..slashAfterAppId];

            // Load appmanifest to get the install directory for this appid
            KVObject appManifestKv;

            try
            {
                using var appManifestStream = File.OpenRead(appManifestPath);
                appManifestKv = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(appManifestStream, KVSerializerOptions.DefaultOptions);
            }
            catch
            {
                return [];
            }

            var installDir = appManifestKv["installdir"].ToString();

            if (installDir == null)
            {
                return [];
            }

            var gamePath = Path.Combine(steamPath, "common", installDir);

            if (!Directory.Exists(gamePath))
            {
                return [];
            }

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

            var folders = new HashSet<string>();

            foreach (var gameInfoPath in gameInfos)
            {
                var modName = Path.GetFileName(Path.GetDirectoryName(gameInfoPath));

                if (modName == "core")
                {
                    // Skip loading core gameinfo directly, let it be discovered by any of the other mod folders
                    // This is needed to prevent core being found first and having highest priority
                    continue;
                }

                // Language and low violence overlays would mount their own folder in front of the game's
                if (TryReadGameInfo(gameInfoPath) is { LayeredOnMod: null } gameInfo)
                {
                    HandleGameInfo(folders, gameInfoPath, gameInfo);
                }
            }

            return folders;
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
