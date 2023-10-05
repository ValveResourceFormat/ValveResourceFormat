//#define DEBUG_FILE_LOAD

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
    public class GameFileLoader : IFileLoader, IDisposable
    {
        private static readonly string[] ModIdentifiers = new[]
        {
            "gameinfo.gi",
            "addoninfo.txt",
            ".sbproj",
        };

        private readonly Dictionary<string, Package> CachedPackages = new();
        private readonly Dictionary<string, ShaderCollection> CachedShaders = new();
        private readonly object CachedShadersLock = new();
        private readonly HashSet<string> CurrentGameSearchPaths = new();
        private readonly List<Package> CurrentGamePackages = new();
        private readonly string CurrentFileName;
        private readonly Package CurrentPackage;
        private bool ShaderPackagesScanned;

        /// <summary>
        /// fileName is needed when used by GUI when package has not yet been resolved
        /// </summary>
        /// <param name="currentPackage">The current package to search for files in.</param>
        /// <param name="currentFileName">The path on disk to the current file that is being opened.</param>
        public GameFileLoader(Package currentPackage, string currentFileName)
        {
            CurrentPackage = currentPackage;
            CurrentFileName = currentFileName;

            // Find gameinfo.gi by walking up from the current file, preload vpks and add folders to search paths
            FindAndLoadSearchPaths();

#if DEBUG_FILE_LOAD
            Console.Error.WriteLine("Current VPKs to search in order:");

            foreach (var searchPath in CurrentGamePackages)
            {
                Console.Error.WriteLine($"{searchPath.FileName}.vpk");
            }

            foreach (var searchPath in CurrentGameSearchPaths)
            {
                Console.Error.WriteLine(searchPath);
            }
#endif
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var package in CachedPackages.Values)
                {
                    package.Dispose();
                }

                CachedPackages.Clear();

                foreach (var package in CurrentGamePackages)
                {
                    package.Dispose();
                }

                CurrentGamePackages.Clear();

                lock (CachedShadersLock)
                {
                    foreach (var shader in CachedShaders.Values)
                    {
                        shader.Dispose();
                    }
                }
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        public virtual (string PathOnDisk, Package Package, PackageEntry PackageEntry) FindFile(string file, bool logNotFound = true)
        {
            // Check current package
            var entry = CurrentPackage?.FindEntry(file);

            if (entry != null)
            {
#if DEBUG_FILE_LOAD
                Console.WriteLine($"Loaded \"{file}\" from current vpk");
#endif

                return (null, CurrentPackage, entry);
            }

            // Check additional packages
            foreach (var package in CurrentGamePackages)
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

            // Check for any nested packages in current package (e.g. skybox vpk in a map vpk)
            if (CurrentPackage != null && CurrentPackage.Entries.TryGetValue("vpk", out var vpkEntries))
            {
                foreach (var searchPath in vpkEntries)
                {
                    if (!CachedPackages.TryGetValue(searchPath.GetFileName(), out var package))
                    {
                        Console.WriteLine($"Preloading vpk \"{searchPath.GetFullPath()}\" from parent vpk");

                        var stream = GetPackageEntryStream(CurrentPackage, searchPath);
                        package = new Package();
                        package.OptimizeEntriesForBinarySearch(StringComparison.OrdinalIgnoreCase);
                        package.SetFileName(searchPath.GetFileName());
                        package.Read(stream);
                        CachedPackages[searchPath.GetFileName()] = package;
                    }

                    entry = package?.FindEntry(file);

                    if (entry != null)
                    {
#if DEBUG_FILE_LOAD
                        Console.WriteLine($"Loaded \"{file}\" from nested vpk \"{package.FileName}\"");
#endif

                        return (null, package, entry);
                    }
                }
            }

            // As a last resort, check on disk
            var path = FindResourcePath(CurrentGameSearchPaths, file, CurrentFileName);

            if (path != null)
            {
                return (path, null, null);
            }

            if (logNotFound)
            {
                Console.Error.WriteLine($"Failed to load \"{file}\". Did you configure VPK paths in settings correctly?");
            }

#if DEBUG
            if (string.IsNullOrEmpty(file) || file == "_c")
            {
                Console.Error.WriteLine($"Empty string passed to file loader here: {Environment.StackTrace}");

#if DEBUG_FILE_LOAD
                System.Diagnostics.Debugger.Break();
#endif
            }

#endif

            return (null, null, null);
        }

        protected virtual ShaderCollection LoadShaderFromDisk(string shaderName)
        {
            if (!ShaderPackagesScanned)
            {
                ShaderPackagesScanned = true;
                FindAndLoadShaderPackages();
            }

            var collection = new ShaderCollection();

            bool TryLoadShader(VcsProgramType programType, VcsPlatformType platformType, VcsShaderModelType modelType)
            {
                var shaderFile = new ShaderFile();
                var path = Path.Join("shaders", "vfx", ShaderUtilHelpers.ComputeVCSFileName(shaderName, programType, platformType, modelType));
                var foundFile = FindFile(path, logNotFound: false);

                if (foundFile.PathOnDisk != null)
                {
                    var stream = File.OpenRead(foundFile.PathOnDisk);
                    shaderFile.Read(path, stream);
                }
                else if (foundFile.PackageEntry != null)
                {
                    var stream = GetPackageEntryStream(foundFile.Package, foundFile.PackageEntry);
                    shaderFile.Read(path, stream);
                }

                if (shaderFile.VcsPlatformType == platformType)
                {
                    collection.Add(shaderFile);
                    return true;
                }

                return false;
            }

            var selectedPlatformType = VcsPlatformType.Undetermined;
            var selectedModelType = VcsShaderModelType.Undetermined;

            for (var platformType = (VcsPlatformType)0; platformType < VcsPlatformType.Undetermined && selectedPlatformType == VcsPlatformType.Undetermined; platformType++)
            {
                for (var modelType = VcsShaderModelType._60; modelType > VcsShaderModelType._20; modelType--)
                {
                    if (TryLoadShader(VcsProgramType.Features, platformType, modelType))
                    {
                        selectedPlatformType = platformType;
                        selectedModelType = modelType;
                        break;
                    }
                }
            }

            if (selectedPlatformType == VcsPlatformType.Undetermined)
            {
                Console.Error.WriteLine($"Failed to find shader \"{shaderName}\".");

                return collection;
            }

            for (var programType = VcsProgramType.VertexShader; programType < VcsProgramType.Undetermined; programType++)
            {
                TryLoadShader(programType, selectedPlatformType, selectedModelType);
            }

            return collection;
        }

        public ShaderCollection LoadShader(string shaderName)
        {
            lock (CachedShadersLock)
            {
                if (CachedShaders.TryGetValue(shaderName, out var shader))
                {
                    return shader;
                }

                shader = LoadShaderFromDisk(shaderName);
                CachedShaders.Add(shaderName, shader);
                return shader;
            }
        }

        public virtual Resource LoadFile(string file)
        {
            var resource = new Resource
            {
                FileName = file,
            };

            var foundFile = FindFile(file);

            if (foundFile.PathOnDisk != null)
            {
                resource.Read(foundFile.PathOnDisk);
                return resource;
            }
            else if (foundFile.PackageEntry != null)
            {
                var stream = GetPackageEntryStream(foundFile.Package, foundFile.PackageEntry);
                resource.Read(stream);
                return resource;
            }

            return null;
        }

        private static void HandleGameInfo(HashSet<string> folders, string gameRoot, string gameinfoPath)
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

        public bool AddDiskPathToSearch(string searchPath)
        {
            var success = CurrentGameSearchPaths.Add(searchPath);

            if (success)
            {
                Console.WriteLine($"Added folder \"{searchPath}\" to game search paths");
            }

            return success;
        }

        public void AddPackageToSearch(string searchPath)
        {
            if (!CachedPackages.TryGetValue(searchPath, out var package))
            {
                Console.WriteLine($"Preloading vpk \"{searchPath}\"");

                package = new Package();
                package.OptimizeEntriesForBinarySearch(StringComparison.OrdinalIgnoreCase);
                package.Read(searchPath);
                CachedPackages[searchPath] = package;

                AddPackageToSearch(package);
            }
        }

        public void AddPackageToSearch(Package package)
        {
            CurrentGamePackages.Add(package);
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

                    if (CurrentFileName == vpk)
                    {
#if DEBUG_FILE_LOAD
                        Console.WriteLine($"VPK \"{vpk}\" is the same we just opened, skipping");
#endif
                        continue;
                    }

                    AddPackageToSearch(vpk);
                }

                AddDiskPathToSearch(folder);
            }
        }

        private string GetModIdentifierFile()
        {
            var directory = CurrentFileName;
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

                foreach (var modIdentifier in ModIdentifiers)
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

        private HashSet<string> FindGameFoldersForWorkshopFile()
        {
            var folders = new HashSet<string>();

            // If we're loading a file from steamapps/workshop folder, attempt to discover gameinfos and load vpks for the game
            const string STEAMAPPS_WORKSHOP_CONTENT = "steamapps/workshop/content";
            var filePath = CurrentFileName.Replace('\\', '/');
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

        private static string FindResourcePath(HashSet<string> paths, string file, string currentFullPath = null)
        {
            if (currentFullPath != null)
            {
                // TODO: Remove this sorting
                paths = paths.OrderByDescending(x => currentFullPath.StartsWith(x, StringComparison.Ordinal)).ToHashSet();
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

        private void FindAndLoadShaderPackages()
        {
            foreach (var folder in CurrentGameSearchPaths)
            {
                for (var platformType = (VcsPlatformType)0; platformType < VcsPlatformType.Undetermined; platformType++)
                {
                    var shaderName = $"shaders_{platformType.ToString().ToLowerInvariant()}_dir.vpk";
                    var vpk = Path.Combine(folder, shaderName);

                    if (File.Exists(vpk))
                    {
                        Console.WriteLine($"Preloading vpk \"{vpk}\"");

                        var package = new Package();
                        package.OptimizeEntriesForBinarySearch(StringComparison.OrdinalIgnoreCase);
                        package.Read(vpk);
                        CurrentGamePackages.Add(package);

                        break; // One for each folder
                    }
                }
            }
        }

        /// <summary>
        /// Do not use this method, it will be removed in the future in favor of a method in the ValvePak library.
        /// </summary>
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
    }
}
