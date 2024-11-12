//#define DEBUG_FILE_LOAD

using System.Diagnostics;
using System.IO;
using System.IO.Enumeration;
using System.Threading;
using SteamDatabase.ValvePak;
using ValveKeyValue;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.IO
{
    public class GameFileLoader : IFileLoader, IDisposable
    {
        private const string AddonsSuffix = "_addons";
        private const string GameinfoGi = "gameinfo.gi";
        public const string CompiledFileSuffix = "_c";

        private static readonly string[] ModIdentifiers =
        [
            GameinfoGi,
            "addoninfo.txt",
            ".sbproj",
        ];

        private readonly Dictionary<string, ShaderCollection> CachedShaders = [];
        private readonly Lock CachedShadersLock = new();
        private readonly HashSet<string> CurrentGameSearchPaths = [];
        private readonly List<Package> CurrentGamePackages = [];
        private readonly string CurrentFileName;
        private string PreferredAddonFolderOnDisk;
        private bool ShaderPackagesScanned;
        private bool AttemptToLoadWorkshopDependencies;
        private bool StoredSurfacePropertyStringTokens;

        public Package CurrentPackage { get; set; }

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
            if (CurrentFileName != null)
            {
                FindAndLoadSearchPaths();
            }

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

                    CachedShaders.Clear();
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

            // For addons, always check addon folder first before checking all the other game search paths
            if (PreferredAddonFolderOnDisk != null)
            {
                var addonPath = Path.Combine(PreferredAddonFolderOnDisk, file);
                addonPath = Path.GetFullPath(addonPath);

                if (File.Exists(addonPath))
                {
#if DEBUG_FILE_LOAD
                    Console.WriteLine($"Loaded addon file \"{file}\" from disk: \"{addonPath}\"");
#endif

                    return (addonPath, null, null);
                }
            }

            if (AttemptToLoadWorkshopDependencies && CurrentPackage != null)
            {
                AttemptToLoadWorkshopDependencies = false;

#if DEBUG_FILE_LOAD
                Console.WriteLine($"Attempting to find workshop dependencies while loading \"{file}\"");
#endif

                entry = CurrentPackage.FindEntry("addoninfo.txt");

                if (entry != null)
                {
                    try
                    {
                        using var stream = GetPackageEntryStream(CurrentPackage, entry);
                        var addonInfo = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream);

                        // Completely assuming that workshop files are always packed as /appid/ugcid/ugcid.vpk
                        var workshopRoot = Path.GetDirectoryName(Path.GetDirectoryName(CurrentFileName.AsSpan()));

                        foreach (var dependency in (IEnumerable<KVObject>)addonInfo["Dependencies"])
                        {
                            var dependencyId = (uint)dependency.Value;
                            var dependencyVpkPath = Path.Join(workshopRoot, $"{dependencyId}", $"{dependencyId}.vpk");

                            if (File.Exists(dependencyVpkPath))
                            {
                                AddPackageToSearch(dependencyVpkPath);
                            }
                        }
                    }
                    catch
                    {
                        //
                    }
                }
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

            // As a last resort, check on disk
            var path = FindFileOnDisk(file);

            if (path != null)
            {
                return (path, null, null);
            }

            if (logNotFound)
            {
                Console.Error.WriteLine($"Failed to load \"{file}\"");
            }

#if DEBUG
            if (string.IsNullOrEmpty(file) || file == CompiledFileSuffix)
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

                try
                {
                    var path = Path.Join("shaders", "vfx", ShaderUtilHelpers.ComputeVCSFileName(shaderName, programType, platformType, modelType));
                    var foundFile = FindFile(path, logNotFound: false);

                    if (foundFile.PathOnDisk != null)
                    {
                        shaderFile.Read(foundFile.PathOnDisk);
                    }
                    else if (foundFile.PackageEntry != null)
                    {
                        var stream = GetPackageEntryStream(foundFile.Package, foundFile.PackageEntry);
                        shaderFile.Read(path, stream);
                    }

                    if (shaderFile.VcsPlatformType == platformType)
                    {
                        collection.Add(shaderFile);
                        shaderFile = null;
                        return true;
                    }
                }
                finally
                {
                    shaderFile?.Dispose();
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

        public virtual Resource LoadFileCompiled(string file) => LoadFile(string.Concat(file, CompiledFileSuffix));

        public virtual Resource LoadFile(string file)
        {
            var resource = new Resource
            {
                FileName = file,
            };
            Resource resourceToReturn = null;

            try
            {
                var foundFile = FindFile(file);

                if (foundFile.PathOnDisk != null)
                {
                    resource.Read(foundFile.PathOnDisk);
                    resourceToReturn = resource;
                    resource = null;
                }
                else if (foundFile.PackageEntry != null)
                {
                    var stream = GetPackageEntryStream(foundFile.Package, foundFile.PackageEntry);
                    resource.Read(stream);
                    resourceToReturn = resource;
                    resource = null;
                }
            }
            finally
            {
                resource?.Dispose();
            }

            return resourceToReturn;
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

        public void EnsureStringTokenGameKeys()
        {
            if (!StoredSurfacePropertyStringTokens)
            {
                using var vsurf = LoadFileCompiled("surfaceproperties/surfaceproperties.vsurf");
                if (vsurf is not null && vsurf.DataBlock is BinaryKV3 kv3)
                {
                    var surfacePropertiesList = kv3.Data.GetArray("SurfacePropertiesList");
                    foreach (var surface in surfacePropertiesList)
                    {
                        var name = surface.GetProperty<string>("surfacePropertyName");
                        var hash = StringToken.Store(name.ToLowerInvariant());
                        Debug.Assert(
                            hash == surface.GetUnsignedIntegerProperty("m_nameHash"),
                            "Stored surface property hash should be the same as the calculated one."
                        );
                    }
                }

                StoredSurfacePropertyStringTokens = true;
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

        public bool RemoveDiskPathFromSearch(string searchPath)
        {
            var success = CurrentGameSearchPaths.Remove(searchPath);

            if (success)
            {
                Console.WriteLine($"Removed folder \"{searchPath}\" from game search paths");
            }

            return success;
        }

        public Package AddPackageToSearch(string searchPath)
        {
            Console.WriteLine($"Preloading vpk \"{searchPath}\"");

            var package = new Package();
            package.OptimizeEntriesForBinarySearch(StringComparison.OrdinalIgnoreCase);
            package.Read(searchPath);

            AddPackageToSearch(package);

            return package;
        }

        public void AddPackageToSearch(Package package)
        {
            CurrentGamePackages.Add(package);
        }

        public bool RemovePackageFromSearch(Package package)
        {
            return CurrentGamePackages.Remove(package);
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

                var rootFolder = Path.GetDirectoryName(modIdentifierPath);
                var assumedGameRoot = Path.GetDirectoryName(rootFolder);

                if (Path.GetFileName(modIdentifierPath) == GameinfoGi)
                {
                    folders = [];

                    HandleGameInfo(folders, assumedGameRoot, modIdentifierPath);
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
                            HandleGameInfo(folders, Path.GetDirectoryName(mainGameDir), mainGameInfo);
                        }
                        else if (Directory.Exists(mainGameDir))
                        {
                            folders.Add(mainGameDir);
                        }
                    }

                    PreferredAddonFolderOnDisk = rootFolder;
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

            if (!Path.IsPathFullyQualified(directory))
            {
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
            }

            return null;
        }

        private HashSet<string> FindGameFoldersForWorkshopFile()
        {
            // If we're loading a file from steamapps/workshop folder, attempt to discover gameinfos and load vpks for the game
            const string STEAMAPPS_WORKSHOP_CONTENT = "steamapps/workshop/content";
            var filePath = CurrentFileName.Replace('\\', '/');
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

            // SteamVR addons have addoninfo.txt file which contain dependency workshop ids,
            // seemingly other games do not have this.
            AttemptToLoadWorkshopDependencies = appId == 250820;

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
            var gamePath = Path.Combine(steamPath, "common", installDir);

            if (!Directory.Exists(gamePath))
            {
                return [];
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
                ShouldIncludePredicate = static (ref FileSystemEntry entry) => !entry.IsDirectory && entry.FileName.Equals(GameinfoGi, StringComparison.Ordinal)
            };

            var folders = new HashSet<string>();

            foreach (var gameInfo in gameInfos)
            {
                var directory = Path.GetDirectoryName(gameInfo);
                var modName = Path.GetFileName(directory);
                var assumedGameRoot = Path.GetDirectoryName(directory);

                if (modName == "core")
                {
                    // Skip loading core gameinfo directly, let it be discovered by any of the other mod folders
                    // This is needed to prevent core being found first and having highest priority
                    continue;
                }

                HandleGameInfo(folders, assumedGameRoot, gameInfo);
            }

            return folders;
        }

        private string FindFileOnDisk(string file)
        {
            foreach (var folder in CurrentGameSearchPaths)
            {
                var path = Path.Combine(folder, file);
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
                        AddPackageToSearch(vpk);

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
            lock (package)
            {
                return package.GetMemoryMappedStreamIfPossible(entry);
            }
        }
    }
}
