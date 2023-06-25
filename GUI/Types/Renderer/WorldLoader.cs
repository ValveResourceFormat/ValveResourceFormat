using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Utils;

namespace GUI.Types.Renderer
{
    class WorldLoader
    {
        private readonly World world;
        private readonly VrfGuiContext guiContext;

        // Contains metadata that can't be captured by manipulating the scene itself. Returned from Load().
        public class LoadResult
        {
            public HashSet<string> DefaultEnabledLayers { get; } = new HashSet<string>();

            public IDictionary<string, Matrix4x4> CameraMatrices { get; } = new Dictionary<string, Matrix4x4>();

            public World Skybox { get; set; }
            public float SkyboxScale { get; set; } = 1.0f;
            public Vector3 SkyboxOrigin { get; set; } = Vector3.Zero;
        }

        public WorldLoader(VrfGuiContext vrfGuiContext, World world)
        {
            this.world = world;
            guiContext = vrfGuiContext;
        }

        public LoadResult Load(Scene scene)
        {
            var result = new LoadResult();
            result.DefaultEnabledLayers.Add("Entities");

            LoadWorldLightingInfo(scene);

            scene.RenderAttributes.TryAdd("LightmapGameVersionNumber", (byte)scene.LightingInfo.LightmapGameVersionNumber);

            var brdfLut = guiContext.MaterialLoader.LoadTexture("textures/dev/ggx_integrate_brdf_lut_schlick.vtex");

            if (brdfLut != null)
            {
                // TODO: add annoying force clamp for lut
                scene.LightingInfo.Lightmaps["g_tBRDFLookup"] = brdfLut;
            }

            foreach (var lumpName in world.GetEntityLumpNames())
            {
                if (lumpName == null)
                {
                    continue;
                }

                var newResource = guiContext.LoadFileByAnyMeansNecessary(lumpName + "_c");

                if (newResource == null)
                {
                    continue;
                }

                var entityLump = (EntityLump)newResource.DataBlock;
                LoadEntitiesFromLump(scene, result, entityLump, "world_layer_base"); // TODO
            }

            // Output is World_t we need to iterate m_worldNodes inside it.
            var worldNodes = world.GetWorldNodeNames();
            foreach (var worldNode in worldNodes)
            {
                if (worldNode != null)
                {
                    var newResource = guiContext.LoadFileByAnyMeansNecessary(worldNode + ".vwnod_c");
                    if (newResource == null)
                    {
                        continue;
                    }

                    var subloader = new WorldNodeLoader(guiContext, (WorldNode)newResource.DataBlock);
                    subloader.Load(scene);

                    foreach (var layer in subloader.LayerNames)
                    {
                        result.DefaultEnabledLayers.Add(layer);
                    }
                }
            }

            scene.CalculateEnvironmentMaps();

            // TODO: Ideally we would use the vrman files to find relevant files
            var physResource = guiContext.LoadFileByAnyMeansNecessary(Path.Join(Path.GetDirectoryName(guiContext.FileName), "world_physics.vphys_c"));
            if (physResource != null)
            {
                var phys = (PhysAggregateData)physResource.DataBlock;

                foreach (var physSceneNode in PhysSceneNode.CreatePhysSceneNodes(scene, phys))
                {
                    physSceneNode.LayerName = "world_layer_base";

                    scene.Add(physSceneNode, false);
                }
            }

            return result;
        }

        private readonly Dictionary<string, string> LightmapNameToUniformName = new()
        {
            {"irradiance", "g_tIrradiance"},
            {"directional_irradiance", "g_tDirectionalIrradiance"},
            {"direct_light_shadows", "g_tDirectLightShadows"},
            {"direct_light_indices", "g_tDirectLightIndices"},
            {"direct_light_strengths", "g_tDirectLightStrengths"},
        };

        private readonly string[] LightmapSetV81 = { "g_tIrradiance", "g_tDirectionalIrradiance", "g_tDirectLightIndices", "g_tDirectLightStrengths" };
        private readonly string[] LightmapSetV82 = { "g_tIrradiance", "g_tDirectionalIrradiance", "g_tDirectLightShadows" };

        private void LoadWorldLightingInfo(Scene scene)
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
                result.LightmapUvScale = worldLightingInfo.GetSubCollection("m_vLightmapUvScale").ToVector2();
            }

            foreach (var lightmap in worldLightingInfo.GetArray<string>("m_lightMaps"))
            {
                var name = Path.GetFileNameWithoutExtension(lightmap);
                if (LightmapNameToUniformName.TryGetValue(name, out var uniformName))
                {
                    result.Lightmaps[uniformName] = guiContext.MaterialLoader.LoadTexture(lightmap);
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
        }

        private void LoadEntitiesFromLump(Scene scene, LoadResult result, EntityLump entityLump, string layerName = null)
        {
            var childEntities = entityLump.GetChildEntityNames();

            foreach (var childEntityName in childEntities)
            {
                var newResource = guiContext.LoadFileByAnyMeansNecessary(childEntityName + "_c");

                if (newResource == null)
                {
                    continue;
                }

                var childLump = (EntityLump)newResource.DataBlock;
                var childName = childLump.Data.GetProperty<string>("m_name");

                LoadEntitiesFromLump(scene, result, childLump, childName);
            }

            static bool IsCubemapOrProbe(string cls)
                => cls == "env_combined_light_probe_volume"
                || cls == "env_light_probe_volume"
                || cls == "env_cubemap_box"
                || cls == "env_cubemap";

            var entitiesReordered = entityLump.GetEntities()
                .Select(e => (e, e.GetProperty<string>("classname")))
                .OrderByDescending(x => IsCubemapOrProbe(x.Item2));

            foreach (var (entity, classname) in entitiesReordered)
            {
                if (classname == "worldspawn")
                {
                    continue; // do not draw
                }

                if (classname == "info_world_layer")
                {
                    var spawnflags = entity.GetProperty<uint>("spawnflags");
                    var layername = entity.GetProperty<string>("layername");

                    // Visible on spawn flag
                    if ((spawnflags & 1) == 1)
                    {
                        result.DefaultEnabledLayers.Add(layername);
                    }
                }
                else if (classname == "skybox_reference")
                {
                    //var worldgroupid = entity.GetProperty<string>("worldgroupid");
                    var targetmapname = entity.GetProperty<string>("targetmapname");

                    if (targetmapname != null)
                    {
                        // TODO: Skybox loading always differs per game for some reason, we need to figure out how to load them properly without hackery
                        var skyboxWorldPath = $"maps/{Path.GetFileNameWithoutExtension(targetmapname)}/world.vwrld_c";
                        var skyboxPackage = guiContext.LoadFileByAnyMeansNecessary(skyboxWorldPath);

                        if (skyboxPackage == null && guiContext.ParentGuiContext != null)
                        {
                            var mapName = Path.GetFileNameWithoutExtension(guiContext.ParentGuiContext.FileName);
                            var mapsFolder = Path.GetDirectoryName(guiContext.ParentGuiContext.FileName);
                            string skyboxVpk;

                            if (targetmapname.EndsWith(".vmap")) // CS2
                            {
                                skyboxVpk = Path.Join(Path.GetDirectoryName(mapsFolder), string.Concat(targetmapname[..^5], ".vpk"));
                                skyboxWorldPath = string.Concat(targetmapname[..^5], "/world.vwrld_c");
                            }
                            else
                            {
                                skyboxVpk = Path.Join(mapsFolder, mapName, $"{Path.GetFileNameWithoutExtension(targetmapname)}.vpk");
                                skyboxWorldPath = $"maps/{mapName}/{Path.GetFileNameWithoutExtension(targetmapname)}/world.vwrld_c";
                            }

                            if (File.Exists(skyboxVpk))
                            {
                                var skyboxNewPackage = new SteamDatabase.ValvePak.Package();
                                skyboxNewPackage.Read(skyboxVpk);

                                guiContext.ParentGuiContext.FileLoader.AddPackageToSearch(skyboxNewPackage);
                                skyboxPackage = guiContext.LoadFileByAnyMeansNecessary(skyboxWorldPath);
                            }
                        }

                        if (skyboxPackage != null)
                        {
                            result.Skybox = (World)skyboxPackage.DataBlock;
                        }
                    }
                }
                else if (classname == "env_sky" || classname == "ent_dota_lightinfo")
                {
                    var skyname = entity.GetProperty<string>("skyname") ?? entity.GetProperty<string>("skybox_material_day");
                    using var skyMaterial = guiContext.LoadFileByAnyMeansNecessary(skyname + "_c");
                    scene.Sky = new SceneSky(scene)
                    {
                        Name = skyname,
                        LayerName = layerName,
                        Material = guiContext.MaterialLoader.LoadMaterial(skyMaterial),
                    };
                }
                else if (IsCubemapOrProbe(classname))
                {
                    var handShakeString = entity.GetProperty<string>("handshake");
                    if (!int.TryParse(handShakeString, out var handShake))
                    {
                        handShake = 0;
                    }

                    var transform = EntityTransformHelper.CalculateTransformationMatrix(entity);

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

                        scene.RenderAttributes["SCENE_ENVIRONMENT_TYPE"] = envMapTexture.Target switch
                        {
                            TextureTarget.TextureCubeMapArray => 2,
                            TextureTarget.TextureCubeMap => 1,
                            _ => 0,
                        };

                        scene.LightingInfo.Lightmaps.TryAdd("g_tEnvironmentMap", envMapTexture);

                        var arrayIndexData = entity.GetProperty("array_index")?.Data;
                        var arrayIndex = arrayIndexData switch
                        {
                            int i => i,
                            string s => int.Parse(s, CultureInfo.InvariantCulture),
                            _ => 0,
                        };

                        var envMap = new SceneEnvMap(scene, bounds)
                        {
                            LayerName = layerName,
                            Transform = transform,
                            HandShake = handShake,
                            ArrayIndex = arrayIndex,
                            ProjectionMode = classname == "env_cubemap" ? 0 : 1,
                            EnvMapTexture = envMapTexture,
                        };

                        if (handShake > 0)
                        {
                            scene.LightingInfo.EnvMaps.Add(handShake, envMap);
                        }

                        scene.Add(envMap, true);
                    }

                    if (classname == "env_combined_light_probe_volume" || classname == "env_light_probe_volume")
                    {
                        var irradianceTexture = guiContext.MaterialLoader.GetTexture(
                            entity.GetProperty<string>("lightprobetexture")
                        );

                        var lightProbe = new SceneLightProbe(scene, bounds)
                        {
                            LayerName = layerName,
                            Transform = transform,
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

                        if (handShake > 0)
                        {
                            scene.LightingInfo.LightProbes.Add(handShake, lightProbe);
                        }
                    }
                }

                var transformationMatrix = EntityTransformHelper.CalculateTransformationMatrix(entity);

                if (transformationMatrix == default)
                {
                    continue;
                }

                var model = entity.GetProperty<string>("model");
                var skin = entity.GetProperty<string>("skin");
                var particle = entity.GetProperty<string>("effect_name");
                var animation = entity.GetProperty<string>("defaultanim");

                var isGlobalLight = classname == "env_global_light" || classname == "light_environment";
                var isCamera =
                    classname == "sky_camera" ||
                    classname == "point_devshot_camera" ||
                    classname == "point_camera_vertical_fov" ||
                    classname == "point_camera";

                var positionVector = transformationMatrix.Translation;

                if (classname == "sky_camera")
                {
                    var skyboxScale = entity.GetProperty("scale");
                    result.SkyboxScale = skyboxScale.Type switch
                    {
                        EntityFieldType.Integer => (int)skyboxScale.Data,
                        EntityFieldType.Integer64 => (ulong)skyboxScale.Data,
                        _ => throw new NotImplementedException($"Unsupported skybox scale {skyboxScale.Type}"),
                    };
                    result.SkyboxOrigin = positionVector * -result.SkyboxScale;
                }

                if (particle != null)
                {
                    var particleResource = guiContext.LoadFileByAnyMeansNecessary(particle + "_c");

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
                            Console.Error.WriteLine($"Failed to setup particle '{particle}': {e}");
                        }
                    }

                    continue;
                }

                if (isCamera)
                {
                    var name = entity.GetProperty<string>("targetname") ?? string.Empty;
                    var cameraName = string.IsNullOrEmpty(name)
                        ? classname
                        : name;

                    result.CameraMatrices.TryAdd(cameraName, transformationMatrix);
                }
                else if (isGlobalLight)
                {
                    scene.GlobalLightTransform = transformationMatrix;
                    var colorNormalized = entity.GetProperty("color").Data switch
                    {
                        byte[] bytes => new Vector3(bytes[0], bytes[1], bytes[2]),
                        Vector3 vec => vec,
                        _ => throw new NotImplementedException()
                    } / 255.0f;

                    var brightness = 1.0f;
                    if (classname == "light_environment")
                    {
                        brightness = Convert.ToSingle(entity.GetProperty("brightness").Data, CultureInfo.InvariantCulture);
                    }

                    scene.GlobalLightColor = new Vector4(colorNormalized, brightness);
                }

                var objColor = Vector4.One;

                // Parse colour if present
                var colour = entity.GetProperty("rendercolor");

                // HL Alyx has an entity that puts rendercolor as a string instead of color255
                if (colour != default && colour.Type == EntityFieldType.Color32)
                {
                    var colourBytes = (byte[])colour.Data;
                    objColor.X = colourBytes[0] / 255.0f;
                    objColor.Y = colourBytes[1] / 255.0f;
                    objColor.Z = colourBytes[2] / 255.0f;
                    objColor.W = colourBytes[3] / 255.0f;
                }

                if (model == null)
                {
                    var sceneNode = AddToolModel(scene, entity, classname, transformationMatrix, positionVector);
                    continue;
                }

                var newEntity = guiContext.LoadFileByAnyMeansNecessary(model + "_c");

                if (newEntity == null)
                {
                    var errorModelResource = guiContext.LoadFileByAnyMeansNecessary("models/dev/error.vmdl_c");

                    if (errorModelResource != null)
                    {
                        var errorModel = new ModelSceneNode(scene, (Model)errorModelResource.DataBlock, skin, false)
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

                var modelNode = new ModelSceneNode(scene, newModel, skin, false)
                {
                    Transform = transformationMatrix,
                    Tint = objColor,
                    LayerName = layerName,
                    Name = model,
                    EntityData = entity,
                };

                // Animation
                {
                    modelNode.LoadAnimations();
                    modelNode.SetAnimationForWorldPreview(animation);

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

                scene.Add(modelNode, animation != default);

                var phys = newModel.GetEmbeddedPhys();
                if (phys == null)
                {
                    var refPhysicsPaths = newModel.GetReferencedPhysNames().ToArray();
                    if (refPhysicsPaths.Any())
                    {
                        var newResource = guiContext.LoadFileByAnyMeansNecessary(refPhysicsPaths.First() + "_c");
                        if (newResource != null)
                        {
                            phys = (PhysAggregateData)newResource.DataBlock;
                        }
                    }
                }

                if (phys != null)
                {
                    foreach (var physSceneNode in PhysSceneNode.CreatePhysSceneNodes(scene, phys))
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

        private SceneNode AddToolModel(Scene scene, EntityLump.Entity entity, string classname, Matrix4x4 transformationMatrix, Vector3 position)
        {
            var filenames = HammerEntities.GetToolModel(classname);
            string filename = null;
            Resource resource = null;

            foreach (var file in filenames)
            {
                filename = file;
                resource = guiContext.LoadFileByAnyMeansNecessary(file + "_c");

                if (resource != null)
                {
                    break;
                }
            }

            if (resource == null)
            {
                // TODO: Create a 16x16x16 box to emulate how Hammer draws them
                resource = guiContext.LoadFileByAnyMeansNecessary("materials/editor/obsolete.vmat_c");

                if (resource == null)
                {
                    return null;
                }
            }

            if (resource.ResourceType == ResourceType.Model)
            {
                var modelNode = new ModelSceneNode(scene, (Model)resource.DataBlock, null, false)
                {
                    Transform = transformationMatrix,
                    LayerName = "Entities",
                    Name = filename,
                    EntityData = entity,
                };

                modelNode.LoadAnimations();
                modelNode.SetAnimationForWorldPreview("tools_preview");

                scene.Add(modelNode, false);

                return modelNode;
            }
            else if (resource.ResourceType == ResourceType.Material)
            {
                var spriteNode = new SpriteSceneNode(scene, guiContext, resource, position)
                {
                    LayerName = "Entities",
                    Name = filename,
                    EntityData = entity,
                };
                scene.Add(spriteNode, false);

                return spriteNode;
            }
            else
            {
                throw new InvalidDataException($"Got resource {resource.ResourceType} for class \"{classname}\"");
            }
        }
    }
}
