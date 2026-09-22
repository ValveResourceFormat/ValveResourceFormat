using System.Diagnostics;
using System.Globalization;
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

        // A template child's own keyvalues are in template space, so the spawning point_template's
        // transform is kept here to compose a world transform for entities looked up by name.
        private readonly Dictionary<Entity, Matrix4x4> entityParentTransforms = [];
        private readonly List<ParticleSceneNode> entityParticleNodes = [];

        private static readonly string[] ControlPointKeys = CreateControlPointKeys();

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

            ResolveAttachmentParenting();
            ResolveParticleControlPoints();

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
        /// Parents entities with a <c>parentname</c> to that parent each frame, snapping onto the
        /// <c>parentattachmentname</c> attachment (or the bone with that name when no attachment matches)
        /// when one is given, otherwise following the parent's transform. <c>uselocaloffset</c> is ignored,
        /// as the engine does here too. Done after all entities are loaded so the parent is registered
        /// regardless of spawn order.
        /// </summary>
        private void ResolveAttachmentParenting()
        {
            var modelsByTargetName = new Dictionary<string, ModelSceneNode>(StringComparer.OrdinalIgnoreCase);

            foreach (var model in scene.AllNodes.OfType<ModelSceneNode>())
            {
                var targetName = model.EntityData?.TargetName;

                if (targetName != null)
                {
                    // first registered wins, matching the previous FirstOrDefault lookup
                    modelsByTargetName.TryAdd(targetName, model);
                }
            }

            foreach (var node in scene.AllNodes)
            {
                if (node.Parent != null || node.EntityInstance != null)
                {
                    continue; // already driven by a simulated entity, which owns its transform
                }

                var parentName = node.EntityData?.GetStringProperty("parentname");

                if (parentName is null || !modelsByTargetName.TryGetValue(parentName, out var parentNode))
                {
                    continue;
                }

                var attachmentName = node.EntityData!.GetStringProperty("parentattachmentname");

                if (attachmentName is null)
                {
                    // plain parenting keeps the child where it is, so it only moves if the parent does
                    parentNode.AttachNodeKeepingTransform(node);
                    continue;
                }

                if (!parentNode.HasAttachmentOrBone(attachmentName))
                {
                    RendererContext.Logger.LogWarning("Parent {ParentName} has no attachment or bone {AttachmentName} to parent {NodeName} to", parentName, attachmentName, node.Name);
                    continue;
                }

                // attachment parenting snaps the child onto the attachment point or bone
                parentNode.AttachNode(node, attachmentName);
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

            // TODO: Ideally we would use the vrman files to find relevant files.
            PhysAggregateData? phys = null;
            var physResource = RendererContext.FileLoader.LoadFile($"{MapName}/world_physics.vmdl_c");

            if (physResource != null)
            {
                phys = (PhysAggregateData?)physResource.GetBlockByType(BlockType.PHYS);
            }
            else
            {
                physResource = RendererContext.FileLoader.LoadFile($"{MapName}/world_physics.vphys_c");

                if (physResource != null)
                {
                    phys = (PhysAggregateData?)physResource.DataBlock;
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

            foreach (var t in traversed)
            {
                if (t.ParentTransform != Matrix4x4.Identity)
                {
                    entityParentTransforms[t.Entity] = t.ParentTransform;
                }
            }
            var connectionTargets = new EntityIOTargetResolver(Entities);

            void LoadEntity(string classname, Entity entity, Matrix4x4 parentTransform, bool fromTemplate)
            {
                var transformationMatrix = EntityTransformHelper.ToTransformationMatrix(entity) * parentTransform;

                if (entity.Connections != null)
                {
                    CreateEntityConnectionLines(entity, transformationMatrix.Translation, connectionTargets);
                }

                var layerName = fromTemplate ? "Template Entities" : originalLayerName;

                // group the point_template marker and its spawned children under the same layer
                var toolEntityLayer = fromTemplate || classname == "point_template" ? "Template Entities" : EditorEntityNode.LayerName;

                var disabled = entity.GetBooleanProperty("startdisabled");

                if (!disabled)
                {
                    disabled = !entity.GetBooleanProperty("enabled", true);
                }

                if (disabled && layerName == "Entities")
                {
                    layerName = "Entities (disabled)";
                }

                // Classnames the entity system implements are spawned as simulated entities, which own
                // whatever scene nodes they need.
                if (EntityFactory.IsRegistered(classname))
                {
                    switch (entitySystem.CreateEntity(entity, parentTransform, layerName, scene))
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

                    return;
                }

                var defaultEntityLayer = toolEntityLayer == EditorEntityNode.LayerName && HammerEntities.Get(classname)?.Studio == true
                    ? layerName
                    : toolEntityLayer;

                if (classname == "skybox_reference")
                {
                    LoadSkybox(entity);
                }

                if (transformationMatrix == default)
                {
                    return;
                }

                var model = entity.GetStringProperty("model");
                var particle = entity.GetStringProperty("effect_name");
                var animation = entity.GetStringProperty("startinganim") ?? entity.GetStringProperty("defaultanim") ?? entity.GetStringProperty("idleanim");

                var skin = entity.GetStringProperty("skin");

                if (classname is "path_particle_rope" or "path_particle_rope_clientside")
                {
                    try
                    {
                        if (CableSceneNode.TryCreate(scene, entity, parentTransform, out var cable) && cable != null)
                        {
                            // The snapshot positions are already world-space (pathnodes placed by the parent
                            // transform), so the node keeps an identity transform.
                            cable.LayerName = Scene.ParticlesLayerName;
                            cable.EntityData = entity;
                            scene.Add(cable, true);
                        }
                        else
                        {
                            RendererContext.Logger.LogWarning("Skipped degenerate path_particle_rope '{Target}' at ({Origin})",
                                entity.TargetName, entity.GetStringProperty("origin"));
                        }
                    }
                    catch (Exception e)
                    {
                        RendererContext.Logger.LogError(e, "Failed to setup path_particle_rope '{Target}'", entity.TargetName);
                    }

                    // A degenerate or failed cable renders nothing. Never fall through to the
                    // generic effect_name path: without a runtime snapshot the cable vpcf loads its m_hSnapshot
                    // editor-preview placeholder and draws a rope at the world origin.
                    return;
                }

                if (classname == "xen_flora_animatedmover" && model != null)
                {
                    var moverResource = RendererContext.FileLoader.LoadFileCompiled(model);

                    if (moverResource?.DataBlock is not Model moverModel)
                    {
                        RendererContext.Logger.LogWarning("xen_flora_animatedmover '{Target}' failed to load model \"{Model}\"",
                            entity.GetStringProperty("targetname"), model);
                        return;
                    }

                    var (moverPath, moverLoopBackIndex) = ResolveFloraMoverPath(entity.GetStringProperty("path_start"));

                    if (moverPath.Count == 0)
                    {
                        RendererContext.Logger.LogWarning("xen_flora_animatedmover '{Target}' has no valid path starting at '{PathStart}', it will not move",
                            entity.GetStringProperty("targetname"), entity.GetStringProperty("path_start"));
                    }

                    var moverNode = new XenFloraAnimatedMoverSceneNode(
                        scene,
                        moverModel,
                        skin,
                        entity,
                        moverPath,
                        moverLoopBackIndex,
                        authoredTransform: transformationMatrix)
                    {
                        Tint = entity.GetRenderTint(),
                        LayerName = layerName,
                        Name = model,
                    };

                    if (entity.GetBooleanProperty("disable_shadows"))
                    {
                        moverNode.Flags |= ObjectTypeFlags.NoShadows;
                    }

                    scene.Add(moverNode, true);

                    var moverParticleName = entity.GetStringProperty("particle_effect");

                    if (moverParticleName != null)
                    {
                        var moverParticleResource = RendererContext.FileLoader.LoadFileCompiled(moverParticleName);

                        if (moverParticleResource?.DataBlock is ParticleSystem moverParticleSystem)
                        {
                            var moverParticleNode = new ParticleSceneNode(scene, moverParticleSystem)
                            {
                                Name = moverParticleName,
                                LayerName = Scene.ParticlesLayerName,
                            };

                            scene.Add(moverParticleNode, true);
                            moverNode.AttachNode(moverParticleNode, rotation: Quaternion.Identity);
                        }
                    }

                    return;
                }

                if (particle != null)
                {
                    var particleResource = RendererContext.FileLoader.LoadFileCompiled(particle);
                    var particleSystem = (ParticleSystem?)particleResource?.DataBlock;

                    if (particleSystem != null)
                    {
                        try
                        {
                            ParticleSnapshot? particleSnapshot = null;
                            var snapshotFile = entity.GetStringProperty("snapshot_file");

                            if (!string.IsNullOrEmpty(snapshotFile))
                            {
                                var snapshotResource = RendererContext.FileLoader.LoadFileCompiled(snapshotFile);

                                if (snapshotResource?.GetBlockByType(BlockType.SNAP) is ParticleSnapshot snapshot)
                                {
                                    particleSnapshot = snapshot;
                                }
                            }

                            var particleNode = new ParticleSceneNode(scene, particleSystem, particleSnapshot, playedByEntity: true)
                            {
                                Name = particle,
                                Transform = ResolveControlPoint0Transform(entity, transformationMatrix),
                                LayerName = Scene.ParticlesLayerName,
                                EntityData = entity,
                            };

                            entityParticleNodes.Add(particleNode);
                            scene.Add(particleNode, true);
                        }
                        catch (Exception e)
                        {
                            RendererContext.Logger.LogError(e, "Failed to setup particle '{Particle}'", particle);
                        }
                    }
                }

                if (model == null)
                {
                    CreateDefaultEntity(entity, classname, transformationMatrix, defaultEntityLayer);
                    return;
                }

                var newEntity = RendererContext.FileLoader.LoadFileCompiled(model);

                if (newEntity == null)
                {
                    var errorModelResource = RendererContext.FileLoader.LoadFile("models/dev/error.vmdl_c");

                    if (errorModelResource?.DataBlock is Model errorModelData)
                    {
                        var errorModel = new ModelSceneNode(scene, errorModelData, skin)
                        {
                            Name = "error",
                            Transform = transformationMatrix,
                            LayerName = layerName,
                            EntityData = entity,
                        };

                        scene.Add(errorModel, true);
                    }

                    return;
                }

                if (newEntity.DataBlock is not Model newModel)
                {
                    return;
                }

                var modelNode = new ModelSceneNode(scene, newModel, skin)
                {
                    Transform = transformationMatrix,
                    Tint = entity.GetRenderTint(),
                    LayerName = layerName,
                    Name = model,
                    EntityData = entity,
                };

                if (modelNode.HasMeshes)
                {
                    if (animation != null)
                    {
                        var isAnimated = modelNode.SetAnimationForWorldPreview(animation);
                        if (isAnimated)
                        {
                            var holdAnimationOn = entity.GetBooleanProperty("holdanimation");
                            if (holdAnimationOn)
                            {
                                modelNode.AnimationController.PauseLastFrame();
                            }
                        }
                    }

                    var body = entity.GetIntegerProperty("body", -1L);
                    if (body != -1L)
                    {
                        var groups = modelNode.GetMeshGroups();
                        modelNode.SetActiveMeshGroups(groups.Skip((int)body).Take(1));
                    }
                }

                // Model-referenced particles spawn regardless of meshes; a particle-only model is
                // still added so its follow attachments get updated (the scene skips parented nodes).
                var modelParticleNodes = ParticleSceneNode.CreateModelParticles(scene, newModel, modelNode);

                if (modelNode.HasMeshes || modelParticleNodes.Count > 0)
                {
                    scene.Add(modelNode, true);

                    foreach (var modelParticleNode in modelParticleNodes)
                    {
                        modelParticleNode.LayerName = Scene.ParticlesLayerName;
                        scene.Add(modelParticleNode, true);
                    }
                }

                var phys = newModel?.GetEmbeddedPhys();
                if (newModel != null && phys == null)
                {
                    var refPhysicsPaths = newModel.GetReferencedPhysNames().ToArray();
                    if (refPhysicsPaths.Length != 0)
                    {
                        var newResource = RendererContext.FileLoader.LoadFileCompiled(refPhysicsPaths.First());
                        if (newResource != null)
                        {
                            phys = (PhysAggregateData?)newResource.DataBlock;
                        }
                    }
                }

                if (phys != null)
                {
                    foreach (var physSceneNode in PhysSceneNode.CreatePhysSceneNodes(scene, phys, model, classname))
                    {
                        physSceneNode.Transform = transformationMatrix;
                        physSceneNode.LayerName = layerName;
                        physSceneNode.EntityData = entity;

                        scene.Add(physSceneNode, true);
                    }
                }
                else if (!modelNode.HasMeshes && modelParticleNodes.Count == 0)
                {
                    // If the loaded model has no meshes, particles, or physics, fallback to default entity
                    CreateDefaultEntity(entity, classname, transformationMatrix, defaultEntityLayer);
                }
            }

            foreach (var (entity, parentTransform, fromTemplate, classname) in entitiesReordered)
            {
                CancellationToken.ThrowIfCancellationRequested();

                try
                {
                    LoadEntity(classname, entity, parentTransform, fromTemplate);
                }
                catch (Exception e)
                {
                    var id = entity.GetStringProperty("hammeruniqueid", string.Empty);

                    throw new InvalidDataException($"Failed to process entity '{classname}' (hammeruniqueid={id})", e);
                }
            }
        }

        private void LoadSkybox(Entity entity)
        {
            var targetmapname = entity.GetStringProperty("targetmapname");

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
            var reference = EntityTransformHelper.ToRigidTransformationMatrix(entity);

            if (entityParentTransforms.TryGetValue(entity, out var referenceParentTransform))
            {
                reference *= referenceParentTransform;
            }

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

        private void CreateDefaultEntity(Entity entity, string classname, Matrix4x4 transformationMatrix, string layerName)
        {
            entityParentTransforms.TryGetValue(entity, out var entityParentTransform);

            var createdNode = EditorEntityNode.Create(scene, entity, classname, transformationMatrix, ObjectTypeFlags.None, layerName,
                parentTransform: entityParentTransform == default ? null : entityParentTransform);

            scene.Add(createdNode, true);

            var hammerEntity = HammerEntities.Get(classname);

            if (hammerEntity?.Lines.Length > 0)
            {
                foreach (var line in hammerEntity.Lines)
                {
                    if (!entity.TryGetValue(line.StartValueKey, out var startKeyValue))
                    {
                        continue;
                    }

                    var startEntity = FindEntityByKeyValue(line.StartKey, (string)startKeyValue);

                    if (startEntity == null)
                    {
                        continue;
                    }

                    var end = transformationMatrix.Translation;
                    var start = GetEntityWorldTransform(startEntity).Translation;

                    if (line.EndKey != null && line.EndValueKey != null)
                    {
                        if (!entity.TryGetValue(line.EndValueKey, out var endKeyValue))
                        {
                            continue;
                        }

                        var endEntity = FindEntityByKeyValue(line.EndKey, (string)endKeyValue);

                        if (endEntity == null)
                        {
                            continue;
                        }

                        end = GetEntityWorldTransform(endEntity).Translation;
                    }

                    var origin = (start + end) / 2f;
                    end -= origin;
                    start -= origin;

                    var lineNode = new LineSceneNode(scene, start, end, line.Color, line.Color)
                    {
                        LayerName = layerName,
                        Transform = Matrix4x4.CreateTranslation(origin)
                    };
                    scene.Add(lineNode, true);
                }
            }
        }

        private void CreateEntityConnectionLines(Entity entity, Vector3 start, EntityIOTargetResolver connectionTargets)
        {
            if (entity.Connections == null)
            {
                return;
            }

            var alreadySeen = new HashSet<Entity>(entity.Connections.Count);
            var targets = new List<Entity>();

            foreach (var connectionData in entity.Connections)
            {
                targets.Clear();
                var outcome = connectionTargets.Resolve(connectionData, targets);

                if (outcome != EntityIOTargetOutcome.Matched)
                {
                    RendererContext.Logger.LogDebug("Skipping entity i/o output {TargetName}: {Outcome}", connectionData.TargetName, outcome);
                    continue;
                }

                foreach (var endEntity in targets)
                {
                    if (!alreadySeen.Add(endEntity))
                    {
                        continue;
                    }

                    var end = GetEntityWorldTransform(endEntity).Translation;

                    var origin = (start + end) / 2f;
                    end -= origin;
                    var lineStart = start - origin;

                    var lineNode = new LineSceneNode(scene, lineStart, end, new Color32(0, 255, 0), new Color32(255, 0, 0))
                    {
                        LayerName = "Entity Connections",
                        Transform = Matrix4x4.CreateTranslation(origin),
#if DEBUG
                        Name = $"Line from {entity.GetStringProperty("hammeruniqueid")} to {endEntity.GetStringProperty("hammeruniqueid")}"
#endif
                    };
                    scene.Add(lineNode, true);
                }
            }
        }

        // Walks the target chain starting at the path_corner named startName, in the same way path_track/
        // func_tracktrain follow theirs. LoopBackIndex is set when the chain itself points back to an
        // already-visited node (an authored closed loop), so a looping mover can honor that entry point
        // instead of always restarting from the first node.
        private (List<FloraMoverPathNode> Nodes, int LoopBackIndex) ResolveFloraMoverPath(string? startName)
        {
            var nodes = new List<FloraMoverPathNode>();
            var loopBackIndex = -1;

            if (string.IsNullOrEmpty(startName))
            {
                return (nodes, loopBackIndex);
            }

            var visited = new Dictionary<Entity, int>();
            var current = FindEntityByTargetName(startName);

            while (current != null && current.GetStringProperty("classname") == "path_corner")
            {
                if (visited.TryGetValue(current, out var existingIndex))
                {
                    loopBackIndex = existingIndex;
                    break;
                }

                visited[current] = nodes.Count;
                nodes.Add(new FloraMoverPathNode(
                    GetEntityWorldTransform(current).Translation,
                    current.GetFloatProperty("speed"),
                    current.GetFloatProperty("wait")));

                var nextName = current.GetStringProperty("target");
                current = string.IsNullOrEmpty(nextName) ? null : FindEntityByTargetName(nextName);
            }

            return (nodes, loopBackIndex);
        }

        // cpoint0 hands the effect's placement to another entity: control point 0 sits at that entity's
        // location rather than at the particle entity's own origin.
        private Matrix4x4 ResolveControlPoint0Transform(Entity entity, Matrix4x4 transformationMatrix)
        {
            var controlPoint0 = entity.GetStringProperty("cpoint0");

            if (string.IsNullOrEmpty(controlPoint0))
            {
                return transformationMatrix;
            }

            var target = FindEntityByTargetName(controlPoint0);

            if (target == null)
            {
                RendererContext.Logger.LogWarning("Particle entity '{Target}' points cpoint0 at '{ControlPoint0}', which does not exist",
                    entity.TargetName, controlPoint0);
                return transformationMatrix;
            }

            return GetEntityWorldTransform(target);
        }

        private static void ApplyParticleGlowProperties(Entity entity, ParticleSceneNode particleNode)
        {
            particleNode.GetControlPoint(16).Position = entity.GetVector3Property("colortint", new Vector3(255f));
            particleNode.GetControlPoint(17).Position = new Vector3(
                entity.GetFloatProperty("alphascale", 1f),
                entity.GetFloatProperty("scale", 1f),
                entity.GetFloatProperty("selfillumscale", 1f));

            var textureOverride = entity.GetStringProperty("effect_textureOverride");

            if (!string.IsNullOrEmpty(textureOverride))
            {
                particleNode.SetTextureOverride(textureOverride);
            }
        }

        private static string[] CreateControlPointKeys()
        {
            var keys = new string[64];

            for (var i = 0; i < keys.Length; i++)
            {
                keys[i] = string.Concat("cpoint", i.ToString(CultureInfo.InvariantCulture));
            }

            return keys;
        }

        /// <summary>
        /// Applies the control point overrides authored on particle entities: <c>cpoint1</c> to
        /// <c>cpoint63</c> each name an entity whose transform that control point takes. Runs once
        /// every lump is loaded, so a target in another lump resolves regardless of load order.
        /// </summary>
        private void ResolveParticleControlPoints()
        {
            if (entityParticleNodes.Count == 0)
            {
                return;
            }

            var entitiesByTargetName = new Dictionary<string, Entity>(Entities.Count, StringComparer.OrdinalIgnoreCase);

            foreach (var entity in Entities)
            {
                var targetName = entity.TargetName;

                if (!string.IsNullOrEmpty(targetName))
                {
                    entitiesByTargetName.TryAdd(targetName, entity);
                }
            }

            var nodesByEntity = new Dictionary<Entity, SceneNode>();

            foreach (var node in scene.AllNodes)
            {
                // A particle entity also spawns its own effect node, whose transform follows the
                // simulation rather than staying at the origin the entity was authored at.
                if (node.EntityData != null && node is not ParticleSceneNode)
                {
                    nodesByEntity.TryAdd(node.EntityData, node);
                }
            }

            foreach (var particleNode in entityParticleNodes)
            {
                var entity = particleNode.EntityData!;

                for (var index = 1; index < ControlPointKeys.Length; index++)
                {
                    var targetName = entity.GetStringProperty(ControlPointKeys[index]);

                    if (string.IsNullOrEmpty(targetName))
                    {
                        continue;
                    }

                    if (targetName[0] == '!')
                    {
                        if (string.Equals(targetName, "!self", StringComparison.OrdinalIgnoreCase))
                        {
                            particleNode.SetControlPoint(index, GetEntityWorldTransform(entity));
                        }
                        else
                        {
                            RendererContext.Logger.LogDebug("Particle entity '{Target}' points {Key} at '{ControlPointTarget}', which only exists at runtime",
                                entity.TargetName, ControlPointKeys[index], targetName);
                        }

                        continue;
                    }

                    if (!entitiesByTargetName.TryGetValue(targetName, out var target))
                    {
                        RendererContext.Logger.LogWarning("Particle entity '{Target}' points {Key} at '{ControlPointTarget}', which does not exist",
                            entity.TargetName, ControlPointKeys[index], targetName);
                        continue;
                    }

                    var transform = GetEntityWorldTransform(target);

                    if (nodesByEntity.TryGetValue(target, out var targetNode))
                    {
                        particleNode.BindControlPoint(index, targetNode, transform);
                    }
                    else
                    {
                        particleNode.SetControlPoint(index, transform);
                    }
                }

                ApplyLiteralControlPointValues(entity, particleNode);

                if (entity.GetStringProperty("classname") == "env_particle_glow")
                {
                    ApplyParticleGlowProperties(entity, particleNode);
                }
            }
        }

        /// <summary>
        /// Pins control points to the literal values authored on the entity: <c>data_cp</c> takes a
        /// vector and <c>tint_cp</c> a colour, both fed through as authored. Applied after the
        /// <c>cpointN</c> bindings, which is the order the engine resolves the two in.
        /// </summary>
        private static void ApplyLiteralControlPointValues(Entity entity, ParticleSceneNode particleNode)
        {
            var dataControlPoint = LiteralControlPointIndex(entity, "data_cp");

            if (dataControlPoint >= 0)
            {
                particleNode.GetControlPoint(dataControlPoint).Position = entity.GetVector3Property("data_cp_value");
            }

            var tintControlPoint = LiteralControlPointIndex(entity, "tint_cp");

            if (tintControlPoint >= 0)
            {
                particleNode.GetControlPoint(tintControlPoint).Position = entity.GetVector3Property("tint_cp_color", new Vector3(255f));
            }
        }

        /// <summary>
        /// Reads a literal control point index, clamped the way the engine clamps it at spawn. Returns
        /// -1 when unused, which is also what an index past the last control point resolves to.
        /// </summary>
        private static int LiteralControlPointIndex(Entity entity, string key)
        {
            var index = Math.Clamp(entity.GetInt32Property(key, -1), -1, 64);

            return index < ControlPointKeys.Length ? index : -1;
        }

        /// <summary>
        /// Gets the world transform of an entity, composing the transform of the
        /// <c>point_template</c> that spawned it when it came from a template child lump.
        /// </summary>
        /// <param name="entity">The entity to place.</param>
        /// <returns>The entity's transform in world space.</returns>
        public Matrix4x4 GetEntityWorldTransform(Entity entity)
        {
            var transform = EntityTransformHelper.ToTransformationMatrix(entity);

            return entityParentTransforms.TryGetValue(entity, out var parentTransform)
                ? transform * parentTransform
                : transform;
        }

        private Entity? FindEntityByKeyValue(string keyToFind, string valueToFind)
        {
            if (valueToFind == null)
            {
                return null;
            }

            foreach (var entity in Entities)
            {
                if (entity.TryGetValue(keyToFind, out var propertyValue)
                    && propertyValue.ValueType == ValveKeyValue.KVValueType.String
                    && valueToFind.Equals((string)propertyValue, StringComparison.OrdinalIgnoreCase))
                {
                    return entity;
                }
            }

            return null;
        }

        /// <summary>
        /// Finds the first entity which matches its name with the given pattern.
        /// </summary>
        /// <param name="pattern">Targetname to match against, may contain wildcards: `*` and `?` (e.g. <c>door_*</c>).</param>
        /// <returns>The matching <see cref="Entity"/>, or <see langword="null"/> if not found.</returns>
        public Entity? FindEntityByTargetName(string pattern)
        {
            if (pattern == null)
            {
                return null;
            }

            foreach (var entity in Entities)
            {
                if (entity.TryGetValue("targetname", out var propertyValue)
                    && propertyValue.ValueType == ValveKeyValue.KVValueType.String
                    && EntityNameMatches(pattern, (string)propertyValue))
                {
                    return entity;
                }
            }

            return null;
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
