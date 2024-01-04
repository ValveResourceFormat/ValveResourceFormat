using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Utils;

namespace GUI.Types.Renderer
{
    class WorldLoader
    {
        private readonly Scene scene;
        private readonly World world;
        private readonly VrfGuiContext guiContext;

        public List<EntityLump.Entity> Entities { get; } = [];

        public HashSet<string> DefaultEnabledLayers { get; } = ["Entities"];

        public Dictionary<string, Matrix4x4> CameraMatrices { get; } = [];

        public Scene SkyboxScene { get; set; }

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
                result.LightingData.LightmapUvScale = new Vector4(worldLightingInfo.GetSubCollection("m_vLightmapUvScale").ToVector2(), 0f, 0f);
            }

            foreach (var lightmap in worldLightingInfo.GetArray<string>("m_lightMaps"))
            {
                var name = Path.GetFileNameWithoutExtension(lightmap);
                if (LightmapNameToUniformName.TryGetValue(name, out var uniformName))
                {
                    var renderTexture = guiContext.MaterialLoader.LoadTexture(lightmap);
                    result.Lightmaps[uniformName] = renderTexture;

                    using (renderTexture.BindingContext())
                    {
                        if (name == "direct_light_indices")
                        {
                            // point sampling
                            renderTexture.SetFiltering(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
                        }

                        renderTexture.SetWrapMode(TextureWrapMode.ClampToEdge);
                    }
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

            var entities = entityLump.GetEntities().ToList();
            var entitiesReordered = entities
                .Select(e => (e, e.GetProperty<string>("classname")))
                .OrderByDescending(x => IsCubemapOrProbe(x.Item2) || IsFog(x.Item2));

            Entities.AddRange(entities);

            foreach (var (entity, classname) in entitiesReordered)
            {
                if (classname == "worldspawn")
                {
                    continue; // do not draw
                }

                var transformationMatrix = parentTransform * EntityTransformHelper.CalculateTransformationMatrix(entity);

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
                else if (classname == "point_template")
                {
                    var entityLumpName = entity.GetProperty<string>("entitylumpname");

                    if (childEntityLumps.TryGetValue(entityLumpName, out var childLump))
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
                        disabled = entity.GetProperty<bool>("startdisabled");
                        var skyTintColor = entity.GetProperty("tint_color");
                        tintColor = skyTintColor?.Data switch
                        {
                            byte[] col32 when skyTintColor.Type == EntityFieldType.Color32 => new Vector3(col32[0], col32[1], col32[2]) / 255.0f,
                            Vector3 vec3 => vec3 / 255.0f,
                            _ => Vector3.One,
                        };
                    }

                    if (!disabled)
                    {
                        var rotation = transformationMatrix with
                        {
                            Translation = Vector3.Zero
                        };
                        using var skyMaterial = guiContext.LoadFileCompiled(skyname);

                        scene.Sky = new SceneSky(scene)
                        {
                            Name = skyname,
                            LayerName = layerName,
                            Tint = tintColor,
                            Transform = rotation,
                            Material = guiContext.MaterialLoader.LoadMaterial(skyMaterial),
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
                        var useHeightFog = entity.GetProperty("fogverticalexponent") != default; // the oldest versions have these values missing, so disable it there
                        if (entity.GetProperty("heightfog") != default) // New in CS2
                        {
                            useHeightFog &= entity.GetProperty<bool>("heightfog");
                        }
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
                        var colorData = entity.GetProperty("fogcolor");

                        var color = new Vector3(1f);

                        if (colorData != null)
                        {
                            switch (colorData.Type)
                            {
                                case EntityFieldType.Vector:
                                    color = (Vector3)colorData.Data / 255.0f;
                                    break;
                                case EntityFieldType.Color32:
                                    // todo make this a function
                                    var colorBytes = (byte[])colorData.Data;
                                    color.X = colorBytes[0] / 255.0f;
                                    color.Y = colorBytes[1] / 255.0f;
                                    color.Z = colorBytes[2] / 255.0f;
                                    break;
                                default:
                                    throw new UnexpectedMagicException("unknown entity type", (int)colorData.Type, nameof(colorData.Type));
                            }
                        }

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
                    if (!entity.GetProperty<bool>("start_disabled") || scene.FogInfo.CubeFogActive)
                    {
                        scene.FogInfo.CubeFogActive = true;

                        var lodBias = entity.GetPropertyUnchecked<float>("cubemapfoglodbiase");

                        var falloffExponent = entity.GetPropertyUnchecked<float>("cubemapfogfalloffexponent");
                        var startDist = entity.GetPropertyUnchecked<float>("cubemapfogstartdistance");
                        var endDist = entity.GetPropertyUnchecked<float>("cubemapfogenddistance");

                        var hasHeightEnd = entity.GetProperty("cubemapfogheightend") != default;

                        var useHeightFog = entity.GetProperty("cubemapfogheightexponent") != default; // the oldest versions have these values missing, so disable it there
                        if (entity.GetProperty("cubemapheightfog") != default) // New in CS2
                        {
                            useHeightFog &= entity.GetProperty<bool>("cubemapheightfog");
                        }

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

                        var opacity = 1f;
                        if (entity.GetProperty("cubemapfogmaxopacity") != default)
                        {
                            opacity = entity.GetPropertyUnchecked<float>("cubemapfogmaxopacity");
                        }

                        var fogSource = "0";
                        if (entity.GetProperty("cubemapfogsource") != default)
                        {
                            fogSource = entity.GetProperty<string>("cubemapfogsource");
                        }

                        RenderTexture fogTexture = null;
                        var exposureBias = 0.0f;

                        if (fogSource == "0") // Cubemap From Texture, Disabled in CS2
                        {
                            fogTexture = guiContext.MaterialLoader.LoadTexture(
                                entity.GetProperty<string>("cubemapfogtexture"));
                        }
                        else
                        {
                            string material = null;

                            if (fogSource == "1") // Cubemap From Env_Sky
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
                            else if (fogSource == "2") // Cubemap From Material
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
                        var radius = entity.GetProperty<float>("influenceradius");
                        bounds = new AABB(-radius, -radius, -radius, radius, radius, radius);
                    }
                    else
                    {
                        bounds = new AABB(
                            entity.GetProperty<Vector3>("box_mins"),
                            entity.GetProperty<Vector3>("box_maxs")
                        );
                    }

                    if (classname != "env_light_probe_volume")
                    {
                        var envMapTexture = guiContext.MaterialLoader.GetTexture(
                            entity.GetProperty<string>("cubemaptexture")
                        );

                        var arrayIndexData = entity.GetProperty("array_index")?.Data;
                        var arrayIndex = arrayIndexData switch
                        {
                            int i => i,
                            string s => int.Parse(s, CultureInfo.InvariantCulture),
                            _ => 0,
                        };

                        var indoorOutdoorLevelData = entity.GetProperty("indoor_outdoor_level")?.Data;
                        var indoorOutdoorLevel = indoorOutdoorLevelData switch
                        {
                            int i => i,
                            string s => int.Parse(s, CultureInfo.InvariantCulture),
                            _ => 0,
                        };

                        var edgeFadeDists = entity.GetProperty<Vector3>("edge_fade_dists"); // TODO: Not available on all entities

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

                        scene.LightingInfo.AddEnvironmentMap(envMap);
                    }

                    if (classname == "env_combined_light_probe_volume" || classname == "env_light_probe_volume")
                    {
                        var irradianceTexture = guiContext.MaterialLoader.GetTexture(
                            entity.GetProperty<string>("lightprobetexture")
                        );

                        var lightProbe = new SceneLightProbe(scene, bounds)
                        {
                            LayerName = layerName,
                            Transform = transformationMatrix,
                            HandShake = handShake,
                            Irradiance = irradianceTexture,
                        };

                        var dliName = entity.GetProperty<string>("lightprobetexture_dli");
                        var dlsName = entity.GetProperty<string>("lightprobetexture_dls");
                        var dlsdName = entity.GetProperty<string>("lightprobetexture_dlsd");

                        if (dlsName != null)
                        {
                            lightProbe.DirectLightScalars = guiContext.MaterialLoader.GetTexture(dlsName);
                        }

                        if (dliName != null)
                        {
                            lightProbe.DirectLightIndices = guiContext.MaterialLoader.GetTexture(dliName);
                        }

                        if (dlsdName != null)
                        {
                            lightProbe.DirectLightShadows = guiContext.MaterialLoader.GetTexture(dlsdName);
                        }

                        scene.LightingInfo.AddProbe(lightProbe);
                    }
                }

                if (transformationMatrix == default)
                {
                    continue;
                }

                var model = entity.GetProperty<string>("model");
                var particle = entity.GetProperty<string>("effect_name");
                var animation = entity.GetProperty<string>("defaultanim");

                string skin = default;
                var skinRaw = entity.GetProperty("skin");

                if (skinRaw?.Type == EntityFieldType.CString)
                {
                    skin = (string)skinRaw.Data;
                }

                var isGlobalLight = classname == "env_global_light" || classname == "light_environment";
                var isCamera =
                    classname == "sky_camera" ||
                    classname == "point_devshot_camera" ||
                    classname == "point_camera_vertical_fov" ||
                    classname == "point_camera";

                var positionVector = transformationMatrix.Translation;

                if (classname == "sky_camera")
                {
                    scene.WorldScale = entity.GetPropertyUnchecked<float>("scale");
                    scene.WorldOffset = positionVector * -scene.WorldScale;
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
                                Transform = Matrix4x4.CreateTranslation(origin),
                                LayerName = layerName,
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

                if (isCamera)
                {
                    var name = entity.GetProperty<string>("targetname") ?? string.Empty;
                    var cameraName = string.IsNullOrEmpty(name)
                        ? classname
                        : name;

                    CameraMatrices.TryAdd(cameraName, transformationMatrix);
                }
                else if (isGlobalLight)
                {
                    var colorNormalized = entity.GetProperty("color").Data switch
                    {
                        byte[] bytes => new Vector3(bytes[0], bytes[1], bytes[2]),
                        Vector3 vec => vec,
                        Vector4 vec4 => new Vector3(vec4.X, vec4.Y, vec4.Z),
                        _ => throw new NotImplementedException()
                    } / 255.0f;

                    var brightness = 1.0f;
                    if (classname == "light_environment")
                    {
                        brightness = Convert.ToSingle(entity.GetProperty("brightness").Data, CultureInfo.InvariantCulture);
                    }

                    scene.LightingInfo.LightingData.SunLightPosition = transformationMatrix;
                    scene.LightingInfo.LightingData.SunLightColor = new Vector4(colorNormalized, brightness);
                }

                var rendercolor = entity.GetProperty("rendercolor");
                var renderamt = entity.GetProperty("renderamt")?.Data switch
                {
                    float f => f,
                    _ => 1.0f,
                };

                var tint = rendercolor?.Data switch
                {
                    byte[] col32 when rendercolor.Type == EntityFieldType.Color32 => new Vector4(col32[0], col32[1], col32[2], col32[3]) / 255.0f,
                    Vector3 vec3 => new Vector4(vec3 / 255.0f, renderamt),
                    _ => Vector4.One,
                };

                if (model == null)
                {
                    CreateDefaultEntity(entity, classname, transformationMatrix);
                    continue;
                }

                var newEntity = guiContext.LoadFileCompiled(model);

                if (newEntity == null)
                {
                    var errorModelResource = guiContext.LoadFile("models/dev/error.vmdl_c");

                    if (errorModelResource != null)
                    {
                        var errorModel = new ModelSceneNode(scene, (Model)errorModelResource.DataBlock, skin, optimizeForMapLoad: true)
                        {
                            Transform = transformationMatrix,
                            LayerName = layerName,
                            EntityData = entity,
                        };
                        scene.Add(errorModel, false);
                    }

                    continue;
                }

                var newModel = (Model)newEntity.DataBlock;

                var modelNode = new ModelSceneNode(scene, newModel, skin, optimizeForMapLoad: true)
                {
                    Transform = transformationMatrix,
                    Tint = tint,
                    LayerName = layerName,
                    Name = model,
                    EntityData = entity,
                };

                // Animation
                var isAnimated = modelNode.SetAnimationForWorldPreview(animation);
                if (isAnimated)
                {
                    var holdAnimation = entity.GetProperty("holdanimation");
                    if (holdAnimation != default)
                    {
                        var holdAnimationOn = holdAnimation.Type switch
                        {
                            EntityFieldType.Boolean => (bool)holdAnimation.Data,
                            EntityFieldType.CString => (string)holdAnimation.Data switch
                            {
                                "0" => false,
                                "1" => true,
                                _ => throw new NotImplementedException($"Unsupported holdanimation string value {holdAnimation.Data}"),
                            },
                            _ => throw new NotImplementedException($"Unsupported holdanimation type {holdAnimation.Type}"),
                        };

                        if (holdAnimationOn)
                        {
                            modelNode.AnimationController.PauseLastFrame();
                        }
                    }
                }

                var bodyHash = StringToken.Get("body");
                if (entity.Properties.TryGetValue(bodyHash, out var bodyProp))
                {
                    var groups = modelNode.GetMeshGroups();
                    var body = bodyProp.Data;
                    var bodyGroup = -1;

                    if (body is ulong bodyGroupLong)
                    {
                        bodyGroup = (int)bodyGroupLong;
                    }
                    else if (body is string bodyGroupString)
                    {
                        if (!int.TryParse(bodyGroupString, out bodyGroup))
                        {
                            bodyGroup = -1;
                        }
                    }

                    modelNode.SetActiveMeshGroups(groups.Skip(bodyGroup).Take(1));
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
                    foreach (var physSceneNode in PhysSceneNode.CreatePhysSceneNodes(scene, phys, model))
                    {
                        physSceneNode.Transform = transformationMatrix;
                        physSceneNode.PhysGroupName = classname;
                        physSceneNode.LayerName = layerName;
                        physSceneNode.EntityData = entity;

                        scene.Add(physSceneNode, false);
                    }
                }
            }
        }

        private void LoadSkybox(EntityLump.Entity entity)
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
                var stream = AdvancedGuiFileLoader.GetPackageEntryStream(vpkFound.Package, vpkFound.PackageEntry);

                package = new Package();
                package.SetFileName(innerVpkName);
                package.OptimizeEntriesForBinarySearch(StringComparison.OrdinalIgnoreCase);
                package.Read(stream);

                guiContext.FileLoader.AddPackageToSearch(package);
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

            SkyboxScene.FogInfo.GradientFogActive = scene.FogInfo.GradientFogActive;
            SkyboxScene.FogInfo.CubeFogActive = scene.FogInfo.CubeFogActive;

            var skyboxResult = new WorldLoader((World)skyboxWorld.DataBlock, SkyboxScene);

            var skyboxReferenceOffset = EntityTransformHelper.ParseVector(entity.GetProperty<string>("origin"));
            SkyboxScene.WorldOffset += skyboxReferenceOffset;

            guiContext.FileLoader.RemovePackageFromSearch(package);
        }

        private void CreateDefaultEntity(EntityLump.Entity entity, string classname, Matrix4x4 transformationMatrix)
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
                var color = hammerEntity?.Color ?? new Vector3(255, 0, 255);

                var boxNode = new SimpleBoxSceneNode(scene, color, new Vector3(16f))
                {
                    Transform = transformationMatrix,
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
                    if (!entity.Properties.TryGetValue(StringToken.Get(line.StartValueKey), out var value))
                    {
                        continue;
                    }

                    var startEntity = FindEntityByKeyValue(line.StartKey, (string)value.Data);

                    if (startEntity == null)
                    {
                        continue;
                    }

                    var end = transformationMatrix.Translation;
                    var start = EntityTransformHelper.CalculateTransformationMatrix(startEntity).Translation;

                    if (line.EndKey != null)
                    {
                        if (!entity.Properties.TryGetValue(StringToken.Get(line.EndValueKey), out value))
                        {
                            continue;
                        }

                        var endEntity = FindEntityByKeyValue(line.EndKey, (string)value.Data);

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

        private EntityLump.Entity FindEntityByKeyValue(string keyToFind, string valueToFind)
        {
            var stringToken = StringToken.Get(keyToFind);

            foreach (var entity in Entities)
            {
                if (entity.Properties.TryGetValue(stringToken, out var value)
                    && value.Data is string outString
                    && valueToFind.Equals(outString, StringComparison.OrdinalIgnoreCase))
                {
                    return entity;
                }
            }

            return null;
        }
    }
}
