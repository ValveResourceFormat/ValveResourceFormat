using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.NavMesh;
using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.GenericData.CS2;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.EntityLump;
using WorldResource = ValveResourceFormat.ResourceTypes.World;

namespace ValveResourceFormat.Renderer.World
{
    /// <summary>
    /// Loads and manages Source 2 world data including entities, lighting, and geometry.
    /// </summary>
    public class WorldLoader
    {
        private readonly Scene scene;
        private readonly RendererContext RendererContext;

        private CancellationToken CancellationToken => RendererContext.CancellationToken;

        /// <summary>The directory path of the map, e.g. <c>maps/de_dust2</c>.</summary>
        public string MapName { get; }

        /// <summary>The world resource being loaded.</summary>
        public WorldResource World { get; }

        internal Scene Scene => scene;

        /// <summary>
        /// All entities parsed from the world's entity lumps, including the ones a <c>point_template</c> spawns
        /// only when told to.
        /// </summary>
        public List<Entity> Entities { get; } = [];
        /// <summary>The first world node encountered during loading.</summary>
        public WorldNode? MainWorldNode { get; private set; }

        // Kept apart from world_layer_base so the world geometry can be hidden while collision stays visible.
        // Always enabled, the physics group filter decides what actually draws.
        private const string PhysicsDebugLayerName = "Physics Visualization Layer";

        /// <summary>
        /// Visibility layer of the entities a <c>point_template</c> shows while the world is stopped. The template
        /// itself stays on its own layer, and what it spawns in game goes on the layer its lump would spawn on.
        /// </summary>
        public const string TemplateLayerName = "Template Entities";

        /// <summary>Visibility layer of the entities that start disabled, which the game keeps hidden until enabled.</summary>
        public const string DisabledEntitiesLayerName = "Entities (disabled)";

        /// <summary>Layer names that should be visible by default, populated during loading.</summary>
        public HashSet<string> DefaultEnabledLayers { get; } = ["No layer", "Entities", Scene.ParticlesLayerName, PhysicsDebugLayerName];

        /// <summary>Names of info_camera_link entities found in the world.</summary>
        public List<string> CameraNames { get; } = [];
        /// <summary>Transform matrices corresponding to each entry in <see cref="CameraNames"/>.</summary>
        public List<Matrix4x4> CameraMatrices { get; } = [];

        /// <summary>
        /// Camera transform for the best spawn marker entity found, per <see cref="SpawnCameraClasses"/>
        /// priority, with eye height already applied to ground markers.
        /// </summary>
        public Matrix4x4? SpawnCameraMatrix { get; private set; }

        /// <summary>
        /// Spawn marker classnames in priority order; earlier entries win regardless of entity order.
        /// team_select and T/CT spawns are CS2, info_team_spawn is Deadlock, goodguys/badguys are
        /// Dota 2, then the generic player starts, then the camera entities as last resorts.
        /// </summary>
        private static readonly string[] SpawnCameraClasses =
        [
            "team_select",
            "info_player_terrorist",
            "info_player_counterterrorist",
            "info_team_spawn",
            "info_player_start_goodguys",
            "info_player_start_badguys",
            "info_player_start",
            "info_player_start_dota",
            "point_camera",
            "point_camera_vertical_fov",
            "point_devshot_camera",
        ];

        private const float PlayerEyeHeight = 64f;
        private int spawnCameraPriority = int.MaxValue;

        /// <summary>The loaded navigation mesh, populated by <see cref="LoadNavigationMesh"/>.</summary>
        public NavMeshFile? NavMesh { get; set; }
        /// <summary>The names of the navigation hulls in hull index order, populated by <see cref="LoadNavigationMesh"/>.</summary>
        public string[] NavigationHullNames { get; set; } = [];
        /// <summary>The loaded navigation space, null if it doesn't exist. Populated by <see cref="LoadNavigationSpace"/>.</summary>
        public NavSpaceFile? NavSpace { get; set; }
        /// <summary>The loaded navigation flow map, null if it doesn't exist. Populated by <see cref="LoadNavigationFlowMap"/>.</summary>
        public NavFlowMapFile? NavFlowMap { get; set; }
        /// <summary>Baked bomb damage data for CS2, null if it doesn't exist. Populated by <see cref="LoadBombDamageData"/>.</summary>
        public BombDamage? BombDamage { get; set; }

        /// <summary>Applied to everything this map loads.</summary>
        private readonly Matrix4x4 rootTransform;

        internal Matrix4x4 RootTransform => rootTransform;

        private readonly EntitySystem entitySystem;

        // Prefabs are compiled without lighting or visibility, so only entities and geometry are loaded into the
        // scene of the map that placed them
        internal bool OwnsScene { get; }

        /// <summary>
        /// Loads a map by name, performing a full load of all world components, then spawns its entities.
        /// </summary>
        /// <param name="mapResourceName">Path to the <c>.vmap</c> or <c>.vmap_c</c> resource.</param>
        /// <param name="scene">The scene to load the world into.</param>
        /// <param name="entitySystem">The entity world this map's entities spawn into.</param>
        /// <param name="rootTransform">Transform applied to the whole map, identity when <see langword="null"/>.</param>
        public static WorldLoader LoadMap(string mapResourceName, Scene scene, EntitySystem entitySystem, Matrix4x4? rootTransform = null)
        {
            var (loader, mapReferences) = OpenMap(mapResourceName, scene, entitySystem, rootTransform ?? Matrix4x4.Identity, ownsScene: true);

            loader.Load(mapReferences);
            return loader;
        }

        /// <summary>
        /// Loads a map placed by a spawn group, up to its entities, which the group then spawns before the rest
        /// of the world loads.
        /// </summary>
        internal static WorldLoader LoadSpawnGroupWorld(string mapResourceName, Scene scene, EntitySystem entitySystem, Matrix4x4 rootTransform, bool ownsScene)
        {
            var (loader, mapReferences) = OpenMap(mapResourceName, scene, entitySystem, rootTransform, ownsScene);

            loader.LoadBeforeEntities(mapReferences);
            return loader;
        }

        private static (WorldLoader Loader, ResourceExtRefList? MapReferences) OpenMap(string mapResourceName, Scene scene, EntitySystem entitySystem, Matrix4x4 rootTransform, bool ownsScene)
        {
            var renderContext = scene.RendererContext;
            Resource? mapResource = null;

            if (mapResourceName.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.OrdinalIgnoreCase))
            {
                mapResource = renderContext.FileLoader.LoadFile(mapResourceName);
            }
            else
            {
                mapResource = renderContext.FileLoader.LoadFileCompiled(mapResourceName);
            }

            if (mapResource == null)
            {
                throw new FileNotFoundException($"Failed to load map file '{mapResourceName}'.");
            }

            var worldPath = GetWorldNameFromMap(mapResourceName);
            var worldResource = renderContext.FileLoader.LoadFileCompiled(worldPath) ?? throw new FileNotFoundException($"Failed to load world file '{worldPath}'.");

            var loader = new WorldLoader((WorldResource)worldResource.DataBlock!, scene, entitySystem, rootTransform, ownsScene);

            return (loader, mapResource.ExternalReferences);
        }

        /// <summary>
        /// Initializes a new <see cref="WorldLoader"/> for the given world resource.
        /// Call <see cref="Load"/> to begin loading world components into the scene.
        /// </summary>
        /// <param name="world">The world data block to load.</param>
        /// <param name="scene">The scene to load the world into.</param>
        /// <param name="entitySystem">The entity world this map's entities spawn into.</param>
        /// <param name="rootTransform">Transform applied to the whole map, identity when <see langword="null"/>.</param>
        public WorldLoader(WorldResource world, Scene scene, EntitySystem entitySystem, Matrix4x4? rootTransform = null)
            : this(world, scene, entitySystem, rootTransform ?? Matrix4x4.Identity, ownsScene: true)
        {
        }

        private WorldLoader(WorldResource world, Scene scene, EntitySystem entitySystem, Matrix4x4 rootTransform, bool ownsScene)
        {
            MapName = Path.GetDirectoryName(world.Resource!.FileName!)!.Replace('\\', '/');
            World = world;
            this.scene = scene;
            this.entitySystem = entitySystem;
            this.rootTransform = rootTransform;
            OwnsScene = ownsScene;
            RendererContext = scene.RendererContext;
        }

        /// <summary> Loading screen hints.</summary>
        public IProgress<string>? LoadingProgress { get; set; }

        // The phase reported before a placed 3D sky reported its own, to report again once it has loaded
        internal string? CurrentLoadingPhase { get; private set; }

        private void ReportLoadingPhase(string phase)
        {
            CurrentLoadingPhase = phase;
            LoadingProgress?.Report(phase);
        }

        /// <summary>
        /// Preloads referenced resources in parallel using the map's external reference list.
        /// This is called automatically by <see cref="Load"/> and does not need to be called manually.
        /// </summary>
        /// <param name="mapResourceReferences">External reference list from the map resource, used to preload models and entity icons.</param>
        public void ParallelPreloadResources(ResourceExtRefList? mapResourceReferences = null)
        {
            if (mapResourceReferences != null)
            {
                ReportLoadingPhase("Loading map resources…");

                Resource? PreloadResource(string resourceName)
                {
                    var resource = RendererContext.FileLoader.LoadFileCompiled(resourceName);
                    if (resource is { DataBlock: Model model })
                    {
                        lock (resource)
                        {
                            foreach (var mesh in model.GetEmbeddedMeshes())
                            {
                                var __ = mesh.Mesh.VBIB;
                            }
                        }
                    }

                    return resource;
                }

                var mapFolder = string.Concat(MapName, "/");

                bool IsExternalModel(string name)
                {
                    var isModel = name.EndsWith(".vmdl", StringComparison.OrdinalIgnoreCase);
                    var isInMapFolder = name.StartsWith(mapFolder, StringComparison.OrdinalIgnoreCase);
                    return isModel && !isInMapFolder;
                }

                var resourceNames = mapResourceReferences.ResourceRefInfoList
                    .Select(x => x.Name)
                    .Where(r => !r.StartsWith("_bakeresourcecache", StringComparison.Ordinal))
                    .Where(r => !IsExternalModel(r));

                var parallelOptions = new ParallelOptions { CancellationToken = CancellationToken };

                Parallel.ForEach(resourceNames, parallelOptions, resourceReference =>
                {
                    var resource = PreloadResource(resourceReference);

                    if (resource is { ExternalReferences.ResourceRefInfoList: var refs })
                    {
                        Parallel.ForEach(refs, parallelOptions, extRef =>
                        {
                            var referenced = PreloadResource(extRef.Name);

                            if (referenced is { ResourceType: ResourceType.ResourceManifest, ExternalReferences.ResourceRefInfoList: var manifestRefs })
                            {
                                Parallel.ForEach(manifestRefs, parallelOptions, manifestRef => PreloadResource(manifestRef.Name));
                            }
                        });
                    }

                    if (resource is { ResourceType: ResourceType.EntityLump, DataBlock: EntityLump entityLump })
                    {
                        HashSet<string> entityResources = [];
                        foreach (var entity in entityLump.GetEntities())
                        {
                            if (entity.GetStringProperty("model") is { Length: > 0 } modelName)
                            {
                                entityResources.Add(modelName);
                            }

                            var className = entity.GetStringProperty("classname");
                            if (className != null && entitySystem.ToolVisuals is { } toolVisuals)
                            {
                                entityResources.UnionWith(toolVisuals.GetResourcesToPreload(className));
                            }
                        }

                        Parallel.ForEach(entityResources, parallelOptions, file =>
                        {
                            PreloadResource(file);
                        });
                    }
                });
            }
        }

        /// <summary>
        /// Loads all world components and spawns the map's entities as its spawn group. See
        /// <see cref="LoadEntities"/>.
        /// </summary>
        /// <param name="mapResourceReferences">Optional external reference list from the map resource, used to preload assets in parallel.</param>
        public void Load(ResourceExtRefList? mapResourceReferences = null)
        {
            LoadBeforeEntities(mapResourceReferences);
            LoadEntities();
        }

        // Started before the entities spawn and waited for once the world has finished loading
        private Task navigationTask = Task.CompletedTask;

        private bool loadedAfterEntities;

        /// <summary>
        /// Loads what the entities need before they spawn: resources preloaded in parallel and the lighting. The
        /// navigation files load in the background until <see cref="LoadAfterEntities"/>.
        /// </summary>
        private void LoadBeforeEntities(ResourceExtRefList? mapResourceReferences)
        {
            // Non resource files not covered by ParallelPreloadResources
            if (OwnsScene)
            {
                navigationTask = Task.WhenAll(Task.Run(LoadNavigationMesh), Task.Run(LoadNavigationSpace), Task.Run(LoadNavigationFlowMap));
            }

            ParallelPreloadResources(mapResourceReferences);

            if (OwnsScene)
            {
                LoadWorldLightingInfo();
            }
        }

        /// <summary>
        /// Loads the rest of the world once its entities have spawned the first time: world nodes, physics,
        /// visibility and bomb damage data, and waits for the navigation files. Does nothing after that.
        /// </summary>
        /// <remarks>
        /// The engine loads all of a world before its entities, but a world mesh here picks its baked lighting
        /// when it is created, from the light probe volumes that the entities register as they spawn.
        /// </remarks>
        internal void LoadAfterEntities()
        {
            if (loadedAfterEntities)
            {
                return;
            }

            loadedAfterEntities = true;

            LoadWorldNodes();
            LoadWorldPhysics();

            if (OwnsScene)
            {
                LoadWorldVisibility();
                LoadBombDamageData();
            }

            navigationTask.Wait();
        }

        /// <summary>
        /// Spawns the map's entities from its entity lumps, as a spawn group that becomes the active one when
        /// there is none. Called again once the entities it spawned have been removed, they spawn as they did when
        /// the map loaded: the 3D sky and the other maps they place load again, reusing the geometry of the
        /// prefabs that is still in the scene.
        /// </summary>
        public void LoadEntities()
        {
            ReportLoadingPhase("Loading entities…");

            // Made again each time, a group that already has its world, as the engine reuses the world a spawn
            // group handle already owns
            var group = new SpawnGroup(MapName, scene, rootTransform)
            {
                World = this,
            };

            var spawnGroupManager = entitySystem.SpawnGroupManager;
            spawnGroupManager.LoadSpawnGroup(group);
            spawnGroupManager.SynchronouslySpawnGroup(group);

            if (entitySystem.ActiveSpawnGroup == null)
            {
                spawnGroupManager.PerformActivateSpawnGroup(group);
            }
        }

        /// <summary>
        /// Loads all world nodes (<c>.vwnod_c</c>) referenced by the world into the scene.
        /// </summary>
        public void LoadWorldNodes()
        {
            ReportLoadingPhase("Loading world geometry…");

            // Output is World_t we need to iterate m_worldNodes inside it.
            var worldNodes = World.GetWorldNodeNames();
            foreach (var worldNode in worldNodes)
            {
                CancellationToken.ThrowIfCancellationRequested();

                if (worldNode != null)
                {
                    var worldNodeResource = RendererContext.FileLoader.LoadFile(string.Concat(worldNode, ".vwnod_c"));
                    if (worldNodeResource == null)
                    {
                        continue;
                    }

                    var worldNodeData = (WorldNode?)worldNodeResource.DataBlock;
                    if (worldNodeData == null)
                    {
                        continue;
                    }

                    MainWorldNode ??= worldNodeData;

                    var subloader = new WorldNodeLoader(RendererContext, worldNodeData);
                    subloader.Load(scene, rootTransform);

                    foreach (var layer in subloader.LayerNames)
                    {
                        DefaultEnabledLayers.Add(layer);
                    }
                }
            }
        }

        /// <summary>
        /// Loads world physics collision geometry into the scene.
        /// </summary>
        public void LoadWorldPhysics()
        {
            ReportLoadingPhase("Loading world physics…");

            PhysAggregateData? phys = null;
            var physResource = RendererContext.FileLoader.LoadFile($"{MapName}/world_physics.vphys_c");

            if (physResource != null)
            {
                phys = (PhysAggregateData?)physResource.DataBlock;
            }
            else
            {
                physResource = RendererContext.FileLoader.LoadFile($"{MapName}/world_physics.vmdl_c");

                if (physResource != null)
                {
                    phys = (PhysAggregateData?)physResource.GetBlockByType(BlockType.PHYS);
                }
            }

            if (phys != null)
            {
                Debug.Assert(physResource?.FileName != null);

                foreach (var node in entitySystem.ToolVisuals?.CreateCollisionVisuals(scene, phys, physResource.FileName[..^2], null) ?? [])
                {
                    node.Transform = rootTransform;
                    node.LayerName = PhysicsDebugLayerName;
                    scene.Add(node, false);
                }

                // Every spawn group adds its collision to the physics world of its world group, a 3D sky's included
                if (phys.Parts.Length > 0)
                {
                    entitySystem.GetPhysicsWorld(scene.WorldGroup).Add(scene, new Rubikon(phys), rootTransform);
                }
            }
        }

        /// <summary>
        /// Loads world voxel visibility (<c>.vvis_c</c>) into the scene.
        /// </summary>
        public void LoadWorldVisibility()
        {
            ReportLoadingPhase("Loading world visibility…");

            var visResource = RendererContext.FileLoader.LoadFile($"{MapName}/world_visibility.vvis_c");
            if (visResource == null)
            {
                return;
            }

            var voxelVisibility = VoxelVisibility.GetWorldVisibility(visResource);

            if (voxelVisibility == null)
            {
                return;
            }

            scene.VoxelVisibility = voxelVisibility;

            // Compiled for the map where it was built, not where it was placed
            if (Matrix4x4.Invert(rootTransform, out var worldToVisibility))
            {
                scene.WorldToVisibility = worldToVisibility;
            }

            var visNode = new VisibilitySceneNode(scene, voxelVisibility)
            {
                LayerName = "Visibility clusters",
                Transform = rootTransform,
            };
            scene.Add(visNode, false);
        }

        private readonly Dictionary<string, string> LightmapNameToUniformName = new()
        {
            {"irradiance", "g_tIrradiance"},
            {"directional_irradiance_sh2_dc", "g_tIrradiance"},
            {"directional_irradiance", "g_tDirectionalIrradiance"},
            {"directional_irradiance_sh2_r", "g_tDirectionalIrradianceR"},
            {"directional_irradiance_sh2_g", "g_tDirectionalIrradianceG"},
            {"directional_irradiance_sh2_b", "g_tDirectionalIrradianceB"},
            {"directional_irradiance_sh2_lum", "g_tDirectionalIrradianceLum"},
            {"direct_light_shadows", "g_tDirectLightShadows"},
            {"direct_light_indices", "g_tDirectLightIndices"},
            {"direct_light_strengths", "g_tDirectLightStrengths"},
            {"debug_chart_color", "g_tLightmapDebugCharts"},
        };

        private readonly string[] LightmapSetV81_SteamVr = ["g_tIrradiance", "g_tDirectionalIrradiance"];
        private readonly string[] LightmapSetV81 = ["g_tIrradiance", "g_tDirectionalIrradiance", "g_tDirectLightIndices", "g_tDirectLightStrengths"];
        private readonly string[] LightmapSetV82 = ["g_tIrradiance", "g_tDirectionalIrradiance", "g_tDirectLightShadows"];
        private readonly string[] LightmapSetV83 = ["g_tIrradiance", "g_tDirectionalIrradianceR", "g_tDirectionalIrradianceG", "g_tDirectionalIrradianceB", "g_tDirectLightShadows"];
        private readonly string[] LightmapSetV85 = ["g_tIrradiance", "g_tDirectionalIrradianceLum", "g_tDirectLightShadows"];

        /// <summary>
        /// Loads lightmap and lighting information from the world into the scene.
        /// </summary>
        public void LoadWorldLightingInfo()
        {
            ReportLoadingPhase("Loading lighting…");

            var worldLightingInfo = World.GetWorldLightingInfo();
            if (worldLightingInfo == null)
            {
                return;
            }

            var result = scene.LightingInfo;
            result.LightmapVersionNumber = worldLightingInfo.GetInt32Property("m_nLightmapVersionNumber");
            result.LightingData.LightmapUvScale = World.GetLightmapUvScale();
            if (scene.LightingInfo.LightmapVersionNumber == 8)
            {
                result.LightmapGameVersionNumber = worldLightingInfo.GetInt32Property("m_nLightmapGameVersionNumber");
            }

            var lightmaps = worldLightingInfo.GetArray<string>("m_lightMaps") ?? [];

            foreach (var lightmap in lightmaps)
            {
                CancellationToken.ThrowIfCancellationRequested();

                var name = Path.GetFileNameWithoutExtension(lightmap);
                if (LightmapNameToUniformName.TryGetValue(name, out var uniformName))
                {
                    var srgbRead = name == "irradiance";
                    var renderTexture = RendererContext.MaterialLoader.GetTexture(lightmap, srgbRead);
                    result.Lightmaps[uniformName] = renderTexture;

                    if (name == "direct_light_indices")
                    {
                        // point sampling
                        renderTexture.SetFiltering(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
                    }

                    renderTexture.SetWrapMode(RsTextureAddressMode.Clamp);
                }
            }

            bool lightmapPresent(string x) => result.Lightmaps.ContainsKey(x);
            result.HasValidLightmaps = (result.LightmapVersionNumber, result.LightmapGameVersionNumber) switch
            {
                (6, 0) => false,
                (8, 1) => LightmapSetV81.All(lightmapPresent),
                (8, 2) => LightmapSetV82.All(lightmapPresent),
                (8, 3 or 4) => LightmapSetV83.All(lightmapPresent),
                (8, 5) => LightmapSetV85.All(lightmapPresent),
                _ => false,
            };

            if (!result.HasValidLightmaps && LightmapSetV81_SteamVr.All(lightmapPresent))
            {
                // SteamVR Home for now
                if (result.LightmapVersionNumber == 8 && result.LightmapGameVersionNumber == 1)
                {
                    result.LightmapGameVersionNumber = 0;
                    result.HasValidLightmaps = true;
                }
            }

            var attributes = scene.RenderAttributes;
            attributes.TryAdd("S_LIGHTMAP_VERSION_MINOR", (byte)scene.LightingInfo.LightmapGameVersionNumber);

            var blendsFogByMax = attributes["S_LIGHTMAP_VERSION_MINOR"] < 2
                || scene.RendererContext.FileLoader.GameInfo.TonemappingVrForward;
            attributes.TryAdd("S_FOG_CUBE_BLEND_MAX", (byte)(blendsFogByMax ? 1 : 0));

            if (scene.RendererContext.FileLoader.GameInfo.VrLightBinnerSingleLightFastPath)
            {
                scene.RenderAttributes.TryAdd("S_VR_FAST_PATH_LIGHTING", 1);
            }
        }

        // Lumps of entities parsed for Entities that have not spawned yet, by lump name. The first spawn takes
        // them, so that spawn's entities have the keyvalues Entities lists. Any later spawn parses its own, as each
        // instance of a template is an entity of its own.
        private readonly Dictionary<string, List<Entity>> listedLumpEntities = new(StringComparer.OrdinalIgnoreCase);

        // Every lump listed in Entities, so spawning one again lists nothing twice
        private readonly HashSet<string> listedLumps = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Adds a lump's entities to <see cref="Entities"/>, each <c>point_template</c> followed by the entities
        /// of its lump, which spawn only when the template says so.
        /// </summary>
        private void ListEntities(EntityLump lump)
        {
            // Also stops a template whose lump leads back to its own
            if (!listedLumps.Add(lump.Name))
            {
                return;
            }

            var entities = lump.GetEntities();
            listedLumpEntities.Add(lump.Name, entities);

            foreach (var entity in entities)
            {
                Entities.Add(entity);

                if (entity.GetStringProperty("classname") == "point_template"
                    && entity.GetStringProperty("entitylumpname") is { Length: > 0 } templateLumpName
                    && FindEntityLump(templateLumpName) is { } templateLump)
                {
                    ListEntities(templateLump);
                }
            }
        }

        private List<Entity> TakeLumpEntities(EntityLump lump)
            => listedLumpEntities.Remove(lump.Name, out var listed) ? listed : lump.GetEntities();

        /// <summary>
        /// Lists the entities of the lumps the world spawns when it loads in <see cref="Entities"/>, starting over
        /// from what an earlier spawn of the map listed.
        /// </summary>
        /// <returns>The entities of those lumps alone, as the lumps of their templates spawn when told to.</returns>
        internal List<Entity> ListMapEntities()
        {
            Entities.Clear();
            listedLumps.Clear();
            listedLumpEntities.Clear();
            CameraNames.Clear();
            CameraMatrices.Clear();

            var mapEntities = new List<Entity>();

            foreach (var lumpName in World.GetEntityLumpNames())
            {
                CancellationToken.ThrowIfCancellationRequested();

                if (lumpName == null || RendererContext.FileLoader.LoadFileCompiled(lumpName)?.DataBlock is not EntityLump entityLump)
                {
                    continue;
                }

                ListEntities(entityLump);
                mapEntities.AddRange(TakeLumpEntities(entityLump));
            }

            return mapEntities;
        }

        /// <summary>Lists a lump's entities in <see cref="Entities"/> unless they already are.</summary>
        /// <returns>The entities to spawn from the lump, parsed again unless no spawn took the listed ones yet.</returns>
        internal List<Entity> ListLumpEntities(EntityLump lump)
        {
            ListEntities(lump);

            return TakeLumpEntities(lump);
        }

        // Lumps spawning right now, which a stopped world reaches again through templates inside templates
        internal HashSet<string> SpawningLumps { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Notes the cameras, spawn markers and visible world layers among entities spawned from this world.</summary>
        internal void RecordSpawned(IReadOnlyList<BaseEntity> spawned)
        {
            foreach (var entity in spawned)
            {
                switch (entity)
                {
                    case PointCamera camera:
                        CameraNames.Add(camera.CameraName);
                        CameraMatrices.Add(camera.Transform);
                        OfferSpawnCamera(camera, isMaster: false);
                        break;

                    case SpawnPoint spawnPoint:
                        OfferSpawnCamera(spawnPoint, spawnPoint.IsMasterPlayerStart);
                        break;

                    case InfoWorldLayer { IsVisibleOnSpawn: true, WorldLayerName: { } worldLayerName }:
                        DefaultEnabledLayers.Add(worldLayerName);
                        break;
                }
            }
        }

        private List<EntityLump>? worldEntityLumps;

        /// <summary>
        /// Finds a lump of this world by the name the lump gives itself, searching the world's lumps and
        /// their child lumps depth first, so the first match in that order wins.
        /// </summary>
        internal EntityLump? FindEntityLump(string name)
        {
            if (worldEntityLumps == null)
            {
                worldEntityLumps = [];

                HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);

                foreach (var lumpName in World.GetEntityLumpNames().OfType<string>())
                {
                    AddEntityLump(lumpName, worldEntityLumps, visited);
                }
            }

            return worldEntityLumps.Find(lump => lump.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        private void AddEntityLump(string lumpName, List<EntityLump> lumps, HashSet<string> visited)
        {
            if (!visited.Add(lumpName) || RendererContext.FileLoader.LoadFileCompiled(lumpName)?.DataBlock is not EntityLump lump)
            {
                return;
            }

            lumps.Add(lump);

            foreach (var childName in lump.GetChildEntityNames())
            {
                AddEntityLump(childName, lumps, visited);
            }
        }

        /// <summary>
        /// Converts a <c>targetmapname</c> keyvalue to a map path without extension, e.g.
        /// <c>prefabs/misc/team_select</c> to <c>maps/prefabs/misc/team_select</c>.
        /// </summary>
        public static string GetSpawnGroupMapName(string targetMapName)
        {
            ArgumentNullException.ThrowIfNull(targetMapName);

            var name = targetMapName.Replace('\\', '/');

            if (name.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^GameFileLoader.CompiledFileSuffix.Length];
            }

            if (name.EndsWith(".vmap", StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^".vmap".Length];
            }

            if (name.StartsWith("maps/", StringComparison.OrdinalIgnoreCase))
            {
                name = name["maps/".Length..];
            }

            return $"maps/{name}";
        }

        /// <summary>
        /// Loads the navigation mesh for this world. Populates <see cref="NavMesh"/>.
        /// Skips loading if <see cref="NavMesh"/> is already set.
        /// </summary>
        public void LoadNavigationMesh()
        {
            if (NavMesh is not null)
            {
                return;
            }

            var navFilePath = Path.ChangeExtension(MapName, ".nav");
            try
            {
                using var navFileStream = RendererContext.FileLoader.GetFileStream(navFilePath);
                if (navFileStream != null)
                {
                    NavMesh = new NavMeshFile();
                    NavMesh.Read(navFileStream);
                    NavMesh.CustomData = null; // large object, drop it we don't use it
                    NavigationHullNames = NavHullNames.Read(RendererContext.FileLoader, NavMesh.GenerationParams?.HullPresetName);
                    RendererContext.Logger.LogInformation("Navigation mesh loaded from '{NavFilePath}'", navFilePath);
                }
            }
            catch (Exception e)
            {
                RendererContext.Logger.LogError(e, "Couldn't load navigation mesh from '{NavFilePath}'", navFilePath);
            }
        }

        /// <summary>
        /// Loads the navigation space for this world. Populates <see cref="NavSpace"/>.
        /// Skips loading if <see cref="NavSpace"/> is already set.
        /// </summary>
        public void LoadNavigationSpace()
        {
            if (NavSpace is not null)
            {
                return;
            }

            var navSpaceFilePath = Path.ChangeExtension(MapName, ".navspace");
            try
            {
                using var navSpaceFileStream = RendererContext.FileLoader.GetFileStream(navSpaceFilePath);
                if (navSpaceFileStream != null)
                {
                    NavSpace = new NavSpaceFile();
                    NavSpace.Read(navSpaceFileStream);
                    RendererContext.Logger.LogInformation("Navigation space loaded from '{NavSpaceFilePath}'", navSpaceFilePath);
                }
            }
            catch (Exception e)
            {
                RendererContext.Logger.LogError(e, "Couldn't load navigation space from '{NavSpaceFilePath}'", navSpaceFilePath);
            }
        }

        /// <summary>
        /// Loads the navigation flow map for this world. Populates <see cref="NavFlowMap"/>.
        /// Skips loading if <see cref="NavFlowMap"/> is already set.
        /// </summary>
        public void LoadNavigationFlowMap()
        {
            if (NavFlowMap is not null)
            {
                return;
            }

            var navFlowMapFilePath = Path.ChangeExtension(MapName, ".navflowmap");
            try
            {
                using var navFlowMapFileStream = RendererContext.FileLoader.GetFileStream(navFlowMapFilePath);
                if (navFlowMapFileStream != null)
                {
                    NavFlowMap = new NavFlowMapFile();
                    NavFlowMap.Read(navFlowMapFileStream);
                    RendererContext.Logger.LogInformation("Navigation flow map loaded from '{NavFlowMapFilePath}'", navFlowMapFilePath);
                }
            }
            catch (Exception e)
            {
                RendererContext.Logger.LogError(e, "Couldn't load navigation flow map from '{NavFlowMapFilePath}'", navFlowMapFilePath);
            }
        }

        /// <summary>
        /// Loads CS2 baked bomb damage data for this world. Populates <see cref="BombDamage"/>.
        /// Skips loading if <see cref="BombDamage"/> is already set.
        /// </summary>
        public void LoadBombDamageData()
        {
            if (BombDamage is not null)
            {
                return;
            }

            var bombDamagePath = Path.Combine(MapName, "baked_bomb_damage.vdata_c");
            try
            {
                using var bombDamageFile = RendererContext.FileLoader.LoadFile(bombDamagePath);
                if (bombDamageFile?.DataBlock is BombDamage bombDamage)
                {
                    BombDamage = bombDamage;
                    RendererContext.Logger.LogInformation("Loaded CS2 baked bomb damage data from '{BakedBombDamagePath}'", bombDamagePath);
                }
            }
            catch (Exception e)
            {
                RendererContext.Logger.LogError(e, "Couldn't load CS2 baked bomb damage data from '{BakedBombDamagePath}'", bombDamagePath);
            }
        }

        /// <summary>
        /// Takes the entity as <see cref="SpawnCameraMatrix"/> when its classname ranks above the best one
        /// so far in <see cref="SpawnCameraClasses"/>. Ground markers are raised to eye height.
        /// </summary>
        private void OfferSpawnCamera(BaseEntity entity, bool isMaster)
        {
            var priority = Array.IndexOf(SpawnCameraClasses, entity.Classname) * 2;

            if (priority < 0)
            {
                return;
            }

            // A marker flagged as the one to use beats the others of its class
            if (isMaster)
            {
                priority--;
            }

            if (priority >= spawnCameraPriority)
            {
                return;
            }

            var spawnMatrix = entity.Transform;

            if (entity is not PointCamera)
            {
                spawnMatrix.Translation += new Vector3(0, 0, PlayerEyeHeight);
            }

            spawnCameraPriority = priority;
            SpawnCameraMatrix = spawnMatrix;
        }

        /// <summary>
        /// Returns the path to the world resource (<c>.vwrld</c>) for a given map name.
        /// </summary>
        /// <param name="mapName">Path to the map, with or without the compiled file suffix.</param>
        public static string GetWorldNameFromMap(string mapName)
        {
            mapName = mapName.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.InvariantCultureIgnoreCase)
                ? mapName[..^GameFileLoader.CompiledFileSuffix.Length]
                : mapName;

            const string VmapExtension = ".vmap";
            return $"{mapName[..^VmapExtension.Length]}/world.vwrld";
        }
    }
}
