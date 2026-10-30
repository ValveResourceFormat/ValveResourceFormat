using System.Globalization;
using System.IO;
using ValveKeyValue;
using ValveResourceFormat.IO.ContentFormats.HalfEdgeMesh;
using ValveResourceFormat.Serialization.KeyValues;
using KVObject = ValveKeyValue.KVObject;

namespace ValveResourceFormat.IO
{
    /// <summary>
    /// A mod's <c>gameinfo.gi</c>, with <c>gameinfo_branchspecific.gi</c> merged on top and merged onto its <c>LayeredOnMod</c>.
    /// </summary>
    public sealed class GameInfo
    {
        private const string BranchSpecificFileName = "gameinfo_branchspecific.gi";
        private const int MaxLayerDepth = 8;

        // Test and tools apps of a game map to the same game
        private static readonly Dictionary<int, Game> GamesBySteamAppId = new()
        {
            [570] = Game.Dota,
            [710] = Game.Csgo,
            [730] = Game.Csgo,
            [205790] = Game.Dota,
            [247040] = Game.Dota,
            [250820] = Game.Steamtours,
            [450390] = Game.Vr,
            [453170] = Game.Steamtours,
            [546560] = Game.Hlvr,
            [1422450] = Game.Citadel,
            [1902490] = Game.Steampal,
            [3488080] = Game.Citadel,
            [3781850] = Game.Citadel,
        };

        /// <summary>Gets an empty game info, with every value at its default, for files of no known game.</summary>
        public static GameInfo Empty { get; } = new(string.Empty, KVObject.ListCollection());

        /// <summary>Gets the game, see the remarks of <see cref="IO.Game"/> before using it.</summary>
        public Game Game { get; }

        /// <summary><c>Hammer/DefaultTextureScale</c>: units one texel spans on a projected face.</summary>
        public float HammerTextureScale { get; } = PolygonMesh.DefaultTextureScale;

        /// <summary><c>Hammer/UseFirstAnimationAsDefault</c>: whether models preview their first animation by default.</summary>
        public bool UseFirstAnimationAsDefault { get; }

        /// <summary><c>Engine2/MapMaxCoord</c>: the largest absolute coordinate of a map.</summary>
        public float MapMaxCoord { get; } = 16384f;

        /// <summary><c>Engine2/RenderingPipeline/Tonemapping_DefaultAutoExposureMin</c>.</summary>
        public float? DefaultAutoExposureMin { get; }

        /// <summary><c>Engine2/RenderingPipeline/Tonemapping_DefaultAutoExposureMax</c>.</summary>
        public float? DefaultAutoExposureMax { get; }

        /// <summary>
        /// <c>Engine2/RenderingPipeline/SkipPostProcessing</c>: whether the engine skips its tonemap, bloom and color
        /// correction pass, leaving the final color to the materials.
        /// </summary>
        public bool SkipPostProcessing { get; }

        /// <summary>
        /// <c>Engine2/RenderingPipeline/TonemappingVRForward</c>: whether the engine tonemaps in its VR forward pipeline,
        /// which does not clamp the exposed color to the white point and fogs by the stronger of the distance and height terms.
        /// </summary>
        public bool TonemappingVrForward { get; }

        /// <summary>
        /// <c>SceneSystem/VrLightBinnerSingleLightFastPath</c>: whether the VR light binner shades one shadowed light and
        /// a few unshadowed point lights per pixel, and lights what nothing baked with the sky's spherical harmonics.
        /// </summary>
        public bool VrLightBinnerSingleLightFastPath { get; }

        /// <summary>
        /// <c>WorldRenderer/EnvironmentMapMipProcessor</c> names RGBM: whether the environment maps store their
        /// brightness in the alpha channel.
        /// </summary>
        public bool EnvironmentMapsAreRgbm { get; }

        /// <summary><c>Particles/ParticlesFoggedByDefault</c>: whether particles that leave fog to the game are fogged.</summary>
        public bool ParticlesFoggedByDefault { get; }

        /// <summary><c>Particles/EnableParticleShaderFeatureBranching</c>: whether particle shader features switch at run time.</summary>
        public bool ParticleShaderFeatureBranching { get; }

        /// <summary><c>Particles/ParticleTraceOffsetOnlyHit</c>: whether ground placement pushes particles off what they hit.</summary>
        public bool ParticleTraceOffsetOnlyHit { get; }

        /// <summary><c>WorldRenderer/DirectLightBaking</c>: whether direct light is baked into maps.</summary>
        public bool DirectLightBaking { get; } = true;

        /// <summary><c>WorldRenderer/LPVEdgeBlending</c>: whether light probe volumes fade out over their edges.</summary>
        public bool LpvEdgeBlending { get; } = true;

        internal string FilePath { get; }

        internal string GameRoot { get; }

        internal string? Name { get; }

        internal int SteamAppId { get; }

        internal bool AllowAddonDependencies { get; }

        internal List<KeyValuePair<string, string>> SearchPaths { get; } = [];

        private GameInfo(string filePath, KVObject root)
        {
            FilePath = filePath;
            GameRoot = Path.GetDirectoryName(Path.GetDirectoryName(filePath)) ?? string.Empty;
            Name = GetString(root, "game");

            if (root.TryGetValue("FileSystem", out var fileSystem))
            {
                AllowAddonDependencies = fileSystem.GetBooleanProperty("AllowAddonDependencies");

                if (int.TryParse(GetString(fileSystem, "SteamAppId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var steamAppId))
                {
                    SteamAppId = steamAppId;
                    Game = GetGame(steamAppId);
                }

                if (fileSystem.TryGetValue("SearchPaths", out var searchPaths))
                {
                    foreach (var (key, searchPath) in searchPaths)
                    {
                        SearchPaths.Add(new(key, searchPath.ToString()!));
                    }
                }
            }

            if (root.TryGetValue("Hammer", out var hammer))
            {
                var textureScale = ReadSingle(hammer, "DefaultTextureScale") ?? 0f;

                if (textureScale != 0f)
                {
                    HammerTextureScale = textureScale;
                }

                UseFirstAnimationAsDefault = hammer.GetBooleanProperty("UseFirstAnimationAsDefault");
            }

            if (root.TryGetValue("Engine2", out var engine))
            {
                MapMaxCoord = ReadSingle(engine, "MapMaxCoord") ?? MapMaxCoord;

                if (engine.TryGetValue("RenderingPipeline", out var pipeline))
                {
                    SkipPostProcessing = pipeline.GetBooleanProperty("SkipPostProcessing");
                    TonemappingVrForward = pipeline.GetBooleanProperty("TonemappingVRForward");
                    DefaultAutoExposureMin = ReadSingle(pipeline, "Tonemapping_DefaultAutoExposureMin");
                    DefaultAutoExposureMax = ReadSingle(pipeline, "Tonemapping_DefaultAutoExposureMax");
                }
            }

            if (root.TryGetValue("SceneSystem", out var sceneSystem))
            {
                VrLightBinnerSingleLightFastPath = sceneSystem.GetBooleanProperty("VrLightBinnerSingleLightFastPath");
            }

            if (root.TryGetValue("Particles", out var particles))
            {
                ParticlesFoggedByDefault = particles.GetBooleanProperty("ParticlesFoggedByDefault");
                ParticleShaderFeatureBranching = particles.GetBooleanProperty("EnableParticleShaderFeatureBranching");
                ParticleTraceOffsetOnlyHit = particles.GetBooleanProperty("ParticleTraceOffsetOnlyHit");
            }

            if (root.TryGetValue("WorldRenderer", out var worldRenderer))
            {
                DirectLightBaking = worldRenderer.GetBooleanProperty("DirectLightBaking", defaultValue: true);
                LpvEdgeBlending = worldRenderer.GetBooleanProperty("LPVEdgeBlending", defaultValue: true);

                var mipProcessor = GetString(worldRenderer, "EnvironmentMapMipProcessor");
                EnvironmentMapsAreRgbm = mipProcessor != null && mipProcessor.Contains("RGBM", StringComparison.Ordinal);
            }
        }

        internal static GameInfo Read(string filePath) => new(filePath, ReadLayered(filePath, depth: 0));

        internal static Game GetGame(int steamAppId) => GamesBySteamAppId.GetValueOrDefault(steamAppId);

        internal static IEnumerable<int> GetSteamAppIds(Game game)
        {
            foreach (var (steamAppId, appGame) in GamesBySteamAppId)
            {
                if (appGame == game)
                {
                    yield return steamAppId;
                }
            }
        }

        internal static Game GetGameOfMod(string mod)
        {
            foreach (var game in Enum.GetValues<Game>())
            {
                if (game != Game.Unknown && game.ToString().Equals(mod, StringComparison.OrdinalIgnoreCase))
                {
                    return game;
                }
            }

            return Game.Unknown;
        }

        private static KVObject ReadLayered(string filePath, int depth)
        {
            var root = ReadFile(filePath);
            var modFolder = Path.GetDirectoryName(filePath);
            var branchSpecificPath = Path.Join(modFolder, BranchSpecificFileName);

            if (File.Exists(branchSpecificPath))
            {
                root = Merge(root, ReadFile(branchSpecificPath));
            }

            var parentMod = GetString(root, "LayeredOnMod");

            if (depth >= MaxLayerDepth || parentMod == null)
            {
                return root;
            }

            var parentPath = Path.Join(Path.GetDirectoryName(modFolder), parentMod, Path.GetFileName(filePath));

            if (File.Exists(parentPath))
            {
                root = Merge(ReadLayered(parentPath, depth + 1), root);
            }

            return root;
        }

        private static KVObject ReadFile(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            return KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream);
        }

        // Child keys win, blocks that appear once on both sides merge recursively, and SearchPaths is replaced outright
        private static KVObject Merge(KVObject parent, KVObject child)
        {
            var childCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var (key, _) in child)
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

                var isMergeableBlock = childCount == 1
                    && parentValue.IsCollection
                    && !key.Equals("SearchPaths", StringComparison.OrdinalIgnoreCase);

                if (!isMergeableBlock)
                {
                    continue;
                }

                var childValue = FindValue(child, key)!;

                if (childValue.IsCollection && mergedKeys.Add(key))
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

        private static string? GetString(KVObject block, string name)
        {
            if (!block.TryGetValue(name, out var value) || value.IsCollection)
            {
                return null;
            }

            var text = value.ToString();
            return string.IsNullOrEmpty(text) ? null : text;
        }

        private static float? ReadSingle(KVObject block, string name)
        {
            if (!float.TryParse(GetString(block, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                return null;
            }

            return value;
        }
    }
}
