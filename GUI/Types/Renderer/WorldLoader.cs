using System.Globalization;
using System.IO;
using System.Linq;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace GUI.Types.Renderer
{
    class WorldLoader
    {
        private readonly Scene scene;
        private readonly World world;
        private readonly VrfGuiContext guiContext;

        public List<EntityLump.Entity> Entities { get; } = [];

        public HashSet<string> DefaultEnabledLayers { get; } = ["Entities", "Particles"];

        public List<string> CameraNames { get; } = [];
        public List<Matrix4x4> CameraMatrices { get; } = [];

        public Scene SkyboxScene { get; set; }
        public SceneSkybox2D Skybox2D { get; set; }

        public Vector3 WorldOffset { get; set; } = Vector3.Zero;
        public float WorldScale { get; set; } = 1.0f;
        // TODO: also store skybox reference rotation

        public WorldLoader(World world, Scene scene)
        {
            this.world = world;
            this.scene = scene;
            guiContext = scene.GuiContext;

            Load();
        }

        private void Load()
        {
            LoadWorldLightingInfo();

            scene.RenderAttributes.TryAdd("LightmapGameVersionNumber", (byte)scene.LightingInfo.LightmapGameVersionNumber);

            foreach (var lumpName in world.GetEntityLumpNames())
            {
                if (lumpName == null)
                {
                    continue;
                }

                var newResource = guiContext.LoadFileCompiled(lumpName);

                if (newResource == null)
                {
                    continue;
                }

                var entityLump = (EntityLump)newResource.DataBlock;
                LoadEntitiesFromLump(entityLump, "world_layer_base", Matrix4x4.Identity); // TODO: Hardcoded layer name
            }

            Action<List<SceneLight>> lightEntityStore = scene.LightingInfo.LightmapGameVersionNumber switch
            {
                0 or 1 => scene.LightingInfo.StoreLightMappedLights_V1,
                2 => scene.LightingInfo.StoreLightMappedLights_V2,
                _ => (List<SceneLight> x) => Log.Error(nameof(WorldLoader), $"Storing lights for lightmap version {scene.LightingInfo.LightmapGameVersionNumber} is not supported."),
            };

            lightEntityStore.Invoke(
                scene.AllNodes.Where(n => n is SceneLight).Cast<SceneLight>().ToList()
            );

            // Output is World_t we need to iterate m_worldNodes inside it.
            var worldNodes = world.GetWorldNodeNames();
            foreach (var worldNode in worldNodes)
            {
                if (worldNode != null)
                {
                    var newResource = guiContext.LoadFile(string.Concat(worldNode, ".vwnod_c"));
                    if (newResource == null)
                    {
                        continue;
                    }

                    var subloader = new WorldNodeLoader(guiContext, (WorldNode)newResource.DataBlock);
                    subloader.Load(scene);

                    foreach (var layer in subloader.LayerNames)
                    {
                        DefaultEnabledLayers.Add(layer);
                    }
                }
            }

            LoadWorldPhysics(scene);
        }

        public void LoadWorldPhysics(Scene scene)
        {
            // TODO: Ideally we would use the vrman files to find relevant files.
            string worldPhysicsFolder = null;

            if (Path.GetExtension(guiContext.FileName) == ".vmap_c")
            {
                worldPhysicsFolder = guiContext.FileName[..^7];
            }
            else
            {
                worldPhysicsFolder = Path.GetDirectoryName(guiContext.FileName);
            }

            PhysAggregateData phys = null;
            var physResource = guiContext.LoadFile(Path.Join(worldPhysicsFolder, "world_physics.vmdl_c"));

            if (physResource != null)
            {
                phys = (PhysAggregateData)physResource.GetBlockByType(BlockType.PHYS);
            }
            else
            {
                physResource = guiContext.LoadFile(Path.Join(worldPhysicsFolder, "world_physics.vphys_c"));

                if (physResource != null)
                {
                    phys = (PhysAggregateData)physResource.DataBlock;
                }
            }

            if (phys != null)
            {
                foreach (var physSceneNode in PhysSceneNode.CreatePhysSceneNodes(scene, phys, physResource.FileName[..^2]))
                {
                    physSceneNode.LayerName = "world_layer_base";

                    scene.Add(physSceneNode, false);
                }
            }
        }

        private readonly Dictionary<string, string> LightmapNameToUniformName = new()
        {
            {"irradiance", "g_tIrradiance"},
            {"directional_irradiance", "g_tDirectionalIrradiance"},
            {"direct_light_shadows", "g_tDirectLightShadows"},
            {"direct_light_indices", "g_tDirectLightIndices"},
            {"direct_light_strengths", "g_tDirectLightStrengths"},
        };

        private readonly string[] LightmapSetV81_SteamVr = ["g_tIrradiance", "g_tDirectionalIrradiance"];
        private readonly string[] LightmapSetV81 = ["g_tIrradiance", "g_tDirectionalIrradiance", "g_tDirectLightIndices", "g_tDirectLightStrengths"];
        private readonly string[] LightmapSetV82 = ["g_tIrradiance", "g_tDirectionalIrradiance", "g_tDirectLightShadows"];

        private void LoadWorldLightingInfo()
        {
            var worldLightingInfo = world.GetWorldLightingInfo();
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

            foreach (var lightmap in worldLightingInfo.GetArray<string>("m_lightMaps"))
            {
                var name = Path.GetFileNameWithoutExtension(lightmap);
                if (LightmapNameToUniformName.TryGetValue(name, out var uniformName))
                {
                    var srgbRead = name == "irradiance";
                    var renderTexture = guiContext.MaterialLoader.GetTexture(lightmap, srgbRead);
                    result.Lightmaps[uniformName] = renderTexture;

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
        }

        private void LoadEntitiesFromLump(EntityLump entityLump, string layerName, Matrix4x4 parentTransform)
        {
            var childEntities = entityLump.GetChildEntityNames();
            var childEntityLumps = new Dictionary<string, EntityLump>(childEntities.Length);

            foreach (var childEntityName in childEntities)
            {
                var newResource = guiContext.LoadFileCompiled(childEntityName);

                if (newResource == null)
                {
                    continue;
                }

                var childLump = (EntityLump)newResource.DataBlock;
                var childName = childLump.Data.GetProperty<string>("m_name");

                childEntityLumps.Add(childName, childLump);
            }

            static bool IsCubemapOrProbe(string cls)
                => cls == "env_combined_light_probe_volume"
                || cls == "env_light_probe_volume"
                || cls == "env_cubemap_box"
                || cls == "env_cubemap";

            static bool IsFog(string cls)
                => cls is "env_cubemap_fog" or "env_gradient_fog";

            static bool IsCamera(string cls)
                => cls == "sky_camera"
                || cls == "point_devshot_camera"
                || cls == "point_camera_vertical_fov"
                || cls == "point_camera";

            var entities = entityLump.GetEntities().ToList();
            var entitiesReordered = entities
                .Select(e => (e, e.GetProperty<string>("classname")))
                .OrderByDescending(x => IsCubemapOrProbe(x.Item2) || IsFog(x.Item2));

            Entities.AddRange(entities);

            void LoadEntity(string classname, Entity entity)
            {
                if (classname == "worldspawn")
                {
                    return; // do not draw
                }

                //var entity = entityData.Properties;

                var transformationMatrix = parentTransform * EntityTransformHelper.CalculateTransformationMatrix(entity);
                var light = SceneLight.IsAccepted(classname);

                if (classname == "info_world_layer")
                {
                    var spawnflags = entity.GetPropertyUnchecked<uint>("spawnflags");
                    var layername = entity.GetProperty<string>("layername");

                    // Visible on spawn flag
                    if ((spawnflags & 1) == 1)
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
                    scene.Add(lightNode, false);
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
                        Log.Warn(nameof(WorldLoader), $"Failed to find child entity lump with name {entityLumpName}.");
                    }
                }
                else if (classname == "env_sky" || classname == "ent_dota_lightinfo")
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

                    if (!disabled)
                    {
                        var rotation = transformationMatrix with
                        {
                            Translation = Vector3.Zero
                        };
                        using var skyMaterial = guiContext.LoadFileCompiled(skyname);

                        Skybox2D = new SceneSkybox2D(guiContext.MaterialLoader.LoadMaterial(skyMaterial))
                        {
                            Tint = tintColor,
                            Transform = rotation,
                        };
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

                        RenderTexture fogTexture = null;
                        var exposureBias = 0.0f;

                        if (fogSource == 0) // Cubemap From Texture, Disabled in CS2
                        {
                            var textureName = entity.GetProperty<string>("cubemapfogtexture");
                            fogTexture = guiContext.MaterialLoader.GetTexture(textureName);
                        }
                        else
                        {
                            string material = null;

                            if (fogSource == 1) // Cubemap From Env_Sky
                            {
                                var skyEntTargetName = entity.GetProperty<string>("cubemapfogskyentity");
                                var skyEntity = FindEntityByKeyValue("targetname", skyEntTargetName);

                                // env_sky target //  && (scene.Sky.TargetName == skyEntTargetName)
                                if (skyEntity != null)
                                {
                                    material = skyEntity.GetProperty<string>("skyname") ?? skyEntity.GetProperty<string>("skybox_material_day");
                                    transformationMatrix = EntityTransformHelper.CalculateTransformationMatrix(skyEntity); // steal rotation from env_sky
                                }
                                else
                                {
                                    Log.Warn(nameof(WorldLoader), $"Disabling cubemap fog because failed to find env_sky of target name {skyEntTargetName}.");
                                    scene.FogInfo.CubeFogActive = false;
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
                                using var matFile = guiContext.LoadFileCompiled(material);
                                var mat = guiContext.MaterialLoader.LoadMaterial(matFile);

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
                        var envMapTexture = guiContext.MaterialLoader.GetTexture(
                            entity.GetProperty<string>("cubemaptexture")
                        );

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

                    if (classname == "env_combined_light_probe_volume" || classname == "env_light_probe_volume")
                    {
                        var irradianceTexture = guiContext.MaterialLoader.GetTexture(
                            entity.GetProperty<string>("lightprobetexture"),
                            srgbRead: true
                        );

                        var lightProbe = new SceneLightProbe(scene, bounds)
                        {
                            LayerName = layerName,
                            Transform = transformationMatrix,
                            HandShake = handShake,
                            Irradiance = irradianceTexture,
                            IndoorOutdoorLevel = indoorOutdoorLevel,
                        };

                        var dliName = entity.GetProperty<string>("lightprobetexture_dli");
                        var dlsName = entity.GetProperty<string>("lightprobetexture_dls");
                        var dlsdName = entity.GetProperty<string>("lightprobetexture_dlshd");

                        if (dlsName != null)
                        {
                            lightProbe.DirectLightScalars = guiContext.MaterialLoader.GetTexture(dlsName);
                        }

                        if (dliName != null)
                        {
                            lightProbe.DirectLightIndices = guiContext.MaterialLoader.GetTexture(dliName);
                            lightProbe.DirectLightIndices.SetFiltering(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
                        }

                        scene.LightingInfo.LightProbeType = entity.ContainsKey("light_probe_atlas_x") switch
                        {
                            false => Scene.LightProbeType.IndividualProbes,
                            true => Scene.LightProbeType.ProbeAtlas,
                        };

                        if (dlsdName != null)
                        {
                            lightProbe.DirectLightShadows = guiContext.MaterialLoader.GetTexture(dlsdName);

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
                    var particleResource = guiContext.LoadFileCompiled(particle);

                    if (particleResource != null)
                    {
                        var particleSystem = (ParticleSystem)particleResource.DataBlock;
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
                            Log.Error(nameof(WorldLoader), $"Failed to setup particle '{particle}': {e}");
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
                    ExposureSettings exposureParams = new();
                    exposureParams.LoadFromEntity(entity);

                    var isMaster = entity.GetProperty<bool>("master");
                    var useExposure = entity.GetProperty<bool>("enableexposure");
                    var fadeTime = entity.GetPropertyUnchecked<float>("fadetime");

                    // todo: test where this is enabled/disabled
                    exposureParams.AutoExposureEnabled = useExposure;

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
                        var postProcessResource = guiContext.LoadFileCompiled(postProcessResourceFilename);

                        if (postProcessResource != null)
                        {
                            var postProcessAsset = (PostProcessing)postProcessResource.DataBlock;

                            postProcess.LoadPostProcessResource(postProcessAsset);
                        }
                    }

                    var postProcessHasModel = false;

                    if (model != null)
                    {
                        var postProcessModel = guiContext.LoadFileCompiled(model);

                        if (postProcessModel != null)
                        {
                            var ppModelResource = (Model)postProcessModel.DataBlock;

                            postProcess.ModelVolume = ppModelResource;

                            var ppModelNode = new ModelSceneNode(scene, ppModelResource, skin, optimizeForMapLoad: true)
                            {
                                Transform = transformationMatrix,
                                LayerName = layerName,
                                Name = model,
                                EntityData = entity,
                            };

                            postProcessHasModel = true; // for collision we'd need to collect phys data within the class

                            scene.Add(ppModelNode, false);
                        }
                        else
                        {
                            Log.Warn(nameof(WorldLoader), $"Post Process model failed to load file \"{model}\".");
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
                    CreateDefaultEntity(entity, classname, transformationMatrix);
                    return;
                }

                var newEntity = guiContext.LoadFileCompiled(model);

                if (newEntity == null)
                {
                    var errorModelResource = guiContext.LoadFile("models/dev/error.vmdl_c");

                    if (errorModelResource != null)
                    {
                        var errorModel = new ModelSceneNode(scene, (Model)errorModelResource.DataBlock, skin, optimizeForMapLoad: true)
                        {
                            Name = "error",
                            Transform = transformationMatrix,
                            LayerName = layerName,
                            EntityData = entity,
                        };

                        scene.Add(errorModel, false);
                    }

                    return;
                }

                // todo: rendercolor might sometimes be vec4, which holds renderamt
                var rendercolor = entity.GetColor32Property("rendercolor");
                var renderamt = entity.GetPropertyUnchecked("renderamt", 1.0f);

                rendercolor.X = MathF.Pow(rendercolor.X, 2.2f);
                rendercolor.Y = MathF.Pow(rendercolor.Y, 2.2f);
                rendercolor.Z = MathF.Pow(rendercolor.Z, 2.2f);

                var newModel = (Model)newEntity.DataBlock;

                var modelNode = new ModelSceneNode(scene, newModel, skin, optimizeForMapLoad: true)
                {
                    Transform = transformationMatrix,
                    Tint = new Vector4(rendercolor, renderamt),
                    LayerName = layerName,
                    Name = model,
                    EntityData = entity,
                };

                // Animation
                var isAnimated = modelNode.SetAnimationForWorldPreview(animation);
                if (isAnimated)
                {
                    var holdAnimationOn = entity.GetPropertyUnchecked<bool>("holdanimation");
                    if (holdAnimationOn)
                    {
                        modelNode.AnimationController.PauseLastFrame();
                    }
                }

                var body = entity.GetPropertyUnchecked("body", -1L);
                if (body != -1L)
                {
                    var groups = modelNode.GetMeshGroups();
                    modelNode.SetActiveMeshGroups(groups.Skip((int)body).Take(1));
                }

                scene.Add(modelNode, isAnimated);

                var phys = newModel.GetEmbeddedPhys();
                if (phys == null)
                {
                    var refPhysicsPaths = newModel.GetReferencedPhysNames().ToArray();
                    if (refPhysicsPaths.Length != 0)
                    {
                        var newResource = guiContext.LoadFileCompiled(refPhysicsPaths.First());
                        if (newResource != null)
                        {
                            phys = (PhysAggregateData)newResource.DataBlock;
                        }
                    }
                }

                if (phys != null)
                {
                    foreach (var physSceneNode in PhysSceneNode.CreatePhysSceneNodes(scene, phys, model, classname))
                    {
                        physSceneNode.Transform = transformationMatrix;
                        physSceneNode.PhysGroupName = classname;
                        physSceneNode.LayerName = layerName;
                        physSceneNode.EntityData = entity;

                        scene.Add(physSceneNode, false);
                    }
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

            if (targetmapname == null || guiContext.ParentGuiContext == null)
            {
                return;
            }

            if (!targetmapname.EndsWith(".vmap", StringComparison.InvariantCulture))
            {
                Log.Warn(nameof(WorldLoader), $"Not loading skybox '{targetmapname}' because it did not end with .vmap");
                return;
            }

            // Maps have to be packed in a vpk?
            var vpkFile = Path.ChangeExtension(targetmapname, ".vpk");
            var vpkFound = guiContext.FileLoader.FindFile(vpkFile);
            Package package;

            // Load the skybox map vpk and make it searchable in the file loader
            if (vpkFound.PathOnDisk != null)
            {
                // TODO: Due to the way gui contexts works, we're preloading the vpk into parent context
                package = guiContext.FileLoader.AddPackageToSearch(vpkFound.PathOnDisk);
            }
            else if (vpkFound.PackageEntry != null)
            {
                var innerVpkName = vpkFound.PackageEntry.GetFullPath();

                Log.Info(nameof(WorldLoader), $"Preloading vpk \"{innerVpkName}\" from \"{vpkFound.Package.FileName}\"");

                // TODO: Should FileLoader have a method that opens stream for us?
                var stream = GameFileLoader.GetPackageEntryStream(vpkFound.Package, vpkFound.PackageEntry);

                package = new Package();

                try
                {
                    package.SetFileName(innerVpkName);
                    package.OptimizeEntriesForBinarySearch(StringComparison.OrdinalIgnoreCase);
                    package.Read(stream);

                    guiContext.FileLoader.AddPackageToSearch(package);

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

            var skyboxWorld = guiContext.LoadFile(worldName);

            if (skyboxWorld == null)
            {
                guiContext.FileLoader.RemovePackageFromSearch(package);
                return;
            }

            SkyboxScene = new Scene(guiContext);

            var skyboxResult = new WorldLoader((World)skyboxWorld.DataBlock, SkyboxScene);

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

            guiContext.FileLoader.RemovePackageFromSearch(package);
        }

        private void CreateDefaultEntity(Entity entity, string classname, Matrix4x4 transformationMatrix)
        {
            var hammerEntity = HammerEntities.Get(classname);
            string filename = null;
            Resource resource = null;

            if (hammerEntity?.Icons.Length > 0)
            {
                foreach (var file in hammerEntity.Icons)
                {
                    filename = file;

                    resource = guiContext.LoadFileCompiled(file);

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
                    LayerName = "Entities",
                    Name = filename,
                    EntityData = entity,
                };
                scene.Add(boxNode, false);
            }
            else if (resource.ResourceType == ResourceType.Model)
            {
                var modelNode = new ModelSceneNode(scene, (Model)resource.DataBlock, null, optimizeForMapLoad: true)
                {
                    Transform = transformationMatrix,
                    LayerName = "Entities",
                    Name = filename,
                    EntityData = entity,
                };

                var isAnimated = modelNode.SetAnimationForWorldPreview("tools_preview");

                scene.Add(modelNode, isAnimated);
            }
            else if (resource.ResourceType == ResourceType.Material)
            {
                var spriteNode = new SpriteSceneNode(scene, guiContext, resource, transformationMatrix.Translation)
                {
                    LayerName = "Entities",
                    Name = filename,
                    EntityData = entity,
                };
                scene.Add(spriteNode, false);
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

                    var startEntity = FindEntityByKeyValue(line.StartKey, (string)value.Value);

                    if (startEntity == null)
                    {
                        continue;
                    }

                    var end = transformationMatrix.Translation;
                    var start = EntityTransformHelper.CalculateTransformationMatrix(startEntity).Translation;

                    if (line.EndKey != null)
                    {
                        if (!entity.Properties.Properties.TryGetValue(line.EndValueKey, out value))
                        {
                            continue;
                        }

                        var endEntity = FindEntityByKeyValue(line.EndKey, (string)value.Value);

                        if (endEntity == null)
                        {
                            continue;
                        }

                        end = EntityTransformHelper.CalculateTransformationMatrix(endEntity).Translation;
                    }

                    var origin = (start + end) / 2f;
                    end -= origin;
                    start -= origin;

                    var lineNode = new LineSceneNode(scene, line.Color, start, end)
                    {
                        LayerName = "Entities",
                        Transform = Matrix4x4.CreateTranslation(origin)
                    };
                    scene.Add(lineNode, false);
                }
            }
        }

        private Entity FindEntityByKeyValue(string keyToFind, string valueToFind)
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
    }
}
