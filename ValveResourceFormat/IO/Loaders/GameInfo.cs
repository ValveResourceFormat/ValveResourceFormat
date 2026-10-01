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
        private readonly KVObject Root;

        /// <summary>Gets the path of the <c>gameinfo.gi</c> file.</summary>
        public string FilePath { get; }

        /// <summary>Gets the folder containing the mod folders, which <see cref="SearchPaths"/> are relative to.</summary>
        public string GameRoot { get; }

        /// <summary>Gets the game name the file declares, or <see langword="null"/> when it declares none.</summary>
        public string? Name { get; }

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
        /// Reads and parses a <c>gameinfo.gi</c> file.
        /// </summary>
        /// <param name="filePath">Path to the file, inside its mod folder.</param>
        /// <returns>The parsed game info.</returns>
        public static GameInfo Read(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            var root = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream);

            return new GameInfo(filePath, root);
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
