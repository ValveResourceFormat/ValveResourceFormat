using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.IO.MemoryMappedFiles;
using System.Linq;
using SteamDatabase.ValvePak;
using ValveKeyValue;
using ValveResourceFormat.CompiledShader;

namespace ValveResourceFormat.IO
{
    public class GameFileLoader : IFileLoader
    {
        protected virtual List<string> SettingsGameSearchPaths { get; } = new();
        protected HashSet<string> CurrentGameSearchPaths { get; } = new();
        protected List<Package> CurrentGamePackages { get; } = new();
        private string FileName;
        private readonly Package CurrentPackage;
        private bool GamePackagesScanned;
        private readonly string[] modIdentifiers = new[] { "gameinfo.gi", "addoninfo.txt", ".sbproj" };
        private bool ProvidedGameInfosScanned;



        /// <summary>
        /// fileName is needed when used by GUI when package has not yet been resolved
        /// </summary>
        /// <param name="package"></param>
        /// <param name="fileName"></param>
        public GameFileLoader(Package package, string fileName = null)
        {
            CurrentPackage = package;
            FileName = package != null ? package.FileName : fileName;
        }

        protected HashSet<string> FindGameFoldersForWorkshopFile()
        {
            var folders = new HashSet<string>();

            // If we're loading a file from steamapps/workshop folder, attempt to discover gameinfos and load vpks for the game
            const string STEAMAPPS_WORKSHOP_CONTENT = "steamapps/workshop/content";
            var filePath = FileName.Replace('\\', '/');
            var contentIndex = filePath.IndexOf(STEAMAPPS_WORKSHOP_CONTENT, StringComparison.InvariantCultureIgnoreCase);

            if (contentIndex == -1)
            {
                return folders;
            }

            // Extract the appid from path
            var contentIndexEnd = contentIndex + STEAMAPPS_WORKSHOP_CONTENT.Length + 1;
            var slashAfterAppId = filePath.IndexOf('/', contentIndexEnd);

            if (slashAfterAppId == -1)
            {
                return folders;
            }

            var appIdString = filePath[contentIndexEnd..slashAfterAppId];

            if (!uint.TryParse(appIdString, out var appId))
            {
                return folders;
            }

#if DEBUG_FILE_LOAD
            Console.WriteLine($"Parsed appid {appId} for workshop file {filePath}");
#endif

            var steamPath = filePath[..(contentIndex + "steamapps/".Length)];
            var appManifestPath = Path.Join(steamPath, $"appmanifest_{appId}.acf");

            // Load appmanifest to get the install directory for this appid
            KVObject appManifestKv;

            try
            {
                using var appManifestStream = File.OpenRead(appManifestPath);
                appManifestKv = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(appManifestStream, KVSerializerOptions.DefaultOptions);
            }
            catch (Exception)
            {
                return folders;
            }

            var installDir = appManifestKv["installdir"].ToString();
            var gamePath = Path.Combine(steamPath, "common", installDir);

            if (!Directory.Exists(gamePath))
            {
                return folders;
            }

            // Find all the gameinfo.gi files, open them to get game paths
            var gameInfos = new FileSystemEnumerable<string>(
                gamePath,
                (ref FileSystemEntry entry) => entry.ToSpecifiedFullPath(),
                new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    MaxRecursionDepth = 5,
                })
            {
                ShouldIncludePredicate = static (ref FileSystemEntry entry) => !entry.IsDirectory && entry.FileName.Equals("gameinfo.gi", StringComparison.Ordinal)
            };

            foreach (var gameInfo in gameInfos)
            {
                var assumedGameRoot = Path.GetDirectoryName(Path.GetDirectoryName(gameInfo));
                HandleGameInfo(folders, assumedGameRoot, gameInfo);
            }

            return folders;
        }

        protected static void HandleGameInfo(HashSet<string> folders, string gameRoot, string gameinfoPath)
        {
            KVObject gameInfo;
            using (var stream = File.OpenRead(gameinfoPath))
            {
                try
                {
                    gameInfo = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream);
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine(e);
                    return;
                }
            }

            Console.WriteLine($"Found \"{gameInfo["game"]}\" from \"{gameinfoPath}\"");

            foreach (var searchPath in (IEnumerable<KVObject>)gameInfo["FileSystem"]["SearchPaths"])
            {
                if (searchPath.Name != "Game")
                {
                    continue;
                }

                folders.Add(Path.Combine(gameRoot, searchPath.Value.ToString()));
            }
        }
        protected void FindAndLoadSearchPaths(string modIdentifierPath = null)
        {
            modIdentifierPath ??= GetModIdentifierFile();

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

                folders = new HashSet<string>();

                var rootFolder = Path.GetDirectoryName(modIdentifierPath);
                var assumedGameRoot = Path.GetDirectoryName(rootFolder);

                if (Path.GetFileName(modIdentifierPath) == "gameinfo.gi")
                {
                    HandleGameInfo(folders, assumedGameRoot, modIdentifierPath);
                }
                else
                {
                    var addonsSuffix = "_addons";
                    if (assumedGameRoot.EndsWith(addonsSuffix, StringComparison.InvariantCultureIgnoreCase))
                    {
                        var mainGameDir = assumedGameRoot[..^addonsSuffix.Length];
                        if (Directory.Exists(mainGameDir))
                        {
                            folders.Add(mainGameDir);
                        }
                    }

                    folders.Add(rootFolder);
                }
            }

            foreach (var folder in folders)
            {
                // Scan for vpks in folder, same logic as in source engine
                for (var i = 1; i < 99; i++)
                {
                    var vpk = Path.Combine(folder, $"pak{i:D2}_dir.vpk");

                    if (!File.Exists(vpk))
                    {
                        break;
                    }

                    if (FileName == vpk)
                    {
#if DEBUG_FILE_LOAD
                        Console.WriteLine($"VPK \"{vpk}\" is the same we just opened, skipping");
#endif
                        continue;
                    }

                    if (SettingsGameSearchPaths.Contains(vpk))
                    {
#if DEBUG_FILE_LOAD
                        Console.WriteLine($"VPK \"{vpk}\" is already user-defined, skipping");
#endif
                        continue;
                    }

                    Console.WriteLine($"Preloading vpk \"{vpk}\"");

                    var package = new Package();
                    package.OptimizeEntriesForBinarySearch(StringComparison.OrdinalIgnoreCase);
                    package.Read(vpk);
                    CurrentGamePackages.Add(package);
                }

                if (!SettingsGameSearchPaths.Contains(folder) && CurrentGameSearchPaths.Add(folder))
                {
                    Console.WriteLine($"Added folder \"{folder}\" to game search paths");
                }
            }
        }

        protected string GetModIdentifierFile()
        {
            var directory = FileName;
            var i = 10;
            var isLastWorkshop = false;

            while (i-- > 0)
            {
                directory = Path.GetDirectoryName(directory);

#if DEBUG_FILE_LOAD
                Console.WriteLine($"Scanning \"{directory}\"");
#endif

                var currentDirectory = Path.GetFileName(directory);

                if (directory == null)
                {
                    return null;
                }

                if (currentDirectory == "steamapps")
                {
                    if (isLastWorkshop) // Found /steamapps/workshop/ folder
                    {
                        return "<VRF_WORKSHOP>";
                    }

                    return null;
                }

                isLastWorkshop = currentDirectory == "workshop";

                foreach (var modIdentifier in modIdentifiers)
                {
                    var path = Path.Combine(directory, modIdentifier);
                    if (File.Exists(path))
                    {
                        return path;
                    }
                }
            }

            return null;
        }

        public (string PathOnDisk, Package Package, PackageEntry PackageEntry) FindFile(string file, bool logNotFound = true)
        {
            var entry = CurrentPackage?.FindEntry(file);

            if (entry != null)
            {
#if DEBUG_FILE_LOAD
                Console.WriteLine($"Loaded \"{file}\" from current vpk");
#endif

                return (null, CurrentPackage, entry);
            }

            if (!GamePackagesScanned)
            {
                GamePackagesScanned = true;
                FindAndLoadSearchPaths();
            }

            var paths = new List<string>(); //TODO: Settings.Config.GameSearchPaths.ToList();
            var packages = CurrentGamePackages.ToList();

            foreach (var searchPath in paths.Where(searchPath => searchPath.EndsWith(".vpk", StringComparison.InvariantCulture)).ToList())
            {
                paths.Remove(searchPath);

                // if (!CachedPackages.TryGetValue(searchPath, out var package))
                // {
                Console.WriteLine($"Preloading vpk \"{searchPath}\"");

                var package = new Package();
                package.OptimizeEntriesForBinarySearch(StringComparison.OrdinalIgnoreCase);
                package.Read(searchPath);
                // CachedPackages[searchPath] = package;
                // }

                packages.Add(package);
            }

            foreach (var searchPath in paths.Where(searchPath => searchPath.EndsWith("gameinfo.gi", StringComparison.InvariantCulture)).ToList())
            {
                paths.Remove(searchPath);

                if (!ProvidedGameInfosScanned)
                {
                    FindAndLoadSearchPaths(searchPath);
                }
            }

            ProvidedGameInfosScanned = true;

            if (CurrentPackage != null && CurrentPackage.Entries.TryGetValue("vpk", out var vpkEntries))
            {
                foreach (var searchPath in vpkEntries)
                {
                    // if (!CachedPackages.TryGetValue(searchPath.GetFileName(), out var package))
                    // {
                    Console.WriteLine($"Preloading vpk \"{searchPath.GetFullPath()}\" from parent vpk");

                    var stream = GetPackageEntryStream(CurrentPackage, searchPath);
                    var package = new Package();
                    package.OptimizeEntriesForBinarySearch(StringComparison.OrdinalIgnoreCase);
                    package.SetFileName(searchPath.GetFileName());
                    package.Read(stream);
                    //     CachedPackages[searchPath.GetFileName()] = package;
                    // }

                    packages.Add(package);
                }
            }

            foreach (var package in packages)
            {
                entry = package?.FindEntry(file);

                if (entry != null)
                {
#if DEBUG_FILE_LOAD
                    Console.WriteLine($"Loaded \"{file}\" from preloaded vpk \"{package.FileName}\"");
#endif

                    return (null, package, entry);
                }
            }

            var path = FindResourcePath(paths.Concat(CurrentGameSearchPaths).ToList(), file, FileName);

            if (path != null)
            {
                return (path, null, null);
            }

            if (logNotFound)
            {
                Console.Error.WriteLine($"Failed to load \"{file}\". Did you configure VPK paths in settings correctly?");
            }

            if (string.IsNullOrEmpty(file) || file == "_c")
            {
                Console.Error.WriteLine($"Empty string passed to file loader here: {Environment.StackTrace}");

#if DEBUG_FILE_LOAD
                System.Diagnostics.Debugger.Break();
#endif
            }

            return (null, null, null);
        }

        public Resource LoadFile(string file)
        {
            // if (CurrentPackage == null) throw new NullReferenceException("CurrentPackage is null");
            // var entry = CurrentPackage.FindEntry(file);

            // if (entry == null)
            // {
            //     Console.WriteLine("Not found: {0}", file);
            //     return null;
            // }

            // CurrentPackage.ReadEntry(entry, out var output, false);

            // var resource = new Resource
            // {
            //     FileName = file,
            // };
            // resource.Read(new MemoryStream(output));

            // return resource;

            var resource = new Resource
            {
                FileName = file,
            };

            var foundFile = FindFile(file);

            if (foundFile.PathOnDisk != null)
            {
                resource.Read(foundFile.PathOnDisk);
                //CachedResources[file] = resource;

                return resource;
            }
            else if (foundFile.PackageEntry != null)
            {
                var stream = GetPackageEntryStream(foundFile.Package, foundFile.PackageEntry);
                resource.Read(stream);
                //CachedResources[file] = resource;

                return resource;
            }

            Console.WriteLine("Not found: {0}", file);
            return null;

        }

        public ShaderCollection LoadShader(string shaderName) => null;

        public static Stream GetPackageEntryStream(Package package, PackageEntry entry)
        {
            // Files in a vpk that isn't split
            if (!package.IsDirVPK || entry.ArchiveIndex == 32767 || entry.SmallData.Length > 0)
            {
                byte[] output;

                lock (package)
                {
                    package.ReadEntry(entry, out output, false);
                }

                return new MemoryStream(output);
            }

            var path = $"{package.FileName}_{entry.ArchiveIndex:D3}.vpk";
            var stream = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            return stream.CreateViewStream(entry.Offset, entry.Length, MemoryMappedFileAccess.Read);
        }

        protected static string FindResourcePath(IList<string> paths, string file, string currentFullPath = null)
        {
            if (currentFullPath != null)
            {
                paths = paths.OrderByDescending(x => currentFullPath.StartsWith(x, StringComparison.Ordinal)).ToList();
            }

            foreach (var searchPath in paths)
            {
                var path = Path.Combine(searchPath, file);
                path = Path.GetFullPath(path);

                if (File.Exists(path))
                {
#if DEBUG_FILE_LOAD
                    Console.WriteLine($"Loaded \"{file}\" from disk: \"{path}\"");
#endif

                    return path;
                }
            }

            return null;
        }

    }
}
