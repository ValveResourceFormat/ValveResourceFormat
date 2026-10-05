using System.Globalization;
using System.IO;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.IO.ContentFormats.HalfEdgeMesh;
using ValveResourceFormat.Serialization.KeyValues;
using KVObject = ValveKeyValue.KVObject;

namespace ValveResourceFormat.IO
{
    /// <summary>
    /// The parsed contents of a mod's <c>gameinfo.gi</c>.
    /// </summary>
    public sealed class GameInfo
    {
        private const string BranchSpecificFileName = "gameinfo_branchspecific.gi";
        private const int MaxLayerDepth = 8;

        /// <summary>The map coordinate limit when the game declares none.</summary>
        public const float FallbackMapMaxCoord = 16384f;

        /// <summary>The minimum auto exposure when the game declares none.</summary>
        public const float FallbackAutoExposureMin = 0.25f;

        /// <summary>The maximum auto exposure when the game declares none.</summary>
        public const float FallbackAutoExposureMax = 8.0f;

        private readonly KVObject Root;

        /// <summary>Gets an empty game info, with every value at its default, for files that belong to no known game.</summary>
        public static GameInfo Empty { get; } = new(string.Empty, KVObject.ListCollection());

        /// <summary>Gets the path of the <c>gameinfo.gi</c> file.</summary>
        public string FilePath { get; }

        /// <summary>Gets the folder containing the mod folders, which <see cref="SearchPaths"/> are relative to.</summary>
        public string GameRoot { get; }

        /// <summary>Gets the game name the file declares, or <see langword="null"/> when it declares none.</summary>
        public string? Name { get; }

        /// <summary>Gets the mod this file is an overlay of, such as <c>csgo</c> for <c>csgo_lv</c>, or <see langword="null"/> for a standalone mod.</summary>
        public string? LayeredOnMod { get; }

        /// <summary>Gets whether the game mounts the dependencies listed in an addon's <c>addoninfo.txt</c>.</summary>
        public bool AllowAddonDependencies { get; }

        /// <summary>Gets the Steam app id of the game, or <see langword="null"/> when the file declares none.</summary>
        public string? SteamAppId { get; }

        /// <summary>
        /// Gets the <c>FileSystem/SearchPaths</c> entries in file order. Keys are the entry type, such as <c>Game</c>
        /// or <c>AddonRoot</c>, and values are paths relative to <see cref="GameRoot"/>.
        /// </summary>
        public IReadOnlyList<KeyValuePair<string, string>> SearchPaths { get; }

        /// <summary>Gets the units one texel spans on a face Hammer projects a texture onto, <c>Hammer/DefaultTextureScale</c>.</summary>
        public float HammerTextureScale { get; }

        /// <summary>Gets the point entity Hammer places by default, the game's player start, or <see langword="null"/> when the file declares none.</summary>
        public string? HammerDefaultPointEntity { get; }

        /// <summary>Gets whether the game's rendering pipeline skips post processing, so the scene is shown without tonemapping.</summary>
        public bool SkipPostProcessing { get; }

        /// <summary>Gets the minimum auto exposure for maps without an <c>env_tonemap_controller</c>.</summary>
        public float DefaultAutoExposureMin { get; }

        /// <summary>Gets the maximum auto exposure for maps without an <c>env_tonemap_controller</c>.</summary>
        public float DefaultAutoExposureMax { get; }

        /// <summary>Gets the largest absolute coordinate a map can use on any axis.</summary>
        public float MapMaxCoord { get; }

        private GameInfo(string filePath, KVObject root)
        {
            Root = root;
            FilePath = filePath;
            GameRoot = Path.GetDirectoryName(Path.GetDirectoryName(filePath)) ?? string.Empty;
            Name = root.TryGetValue("game", out var name) ? name.ToString() : null;
            LayeredOnMod = root.TryGetValue("LayeredOnMod", out var layeredOnMod) ? layeredOnMod.ToString() : null;

            List<KeyValuePair<string, string>> searchPaths = [];

            if (root.TryGetValue("FileSystem", out var fileSystem))
            {
                AllowAddonDependencies = fileSystem.GetBooleanProperty("AllowAddonDependencies");
                SteamAppId = fileSystem.TryGetValue("SteamAppId", out var steamAppId) ? steamAppId.ToString() : null;

                if (fileSystem.TryGetValue("SearchPaths", out var searchPathsObject))
                {
                    foreach (var (key, searchPath) in searchPathsObject)
                    {
                        searchPaths.Add(new(key, searchPath.ToString()!));
                    }
                }
            }

            SearchPaths = searchPaths;

            var textureScale = GetSingle("Hammer/DefaultTextureScale", PolygonMesh.DefaultTextureScale);
            HammerTextureScale = textureScale != 0f ? textureScale : PolygonMesh.DefaultTextureScale;
            HammerDefaultPointEntity = GetString("Hammer/DefaultPointEntity");
            SkipPostProcessing = GetBoolean("Engine2/RenderingPipeline/SkipPostProcessing");
            DefaultAutoExposureMin = GetSingle("Engine2/RenderingPipeline/Tonemapping_DefaultAutoExposureMin", FallbackAutoExposureMin);
            DefaultAutoExposureMax = GetSingle("Engine2/RenderingPipeline/Tonemapping_DefaultAutoExposureMax", FallbackAutoExposureMax);
            MapMaxCoord = GetSingle("Engine2/MapMaxCoord", FallbackMapMaxCoord);
        }

        /// <summary>
        /// Reads and parses a <c>gameinfo.gi</c> file, with the <c>gameinfo_branchspecific.gi</c> next to it merged on
        /// top, and the whole merged onto the mod it names with <c>LayeredOnMod</c>.
        /// </summary>
        /// <param name="filePath">Path to the file, inside its mod folder.</param>
        /// <returns>The parsed game info.</returns>
        public static GameInfo Read(string filePath)
        {
            return new GameInfo(filePath, ReadLayered(filePath, depth: 0));
        }

        private static KVObject ReadLayered(string filePath, int depth)
        {
            var root = ReadFile(filePath);
            var branchSpecificPath = Path.Join(Path.GetDirectoryName(filePath), BranchSpecificFileName);

            if (File.Exists(branchSpecificPath))
            {
                root = Merge(root, ReadFile(branchSpecificPath));
            }

            if (depth < MaxLayerDepth && root.TryGetValue("LayeredOnMod", out var parentMod))
            {
                var modFolder = Path.GetDirectoryName(filePath);
                var parentPath = Path.Join(Path.GetDirectoryName(modFolder), parentMod.ToString(), Path.GetFileName(filePath));

                if (File.Exists(parentPath) && !string.Equals(Path.GetFullPath(parentPath), Path.GetFullPath(filePath), StringComparison.OrdinalIgnoreCase))
                {
                    root = Merge(ReadLayered(parentPath, depth + 1), root);
                }
            }

            return root;
        }

        private static KVObject ReadFile(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            return KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream);
        }

        /// <summary>
        /// Merges <paramref name="child"/> onto <paramref name="parent"/>. Child keys win, collections that appear once on
        /// both sides merge recursively, and <c>SearchPaths</c> is replaced outright.
        /// </summary>
        private static KVObject Merge(KVObject parent, KVObject child)
        {
            var childCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var key in child.Keys)
            {
                childCounts[key] = childCounts.GetValueOrDefault(key) + 1;
            }

            var merged = KVObject.ListCollection();
            var mergedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (key, parentValue) in parent)
            {
                var childCount = childCounts.GetValueOrDefault(key);

                if (childCount == 0)
                {
                    merged.Add(key, parentValue);
                    continue;
                }

                if (childCount == 1
                    && parentValue.IsCollection
                    && !key.Equals("SearchPaths", StringComparison.OrdinalIgnoreCase)
                    && FindValue(child, key) is { IsCollection: true } childValue
                    && mergedKeys.Add(key))
                {
                    merged.Add(key, Merge(parentValue, childValue));
                }
            }

            foreach (var (key, childValue) in child)
            {
                if (!mergedKeys.Contains(key))
                {
                    merged.Add(key, childValue);
                }
            }

            return merged;
        }

        private static KVObject? FindValue(KVObject collection, string key)
        {
            foreach (var (childKey, value) in collection)
            {
                if (childKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    return value;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets a value by its slash separated key path below the root, such as <c>FileSystem/SteamAppId</c>.
        /// </summary>
        /// <param name="keyPath">The key path.</param>
        /// <returns>The value, or <see langword="null"/> when the file does not contain the path.</returns>
        public KVObject? GetValue(string keyPath)
        {
            KVObject? value = Root;

            foreach (var key in keyPath.Split('/'))
            {
                if (value is null || !value.IsCollection || !value.TryGetValue(key, out value))
                {
                    return null;
                }
            }

            return value;
        }

        private float GetSingle(string keyPath, float defaultValue)
            => float.TryParse(GetValue(keyPath)?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : defaultValue;

        private bool GetBoolean(string keyPath)
            => int.TryParse(GetValue(keyPath)?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value != 0;

        private string? GetString(string keyPath)
            => GetValue(keyPath) is { IsCollection: false } value && value.ToString() is { Length: > 0 } text ? text : null;

        /// <summary>
        /// Gets whether an <c>AddonRoot</c> or <c>OfficialAddonRoot</c> search path entry names the given folder.
        /// </summary>
        /// <param name="folderName">The folder name, such as <c>csgo_addons</c>.</param>
        public bool HasAddonRoot(string folderName)
            => SearchPaths.Any(searchPath => searchPath.Key is "AddonRoot" or "OfficialAddonRoot"
                && string.Equals(searchPath.Value, folderName, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Gets the mod folders the <c>Game</c> and <c>Game_NonTools</c> search path entries mount, joined onto <see cref="GameRoot"/>.
        /// </summary>
        public IEnumerable<string> GameSearchPaths
            => SearchPaths
                .Where(static searchPath => searchPath.Key.Equals("Game", StringComparison.OrdinalIgnoreCase)
                    || searchPath.Key.Equals("Game_NonTools", StringComparison.OrdinalIgnoreCase))
                .Select(searchPath => Path.Combine(GameRoot, searchPath.Value));

        /// <summary>
        /// Gets whether any search path entry names the given mod folder.
        /// </summary>
        /// <param name="folderName">The mod folder name, such as <c>csgo</c>.</param>
        public bool MountsFolder(string folderName)
            => SearchPaths.Any(searchPath => string.Equals(searchPath.Value, folderName, StringComparison.OrdinalIgnoreCase));
    }
}
