//#define DEBUG_FILE_LOAD

using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    /// Loads compiled resources (models, textures, materials) as <see cref="Resource"/> objects. Lookup spans
    /// VPK packages and loose files on disk, with game search paths discovered from gameinfo.gi files.
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

        // Search paths a game mounts only in some modes, such as for a language other than English
        private static readonly HashSet<string> ConditionalGameSearchPathKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "Game_LowViolence",
            "Game_UILanguage",
            "Game_AudioLanguage",
        };

        // The engine mounts Game_LowViolence search paths only for low violence clients
        private static bool MountLowViolence => false;

        private readonly Dictionary<string, ShaderCollection> CachedShaders = [];
        private readonly Lock CachedShadersLock = new();
        private readonly HashSet<string> CurrentGameSearchPaths = [];
        private readonly HashSet<string> CurrentGameOfficialAddonsPaths = [];
        private readonly HashSet<string> CurrentGameAddonsPaths = [];
        private ImmutableArray<Package> CurrentGamePackages = [];

        // Newest first, searched before addons and game packages
        private ImmutableArray<MountedMapPackage> CurrentMapPackages = [];
        private readonly Lock MapPackagesLock = new();
        private readonly List<MountedMapPackage> UnmountedMapPackages = [];

        private sealed class MountedMapPackage(string path, Package package)
        {
            public string Path { get; } = path;
            public Package Package { get; } = package;
            public int References { get; set; } = 1;
        }

        // Addons are mounted in front of the game's own files, so they override them
        private ImmutableArray<AddonSearchPath> CurrentAddonSearchPaths = [];

        /// <summary>An addon vpk or a loose addon folder, exactly one of the two is set.</summary>
        private readonly record struct AddonSearchPath(Package? Package, string? Folder);

        private readonly string? CurrentFileName;
        private readonly ILogger Logger;
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
        /// Gets the Steam app id the first game found mounts its content as, which its <c>gameinfo.gi</c> gives as
        /// <c>FileSystem/SteamAppId</c>, or zero when no game gives one.
        /// </summary>
        public int SteamAppId { get; private set; }

        // Counter-Strike 2 mounts its content as the CS:GO SDK, not under its own app id
        private const int CounterStrike2SteamAppId = 710;

        /// <summary>Gets whether the game found is Counter-Strike 2.</summary>
        public bool IsCounterStrike2 => SteamAppId == CounterStrike2SteamAppId;

        /// <summary>
        /// Gets whether the first game found fogs particles whose renderer leaves fog to the game, which its
        /// <c>gameinfo.gi</c> gives as <c>Particles/ParticlesFoggedByDefault</c>.
        /// </summary>
        public bool ParticlesFoggedByDefault { get; private set; }

        /// <summary>
        /// Gets whether the first game found switches particle shader features at run time rather than through
        /// static combos, which its <c>gameinfo.gi</c> gives as <c>Particles/EnableParticleShaderFeatureBranching</c>.
        /// </summary>
        public bool ParticleShaderFeatureBranching { get; private set; }

        private bool ParticleSettingsRead;

        /// <summary>
        /// Initializes a new instance of the <see cref="GameFileLoader"/> class.
        /// </summary>
        /// <param name="currentPackage">The current package to search for files in.</param>
        /// <param name="currentFileName">The path on disk to the current file that is being opened.</param>
        /// <param name="logger">Logger for the search paths that get mounted and the files that fail to load.</param>
        /// <remarks>
        /// fileName is needed when used by GUI when package has not yet been resolved.
        /// </remarks>
        public GameFileLoader(Package? currentPackage, string? currentFileName, ILogger? logger = null)
        {
            CurrentPackage = currentPackage;
            CurrentFileName = currentFileName;
            Logger = logger ?? NullLogger.Instance;

            // Find gameinfo.gi by walking up from the current file, preload vpks and add folders to search paths
            if (CurrentFileName != null)
            {
                FindAndLoadSearchPaths();
                FindAndLoadOfficialGameAddonPackage();
            }

#if DEBUG_FILE_LOAD
            Logger.LogDebug("Current VPKs to search in order:");

            foreach (var searchPath in CurrentGamePackages)
            {
                Logger.LogDebug("{Package}.vpk", searchPath.FileName);
            }

            foreach (var searchPath in CurrentGameSearchPaths)
            {
                Logger.LogDebug("{SearchPath}", searchPath);
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
                foreach (var package in EnumerateMountedPackages())
                {
                    package.Dispose();
                }

                foreach (var mapPackage in UnmountedMapPackages)
                {
                    mapPackage.Package.Dispose();
                }

                CurrentMapPackages = [];
                UnmountedMapPackages.Clear();
                CurrentAddonSearchPaths = [];
                CurrentGamePackages = [];

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
                Logger.LogDebug("Loaded \"{File}\" from current vpk", file);
#endif

                return (null, CurrentPackage, entry);
            }

            foreach (var mapPackage in CurrentMapPackages)
            {
                entry = mapPackage.Package.FindEntry(file);

                if (entry != null)
                {
#if DEBUG_FILE_LOAD
                    Logger.LogDebug("Loaded \"{File}\" from map vpk \"{Package}\"", file, mapPackage.Package.FileName);
#endif

                    return (null, mapPackage.Package, entry);
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
                        Logger.LogDebug("Attempting to find addon dependencies while loading \"{File}\"", file);
#endif

                        LoadAddonDependencies();
                        AddonDependenciesPending = false;
                    }
                }
            }

            foreach (var (package, folder) in CurrentAddonSearchPaths)
            {
                if (package != null)
                {
                    entry = package.FindEntry(file);

                    if (entry != null)
                    {
#if DEBUG_FILE_LOAD
                        Logger.LogDebug("Loaded \"{File}\" from addon vpk \"{Package}\"", file, package.FileName);
#endif

                        return (null, package, entry);
                    }
                }
                else if (FindFileInFolder(folder!, file) is { } addonPath)
                {
#if DEBUG_FILE_LOAD
                    Logger.LogDebug("Loaded \"{File}\" from addon folder: \"{Path}\"", file, addonPath);
#endif

                    return (addonPath, null, null);
                }
            }

            // Check additional packages
            foreach (var package in CurrentGamePackages)
            {
                entry = package.FindEntry(file);

                if (entry != null)
                {
#if DEBUG_FILE_LOAD
                    Logger.LogDebug("Loaded \"{File}\" from preloaded vpk \"{Package}\"", file, package.FileName);
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
                Logger.LogWarning("Failed to load \"{File}\"", file);
            }

#if DEBUG
            if (string.IsNullOrEmpty(file) || file == CompiledFileSuffix)
            {
                Logger.LogWarning("Empty string passed to file loader here: {StackTrace}", Environment.StackTrace);

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
                        var stream = foundFile.Package!.GetMemoryMappedStreamIfPossible(foundFile.PackageEntry);
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
                Logger.LogWarning("Failed to find shader \"{ShaderName}\"", shaderName);

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
                return foundFile.Package!.GetMemoryMappedStreamIfPossible(foundFile.PackageEntry);
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
                    var stream = foundFile.Package!.GetMemoryMappedStreamIfPossible(foundFile.PackageEntry);
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

        private KVObject? ReadGameInfo(string gameinfoPath)
        {
            try
            {
                using var stream = File.OpenRead(gameinfoPath);
                return KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream);
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Failed to read \"{GameInfo}\"", gameinfoPath);
                return null;
            }
        }

        private static IEnumerable<(string Key, string Value)> EnumerateSearchPaths(KVObject gameInfo)
        {
            if (!gameInfo.TryGetValue("FileSystem", out var fileSystem) || !fileSystem.TryGetValue("SearchPaths", out var searchPaths))
            {
                yield break;
            }

            foreach (var (key, value) in searchPaths)
            {
                yield return (key, value.ToString()!);
            }
        }

        private void LoadGameInfo(HashSet<string> folders, string gameRoot, string gameinfoPath)
        {
            if (ReadGameInfo(gameinfoPath) is { } gameInfo)
            {
                HandleGameInfo(folders, gameRoot, gameinfoPath, gameInfo);
            }
        }

        private void HandleGameInfo(HashSet<string> folders, string gameRoot, string gameinfoPath, KVObject gameInfo)
        {
            var gameName = gameInfo.TryGetValue("game", out var game) ? game.ToString() : null;
            Logger.LogInformation("Found \"{GameName}\" from \"{GameInfo}\"", gameName, gameinfoPath);

            if (!ParticleSettingsRead)
            {
                ParticleSettingsRead = true;

                if (gameInfo.TryGetValue("Particles", out var particles))
                {
                    ParticlesFoggedByDefault = particles.GetBooleanProperty("ParticlesFoggedByDefault");
                    ParticleShaderFeatureBranching = particles.GetBooleanProperty("EnableParticleShaderFeatureBranching");
                }
            }

            if (!gameInfo.TryGetValue("FileSystem", out var fileSystem))
            {
                Logger.LogWarning("\"{GameInfo}\" has no FileSystem block", gameinfoPath);
                return;
            }

            if (SteamAppId == 0 && fileSystem.TryGetValue("SteamAppId", out var appIdValue))
            {
                SteamAppId = int.TryParse(appIdValue.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var appId) ? appId : 0;
            }

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

            foreach (var (key, searchPath) in EnumerateSearchPaths(gameInfo))
            {
                var path = Path.Combine(gameRoot, searchPath);

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
            var core = Path.Combine(gameRoot, "core");

            if (Directory.Exists(core))
            {
                folders.Add(core);
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
                Logger.LogInformation("Added folder \"{SearchPath}\" to game search paths", searchPath);
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
                Logger.LogInformation("Removed folder \"{SearchPath}\" from game search paths", searchPath);
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
            // Replaced rather than mutated, because files are looked up in parallel while shader and spawn group packages
            // get mounted. Shader packages are mounted while this lock is already held by the shader load.
            lock (CachedShadersLock)
            {
                CurrentGamePackages = CurrentGamePackages.Add(package);
            }
        }

        /// <summary>
        /// Removes a package from the search paths.
        /// </summary>
        public bool RemovePackageFromSearch(Package package)
        {
            var addonSearchPath = new AddonSearchPath(package, null);

            if (CurrentAddonSearchPaths.Contains(addonSearchPath))
            {
                ImmutableInterlocked.Update(ref CurrentAddonSearchPaths, static (searchPaths, searchPath) => searchPaths.Remove(searchPath), addonSearchPath);
                return true;
            }

            lock (CachedShadersLock)
            {
                var packages = CurrentGamePackages;
                CurrentGamePackages = packages.Remove(package);
                return CurrentGamePackages.Length != packages.Length;
            }
        }

        /// <summary>
        /// Returns whether a package with the given VPK path is mounted, including <see cref="CurrentPackage"/>.
        /// </summary>
        /// <param name="searchPath">Path to the VPK, with or without the <c>_dir.vpk</c> suffix.</param>
        public bool IsPackageMounted(string searchPath)
        {
            ArgumentNullException.ThrowIfNull(searchPath);

            var packageName = GetFullPackageName(searchPath);

            if (IsSamePackageName(CurrentFileName, packageName))
            {
                return true;
            }

            foreach (var package in EnumerateSearchedPackages())
            {
                if (IsSamePackageName(package.FilePath, packageName))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns whether a package opened from the entry of the outer package is mounted, including <see cref="CurrentPackage"/>.
        /// </summary>
        private bool IsPackageMounted(Package outerPackage, PackageEntry entry)
        {
            foreach (var package in EnumerateSearchedPackages())
            {
                if (package is NestedPackage nested && IsSameEntry(nested.Parent, nested.ParentEntry, outerPackage, entry))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsSamePackageName(string? path, string packageName)
            => path != null && string.Equals(GetFullPackageName(path), packageName, StringComparison.OrdinalIgnoreCase);

        private static string GetFullPackageName(string path) => Path.GetFullPath(PackageFileNames.GetPackageName(path));

        /// <summary>
        /// Returns whether two packages read the same file, or the same entry of the same package.
        /// </summary>
        private static bool IsSamePackage(Package package, Package other)
        {
            if (package == other)
            {
                return true;
            }

            if (package.FilePath is { } filePath)
            {
                return filePath.Equals(other.FilePath, StringComparison.OrdinalIgnoreCase);
            }

            return package is NestedPackage nested
                && other is NestedPackage otherNested
                && IsSameEntry(nested.Parent, nested.ParentEntry, otherNested.Parent, otherNested.ParentEntry);
        }

        private static bool IsSameEntry(Package package, PackageEntry entry, Package otherPackage, PackageEntry otherEntry)
            => entry == otherEntry || (entry.GetFullPath() == otherEntry.GetFullPath() && IsSamePackage(package, otherPackage));

        private IEnumerable<Package> EnumerateSearchedPackages()
        {
            if (CurrentPackage != null)
            {
                yield return CurrentPackage;
            }

            foreach (var package in EnumerateMountedPackages())
            {
                yield return package;
            }
        }

        private IEnumerable<Package> EnumerateMountedPackages()
        {
            foreach (var mapPackage in CurrentMapPackages)
            {
                yield return mapPackage.Package;
            }

            foreach (var searchPath in CurrentAddonSearchPaths)
            {
                if (searchPath.Package != null)
                {
                    yield return searchPath.Package;
                }
            }

            foreach (var package in CurrentGamePackages)
            {
                yield return package;
            }
        }

        /// <summary>
        /// Mounts a map package found through the search paths in front of every search path except
        /// <see cref="CurrentPackage"/>, the way the engine mounts a map: the map mounted last is searched first. Mounting a
        /// map package again moves it to the front. Every mount is released with <see cref="UnmountMapPackage"/>.
        /// </summary>
        /// <param name="mapPackagePath">Path of the map package in the search paths, such as <c>maps/de_dust2.vpk</c>.</param>
        /// <returns>Whether the map package was found.</returns>
        public bool TryMountMapPackage(string mapPackagePath)
        {
            ArgumentNullException.ThrowIfNull(mapPackagePath);

            lock (MapPackagesLock)
            {
                foreach (var mapPackage in CurrentMapPackages)
                {
                    if (mapPackage.Path.Equals(mapPackagePath, StringComparison.OrdinalIgnoreCase))
                    {
                        mapPackage.References++;
                        CurrentMapPackages = CurrentMapPackages.Remove(mapPackage).Insert(0, mapPackage);
                        return true;
                    }
                }

                var unmounted = UnmountedMapPackages.FindIndex(mapPackage => mapPackage.Path.Equals(mapPackagePath, StringComparison.OrdinalIgnoreCase));

                if (unmounted >= 0)
                {
                    var mapPackage = UnmountedMapPackages[unmounted];
                    UnmountedMapPackages.RemoveAt(unmounted);
                    mapPackage.References = 1;
                    CurrentMapPackages = CurrentMapPackages.Insert(0, mapPackage);
                    return true;
                }

                var (pathOnDisk, outerPackage, entry) = FindFile(mapPackagePath);

                if (pathOnDisk == null && entry == null)
                {
                    return false;
                }

                // The opened package, or one the game or an addon mounts, is searched already
                if (pathOnDisk != null ? IsPackageMounted(pathOnDisk) : IsPackageMounted(outerPackage!, entry!))
                {
                    return true;
                }

#pragma warning disable CA2000 // Disposed with the loader
                var package = pathOnDisk != null ? ReadPackage(pathOnDisk) : ReadInnerPackage(outerPackage!, entry!);
#pragma warning restore CA2000
                CurrentMapPackages = CurrentMapPackages.Insert(0, new MountedMapPackage(mapPackagePath, package));

                return true;
            }
        }

        /// <summary>
        /// Releases a mount of <see cref="TryMountMapPackage"/>. The package is removed from the search paths once every
        /// mount of it is released, but stays open until this loader is disposed, because resources loaded from it keep
        /// reading from its entry streams. Mounting it again reuses it.
        /// </summary>
        /// <param name="mapPackagePath">The path the map package was mounted with.</param>
        public void UnmountMapPackage(string mapPackagePath)
        {
            ArgumentNullException.ThrowIfNull(mapPackagePath);

            lock (MapPackagesLock)
            {
                foreach (var mapPackage in CurrentMapPackages)
                {
                    if (!mapPackage.Path.Equals(mapPackagePath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (--mapPackage.References == 0)
                    {
                        CurrentMapPackages = CurrentMapPackages.Remove(mapPackage);
                        UnmountedMapPackages.Add(mapPackage);
                    }

                    return;
                }
            }
        }

        private NestedPackage ReadInnerPackage(Package outerPackage, PackageEntry entry)
        {
            Logger.LogInformation("Preloading vpk \"{InnerPath}\" from \"{Package}\"", entry.GetFullPath(), outerPackage.FileName);

            return NestedPackage.Open(outerPackage, entry);
        }

        private void AddGamePackageToSearch(string searchPath)
        {
            if (!IsPackageMounted(searchPath))
            {
                AddPackageToSearch(searchPath);
            }
        }

        private Package ReadPackage(string searchPath)
        {
            Logger.LogInformation("Preloading vpk \"{SearchPath}\"", searchPath);

            return Package.Open(searchPath);
        }

        /// <summary>
        /// Mounts an addon vpk at <paramref name="index"/> of the addon search paths, which are searched before the game's own.
        /// </summary>
        private void AddAddonPackageToSearch(string searchPath, int index)
        {
            if (IsPackageMounted(searchPath))
            {
                return;
            }

            var addonSearchPath = ReadAddonPackage(searchPath);
            ImmutableInterlocked.Update(ref CurrentAddonSearchPaths, static (searchPaths, item) => searchPaths.Insert(item.Index, item.SearchPath), (Index: index, SearchPath: addonSearchPath));
        }

        /// <summary>
        /// Mounts a loose addon folder at <paramref name="index"/> of the addon search paths, which are searched before the
        /// game's own, with the pak vpks inside it in front of the folder, the highest numbered first.
        /// </summary>
        private void AddAddonFolderToSearch(string folder, int index)
        {
            folder = Path.GetFullPath(folder);

            foreach (var searchPath in CurrentAddonSearchPaths)
            {
                if (string.Equals(searchPath.Folder, folder, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            var searchPaths = ImmutableArray.CreateBuilder<AddonSearchPath>();

            try
            {
                foreach (var vpk in PackageFileNames.EnumeratePakDirectoryFiles(folder).Reverse())
                {
                    if (!IsPackageMounted(vpk))
                    {
                        searchPaths.Add(ReadAddonPackage(vpk));
                    }
                }
            }
            catch
            {
                foreach (var searchPath in searchPaths)
                {
                    searchPath.Package!.Dispose();
                }

                throw;
            }

            searchPaths.Add(new AddonSearchPath(null, folder));

            ImmutableInterlocked.Update(ref CurrentAddonSearchPaths, static (current, item) => current.InsertRange(item.Index, item.SearchPaths), (Index: index, SearchPaths: searchPaths.ToImmutable()));
            Logger.LogInformation("Added addon folder \"{Folder}\" to search paths", folder);
        }

#pragma warning disable CA2000 // Disposed with the loader or by RemovePackageFromSearch
        private AddonSearchPath ReadAddonPackage(string vpk) => new(ReadPackage(vpk), null);
#pragma warning restore CA2000

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
                    LoadGameInfo(folders, assumedGameRoot, modIdentifierPath);
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
                            LoadGameInfo(folders, Path.GetDirectoryName(mainGameDir)!, mainGameInfo);
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

        private bool GameInfoMountsFolder(string gameinfoPath, string folderName)
        {
            if (ReadGameInfo(gameinfoPath) is not { } gameInfo)
            {
                return false;
            }

            foreach (var (_, searchPath) in EnumerateSearchPaths(gameInfo))
            {
                if (string.Equals(searchPath, folderName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsGameSearchPath(KVObject gameInfo, string gameRoot, string folder)
        {
            foreach (var (key, searchPath) in EnumerateSearchPaths(gameInfo))
            {
                if (key.Equals("Game", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(Path.Combine(gameRoot, searchPath), folder, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void FindAndLoadOfficialGameAddonPackage()
        {
            // The engine mounts an addon either from the official addons or from the local ones, never both
            if (CurrentFileName == null || CurrentGameOfficialAddonsPaths.Count == 0 || PreferredAddonFolderOnDisk != null)
            {
                return;
            }

            // Check if vpk with same file name exists in addons folder
            var fileName = CurrentPackage?.FileName is { } packageName ? Path.GetFileName(packageName) : Path.GetFileNameWithoutExtension(CurrentFileName);

            foreach (var officialAddonPath in CurrentGameOfficialAddonsPaths)
            {
                var addonFolder = Path.Combine(officialAddonPath, fileName);

                if (!Directory.Exists(addonFolder))
                {
                    continue;
                }

                if (PackageFileNames.FindAddonPackage(addonFolder, fileName) is { } vpk)
                {
                    AddAddonPackageToSearch(vpk, 0);
                    break;
                }
            }
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
                Logger.LogWarning("Failed to read addoninfo.txt: {Error}", e.Message);
                return;
            }

            if (addonInfo == null || !addonInfo.TryGetValue("Dependencies", out var dependencies))
            {
                return;
            }

            // Each dependency goes in front of the previous one, all behind the addon itself
            var index = CurrentAddonSearchPaths.Length;

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

                    if (PackageFileNames.FindAddonPackage(Path.Join(WorkshopContentFolder, dependencyName), dependencyName) is { } vpk)
                    {
                        AddAddonPackageToSearch(vpk, index);
                    }
                    else
                    {
                        Logger.LogWarning("Addon dependency {DependencyId} is not installed", dependencyId);
                    }

                    continue;
                }

                // Non-numeric dependencies are local addon folders referenced by name (e.g. "steamvr_home") in the first addon root
                if (CurrentGameAddonsPaths.FirstOrDefault() is { } addonsPath)
                {
                    var addonFolder = Path.Combine(addonsPath, dependencyName);

                    if (Directory.Exists(addonFolder))
                    {
                        AddAddonFolderToSearch(addonFolder, index);
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
                CurrentPackage!.ReadEntry(entry, out addonInfo);
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
                foreach (var vpk in PackageFileNames.EnumeratePakDirectoryFiles(folder))
                {
                    AddGamePackageToSearch(vpk);
                }

                AddDiskPathToSearch(folder);
            }
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

            List<(string GameRoot, string ModFolder, string GameInfoPath, KVObject GameInfo)> mods = [];
            List<(string GameRoot, string FolderName)> conditionalFolders = [];

            foreach (var gameInfoPath in gameInfos)
            {
                var modFolder = Path.GetDirectoryName(gameInfoPath)!;
                var assumedGameRoot = Path.GetDirectoryName(modFolder)!;

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

                foreach (var (key, searchPath) in EnumerateSearchPaths(gameInfo))
                {
                    if (ConditionalGameSearchPathKeys.Contains(key))
                    {
                        conditionalFolders.Add((assumedGameRoot, searchPath.Replace("*LANGUAGE*", "*", StringComparison.OrdinalIgnoreCase)));
                    }
                }

                // A gameinfo that does not list its own folder, such as coreH or csgoH, is not a mod of its own
                if (IsGameSearchPath(gameInfo, assumedGameRoot, modFolder))
                {
                    mods.Add((assumedGameRoot, modFolder, gameInfoPath, gameInfo));
                }
            }

            foreach (var (gameRoot, modFolder, gameInfoPath, gameInfo) in mods)
            {
                // Low violence and language folders have a gameinfo of their own, but are only mounted through the main game's
                var modName = Path.GetFileName(modFolder);

                if (!conditionalFolders.Exists(folder => string.Equals(folder.GameRoot, gameRoot, StringComparison.OrdinalIgnoreCase)
                    && FileSystemName.MatchesSimpleExpression(folder.FolderName, modName)))
                {
                    HandleGameInfo(folders, gameRoot, gameInfoPath, gameInfo);
                }
            }
        }

        private string? FindFileOnDisk(string file)
        {
            foreach (var folder in CurrentGameSearchPaths)
            {
                var path = FindFileInFolder(folder, file);

                if (path != null)
                {
#if DEBUG_FILE_LOAD
                    Logger.LogDebug("Loaded \"{File}\" from disk: \"{Path}\"", file, path);
#endif

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

            return Path.GetFullPath(path);
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
                        AddGamePackageToSearch(vpk);

                        break; // One for each folder
                    }
                }
            }
        }
    }
}
