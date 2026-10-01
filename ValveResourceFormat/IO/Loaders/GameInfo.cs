using System.IO;
using System.Linq;
using ValveKeyValue;
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

        private readonly KVObject Root;

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

        /// <summary>
        /// Gets whether any search path entry names the given mod folder.
        /// </summary>
        /// <param name="folderName">The mod folder name, such as <c>csgo</c>.</param>
        public bool MountsFolder(string folderName)
            => SearchPaths.Any(searchPath => string.Equals(searchPath.Value, folderName, StringComparison.OrdinalIgnoreCase));
    }
}
