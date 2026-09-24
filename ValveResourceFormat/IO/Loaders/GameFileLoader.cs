//#define DEBUG_FILE_LOAD

using System.Diagnostics;
using System.IO;
using System.IO.Enumeration;
using System.Threading;
using ValveKeyValue;
using ValvePak;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using KVObject = ValveKeyValue.KVObject;

namespace ValveResourceFormat.IO
{
    /// <summary>
    /// Loads compiled game resources from VPK packages and disk with automatic path resolution.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class is intended for loading compiled resources (such as models, textures, materials)
    /// that need to be parsed as <see cref="Resource"/> objects. It handles resource lookup
    /// across VPK packages and loose files on disk, automatically discovering game search paths
    /// from gameinfo.gi files.
    /// </para>
    /// <para>
    /// To read raw file bytes from a VPK package, use <c>Package.ReadEntry</c> instead.
    /// </para>
    /// </remarks>
    public class GameFileLoader : IFileLoader, IDisposable
    {
        private const string AddonsSuffix = "_addons";
        private const string GameinfoGi = "gameinfo.gi";
        private const string AddonInfoTxt = "addoninfo.txt";

        /// <summary>
        /// The suffix added to compiled file names.
        /// </summary>
        public const string CompiledFileSuffix = "_c";

        private static readonly string[] ModIdentifiers =
        [
            GameinfoGi,
            AddonInfoTxt,
            ".sbproj",
        ];

        /// <summary>Gets the game declared by the nearest <c>gameinfo.gi</c>, or <see langword="null"/> when none was found.</summary>
        public string? GameName { get; private set; }

        private readonly Dictionary<string, ShaderCollection> CachedShaders = [];
        private readonly Lock CachedShadersLock = new();
        private readonly HashSet<string> CurrentGameSearchPaths = [];
        private readonly HashSet<string> CurrentGameOfficialAddonsPaths = [];
        private readonly HashSet<string> CurrentGameAddonsPaths = [];
        private readonly List<Package> CurrentGamePackages = [];

        // Addons are mounted in front of the game's own files, so they override them
        private readonly List<Package> CurrentAddonPackages = [];
        private readonly List<string> CurrentAddonSearchPaths = [];

        private readonly string? CurrentFileName;
        private string? PreferredAddonFolderOnDisk;
        private string? WorkshopContentFolder;
        private bool ShaderPackagesScanned;
        private volatile bool AddonDependenciesPending;
        private readonly Lock AddonDependenciesLock = new();
        private bool StoredSurfacePropertyStringTokens;

        /// <summary>
        /// Gets or sets the current package being processed.
        /// </summary>
        public Package? CurrentPackage { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="GameFileLoader"/> class.
        /// </summary>
        /// <param name="currentPackage">The current package to search for files in.</param>
        /// <param name="currentFileName">The path on disk to the current file that is being opened.</param>
        /// <remarks>
        /// fileName is needed when used by GUI when package has not yet been resolved.
        /// </remarks>
        public GameFileLoader(Package? currentPackage, string? currentFileName)
        {
            CurrentPackage = currentPackage;
            CurrentFileName = currentFileName;

            // Find gameinfo.gi by walking up from the current file, preload vpks and add folders to search paths
            if (CurrentFileName != null)
            {
                FindAndLoadSearchPaths();
                FindAndLoadOfficialGameAddonPackage();
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

        /// <summary>
        /// Releases resources used by this instance.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var package in CurrentAddonPackages)
                {
                    package.Dispose();
                }

                foreach (var package in CurrentGamePackages)
                {
                    package.Dispose();
                }

                CurrentAddonPackages.Clear();
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

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Finds a file in packages or on disk.
        /// </summary>
        /// <param name="file">The file path to find.</param>
        /// <param name="logNotFound">Whether to log if file is not found.</param>
        /// <returns>A tuple containing the path on disk, package, and package entry if found.</returns>
        public virtual (string? PathOnDisk, Package? Package, PackageEntry? PackageEntry) FindFile(string file, bool logNotFound = true)
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
                var addonPath = FindFileInFolder(PreferredAddonFolderOnDisk, file);

                if (addonPath != null)
                {
                    return (addonPath, null, null);
                }
            }

            if (AddonDependenciesPending && (CurrentPackage != null || PreferredAddonFolderOnDisk != null))
            {
                // Files are looked up in parallel, the addon lists must be complete before anyone searches them
                lock (AddonDependenciesLock)
                {
                    if (AddonDependenciesPending)
                    {
#if DEBUG_FILE_LOAD
                        Console.WriteLine($"Attempting to find addon dependencies while loading \"{file}\"");
#endif

                        LoadAddonDependencies();
                        AddonDependenciesPending = false;
                    }
                }
            }

            // Newest first, each addon is mounted in front of the ones before it
            for (var i = CurrentAddonPackages.Count - 1; i >= 0; i--)
            {
                var package = CurrentAddonPackages[i];
                entry = package.FindEntry(file);

                if (entry != null)
                {
#if DEBUG_FILE_LOAD
                    Console.WriteLine($"Loaded \"{file}\" from addon vpk \"{package.FileName}\"");
#endif

                    return (null, package, entry);
                }
            }

            foreach (var folder in CurrentAddonSearchPaths)
            {
                var addonPath = FindFileInFolder(folder, file);

                if (addonPath != null)
                {
                    return (addonPath, null, null);
                }
            }

            // Check additional packages
            foreach (var package in CurrentGamePackages)
            {
                entry = package?.FindEntry(file);

                if (entry != null)
                {
#if DEBUG_FILE_LOAD
                    Debug.Assert(package != null);
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

        /// <summary>
        /// Loads a shader from disk by finding all its program files.
        /// </summary>
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
                var shaderFile = new VfxProgramData();

                try
                {
                    var fileName = ShaderUtilHelpers.ComputeVCSFileName(shaderName, programType, platformType, modelType);
                    var path = Path.Join("shaders", "vfx", fileName);
                    var foundFile = FindFile(path, logNotFound: false);

                    if (foundFile.PathOnDisk != null)
                    {
                        shaderFile.Read(foundFile.PathOnDisk);
                    }
                    else if (foundFile.PackageEntry != null)
                    {
                        var stream = GetPackageEntryStream(foundFile.Package!, foundFile.PackageEntry);
                        shaderFile.Read(fileName, stream);
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

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public Stream? GetFileStream(string file)
        {
            var foundFile = FindFile(file);

            if (foundFile.PathOnDisk != null)
            {
                return File.OpenRead(foundFile.PathOnDisk);
            }
            else if (foundFile.PackageEntry != null)
            {
                return GetPackageEntryStream(foundFile.Package!, foundFile.PackageEntry);
            }
            else
            {
                return null;
            }
        }

        /// <inheritdoc/>
        public virtual Resource? LoadFileCompiled(string file) => LoadFile(string.Concat(file, CompiledFileSuffix));

        /// <inheritdoc/>
        public virtual Resource? LoadFile(string file)
        {
            var resource = new Resource
            {
                FileName = file,
            };
            Resource? resourceToReturn = null;

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
                    var stream = GetPackageEntryStream(foundFile.Package!, foundFile.PackageEntry);
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

        private void HandleGameInfo(HashSet<string> folders, string gameRoot, string gameinfoPath)
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

            gameInfo.TryGetValue("game", out var gameName);
            Console.WriteLine($"Found \"{gameName}\" from \"{gameinfoPath}\"");

            // The walk starts at the file being opened, so the first one found is the mod it belongs to.
            GameName ??= gameName?.ToString();

            var fileSystem = gameInfo["FileSystem"];

            // Only games that opt in mount the dependencies listed in an addon's addoninfo.txt
            if (fileSystem.GetBooleanProperty("AllowAddonDependencies"))
            {
                AddonDependenciesPending = true;

                // Workshop dependencies of a local addon are installed next to the game, in steamapps/workshop/content/appid
                if (WorkshopContentFolder == null
                    && fileSystem.TryGetValue("SteamAppId", out var steamAppId)
                    && FindSteamAppsFolder(gameinfoPath) is { } steamApps)
                {
                    WorkshopContentFolder = Path.Join(steamApps, "workshop", "content", steamAppId.ToString());
                }
            }

            foreach (var (key, searchPath) in fileSystem["SearchPaths"])
            {
                if (key == "Game")
                {
                    folders.Add(Path.Combine(gameRoot, searchPath.ToString()!));
                }
                else if (key == "OfficialAddonRoot")
                {
                    CurrentGameOfficialAddonsPaths.Add(Path.Combine(gameRoot, searchPath.ToString()!));
                }
                else if (key == "AddonRoot")
                {
                    CurrentGameAddonsPaths.Add(Path.Combine(gameRoot, searchPath.ToString()!));
                }
            }
        }

        /// <summary>
        /// Ensures surface property string tokens are loaded and stored.
        /// </summary>
        public void EnsureStringTokenGameKeys()
        {
            if (!StoredSurfacePropertyStringTokens)
            {
                using var vsurf = LoadFileCompiled("surfaceproperties/surfaceproperties.vsurf");
                if (vsurf is not null && vsurf.DataBlock is BinaryKV3 kv3)
                {
                    var surfacePropertiesList = kv3.Data.Root.GetArray("SurfacePropertiesList");
                    foreach (var surface in surfacePropertiesList)
                    {
                        var name = surface.GetStringProperty("surfacePropertyName");
                        var hash = StringToken.Store(name);
                        Debug.Assert(
                            hash == surface.GetUnsignedIntegerProperty("m_nameHash"),
                            "Stored surface property hash should be the same as the calculated one."
                        );
                    }
                }

                StoredSurfacePropertyStringTokens = true;
            }
        }

        /// <summary>
        /// Adds a disk path to the search paths.
        /// </summary>
        public bool AddDiskPathToSearch(string searchPath)
        {
            var success = CurrentGameSearchPaths.Add(searchPath);

            if (success)
            {
                Console.WriteLine($"Added folder \"{searchPath}\" to game search paths");
            }

            return success;
        }

        /// <summary>
        /// Removes a disk path from the search paths.
        /// </summary>
        public bool RemoveDiskPathFromSearch(string searchPath)
        {
            var success = CurrentGameSearchPaths.Remove(searchPath);

            if (success)
            {
                Console.WriteLine($"Removed folder \"{searchPath}\" from game search paths");
            }

            return success;
        }

        /// <summary>
        /// Loads and adds a VPK package to the search paths.
        /// </summary>
        public Package AddPackageToSearch(string searchPath)
        {
            var package = ReadPackage(searchPath);

            AddPackageToSearch(package);

            return package;
        }

        /// <summary>
        /// Adds an already loaded package to the search paths.
        /// </summary>
        public void AddPackageToSearch(Package package)
        {
            CurrentGamePackages.Add(package);
        }

        /// <summary>
        /// Removes a package from the search paths.
        /// </summary>
        public bool RemovePackageFromSearch(Package package)
        {
            return CurrentAddonPackages.Remove(package) || CurrentGamePackages.Remove(package);
        }

        private static Package ReadPackage(string searchPath)
        {
            Console.WriteLine($"Preloading vpk \"{searchPath}\"");

            var package = new Package();
            package.OptimizeEntriesForBinarySearch(StringComparison.OrdinalIgnoreCase);
            package.Read(searchPath);

            return package;
        }

        /// <summary>
        /// Mounts an addon vpk in front of the game's own vpks.
        /// </summary>
        private void AddAddonPackageToSearch(string searchPath)
        {
            CurrentAddonPackages.Add(ReadPackage(searchPath));
        }

        /// <summary>
        /// Mounts a loose addon folder, and the pak vpks inside it, in front of the game's own files.
        /// </summary>
        private void AddAddonFolderToSearch(string folder)
        {
            foreach (var vpk in EnumeratePakVpks(folder))
            {
                AddAddonPackageToSearch(vpk);
            }

            CurrentAddonSearchPaths.Insert(0, folder);
            Console.WriteLine($"Added addon folder \"{folder}\" to search paths");
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
                            HandleGameInfo(folders, Path.GetDirectoryName(mainGameDir)!, mainGameInfo);
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
            KVObject gameInfo;

            try
            {
                using var stream = File.OpenRead(gameinfoPath);
                gameInfo = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or KeyValueException)
            {
                return false;
            }

            if (!gameInfo.TryGetValue("FileSystem", out var fileSystem) || !fileSystem.TryGetValue("SearchPaths", out var searchPaths))
            {
                return false;
            }

            foreach (var (_, searchPath) in searchPaths)
            {
                if (string.Equals(searchPath.ToString(), folderName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void FindAndLoadOfficialGameAddonPackage()
        {
            if (CurrentFileName == null || CurrentGameOfficialAddonsPaths.Count == 0)
            {
                return;
            }

            // Check if vpk with same file name exists in addons folder
            var fileName = Path.GetFileNameWithoutExtension(CurrentFileName);

            foreach (var officialAddonPath in CurrentGameOfficialAddonsPaths)
            {
                var addonFolder = Path.Combine(officialAddonPath, fileName);

                if (!Directory.Exists(addonFolder))
                {
                    continue;
                }

                if (FindAddonVpk(addonFolder, fileName) is { } vpk)
                {
                    AddAddonPackageToSearch(vpk);
                    break;
                }
            }
        }

        /// <summary>
        /// Finds an addon packed as folder/name.vpk, or split into chunks with folder/name_dir.vpk.
        /// </summary>
        private static string? FindAddonVpk(string folder, string name)
        {
            var vpk = Path.Join(folder, $"{name}.vpk");

            if (File.Exists(vpk))
            {
                return vpk;
            }

            vpk = Path.Join(folder, $"{name}_dir.vpk");

            return File.Exists(vpk) ? vpk : null;
        }

        private void LoadAddonDependencies()
        {
            KVObject? addonInfo;

            try
            {
                addonInfo = ReadAddonInfo();
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Failed to read addoninfo.txt: {e.Message}");
                return;
            }

            if (addonInfo == null || !addonInfo.TryGetValue("Dependencies", out var dependencies))
            {
                return;
            }

            // Dependencies are mounted directly, so their own dependencies are not followed
            foreach (var dependency in dependencies.Values)
            {
                var dependencyName = dependency.ToString();

                if (string.IsNullOrEmpty(dependencyName))
                {
                    continue;
                }

                if (ulong.TryParse(dependencyName, out var dependencyId))
                {
                    if (WorkshopContentFolder == null)
                    {
                        continue;
                    }

                    if (FindAddonVpk(Path.Join(WorkshopContentFolder, dependencyName), dependencyName) is { } vpk)
                    {
                        AddAddonPackageToSearch(vpk);
                    }
                    else
                    {
                        Console.Error.WriteLine($"Addon dependency {dependencyId} is not installed");
                    }

                    continue;
                }

                // Non-numeric dependencies are local addons referenced by name (e.g. "steamvr_home")
                foreach (var addonsPath in CurrentGameAddonsPaths)
                {
                    var addonFolder = Path.Combine(addonsPath, dependencyName);

                    if (Directory.Exists(addonFolder))
                    {
                        AddAddonFolderToSearch(addonFolder);
                        break;
                    }
                }
            }
        }

        private KVObject? ReadAddonInfo()
        {
            byte[] addonInfo;

            var entry = CurrentPackage?.FindEntry(AddonInfoTxt);

            if (entry != null)
            {
                lock (CurrentPackage!)
                {
                    CurrentPackage.ReadEntry(entry, out addonInfo);
                }
            }
            else if (PreferredAddonFolderOnDisk != null && FindFileInFolder(PreferredAddonFolderOnDisk, AddonInfoTxt) is { } addonInfoPath)
            {
                addonInfo = File.ReadAllBytes(addonInfoPath);
            }
            else
            {
                return null;
            }

            // Older games write addoninfo.txt as KV1, newer ones as KV3
            var format = addonInfo.AsSpan().StartsWith("<!-- kv3"u8)
                ? KVSerializationFormat.KeyValues3Text
                : KVSerializationFormat.KeyValues1Text;

            using var stream = new MemoryStream(addonInfo);
            return KVSerializer.Create(format).Deserialize(stream);
        }

        private void FindAndLoadVpksInFolders(HashSet<string> folders)
        {
            foreach (var folder in folders)
            {
                foreach (var vpk in EnumeratePakVpks(folder))
                {
                    AddPackageToSearch(vpk);
                }

                AddDiskPathToSearch(folder);
            }
        }

        private IEnumerable<string> EnumeratePakVpks(string folder)
        {
            // Scan for vpks in folder, same logic as in source engine
            for (var i = 1; i < 99; i++)
            {
                var vpk = Path.Combine(folder, $"pak{i:D2}_dir.vpk");

                if (!File.Exists(vpk))
                {
                    yield break;
                }

                if (CurrentFileName == vpk)
                {
#if DEBUG_FILE_LOAD
                    Console.WriteLine($"VPK \"{vpk}\" is the same we just opened, skipping");
#endif
                    continue;
                }

                yield return vpk;
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

            foreach (var gameInfo in gameInfos)
            {
                var directory = Path.GetDirectoryName(gameInfo);
                var modName = Path.GetFileName(directory);
                var assumedGameRoot = Path.GetDirectoryName(directory)!;

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

        private string? FindFileOnDisk(string file)
        {
            foreach (var folder in CurrentGameSearchPaths)
            {
                var path = FindFileInFolder(folder, file);

                if (path != null)
                {
                    return path;
                }
            }

            return null;
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

        private static string? FindFileInFolder(string folder, string file)
        {
            var path = Path.Combine(folder, file);

            if (!File.Exists(path))
            {
                return null;
            }

            path = Path.GetFullPath(path);

#if DEBUG_FILE_LOAD
            Console.WriteLine($"Loaded \"{file}\" from disk: \"{path}\"");
#endif

            return path;
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
        /// Gets a stream for reading a package entry.
        /// </summary>
        /// <remarks>
        /// Do not use this method, it will be removed in the future in favor of a method in the ValvePak library.
        /// </remarks>
        public static Stream GetPackageEntryStream(Package package, PackageEntry entry)
        {
            lock (package)
            {
                return package.GetMemoryMappedStreamIfPossible(entry);
            }
        }
    }
}
