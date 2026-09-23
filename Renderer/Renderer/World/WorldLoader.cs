using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;
using ValvePak;
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

        /// <summary>All entities parsed from the world's entity lumps.</summary>
        public List<Entity> Entities { get; } = [];
        /// <summary>The first world node encountered during loading.</summary>
        public WorldNode? MainWorldNode { get; private set; }

        // Kept apart from world_layer_base so the world geometry can be hidden while collision stays visible.
        // Always enabled, the physics group filter decides what actually draws.
        private const string PhysicsDebugLayerName = "Physics Visualization Layer";

        /// <summary>Layer names that should be visible by default, populated during loading.</summary>
        public HashSet<string> DefaultEnabledLayers { get; } = ["No layer", "Entities", EditorEntityNode.LayerName, Scene.ParticlesLayerName, PhysicsDebugLayerName];

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
        /// Dota 2, then the generic player start, then the camera entities as last resorts.
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
            "point_camera",
            "point_camera_vertical_fov",
            "point_devshot_camera",
        ];

        private const float PlayerEyeHeight = 64f;
        private int spawnCameraPriority = int.MaxValue;

        /// <summary>
        /// The maps this one placed into scenes of their own while loading, such as its 3D sky. They are the
        /// caller's to draw, see <see cref="Renderer.AddSpawnGroup"/>.
        /// </summary>
        public List<SpawnGroup> SpawnGroups { get; } = [];

        /// <summary>The loaded navigation mesh, populated by <see cref="LoadNavigationMesh"/>.</summary>
        public NavMeshFile? NavMesh { get; set; }
        /// <summary>Baked bomb damage data for CS2, null if it doesn't exist. Populated by <see cref="LoadBombDamageData"/>.</summary>
        public BombDamage? BombDamage { get; set; }

        /// <summary>The <c>sky_camera</c>s of this map, the first of which a 3D sky is seen from.</summary>
        private List<SkyCamera> SkyCameras { get; } = [];

        /// <summary>Applied to everything this map loads.</summary>
        private readonly Matrix4x4 rootTransform;

        private readonly EntitySystem entitySystem;

        /// <summary>What a load is, which decides how much of the map it brings in.</summary>
        private enum LoadKind
        {
            /// <summary>The map itself: it owns the physics world, the worldspawn, and activates the entities.</summary>
            Map,

            /// <summary>
            /// A spawn group placed inside another map in a scene of its own, such as a 3D sky. Its lighting
            /// and visibility come along, but its entities are activated by whoever loaded it.
            /// </summary>
            SpawnGroup,

            /// <summary>
            /// A prefab placed into the scene of the map that names it. Prefabs are compiled without lighting
            /// or visibility of their own, so only their entities and geometry come along.
            /// </summary>
            Prefab,
        }

        private readonly LoadKind loadKind;

        /// <summary>Whether this load is placed inside another map rather than being the map itself.</summary>
        private bool IsNested => loadKind != LoadKind.Map;

        /// <summary>
        /// Loads a map by name, performing a full load of all world components.
        /// </summary>
        /// <param name="mapResourceName">Path to the <c>.vmap</c> or <c>.vmap_c</c> resource.</param>
        /// <param name="scene">The scene to load the world into.</param>
        /// <param name="entitySystem">The entity world this map's entities spawn into.</param>
        /// <param name="rootTransform">Transform applied to the whole map, identity when <see langword="null"/>.</param>
        public static WorldLoader LoadMap(string mapResourceName, Scene scene, EntitySystem entitySystem, Matrix4x4? rootTransform = null)
            => LoadMap(mapResourceName, scene, entitySystem, rootTransform, LoadKind.Map);

        private static WorldLoader LoadMap(string mapResourceName, Scene scene, EntitySystem entitySystem, Matrix4x4? rootTransform, LoadKind loadKind)
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

            var loader = new WorldLoader((WorldResource)worldResource.DataBlock!, scene, entitySystem, rootTransform, loadKind);
            loader.Load(mapResource.ExternalReferences);
            return loader;
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
            : this(world, scene, entitySystem, rootTransform, LoadKind.Map)
        {
        }

        private WorldLoader(WorldResource world, Scene scene, EntitySystem entitySystem, Matrix4x4? rootTransform, LoadKind loadKind)
        {
            this.loadKind = loadKind;
            MapName = Path.GetDirectoryName(world.Resource!.FileName!)!.Replace('\\', '/');
            World = world;
            this.scene = scene;
            this.entitySystem = entitySystem;
            this.rootTransform = rootTransform ?? Matrix4x4.Identity;
            RendererContext = scene.RendererContext;
        }

        /// <summary> Loading screen hints.</summary>
        public IProgress<string>? LoadingProgress { get; set; }

        private string? currentLoadingPhase;

        private void ReportLoadingPhase(string phase)
        {
            currentLoadingPhase = phase;
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

                var resourceNames = mapResourceReferences.ResourceRefInfoList
                    .Select(x => x.Name)
                    .Where(r => !r.StartsWith("_bakeresourcecache", StringComparison.Ordinal));

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
                        HashSet<string> toolIcons = [];
                        foreach (var entity in entityLump.GetEntities())
                        {
                            var className = entity.GetStringProperty("classname");
                            if (className != null)
                            {
                                var hammerEntity = HammerEntities.Get(className);
                                if (hammerEntity?.Icons.Length > 0)
                                {
                                    toolIcons.UnionWith(hammerEntity.Icons);
                                }
                            }
                        }

                        Parallel.ForEach(toolIcons, parallelOptions, file =>
                        {
                            PreloadResource(file);
                        });
                    }
                });
            }
        }

        /// <summary>
        /// Loads all world components: lighting, entities, world nodes, physics, visibility, bomb damage data, and navigation mesh.
        /// Navigation mesh loading is parallelized with resource preloading when references are provided.
        /// </summary>
        /// <param name="mapResourceReferences">Optional external reference list from the map resource, used to preload assets in parallel.</param>
        public void Load(ResourceExtRefList? mapResourceReferences = null)
        {
            // A prefab lands in the scene of the map naming it, whose lighting, visibility and gameplay data
            // are that map's own
            var ownsScene = loadKind != LoadKind.Prefab;

            // Non resource files not covered by ParallelPreloadResources
            var navMeshTask = ownsScene ? Task.Run(LoadNavigationMesh) : Task.CompletedTask;

            ParallelPreloadResources(mapResourceReferences);

            if (ownsScene)
            {
                LoadWorldLightingInfo();
            }

            LoadEntities();
            LoadWorldNodes();
            LoadWorldPhysics();

            if (ownsScene)
            {
                LoadWorldVisibility();
                LoadBombDamageData();
            }

            navMeshTask.Wait();
        }

        /// <summary>
        /// Loads all entities from the world's entity lumps into the scene.
        /// </summary>
        public void LoadEntities()
        {
            ReportLoadingPhase("Loading entities…");

            foreach (var lumpName in World.GetEntityLumpNames())
            {
                CancellationToken.ThrowIfCancellationRequested();

                if (lumpName == null)
                {
                    continue;
                }

                var newResource = RendererContext.FileLoader.LoadFileCompiled(lumpName);

                if (newResource == null)
                {
                    continue;
                }

                var entityLump = (EntityLump?)newResource.DataBlock;
                if (entityLump == null)
                {
                    continue;
                }

                LoadEntitiesFromLump(entityLump, "Entities");
            }

            // Every entity exists now, so the simulated ones can resolve each other by name. A nested
            // group loads part way through the outer map's own lump, so it leaves activation to that
            // load, which runs once everything - every spawn group - has spawned.
            if (!IsNested)
            {
                entitySystem.Activate();
            }

            // A prefab's lights are the scene's too, which the map naming it stores once its own lump is done
            if (loadKind != LoadKind.Prefab)
            {
                scene.LightingInfo.StoreLights(
                    scene.AllNodes.OfType<SceneLight>().ToList()
                );
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

                foreach (var physSceneNode in PhysSceneNode.CreatePhysSceneNodes(scene, phys, physResource.FileName[..^2]))
                {
                    physSceneNode.Transform = rootTransform;
                    physSceneNode.LayerName = PhysicsDebugLayerName;
                    scene.Add(physSceneNode, true);
                }

                // Only the player's world needs collision
                if (phys.Parts.Length > 0 && !IsNested)
                {
                    entitySystem.PhysicsWorld = new Rubikon(phys);
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
            {"direct_light_shadows", "g_tDirectLightShadows"},
            {"direct_light_indices", "g_tDirectLightIndices"},
            {"direct_light_strengths", "g_tDirectLightStrengths"},
            {"debug_chart_color", "g_tLightmapDebugCharts"},
        };

        private readonly string[] LightmapSetV81_SteamVr = ["g_tIrradiance", "g_tDirectionalIrradiance"];
        private readonly string[] LightmapSetV81 = ["g_tIrradiance", "g_tDirectionalIrradiance", "g_tDirectLightIndices", "g_tDirectLightStrengths"];
        private readonly string[] LightmapSetV82 = ["g_tIrradiance", "g_tDirectionalIrradiance", "g_tDirectLightShadows"];
        private readonly string[] LightmapSetV83 = ["g_tIrradiance", "g_tDirectionalIrradianceR", "g_tDirectionalIrradianceG", "g_tDirectionalIrradianceB", "g_tDirectLightShadows"];

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
            result.UsesLegacyBarnBrightness = RendererContext.FileLoader.GameName == "Aperture Desk Job"; // adj predates the photometric model.
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

            scene.RenderAttributes.TryAdd("S_LIGHTMAP_VERSION_MINOR", (byte)scene.LightingInfo.LightmapGameVersionNumber);
        }

        private void LoadEntitiesFromLump(EntityLump entityLump, string originalLayerName)
        {
            // Cubemaps and probes spawn before everything else. Meshes copy the scene's render attributes into
            // their shader combos as they are built, and three of those come from these entities:
            // S_SCENE_CUBEMAP_TYPE from the first cubemap's texture (a cube or a cube array),
            // S_SCENE_PROBE_TYPE from whether a probe volume is an atlas, and HasValidLightProbes, which
            // picks D_BAKED_LIGHTING_FROM_PROBE. A model spawned before them compiles the wrong combos.
            // Everything else about them binds after load, in Scene.Initialize, so only this order matters.
            // A read-only pass settling those three values from the keyvalues first would make it unneeded.
            static bool IsCubemapOrProbe(string cls)
                => cls == "env_combined_light_probe_volume"
                || cls == "env_light_probe_volume"
                || cls == "env_cubemap_box"
                || cls == "env_cubemap";

            var traversed = EntityLumpTraversal.EnumerateEntities(
                entityLump,
                RendererContext.FileLoader,
                rootTransform,
                onMissingChildLump: name => RendererContext.Logger.LogWarning("Failed to find child entity lump with name {EntityLumpName}", name))
                .ToList();

            var entitiesReordered = traversed
                .Select(t => (t.Entity, t.ParentTransform, t.FromTemplate, Classname: t.Entity.GetStringProperty("classname")))
                .Where(x => x.Classname != null)
                .Select(x => (x.Entity, x.ParentTransform, x.FromTemplate, Classname: x.Classname!))
                .OrderByDescending(x => IsCubemapOrProbe(x.Classname));

            Entities.AddRange(traversed.Select(t => t.Entity));

            var firstSpawned = entitySystem.Entities.Count;

            foreach (var (entity, parentTransform, fromTemplate, classname) in entitiesReordered)
            {
                CancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // A point_template shares its layer with what it spawns
                    var layerName = fromTemplate || classname == "point_template" ? EditorEntityNode.TemplateLayerName : originalLayerName;

                    var disabled = entity.GetBooleanProperty("startdisabled");

                    if (!disabled)
                    {
                        disabled = !entity.GetBooleanProperty("enabled", true);
                    }

                    if (disabled && layerName == "Entities")
                    {
                        layerName = "Entities (disabled)";
                    }

                    // Every entity joins the entity system, so it can be named and targeted, and draws itself
                    var created = entitySystem.CreateEntity(entity, parentTransform, layerName, scene);

                    switch (created)
                    {
                        // A nested group carries a worldspawn of its own, which stays an ordinary inert entity
                        case WorldEntity worldspawn when !IsNested:
                            entitySystem.SetWorld(worldspawn);
                            break;

                        case InfoWorldLayer { IsVisibleOnSpawn: true, WorldLayerName: { } worldLayerName }:
                            DefaultEnabledLayers.Add(worldLayerName);
                            break;

                        case PointCamera camera:
                            if (camera is SkyCamera sky)
                            {
                                SkyCameras.Add(sky);
                            }

                            CameraNames.Add(camera.CameraName);
                            CameraMatrices.Add(camera.Transform);
                            OfferSpawnCamera(camera, isMaster: false);
                            break;

                        case SpawnPoint spawnPoint:
                            OfferSpawnCamera(spawnPoint, spawnPoint.IsMasterPlayerStart);
                            break;
                    }

                    if (created != null && IsSpawnGroupPlacement(classname, entity))
                    {
                        if (classname == "skybox_reference")
                        {
                            LoadSkybox(created);
                        }
                        else
                        {
                            LoadPrefab(created);
                        }
                    }
                }
                catch (Exception e)
                {
                    var id = entity.GetStringProperty("hammeruniqueid", string.Empty);

                    throw new InvalidDataException($"Failed to process entity '{classname}' (hammeruniqueid={id})", e);
                }
            }

            // Once the whole lump has spawned, so a line can end at an entity authored after the one it starts at.
            // A 3D sky or a prefab loaded part way through adds its own entities, which draw their own lines.
            var lumpEntities = traversed.Select(static t => t.Entity).ToHashSet();

            for (var i = firstSpawned; i < entitySystem.Entities.Count; i++)
            {
                var spawned = entitySystem.Entities[i];

                if (spawned.Scene != scene || spawned.Data == null || !lumpEntities.Contains(spawned.Data))
                {
                    continue;
                }

                CreateEntityConnectionLines(spawned);
                CreateHelperLines(spawned);
            }
        }

        /// <summary>
        /// Whether an entity places another map into this one: a <c>skybox_reference</c>, or a prefab left for
        /// the game to load, which is a <c>point_prefab</c> or any class flagged <c>ispointprefab</c>, as the
        /// CS2 team select and team intro stages are.
        /// </summary>
        private static bool IsSpawnGroupPlacement(string classname, Entity entity)
            => classname is "skybox_reference" or "point_prefab" || entity.GetBooleanProperty("ispointprefab");

        /// <summary>
        /// Loads the map a <c>skybox_reference</c> names as this map's 3D sky: a spawn group in a scene of
        /// its own, drawn through a camera that follows the main one.
        /// </summary>
        private void LoadSkybox(BaseEntity skyboxReference)
        {
            if (skyboxReference.Data?.GetStringProperty("targetmapname") is not { Length: > 0 } targetMapName)
            {
                return;
            }

            // Origin and angles only: a 3D sky is not scaled, the sky camera applies the scale instead
            var reference = skyboxReference.RigidTransform;

            // Scenery: nothing can reach the sky, so its entities never build a collider. Every compiled
            // reference names the sky's world group; one that did not would join the map's.
            var skyScene = new Scene(RendererContext)
            {
                EntitiesCollide = false,
                WorldGroup = skyboxReference.Data.GetStringProperty("worldgroupid") is { Length: > 0 } worldGroup ? worldGroup : "skyboxWorldGroup0",
            };

            LoadingProgress?.Report("Loading 3D sky…");

            var skyLoader = LoadNestedMap(RendererContext, entitySystem, targetMapName, skyScene, reference, LoadKind.SpawnGroup, out var package);

            if (currentLoadingPhase != null)
            {
                LoadingProgress?.Report(currentLoadingPhase);
            }

            if (skyLoader == null)
            {
                skyScene.Dispose();
                return;
            }

            SpawnGroups.Add(new SpawnGroup(skyLoader.MapName, skyScene, reference, skyLoader.Entities)
            {
                PlacedBy = skyboxReference,
                MountedPackage = package,
            });

            // The camera the sky is drawn through magnifies it, so its markers shrink to come back out at
            // their normal size, and anything placed by hand in the viewer lands where it is seen. The view
            // works the sky out every frame, but its entities do not move once loaded.
            var sky = SkyTransform.FromEntities(reference.Translation, skyLoader.SkyCameras.FirstOrDefault());

            skyScene.ToViewerWorld = sky.SkyToWorld;
            skyScene.MarkerScale = 1f / sky.Scale;

            // The scale is only known once the sky map has loaded, so the markers it shrinks are already
            // placed. Whatever an entity owns it re-places itself from here on.
            foreach (var marker in skyScene.AllNodes)
            {
                if (marker.PlacementScale != 1f)
                {
                    marker.Transform = marker.ApplyPlacementScale(marker.Transform);
                }
            }
        }

        /// <summary>
        /// Loads the map a prefab entity names into this map's scene, placed where the entity is. A compiled
        /// map has its prefabs merged in already; the ones left are what the game loads at runtime.
        /// </summary>
        private void LoadPrefab(BaseEntity prefab)
        {
            if (prefab.Data?.GetStringProperty("targetmapname") is not { Length: > 0 } targetMapName)
            {
                return;
            }

            var prefabLoader = LoadNestedMap(RendererContext, entitySystem, targetMapName, scene, prefab.RigidTransform, LoadKind.Prefab, out var package);

            if (package != null)
            {
                RendererContext.FileLoader.RemovePackageFromSearch(package);
                package.Dispose();
            }

            if (prefabLoader != null)
            {
                Entities.AddRange(prefabLoader.Entities);
            }
        }

        /// <summary>
        /// Loads a map as a spawn group while another one plays, the way an <c>info_spawngroup_load_unload</c>
        /// does: into a scene of its own, moved so the entity it names as its landmark lands on
        /// <paramref name="landmarkOrigin"/>. The engine lines the landmarks up by their origins alone and
        /// ignores their angles. The group's entities still need activating, see <see cref="EntitySystem"/>.
        /// </summary>
        /// <param name="rendererContext">The context to load through.</param>
        /// <param name="entitySystem">The entity world the map's entities spawn into.</param>
        /// <param name="targetMapName">The map as the entity names it, such as <c>stages/lms_stage1</c>.</param>
        /// <param name="landmark">The name of the landmark entity in both maps, or <see langword="null"/> to load the map in place.</param>
        /// <param name="landmarkOrigin">Where the landmark is in the map that loads the group.</param>
        /// <returns>The loaded group with its scene initialized, or <see langword="null"/> when the map could not be found.</returns>
        public static SpawnGroup? LoadSpawnGroup(RendererContext rendererContext, EntitySystem entitySystem, string targetMapName,
            string? landmark, Vector3 landmarkOrigin)
        {
            ArgumentNullException.ThrowIfNull(rendererContext);
            ArgumentNullException.ThrowIfNull(entitySystem);

            var mapName = GetSpawnGroupMapName(targetMapName);

            if (!TryMountMapPackage(rendererContext, mapName, out var package))
            {
                return null;
            }

            Scene? scene = null;

            try
            {
                var transform = Matrix4x4.Identity;

                if (landmark != null && FindEntityOrigin(rendererContext, $"{mapName}.vmap", landmark) is { } groupLandmarkOrigin)
                {
                    transform = Matrix4x4.CreateTranslation(landmarkOrigin - groupLandmarkOrigin);
                }

                scene = new Scene(rendererContext);
                var loader = LoadMap($"{mapName}.vmap", scene, entitySystem, transform, LoadKind.SpawnGroup);

                scene.Initialize();

                var group = new SpawnGroup(loader.MapName, scene, transform, loader.Entities)
                {
                    MountedPackage = package,
                };

                scene = null;
                package = null;

                return group;
            }
            catch (FileNotFoundException e)
            {
                rendererContext.Logger.LogWarning("Not loading spawn group '{TargetMapName}': {Message}", targetMapName, e.Message);
                return null;
            }
            finally
            {
                scene?.Dispose();

                if (package != null)
                {
                    rendererContext.FileLoader.RemovePackageFromSearch(package);
                    package.Dispose();
                }
            }
        }

        /// <summary>
        /// Finds where a map places the entity of a name, reading only its entity lumps. Compiled names carry
        /// a <c>[PR#]</c> prefix that the keyvalues naming them leave out, so either spelling matches.
        /// </summary>
        /// <returns>The entity's origin, or <see langword="null"/> when the map has none of that name.</returns>
        private static Vector3? FindEntityOrigin(RendererContext rendererContext, string mapResourceName, string targetName)
        {
            var worldResource = rendererContext.FileLoader.LoadFileCompiled(GetWorldNameFromMap(mapResourceName));

            if (worldResource?.DataBlock is not WorldResource world)
            {
                return null;
            }

            foreach (var lumpName in world.GetEntityLumpNames())
            {
                if (lumpName == null || rendererContext.FileLoader.LoadFileCompiled(lumpName)?.DataBlock is not EntityLump lump)
                {
                    continue;
                }

                foreach (var (entity, parentTransform, _) in EntityLumpTraversal.EnumerateEntities(lump, rendererContext.FileLoader, Matrix4x4.Identity))
                {
                    var name = entity.TargetName;

                    if (name != null && (name.Equals(targetName, StringComparison.OrdinalIgnoreCase)
                        || EntityLump.RemoveTargetnamePrefix(name).Equals(targetName, StringComparison.OrdinalIgnoreCase)))
                    {
                        return Vector3.Transform(entity.GetVector3Property("origin"), parentTransform);
                    }
                }
            }

            rendererContext.Logger.LogWarning("Found no landmark named '{Landmark}' in '{Map}'", targetName, mapResourceName);
            return null;
        }

        /// <summary>
        /// Loads a map that another one places, from the package it ships in. The engine names such maps
        /// relative to <c>maps/</c>, with or without it and the extension, and mounts <c>maps/&lt;name&gt;.vpk</c>.
        /// </summary>
        /// <param name="rendererContext">The context to load through.</param>
        /// <param name="entitySystem">The entity world the map's entities spawn into.</param>
        /// <param name="targetMapName">The map as the placing entity names it.</param>
        /// <param name="intoScene">The scene to load it into.</param>
        /// <param name="transform">Where to place it.</param>
        /// <param name="loadKind">How much of the map to load.</param>
        /// <param name="package">The package mounted for it, which the caller removes once done with the map.</param>
        /// <returns>The finished load, or <see langword="null"/> when the map could not be found.</returns>
        private static WorldLoader? LoadNestedMap(RendererContext rendererContext, EntitySystem entitySystem, string targetMapName,
            Scene intoScene, Matrix4x4 transform, LoadKind loadKind, out Package? package)
        {
            var mapName = GetSpawnGroupMapName(targetMapName);

            if (!TryMountMapPackage(rendererContext, mapName, out package))
            {
                return null;
            }

            try
            {
                return LoadMap($"{mapName}.vmap", intoScene, entitySystem, transform, loadKind);
            }
            catch (FileNotFoundException e)
            {
                rendererContext.Logger.LogWarning("Not loading '{TargetMapName}': {Message}", targetMapName, e.Message);
                return null;
            }
        }

        /// <summary>
        /// Turns the map name a placing entity carries into the path of the map without its extension, e.g.
        /// <c>prefabs/misc/team_select</c> into <c>maps/prefabs/misc/team_select</c>.
        /// </summary>
        /// <param name="targetMapName">The map as the entity names it.</param>
        /// <returns>The map's path under <c>maps/</c>, without an extension.</returns>
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
        /// Mounts the package a map ships in, <c>&lt;map&gt;.vpk</c>, so the map's own files resolve. It can
        /// sit on disk or inside a package that is mounted already, such as a workshop addon.
        /// </summary>
        /// <param name="rendererContext">The context whose file loader to mount it into.</param>
        /// <param name="mapName">The map's path without an extension.</param>
        /// <param name="package">The mounted package, which the caller removes and disposes once done.</param>
        /// <returns>Whether the package was found and mounted.</returns>
        private static bool TryMountMapPackage(RendererContext rendererContext, string mapName, out Package? package)
        {
            package = null;

            var vpkFound = rendererContext.FileLoader.FindFile($"{mapName}.vpk");

            if (vpkFound.PathOnDisk != null)
            {
                // TODO: Due to the way gui contexts work, we're preloading the vpk into parent context
                package = rendererContext.FileLoader.AddPackageToSearch(vpkFound.PathOnDisk);
                return true;
            }

            if (vpkFound.PackageEntry == null)
            {
                return false; // Not found logged by FindFile
            }

            Debug.Assert(vpkFound.Package != null);

            var innerVpkName = vpkFound.PackageEntry.GetFullPath();

            rendererContext.Logger.LogInformation("Preloading vpk \"{InnerVpkName}\" from \"{PackageFileName}\"", innerVpkName, vpkFound.Package.FileName);

            // TODO: Should FileLoader have a method that opens stream for us?
            var stream = GameFileLoader.GetPackageEntryStream(vpkFound.Package, vpkFound.PackageEntry);

            var innerPackage = new Package();

            try
            {
                innerPackage.SetFileName(innerVpkName);
                innerPackage.OptimizeEntriesForBinarySearch(StringComparison.OrdinalIgnoreCase);
                innerPackage.Read(stream);

                rendererContext.FileLoader.AddPackageToSearch(innerPackage);

                package = innerPackage;
                innerPackage = null;
            }
            finally
            {
                innerPackage?.Dispose();
            }

            return true;
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
                    RendererContext.Logger.LogInformation("Navigation mesh loaded from '{NavFilePath}'", navFilePath);
                }
            }
            catch (Exception e)
            {
                RendererContext.Logger.LogError(e, "Couldn't load navigation mesh from '{NavFilePath}'", navFilePath);
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
        /// Draws the helper lines the entity's Hammer class declares, from the entity to the ones its
        /// keyvalues name, as the editor shows them.
        /// </summary>
        private void CreateHelperLines(BaseEntity entity)
        {
            if (entity.Data is not { } data || HammerEntities.Get(entity.Classname) is not { Lines.Length: > 0 } hammerEntity)
            {
                return;
            }

            var layerName = entity.LayerName == EditorEntityNode.TemplateLayerName ? EditorEntityNode.TemplateLayerName : EditorEntityNode.LayerName;

            foreach (var line in hammerEntity.Lines)
            {
                if (data.GetStringProperty(line.StartValueKey) is not { } startValue
                    || FindHelperLineEnd(line.StartKey, startValue) is not { } startEntity)
                {
                    continue;
                }

                var start = startEntity.Transform.Translation;
                var end = entity.Transform.Translation;

                if (line.EndKey != null && line.EndValueKey != null)
                {
                    if (data.GetStringProperty(line.EndValueKey) is not { } endValue
                        || FindHelperLineEnd(line.EndKey, endValue) is not { } endEntity)
                    {
                        continue;
                    }

                    end = endEntity.Transform.Translation;
                }

                var origin = (start + end) / 2f;

                var lineNode = new LineSceneNode(scene, start - origin, end - origin, line.Color, line.Color)
                {
                    LayerName = layerName,
                    Transform = Matrix4x4.CreateTranslation(origin),
                };

                scene.Add(lineNode, true);
            }
        }

        /// <summary>Draws a line from the entity to every entity its entity I/O connections reach.</summary>
        private void CreateEntityConnectionLines(BaseEntity entity)
        {
            if (entity.Data?.Connections is not { } connections)
            {
                return;
            }

            var start = entity.Transform.Translation;
            var alreadySeen = new HashSet<BaseEntity>(connections.Count);

            foreach (var connection in connections)
            {
                var matched = false;

                // The entity as the caller, so a connection aimed at !self reaches it
                foreach (var target in entitySystem.FindTargets(new EntityIOTarget(connection.TargetName, connection.TargetType), caller: entity))
                {
                    // A 3D sky shares names with the map it is placed in
                    if (target.Scene.WorldGroup != scene.WorldGroup)
                    {
                        continue;
                    }

                    matched = true;

                    if (!alreadySeen.Add(target))
                    {
                        continue;
                    }

                    var end = target.Transform.Translation;
                    var origin = (start + end) / 2f;

                    var lineNode = new LineSceneNode(scene, start - origin, end - origin, new Color32(0, 255, 0), new Color32(255, 0, 0))
                    {
                        LayerName = "Entity Connections",
                        Transform = Matrix4x4.CreateTranslation(origin),
#if DEBUG
                        Name = $"Line from {entity.Data.GetStringProperty("hammeruniqueid")} to {target.Data?.GetStringProperty("hammeruniqueid")}"
#endif
                    };

                    scene.Add(lineNode, true);
                }

                if (!matched)
                {
                    RendererContext.Logger.LogDebug("Skipping entity i/o output {TargetName}: no entity matches it", connection.TargetName);
                }
            }
        }

        /// <summary>
        /// Finds the entity a helper line ends at. Lines address entities by name; the few Hammer classes that
        /// address AI nodes by <c>nodeid</c> instead do not appear in compiled maps.
        /// </summary>
        private BaseEntity? FindHelperLineEnd(string key, string name)
            => key.Equals("targetname", StringComparison.OrdinalIgnoreCase)
                ? entitySystem.FindAllByTargetName(name, scene).FirstOrDefault()
                : null;

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
