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

        /// <summary>The 3D sky of this map, if it has one. Populated during entity loading.</summary>
        public Skybox3D? Skybox3D { get; private set; }
        /// <summary>
        /// The 2D skybox, if one was found during entity loading. A map can leave its own <c>env_sky</c>
        /// disabled and carry the enabled one in its 3D sky, as de_vertigo does.
        /// </summary>
        public SceneSkybox2D? Skybox2D => scene.Skybox2D ?? Skybox3D?.Scene.Skybox2D;
        /// <summary>The loaded navigation mesh, populated by <see cref="LoadNavigationMesh"/>.</summary>
        public NavMeshFile? NavMesh { get; set; }
        /// <summary>Baked bomb damage data for CS2, null if it doesn't exist. Populated by <see cref="LoadBombDamageData"/>.</summary>
        public BombDamage? BombDamage { get; set; }

        /// <summary>The first <c>sky_camera</c> in this map, used when it is loaded as another map's 3D sky.</summary>
        private (Vector3 Origin, float Scale)? skyCamera;

        /// <summary>Applied to everything this map loads.</summary>
        private readonly Matrix4x4 rootTransform;

        private readonly EntitySystem entitySystem;

        /// <summary>
        /// Whether this load is a spawn group placed inside another map, such as a 3D sky. Only the
        /// outermost load gets a physics world and activates the entities, once every group has spawned.
        /// </summary>
        private readonly bool isNestedSpawnGroup;

        /// <summary>
        /// Loads a map by name, performing a full load of all world components.
        /// </summary>
        /// <param name="mapResourceName">Path to the <c>.vmap</c> or <c>.vmap_c</c> resource.</param>
        /// <param name="scene">The scene to load the world into.</param>
        /// <param name="entitySystem">The entity world this map's entities spawn into.</param>
        /// <param name="rootTransform">Transform applied to the whole map, identity when <see langword="null"/>.</param>
        public static WorldLoader LoadMap(string mapResourceName, Scene scene, EntitySystem entitySystem, Matrix4x4? rootTransform = null)
            => LoadMap(mapResourceName, scene, entitySystem, rootTransform, nestedSpawnGroup: false);

        private static WorldLoader LoadMap(string mapResourceName, Scene scene, EntitySystem entitySystem, Matrix4x4? rootTransform, bool nestedSpawnGroup)
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

            var loader = new WorldLoader((WorldResource)worldResource.DataBlock!, scene, entitySystem, rootTransform, nestedSpawnGroup);
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
            : this(world, scene, entitySystem, rootTransform, nestedSpawnGroup: false)
        {
        }

        private WorldLoader(WorldResource world, Scene scene, EntitySystem entitySystem, Matrix4x4? rootTransform, bool nestedSpawnGroup)
        {
            this.isNestedSpawnGroup = nestedSpawnGroup;
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
            // Non resource files not covered by ParallelPreloadResources
            var navMeshTask = Task.Run(LoadNavigationMesh);

            ParallelPreloadResources(mapResourceReferences);
            LoadWorldLightingInfo();
            LoadEntities();
            LoadWorldNodes();
            LoadWorldPhysics();
            LoadWorldVisibility();
            LoadBombDamageData();

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
            if (!isNestedSpawnGroup)
            {
                entitySystem.Activate();
            }

            scene.LightingInfo.StoreLights(
                scene.AllNodes.OfType<SceneLight>().ToList()
            );
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
                if (phys.Parts.Length > 0 && !isNestedSpawnGroup)
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
                        case WorldEntity worldspawn when !isNestedSpawnGroup:
                            entitySystem.SetWorld(worldspawn);
                            break;

                        case InfoWorldLayer { IsVisibleOnSpawn: true, WorldLayerName: { } worldLayerName }:
                            DefaultEnabledLayers.Add(worldLayerName);
                            break;

                        case PointCamera camera:
                            // Only the first one is used
                            if (camera is SkyCamera sky)
                            {
                                skyCamera ??= (sky.Transform.Translation, sky.SkyScale);
                            }

                            CameraNames.Add(camera.CameraName);
                            CameraMatrices.Add(camera.Transform);
                            OfferSpawnCamera(camera, isMaster: false);
                            break;

                        case SpawnPoint spawnPoint:
                            OfferSpawnCamera(spawnPoint, spawnPoint.IsMasterPlayerStart);
                            break;
                    }

                    if (classname == "skybox_reference" && created != null)
                    {
                        LoadSkybox(created);
                    }
                }
                catch (Exception e)
                {
                    var id = entity.GetStringProperty("hammeruniqueid", string.Empty);

                    throw new InvalidDataException($"Failed to process entity '{classname}' (hammeruniqueid={id})", e);
                }
            }

            // Once the whole lump has spawned, so a line can end at an entity authored after the one it starts at.
            // A 3D sky loaded part way through adds its own entities, which draw their own lines.
            for (var i = firstSpawned; i < entitySystem.Entities.Count; i++)
            {
                var spawned = entitySystem.Entities[i];

                if (spawned.Scene != scene || spawned.Data == null)
                {
                    continue;
                }

                CreateEntityConnectionLines(spawned);
                CreateHelperLines(spawned);
            }
        }

        private void LoadSkybox(BaseEntity skyboxReference)
        {
            var targetmapname = skyboxReference.Data?.GetStringProperty("targetmapname");

            if (targetmapname == null)
            {
                return;
            }

            if (!targetmapname.EndsWith(".vmap", StringComparison.InvariantCulture))
            {
                RendererContext.Logger.LogWarning("Not loading skybox '{Targetmapname}' because it did not end with .vmap", targetmapname);
                return;
            }

            if (Skybox3D != null)
            {
                RendererContext.Logger.LogWarning("Not loading skybox '{Targetmapname}' because this map already placed one", targetmapname);
                return;
            }

            // Maps have to be packed in a vpk?
            var vpkFile = Path.ChangeExtension(targetmapname, ".vpk");
            var vpkFound = RendererContext.FileLoader.FindFile(vpkFile);
            Package? package;

            // Load the skybox map vpk and make it searchable in the file loader
            if (vpkFound.PathOnDisk != null)
            {
                // TODO: Due to the way gui contexts work, we're preloading the vpk into parent context
                package = RendererContext.FileLoader.AddPackageToSearch(vpkFound.PathOnDisk);
            }
            else if (vpkFound.PackageEntry != null)
            {
                Debug.Assert(vpkFound.Package != null);

                var innerVpkName = vpkFound.PackageEntry.GetFullPath();

                RendererContext.Logger.LogInformation("Preloading vpk \"{InnerVpkName}\" from \"{PackageFileName}\"", innerVpkName, vpkFound.Package.FileName);

                // TODO: Should FileLoader have a method that opens stream for us?
                var stream = GameFileLoader.GetPackageEntryStream(vpkFound.Package, vpkFound.PackageEntry);

                package = new Package();

                try
                {
                    package.SetFileName(innerVpkName);
                    package.OptimizeEntriesForBinarySearch(StringComparison.OrdinalIgnoreCase);
                    package.Read(stream);

                    RendererContext.FileLoader.AddPackageToSearch(package);

                    package = null;
                }
                finally
                {
                    package?.Dispose();
                }
            }
            else
            {
                return; // Not found logged by FindFile
            }

            // Origin and angles only: a 3D sky is not scaled, the sky camera applies the scale instead
            var reference = skyboxReference.RigidTransform;

            // Entities are global: the skybox is another spawn group
            // Scenery: nothing can reach the sky, so its entities never build a collider
            var skyScene = new Scene(RendererContext) { EntitiesCollide = false };

            LoadingProgress?.Report("Loading 3D sky…");

            var skyLoader = LoadMap(targetmapname, skyScene, entitySystem, reference, nestedSpawnGroup: true);

            if (currentLoadingPhase != null)
            {
                LoadingProgress?.Report(currentLoadingPhase);
            }

            var (skyOrigin, skyScale) = skyLoader.skyCamera ?? (Vector3.Zero, 1f);

            Skybox3D = new Skybox3D(skyScene, reference, skyOrigin, skyScale, scene.FogInfo, skyLoader.Entities.ToHashSet());

            PlaceSkyboxEditorMarkers(Skybox3D);

            if (package != null)
            {
                RendererContext.FileLoader.RemovePackageFromSearch(package);
            }
        }

        /// <summary>
        /// Shrinks the editor markers of the 3D sky's entities by the sky scale, so the camera the sky is
        /// drawn through magnifies them back to the size of any other marker.
        /// </summary>
        private static void PlaceSkyboxEditorMarkers(Skybox3D skybox)
        {
            // The scale is only known once the sky map has loaded, so the nodes it applies to are
            // already placed. Whatever an entity owns it re-places itself from here on.
            foreach (var marker in skybox.Scene.AllNodes)
            {
                if (marker.PlacementScale != 1f)
                {
                    marker.Transform = marker.ApplyPlacementScale(marker.Transform);
                }
            }
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
                    if (target.Scene != scene)
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
