using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;
using SteamDatabase.ValvePak;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.NavMesh;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace ValveResourceFormat.Renderer
{
    public class WorldLoader
    {
        private readonly Scene scene;
        private readonly RendererContext RendererContext;

        public string MapName { get; }

        public World World { get; }

        public List<Entity> Entities { get; } = [];
        public WorldNode? MainWorldNode { get; private set; }

        public HashSet<string> DefaultEnabledLayers { get; } = ["Entities", "Particles"];

        public List<string> CameraNames { get; } = [];
        public List<Matrix4x4> CameraMatrices { get; } = [];

        public Scene? SkyboxScene { get; set; }
        public SceneSkybox2D? Skybox2D { get; set; }
        public NavMeshFile? NavMesh { get; set; }

        public Vector3 WorldOffset { get; set; } = Vector3.Zero;
        public float WorldScale { get; set; } = 1.0f;
        // TODO: also store skybox reference rotation

        public static WorldLoader LoadMap(string mapResourceName, Scene scene)
        {
            if (mapResourceName.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.OrdinalIgnoreCase))
            {
                mapResourceName = mapResourceName[..^GameFileLoader.CompiledFileSuffix.Length];
            }

            var renderContext = scene.RendererContext;
            var mapResource = renderContext.FileLoader.LoadFileCompiled(mapResourceName) ?? throw new FileNotFoundException($"Failed to load map file '{mapResourceName}'.");
            var worldPath = GetWorldNameFromMap(mapResourceName);
            var worldResource = renderContext.FileLoader.LoadFileCompiled(worldPath) ?? throw new FileNotFoundException($"Failed to load world file '{worldPath}'.");

            return new WorldLoader((World)worldResource.DataBlock!, scene, mapResource.ExternalReferences);
        }

        public WorldLoader(World world, Scene scene, ResourceExtRefList? mapResourceReferences)
        {
            MapName = Path.GetDirectoryName(world.Resource!.FileName!)!.Replace('\\', '/');
            World = world;
            this.scene = scene;
            RendererContext = scene.RendererContext;

            if (mapResourceReferences != null)
            {
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

                var otherTask = Task.Run(LoadNavigationMesh);

                Parallel.ForEach(resourceNames, resourceReference =>
                {
                    var resource = PreloadResource(resourceReference);

                    if (resource is { ExternalReferences.ResourceRefInfoList: var refs })
                    {
                        Parallel.ForEach(refs, extRef =>
                        {
                            PreloadResource(extRef.Name);
                        });
                    }

                    if (resource is { ResourceType: ResourceType.EntityLump, DataBlock: EntityLump entityLump })
                    {
                        HashSet<string> toolIcons = [];
                        foreach (var entity in entityLump.GetEntities())
                        {
                            var className = entity.GetProperty<string>("classname");
                            if (className != null)
                            {
                                var hammerEntity = HammerEntities.Get(className);
                                if (hammerEntity?.Icons.Length > 0)
                                {
                                    toolIcons.UnionWith(hammerEntity.Icons);
                                }
                            }
                        }

                        Parallel.ForEach(toolIcons, file =>
                        {
                            PreloadResource(file);
                        });
                    }
                });

                otherTask.Wait();
            }

            Load();
        }

        private void Load()
        {
            LoadWorldLightingInfo();
            LoadEntities();
            LoadWorldNodes();
            LoadWorldPhysics();
            LoadNavigationMesh();
        }

        private void LoadEntities()
        {
            foreach (var lumpName in World.GetEntityLumpNames())
            {
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

                LoadEntitiesFromLump(entityLump, "Entities", Matrix4x4.Identity);
            }

            Action<List<SceneLight>> lightEntityStore = scene.LightingInfo.LightmapGameVersionNumber switch
            {
                0 or 1 => scene.LightingInfo.StoreLightMappedLights_V1,
                >= 2 => scene.LightingInfo.StoreLightMappedLights_V2,
                _ => x => RendererContext.Logger.LogError("Storing lights for lightmap version {Version} is not supported", scene.LightingInfo.LightmapGameVersionNumber),
            };

            lightEntityStore.Invoke(
                scene.AllNodes.Where(static n => n is SceneLight).Cast<SceneLight>().ToList()
            );
        }

        private void LoadWorldNodes()
        {
            // Output is World_t we need to iterate m_worldNodes inside it.
            var worldNodes = World.GetWorldNodeNames();
            foreach (var worldNode in worldNodes)
            {
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
                    subloader.Load(scene);

                    foreach (var layer in subloader.LayerNames)
                    {
                        DefaultEnabledLayers.Add(layer);
                    }
                }
            }
        }

        public void LoadWorldPhysics()
        {
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
                    physSceneNode.LayerName = "world_layer_base";
                    scene.Add(physSceneNode, true);
                }

                scene.PhysicsWorld = new Rubikon(phys);
            }
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
        };

        private readonly string[] LightmapSetV81_SteamVr = ["g_tIrradiance", "g_tDirectionalIrradiance"];
        private readonly string[] LightmapSetV81 = ["g_tIrradiance", "g_tDirectionalIrradiance", "g_tDirectLightIndices", "g_tDirectLightStrengths"];
        private readonly string[] LightmapSetV82 = ["g_tIrradiance", "g_tDirectionalIrradiance", "g_tDirectLightShadows"];
        private readonly string[] LightmapSetV83 = ["g_tIrradiance", "g_tDirectionalIrradianceR", "g_tDirectionalIrradianceG", "g_tDirectionalIrradianceB", "g_tDirectLightShadows"];

        private void LoadWorldLightingInfo()
        {
            var worldLightingInfo = World.GetWorldLightingInfo();
            if (worldLightingInfo == null)
            {
                return;
            }

            var result = scene.LightingInfo;
            result.LightmapVersionNumber = worldLightingInfo.GetInt32Property("m_nLightmapVersionNumber");
            if (scene.LightingInfo.LightmapVersionNumber == 8)
            {
                result.LightmapGameVersionNumber = worldLightingInfo.GetInt32Property("m_nLightmapGameVersionNumber");
                result.LightingData.LightmapUvScale = worldLightingInfo.GetSubCollection("m_vLightmapUvScale").ToVector2();
            }

            var lightmaps = worldLightingInfo.GetArray<string>("m_lightMaps") ?? [];

            foreach (var lightmap in lightmaps)
            {
                var name = Path.GetFileNameWithoutExtension(lightmap);
                if (LightmapNameToUniformName.TryGetValue(name, out var uniformName))
                {
                    var srgbRead = name == "irradiance";
                    var renderTexture = RendererContext.MaterialLoader.GetTexture(lightmap, srgbRead);
                    result.Lightmaps[uniformName] = renderTexture;
                    MaterialLoader.ReservedTextures.Add(uniformName);

                    if (name == "direct_light_indices")
                    {
                        // point sampling
                        renderTexture.SetFiltering(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
                    }

                    renderTexture.SetWrapMode(TextureWrapMode.ClampToEdge);
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

        static bool IsCamera(string cls)
            => cls == "sky_camera"
            || cls == "point_devshot_camera"
            || cls == "point_camera_vertical_fov"
            || cls == "point_camera";

        private void LoadEntitiesFromLump(EntityLump entityLump, string layerName, Matrix4x4 parentTransform)
        {
            var childEntities = entityLump.GetChildEntityNames();
            var childEntityLumps = new Dictionary<string, EntityLump>(childEntities.Length);

            foreach (var childEntityName in childEntities)
            {
                var newResource = RendererContext.FileLoader.LoadFileCompiled(childEntityName);

                if (newResource == null)
                {
                    continue;
                }

                var childLump = (EntityLump?)newResource.DataBlock;

                if (childLump == null)
                {
                    continue;
                }

                var childName = childLump.Name;

                childEntityLumps.Add(childName, childLump);
            }

            static bool IsCubemapOrProbe(string cls)
                => cls == "env_combined_light_probe_volume"
                || cls == "env_light_probe_volume"
                || cls == "env_cubemap_box"
                || cls == "env_cubemap";

            static bool IsFog(string cls)
                => cls is "env_cubemap_fog" or "env_gradient_fog";

            var entities = entityLump.GetEntities().ToList();
            var entitiesReordered = entities
                .Select(e => (Entity: e, Classname: e.GetProperty<string>("classname")))
                .Where(x => x.Classname != null)
                .Select(x => (x.Entity, Classname: x.Classname!))
                .OrderByDescending(x => IsCubemapOrProbe(x.Classname) || IsFog(x.Classname));

            Entities.AddRange(entities);

            void LoadEntity(string classname, Entity entity)
            {
                if (classname == "worldspawn")
                {
                    return; // do not draw
                }

                var transformationMatrix = EntityTransformHelper.CalculateTransformationMatrix(entity) * parentTransform;
                var light = SceneLight.IsAccepted(classname);

                if (entity.Connections != null)
                {
                    CreateEntityConnectionLines(entity, transformationMatrix.Translation);
                }

                if (classname == "info_world_layer")
                {
                    var spawnflags = entity.GetPropertyUnchecked<uint>("spawnflags");
                    var layername = entity.GetProperty<string>("layername");

                    // Visible on spawn flag
                    if ((spawnflags & 1) == 1 && layername != null)
                    {
                        DefaultEnabledLayers.Add(layername);
                    }
                }
                else if (classname == "skybox_reference")
                {
                    LoadSkybox(entity);
                }
                else if (light.Accepted)
                {
                    var lightNode = SceneLight.FromEntityProperties(scene, light.Type, entity);
                    lightNode.Transform = transformationMatrix;
                    lightNode.LayerName = layerName;
                    scene.Add(lightNode, true);
                }
                else if (classname == "point_template")
                {
                    var entityLumpName = entity.GetProperty<string>("entitylumpname");

                    if (entityLumpName != null && childEntityLumps.TryGetValue(entityLumpName, out var childLump))
                    {
                        LoadEntitiesFromLump(childLump, entityLumpName, transformationMatrix);
                    }
                    else
                    {
                        RendererContext.Logger.LogWarning("Failed to find child entity lump with name {EntityLumpName}", entityLumpName);
                    }
                }
                else if (classname == "env_sky" || classname == "env_global_light")
                {
                    var skyname = entity.GetProperty<string>("skyname") ?? entity.GetProperty<string>("skybox_material_day");
                    var tintColor = Vector3.One;
                    var disabled = false;

                    if (classname == "env_sky")
                    {
                        // If it has "startdisabled", only take it if we haven't found any others yet.
                        disabled = entity.GetProperty<bool>("startdisabled");
                        disabled = disabled && Skybox2D != null;

                        tintColor = entity.GetColor32Property("tint_color");
                    }

                    if (!disabled && skyname != null)
                    {
                        var rotation = transformationMatrix with
                        {
                            Translation = Vector3.Zero
                        };
                        using var skyMaterial = RendererContext.FileLoader.LoadFileCompiled(skyname);

                        Skybox2D = new SceneSkybox2D(RendererContext.MaterialLoader.LoadMaterial(skyMaterial))
                        {
                            Tint = tintColor,
                            Transform = rotation,
                        };
                    }

                    if (classname == "env_global_light")
                    {
                        var angles = new Vector3(50, 43, 0);
                        scene.Add(new SceneLight(scene)
                        {
                            Type = SceneLight.LightType.Directional,
                            Transform = EntityTransformHelper.CreateRotationMatrixFromEulerAngles(angles),
                            Direction = SceneLight.AnglesToDirection(angles),
                            Color = new Vector3(1.0f, 1.0f, 1.0f),
                            Brightness = 1.0f,
                            LayerName = "world_layer_base",
                            Name = "Source 2 Viewer dynamic sunlight for Dota",
                        }, false);

                        scene.LightingInfo.EnableDynamicShadows = true;
                        scene.LightingInfo.SunLightShadowCoverageScale = 4f;
                    }
                }
                else if (classname == "env_gradient_fog")
                {
                    // If it has "start_disabled", only take it if we haven't found any others yet.
                    if (!entity.GetProperty<bool>("start_disabled") || scene.FogInfo.GradientFogActive)
                    {
                        scene.FogInfo.GradientFogActive = true;

                        var distExponent = entity.GetPropertyUnchecked<float>("fogfalloffexponent");
                        var startDist = entity.GetPropertyUnchecked<float>("fogstart");
                        var endDist = entity.GetPropertyUnchecked<float>("fogend");

                        // Some maps don't have these properties.
                        var useHeightFog = entity.ContainsKey("fogverticalexponent"); // the oldest versions have these values missing, so disable it there
                        useHeightFog = entity.GetProperty("heightfog", useHeightFog); // New in CS2

                        // TODO: find the correct behavior under this condition
                        var startHeight = float.NegativeInfinity;
                        var endHeight = float.NegativeInfinity;
                        var heightExponent = 1.0f;

                        if (useHeightFog)
                        {
                            heightExponent = entity.GetPropertyUnchecked<float>("fogverticalexponent");
                            startHeight = entity.GetPropertyUnchecked<float>("fogstartheight");
                            endHeight = entity.GetPropertyUnchecked<float>("fogendheight");
                        }

                        var strength = entity.GetPropertyUnchecked<float>("fogstrength");
                        var color = entity.GetColor32Property("fogcolor");
                        var maxOpacity = entity.GetPropertyUnchecked<float>("fogmaxopacity");

                        scene.FogInfo.GradientFog = new SceneGradientFog(scene)
                        {
                            StartDist = startDist,
                            EndDist = endDist,
                            FalloffExponent = distExponent,
                            HeightStart = startHeight,
                            HeightEnd = endHeight,
                            VerticalExponent = heightExponent,
                            Color = color,
                            Strength = strength,
                            MaxOpacity = maxOpacity,
                        };
                    }
                }
                else if (classname == "env_cubemap_fog")
                {
                    // If it has "start_disabled", only take it if it's the first one in the map.
                    // this might not be right, and the first env_cubemap_fog found might take priority, like with post processing
                    if (!entity.GetProperty<bool>("start_disabled") || scene.FogInfo.CubeFogActive)
                    {
                        scene.FogInfo.CubeFogActive = true;

                        var lodBias = entity.GetPropertyUnchecked<float>("cubemapfoglodbiase");

                        var falloffExponent = entity.GetPropertyUnchecked<float>("cubemapfogfalloffexponent");
                        var startDist = entity.GetPropertyUnchecked<float>("cubemapfogstartdistance");
                        var endDist = entity.GetPropertyUnchecked<float>("cubemapfogenddistance");

                        var hasHeightEnd = entity.ContainsKey("cubemapfogheightend");

                        var useHeightFog = entity.ContainsKey("cubemapfogheightexponent"); // the oldest versions have these values missing, so disable it there
                        useHeightFog = entity.GetProperty("cubemapheightfog", useHeightFog); // New in CS2

                        var heightExponent = 1.0f;
                        var heightStart = float.PositiveInfinity; // is this right?
                        var heightEnd = float.PositiveInfinity;
                        if (useHeightFog)
                        {
                            heightExponent = entity.GetPropertyUnchecked<float>("cubemapfogheightexponent");
                            heightStart = entity.GetPropertyUnchecked<float>("cubemapfogheightstart");
                            if (hasHeightEnd)
                            {
                                // New in CS2
                                heightEnd = entity.GetPropertyUnchecked<float>("cubemapfogheightend");
                            }
                            else
                            {
                                var heightWidth = entity.GetPropertyUnchecked<float>("cubemapfogheightwidth");
                                heightEnd = heightStart + heightWidth;
                            }
                        }

                        var opacity = entity.GetPropertyUnchecked("cubemapfogmaxopacity", 1f);
                        var fogSource = entity.GetPropertyUnchecked("cubemapfogsource", 0u);

                        RenderTexture? fogTexture = null;
                        var exposureBias = 0.0f;

                        if (fogSource == 0) // Cubemap From Texture, Disabled in CS2
                        {
                            var textureName = entity.GetProperty<string>("cubemapfogtexture");
                            if (textureName != null)
                            {
                                fogTexture = RendererContext.MaterialLoader.GetTexture(textureName);
                            }
                        }
                        else
                        {
                            string? material = null;

                            if (fogSource == 1) // Cubemap From Env_Sky
                            {
                                var skyEntTargetName = entity.GetProperty<string>("cubemapfogskyentity");
                                if (skyEntTargetName != null)
                                {
                                    var skyEntity = FindEntityByKeyValue("targetname", skyEntTargetName);

                                    // env_sky target //  && (scene.Sky.TargetName == skyEntTargetName)
                                    if (skyEntity != null)
                                    {
                                        material = skyEntity.GetProperty<string>("skyname") ?? skyEntity.GetProperty<string>("skybox_material_day");
                                        transformationMatrix = EntityTransformHelper.CalculateTransformationMatrix(skyEntity); // steal rotation from env_sky
                                    }
                                    else
                                    {
                                        RendererContext.Logger.LogWarning("Disabling cubemap fog because failed to find env_sky of target name {SkyEntTargetName}", skyEntTargetName);
                                        scene.FogInfo.CubeFogActive = false;
                                    }
                                }
                            }
                            else if (fogSource == 2) // Cubemap From Material
                            {
                                material = entity.GetProperty<string>("cubemapfogskymaterial");
                            }
                            else
                            {
                                throw new NotImplementedException($"Cubemap fog source {fogSource} is not recognized.");
                            }

                            if (!string.IsNullOrEmpty(material))
                            {
                                using var matFile = RendererContext.FileLoader.LoadFileCompiled(material);
                                var mat = RendererContext.MaterialLoader.LoadMaterial(matFile);

                                if (mat != null && mat.Textures.TryGetValue("g_tSkyTexture", out fogTexture))
                                {
                                    var brightnessExposureBias = mat.Material.FloatParams.GetValueOrDefault("g_flBrightnessExposureBias", 0f);
                                    // todo: make sure this matches with scene post process
                                    var renderOnlyExposureBias = mat.Material.FloatParams.GetValueOrDefault("g_flRenderOnlyExposureBias", 0f);

                                    // These are both logarithms, so this is equivalent to a multiply of the raw value
                                    exposureBias = brightnessExposureBias + renderOnlyExposureBias;
                                }
                            }
                        }

                        if (fogTexture == null)
                        {
                            scene.FogInfo.CubeFogActive = false;
                        }

                        scene.FogInfo.CubemapFog = new SceneCubemapFog(scene)
                        {
                            StartDist = startDist,
                            EndDist = endDist,
                            FalloffExponent = falloffExponent,
                            HeightStart = heightStart,
                            HeightEnd = heightEnd,
                            HeightExponent = heightExponent,
                            LodBias = lodBias,
                            Transform = transformationMatrix,
                            CubemapFogTexture = fogTexture,
                            Opacity = opacity,
                            ExposureBias = exposureBias,
                            UseHeightFog = useHeightFog,
                        };
                    }
                }
                /*else if (classname == "env_volumetric_fog_controller" && entity.GetProperty<bool>("ismaster"))
                {
                    scene.FogInfo.LoadVolumetricFogController(entity);
                }
                else if (classname == "env_volumetric_fog_volume" && !entity.GetProperty<bool>("start_disabled"))
                {
                    scene.FogInfo.LoadFogVolume(entity);
                }*/
                else if (IsCubemapOrProbe(classname))
                {
                    var handShakeString = entity.GetProperty<string>("handshake");
                    if (!int.TryParse(handShakeString, out var handShake))
                    {
                        handShake = 0;
                    }

                    AABB bounds = default;
                    if (classname == "env_cubemap")
                    {
                        var radius = entity.GetPropertyUnchecked<float>("influenceradius");
                        bounds = new AABB(-radius, -radius, -radius, radius, radius, radius);
                    }
                    else
                    {
                        bounds = new AABB(
                            entity.GetVector3Property("box_mins"),
                            entity.GetVector3Property("box_maxs")
                        );
                    }

                    var indoorOutdoorLevel = entity.GetPropertyUnchecked("indoor_outdoor_level", 0);

                    if (classname != "env_light_probe_volume")
                    {
                        var cubemapTextureName = entity.GetProperty<string>("cubemaptexture");
                        var envMapTexture = cubemapTextureName != null
                            ? RendererContext.MaterialLoader.GetTexture(cubemapTextureName)
                            : null;

                        if (envMapTexture != null)
                        {
                            var arrayIndex = entity.GetPropertyUnchecked("array_index", 0);
                            var edgeFadeDists = entity.GetVector3Property("edge_fade_dists"); // TODO: Not available on all entities
                            var isCustomTexture = entity.GetProperty<string>("customcubemaptexture") != null;

                            var envMap = new SceneEnvMap(scene, bounds)
                            {
                                LayerName = layerName,
                                Transform = transformationMatrix,
                                HandShake = handShake,
                                ArrayIndex = arrayIndex,
                                IndoorOutdoorLevel = indoorOutdoorLevel,
                                EdgeFadeDists = edgeFadeDists,
                                ProjectionMode = classname == "env_cubemap" ? 0 : 1,
                                EnvMapTexture = envMapTexture,
                            };

                            if (!isCustomTexture)
                            {
                                scene.LightingInfo.AddEnvironmentMap(envMap);
                            }
                        }
                    }

                    if (classname == "env_combined_light_probe_volume" || classname == "env_light_probe_volume")
                    {
                        var lightProbeTextureName = entity.GetProperty<string>("lightprobetexture");
                        var irradianceTexture = lightProbeTextureName != null
                            ? RendererContext.MaterialLoader.GetTexture(lightProbeTextureName, srgbRead: true)
                            : null;

                        var lightProbe = new SceneLightProbe(scene, bounds)
                        {
                            LayerName = layerName,
                            Transform = transformationMatrix,
                            HandShake = handShake,
                            Irradiance = irradianceTexture,
                            IndoorOutdoorLevel = indoorOutdoorLevel,
                            VoxelSize = entity.GetPropertyUnchecked<float>("voxel_size")
                        };

                        var dliName = entity.GetProperty<string>("lightprobetexture_dli");
                        var dlsName = entity.GetProperty<string>("lightprobetexture_dls");
                        var dlsdName = entity.GetProperty<string>("lightprobetexture_dlshd");

                        if (dlsName != null)
                        {
                            lightProbe.DirectLightScalars = RendererContext.MaterialLoader.GetTexture(dlsName);
                            lightProbe.DirectLightScalars.SetWrapMode(TextureWrapMode.ClampToEdge);
                        }

                        if (dliName != null)
                        {
                            lightProbe.DirectLightIndices = RendererContext.MaterialLoader.GetTexture(dliName);
                            lightProbe.DirectLightIndices.SetFiltering(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
                            lightProbe.DirectLightIndices.SetWrapMode(TextureWrapMode.ClampToEdge);
                        }

                        scene.LightingInfo.LightProbeType = entity.ContainsKey("light_probe_atlas_x") switch
                        {
                            false => LightProbeType.IndividualProbes,
                            true => LightProbeType.ProbeAtlas,
                        };

                        if (dlsdName != null)
                        {
                            lightProbe.DirectLightShadows = RendererContext.MaterialLoader.GetTexture(dlsdName);
                            lightProbe.DirectLightShadows.SetWrapMode(TextureWrapMode.ClampToEdge);

                            lightProbe.AtlasSize = new Vector3(
                                entity.GetPropertyUnchecked<float>("light_probe_size_x"),
                                entity.GetPropertyUnchecked<float>("light_probe_size_y"),
                                entity.GetPropertyUnchecked<float>("light_probe_size_z")
                            );

                            lightProbe.AtlasOffset = new Vector3(
                                entity.GetPropertyUnchecked<float>("light_probe_atlas_x"),
                                entity.GetPropertyUnchecked<float>("light_probe_atlas_y"),
                                entity.GetPropertyUnchecked<float>("light_probe_atlas_z")
                            );
                        }

                        scene.LightingInfo.AddProbe(lightProbe);
                    }
                }

                if (transformationMatrix == default)
                {
                    return;
                }

                var model = entity.GetProperty<string>("model");
                var particle = entity.GetProperty<string>("effect_name");
                var animation = entity.GetProperty<string>("defaultanim") ?? entity.GetProperty<string>("idleanim");

                var skin = entity.GetProperty<string>("skin");
                var positionVector = transformationMatrix.Translation;

                if (classname == "sky_camera")
                {
                    WorldScale = entity.GetPropertyUnchecked<float>("scale");
                    WorldOffset = positionVector;
                }

                if (particle != null)
                {
                    var particleResource = RendererContext.FileLoader.LoadFileCompiled(particle);
                    var particleSystem = (ParticleSystem?)particleResource?.DataBlock;

                    if (particleSystem != null)
                    {
                        var origin = new Vector3(positionVector.X, positionVector.Y, positionVector.Z);

                        try
                        {
                            var particleNode = new ParticleSceneNode(scene, particleSystem)
                            {
                                Name = particle,
                                Transform = Matrix4x4.CreateTranslation(origin),
                                LayerName = "Particles",
                                EntityData = entity,
                            };
                            scene.Add(particleNode, true);
                        }
                        catch (Exception e)
                        {
                            RendererContext.Logger.LogError(e, "Failed to setup particle '{Particle}'", particle);
                        }
                    }
                }

                if (IsCamera(classname))
                {
                    var cameraName = entity.GetProperty<string>("cameraname") ?? entity.GetProperty<string>("targetname") ?? classname;
                    CameraNames.Add(cameraName);
                    CameraMatrices.Add(transformationMatrix);
                }

                if (classname == "post_processing_volume")
                {
                    var exposureParams = ExposureSettings.LoadFromEntity(entity);

                    var isMaster = entity.GetProperty<bool>("master");
                    var useExposure = entity.GetProperty<bool>("enableexposure");
                    var fadeTime = entity.GetPropertyUnchecked<float>("fadetime");

                    var postProcess = new ScenePostProcessVolume(scene)
                    {
                        ExposureSettings = exposureParams,
                        FadeTime = fadeTime,
                        UseExposure = useExposure,
                        IsMaster = isMaster,
                        Transform = transformationMatrix, // needed if model is used
                    };

                    var postProcessResourceFilename = entity.GetProperty<string>("postprocessing");

                    if (postProcessResourceFilename != null)
                    {
                        var postProcessResource = RendererContext.FileLoader.LoadFileCompiled(postProcessResourceFilename);

                        if (postProcessResource?.DataBlock is PostProcessing postProcessAsset)
                        {
                            postProcess.LoadPostProcessResource(postProcessAsset);
                        }
                    }

                    var postProcessHasModel = false;

                    if (model != null)
                    {
                        var postProcessModel = RendererContext.FileLoader.LoadFileCompiled(model);

                        if (postProcessModel?.DataBlock is Model ppModelResource)
                        {
                            postProcess.ModelVolume = ppModelResource;

                            var ppModelNode = new ModelSceneNode(scene, ppModelResource, skin)
                            {
                                Transform = transformationMatrix,
                                LayerName = layerName,
                                Name = model,
                                EntityData = entity,
                            };

                            postProcessHasModel = true; // for collision we'd need to collect phys data within the class

                            scene.Add(ppModelNode, true);
                        }
                        else
                        {
                            RendererContext.Logger.LogWarning("Post Process model failed to load file \"{Model}\"", model);
                        }

                    }

                    scene.PostProcessInfo.AddPostProcessVolume(postProcess);

                    // If the post process model exists, we hackily let it add the model to the scene nodes
                    if (!postProcessHasModel)
                    {
                        return;
                    }
                }
                else if (classname == "env_tonemap_controller")
                {
                    var minExposureTC = entity.GetPropertyUnchecked<float>("minexposure");
                    var maxExposureTC = entity.GetPropertyUnchecked<float>("minexposure");
                    var exposureRate = entity.GetPropertyUnchecked<float>("rate");
                    //var isMasterTC = entity.GetPropertyUnchecked<bool>("master"); // master actually doesn't do anything

                    var exposureSettings = new ExposureSettings()
                    {
                        ExposureMin = minExposureTC,
                        ExposureMax = maxExposureTC,
                        ExposureSpeedDown = exposureRate,
                        ExposureSpeedUp = exposureRate,
                    };

                    var tonemapController = new SceneTonemapController(scene)
                    {
                        ControllerExposureSettings = exposureSettings,
                    };


                    if (scene.PostProcessInfo.MasterTonemapController == null)
                    {
                        scene.PostProcessInfo.MasterTonemapController = tonemapController;
                    }
                }

                if (model == null)
                {
                    CreateDefaultEntity(entity, classname, layerName, transformationMatrix);
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

                // todo: rendercolor might sometimes be vec4, which holds renderamt
                var rendercolor = entity.GetColor32Property("rendercolor");
                var renderamt = entity.GetPropertyUnchecked("renderamt", 1.0f);

                if (renderamt > 1f)
                {
                    renderamt /= 255f;
                }

                if (newEntity.DataBlock is not Model newModel)
                {
                    return;
                }

                var modelNode = new ModelSceneNode(scene, newModel, skin)
                {
                    Transform = transformationMatrix,
                    Tint = new Vector4(rendercolor, renderamt),
                    LayerName = layerName,
                    Name = model,
                    EntityData = entity,
                };

                if (modelNode.HasMeshes)
                {
                    // Animation
                    if (animation != null)
                    {
                        var isAnimated = modelNode.SetAnimationForWorldPreview(animation);
                        if (isAnimated)
                        {
                            var holdAnimationOn = entity.GetPropertyUnchecked<bool>("holdanimation");
                            if (holdAnimationOn)
                            {
                                modelNode.AnimationController.PauseLastFrame();
                            }
                        }
                    }

                    var body = entity.GetPropertyUnchecked("body", -1L);
                    if (body != -1L)
                    {
                        var groups = modelNode.GetMeshGroups();
                        modelNode.SetActiveMeshGroups(groups.Skip((int)body).Take(1));
                    }

                    scene.Add(modelNode, true);
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
                else if (!modelNode.HasMeshes)
                {
                    // If the loaded model has no meshes and has no physics, fallback to default entity
                    CreateDefaultEntity(entity, classname, layerName, transformationMatrix);
                }
            }

            foreach (var (entity, classname) in entitiesReordered)
            {
                try
                {
                    LoadEntity(classname, entity);
                }
                catch (Exception e)
                {
                    var id = entity.GetProperty("hammeruniqueid", string.Empty);

                    throw new InvalidDataException($"Failed to process entity '{classname}' (hammeruniqueid={id})", e);
                }
            }
        }

        private void LoadSkybox(Entity entity)
        {
            var targetmapname = entity.GetProperty<string>("targetmapname");

            if (targetmapname == null)
            {
                return;
            }

            if (!targetmapname.EndsWith(".vmap", StringComparison.InvariantCulture))
            {
                RendererContext.Logger.LogWarning("Not loading skybox '{Targetmapname}' because it did not end with .vmap", targetmapname);
                return;
            }

            // Maps have to be packed in a vpk?
            var vpkFile = Path.ChangeExtension(targetmapname, ".vpk");
            var vpkFound = RendererContext.FileLoader.FindFile(vpkFile);
            Package? package;

            // Load the skybox map vpk and make it searchable in the file loader
            if (vpkFound.PathOnDisk != null)
            {
                // TODO: Due to the way gui contexts works, we're preloading the vpk into parent context
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

            var worldName = Path.Join(
                Path.GetDirectoryName(targetmapname),
                Path.GetFileNameWithoutExtension(targetmapname),
                "world.vwrld_c"
            );

            SkyboxScene = new Scene(RendererContext);
            SkyboxScene.LightingInfo.LightingData.IsSkybox = 1u;

            var skyboxResult = LoadMap(targetmapname, SkyboxScene);

            // Take origin and angles from skybox_reference
            EntityTransformHelper.DecomposeTransformationMatrix(entity, out _, out var skyboxReferenceRotationMatrix, out var skyboxReferencePositionMatrix);
            var skyboxReference = skyboxReferenceRotationMatrix * Matrix4x4.CreateTranslation(skyboxReferencePositionMatrix);

            var offsetTransform = Matrix4x4.CreateTranslation(-skyboxResult.WorldOffset);
            var offsetAndScaleTransform = offsetTransform;

            // Apply skybox_reference transform after scaling
            offsetAndScaleTransform *= Matrix4x4.CreateScale(skyboxResult.WorldScale) * skyboxReference;
            offsetTransform *= skyboxReference;

            foreach (var node in SkyboxScene.AllNodes)
            {
                if (node.LayerName == "Entities")
                {
                    node.Transform *= offsetTransform;
                }
                else
                {
                    node.Transform *= offsetAndScaleTransform;
                }
            }

            foreach (var envmap in SkyboxScene.LightingInfo.EnvMaps)
            {
                envmap.Transform *= offsetAndScaleTransform;
            }

            if (package != null)
            {
                RendererContext.FileLoader.RemovePackageFromSearch(package);
            }
        }

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

        private void CreateDefaultEntity(Entity entity, string classname, string layerName, Matrix4x4 transformationMatrix)
        {
            var hammerEntity = HammerEntities.Get(classname);
            string? filename = null;
            Resource? resource = null;

            if (hammerEntity?.Icons.Length > 0)
            {
                foreach (var file in hammerEntity.Icons)
                {
                    filename = file;

                    resource = RendererContext.FileLoader.LoadFileCompiled(file);

                    if (resource != null)
                    {
                        break;
                    }
                }
            }

            if (resource == null)
            {
                var color = hammerEntity?.Color ?? new Color32(255, 0, 255, 255);

                // Do not use transformationMatrix because scales need to be ignored
                EntityTransformHelper.DecomposeTransformationMatrix(entity, out _, out var rotationMatrix, out var positionVector);

                var boxNode = new SimpleBoxSceneNode(scene, color, new Vector3(16f))
                {
                    Transform = rotationMatrix * Matrix4x4.CreateTranslation(positionVector),
                    LayerName = layerName,
                    Name = filename,
                    EntityData = entity,
                };
                scene.Add(boxNode, true);
            }
            else if (resource.ResourceType == ResourceType.Model && resource.DataBlock is Model modelData)
            {
                var modelNode = IsCamera(classname)
                    ? new CameraSceneNode(scene, modelData)
                    : new ModelSceneNode(scene, modelData, null, isWorldPreview: true) { Name = filename };

                modelNode.Transform = transformationMatrix;
                modelNode.LayerName = layerName;
                modelNode.EntityData = entity;

                var isAnimated = modelNode.SetAnimationForWorldPreview("tools_preview");

                scene.Add(modelNode, true);
            }
            else if (resource.ResourceType == ResourceType.Material)
            {
                var spriteNode = new SpriteSceneNode(scene, RendererContext, resource, transformationMatrix.Translation)
                {
                    LayerName = layerName,
                    Name = filename,
                    EntityData = entity,
                };
                scene.Add(spriteNode, true);
            }
            else
            {
                throw new InvalidDataException($"Got resource {resource.ResourceType} for class \"{classname}\"");
            }

            if (hammerEntity?.Lines.Length > 0)
            {
                foreach (var line in hammerEntity.Lines)
                {
                    if (!entity.Properties.Properties.TryGetValue(line.StartValueKey, out var value))
                    {
                        continue;
                    }

                    var startEntity = FindEntityByKeyValue(line.StartKey, (string)value.Value!);

                    if (startEntity == null)
                    {
                        continue;
                    }

                    var end = transformationMatrix.Translation;
                    var start = EntityTransformHelper.CalculateTransformationMatrix(startEntity).Translation;

                    if (line.EndKey != null && line.EndValueKey != null)
                    {
                        if (!entity.Properties.Properties.TryGetValue(line.EndValueKey, out value))
                        {
                            continue;
                        }

                        var endEntity = FindEntityByKeyValue(line.EndKey, (string)value.Value!);

                        if (endEntity == null)
                        {
                            continue;
                        }

                        end = EntityTransformHelper.CalculateTransformationMatrix(endEntity).Translation;
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

        private void CreateEntityConnectionLines(Entity entity, Vector3 start)
        {
            if (entity.Connections == null)
            {
                return;
            }

            var alreadySeen = new HashSet<Entity>(entity.Connections.Count);

            foreach (var connectionData in entity.Connections)
            {
                var targetType = connectionData.GetEnumValue<EntityIOTargetType>("m_targetType");

                if (targetType != EntityIOTargetType.EntityNameOrClassName)
                {
                    RendererContext.Logger.LogDebug("Skipping entity i/o type {TargetType}", targetType);
                    continue;
                }

                var targetName = connectionData.GetStringProperty("m_targetName");
                var endEntity = FindEntityByKeyValue("targetname", targetName);

                if (endEntity == null)
                {
                    RendererContext.Logger.LogDebug("Did not find entity i/o output {TargetName}", targetName);
                    continue;
                }

                if (!alreadySeen.Add(endEntity))
                {
                    continue;
                }

                var end = EntityTransformHelper.CalculateTransformationMatrix(endEntity).Translation;

                var origin = (start + end) / 2f;
                end -= origin;
                var lineStart = start - origin;

                var lineNode = new LineSceneNode(scene, lineStart, end, new Color32(0, 255, 0), new Color32(255, 0, 0))
                {
                    LayerName = "Entity Connections",
                    Transform = Matrix4x4.CreateTranslation(origin),
#if DEBUG
                    Name = $"Line from {entity.GetProperty<string>("hammeruniqueid")} to {endEntity.GetProperty<string>("hammeruniqueid")}"
#endif
                };
                scene.Add(lineNode, true);
            }
        }

        private Entity? FindEntityByKeyValue(string keyToFind, string valueToFind)
        {
            if (valueToFind == null)
            {
                return null;
            }

            foreach (var entity in Entities)
            {
                if (entity.Properties.Properties.TryGetValue(keyToFind, out var value)
                    && value.Value is string outString
                    && valueToFind.Equals(outString, StringComparison.OrdinalIgnoreCase))
                {
                    return entity;
                }
            }

            return null;
        }

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
