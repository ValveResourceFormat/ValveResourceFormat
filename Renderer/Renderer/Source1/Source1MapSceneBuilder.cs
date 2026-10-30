using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;
using SkiaSharp;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Source1;
using ValveResourceFormat.Source1.Bsp;
using ValveResourceFormat.Source1.FileSystem;
using ValveResourceFormat.Source1.Material;
using ValveResourceFormat.Source1.Texture;

namespace ValveResourceFormat.Renderer.Source1
{
    /// <summary>
    /// Turns a Source 1 BSP into scene nodes: the world and brush models as lightmapped meshes, static props, and the 2D sky.
    /// </summary>
    internal static class Source1MapSceneBuilder
    {
        /// <summary>Name of the sampler the lightmapped Source 1 shaders read the lightmap from.</summary>
        public const string LightmapSamplerName = "g_tLightmap";

        private const string SkyTextureSamplerName = "g_tSkyTexture";

        private readonly record struct SkyFace(string Suffix, Vector3 Forward, Vector3 Right, Vector3 Up);

        private readonly record struct SkyTexture(string Path, string? CompressedHdrPath, Matrix4x4 Transform);

        private static readonly SkyFace[] SkyFaces =
        [
            new("ft", -Vector3.UnitY, -Vector3.UnitX, Vector3.UnitZ),
            new("bk", Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ),
            new("lf", -Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ),
            new("rt", Vector3.UnitX, -Vector3.UnitY, Vector3.UnitZ),
            new("up", Vector3.UnitZ, -Vector3.UnitY, -Vector3.UnitX),
            new("dn", -Vector3.UnitZ, -Vector3.UnitY, Vector3.UnitX),
        ];

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct WorldVertex
        {
            [VertexAttribute(VertexSlot.Position)] public readonly Vector3 Position;
            [VertexAttribute(VertexSlot.Normal)] public readonly Vector3 Normal;
            [VertexAttribute(VertexSlot.Tangent)] public readonly Vector4 Tangent;
            [VertexAttribute(VertexSlot.TexCoord)] public readonly Vector2 TexCoord;
            [VertexAttribute(VertexSlot.LightmapUV)] public readonly Vector2 LightmapUV;
            [VertexAttribute(VertexSlot.Color)] public readonly Color32 Color;

            public static readonly VertexInputLayout InputLayout = VertexInputLayout.FromStruct<WorldVertex>();

            public WorldVertex(BspVertex vertex)
            {
                Position = vertex.Position;
                Normal = vertex.Normal;
                Tangent = vertex.Tangent;
                TexCoord = vertex.TexCoord;
                LightmapUV = vertex.LightmapCoord;
                Color = new Color32(1f, 1f, 1f, MathUtils.Saturate(vertex.DisplacementAlpha));
            }
        }

        /// <summary>Builds the world, brush entities and static props of a map into the scene.</summary>
        /// <param name="scene">The scene to add the nodes to.</param>
        /// <param name="bsp">The map.</param>
        /// <param name="resolveMaterial">Loads the material of a texdata name such as <c>tf/wood/floor</c>.</param>
        /// <param name="createProp">Creates the node of a static prop in a scene from the prop, its index in the map and its transform, or returns null to skip it.</param>
        /// <param name="lighting">Chooses the cube map of world surfaces and finds the 3D sky box area, or null to do neither.</param>
        /// <param name="skyScene">Receives what is inside the 3D sky box area. Without it that area is left out.</param>
        /// <param name="entityNodeAdded">Called for each node of the world model and of the brush entities with the entity it belongs to.</param>
        /// <returns>The bounds of the world model.</returns>
        public static AABB Build(
            Scene scene,
            BspFile bsp,
            Func<string, RenderMaterial> resolveMaterial,
            Func<Scene, BspStaticProp, int, Matrix4x4, SceneNode?>? createProp = null,
            Source1MapLighting? lighting = null,
            Scene? skyScene = null,
            Action<BspEntity, SceneNode>? entityNodeAdded = null)
        {
            ArgumentNullException.ThrowIfNull(scene);
            ArgumentNullException.ThrowIfNull(bsp);
            ArgumentNullException.ThrowIfNull(resolveMaterial);

            var fileSystem = scene.RendererContext.Source1FileSystem;
            var models = BspMeshBuilder.BuildAll(bsp, fileSystem == null ? null : name => GetDecalSize(fileSystem, name));
            var world = new WorldBuilder(scene, skyScene, bsp, resolveMaterial, lighting, models)
            {
                EntityNodeAdded = entityNodeAdded,
            };
            var worldBounds = new AABB();

            if (models.Length > 0)
            {
                world.CurrentEntity = bsp.Entities.FirstOrDefault(static entity => entity.ClassName == "worldspawn");
                world.AddModel(models[0], Matrix4x4.Identity, splitByLighting: true);
                world.CurrentEntity = null;
                world.AddOverlays(models[0]);
                worldBounds = new AABB(bsp.Models[0].Min, bsp.Models[0].Max);
            }

            world.AddBrushEntities(models);

            if (createProp != null)
            {
                AddStaticProps(scene, skyScene, bsp, createProp, lighting?.SkyBounds);
            }

            return worldBounds;
        }

        private sealed class WorldBuilder
        {
            private readonly Scene scene;
            private readonly Scene? skyScene;
            private readonly BspFile bsp;
            private readonly Func<string, RenderMaterial> resolveMaterial;
            private readonly Source1MapLighting? lighting;
            private readonly RenderTexture[] lightmapPages;
            private readonly bool isBumped;
            private readonly HashSet<RenderMaterial> lightmappedMaterials = [];
            private readonly Dictionary<string, RenderMaterial> overlayMaterials = [];

            public Action<BspEntity, SceneNode>? EntityNodeAdded { get; init; }

            // The entity whose model AddModel builds, for EntityNodeAdded
            public BspEntity? CurrentEntity { get; set; }

            public WorldBuilder(Scene scene, Scene? skyScene, BspFile bsp, Func<string, RenderMaterial> resolveMaterial, Source1MapLighting? lighting, BspModelMesh[] models)
            {
                this.scene = scene;
                this.skyScene = skyScene;
                this.bsp = bsp;
                this.resolveMaterial = resolveMaterial;
                this.lighting = lighting;

                var atlas = bsp.LightmapLayout.BuildAtlas();
                lightmapPages = new RenderTexture[LightmapAtlas.PageCount];

                for (var i = 0; i < lightmapPages.Length; i++)
                {
                    lightmapPages[i] = CreateLightmapTexture(atlas, i);
                }

                isBumped = models.Any(static model => model.Materials.Values.Any(static mesh => (mesh.Flags & BspMeshFlags.Bumped) != 0));
            }

            private RenderMaterial Resolve(string name) => BindLightmaps(resolveMaterial(name));

            // A separate instance, so faces that use the same material are not depth biased
            private RenderMaterial ResolveOverlay(string name)
            {
                if (overlayMaterials.TryGetValue(name, out var material))
                {
                    return material;
                }

                material = scene.RendererContext.MaterialLoader.CreateSource1MaterialInstance(resolveMaterial(name), null);
                material.IsOverlay = true;
                material = BindLightmaps(material);
                overlayMaterials.Add(name, material);

                return material;
            }

            private RenderMaterial BindLightmaps(RenderMaterial material)
            {
                if (!lightmappedMaterials.Add(material) || !material.Shader.UniformNames.Contains(LightmapSamplerName))
                {
                    return material;
                }

                material.Textures[LightmapSamplerName] = lightmapPages[0];

                for (var i = 1; i < lightmapPages.Length; i++)
                {
                    material.Textures[LightmapSamplerName + i.ToString(CultureInfo.InvariantCulture)] = lightmapPages[i];
                }

                if (isBumped)
                {
                    material.IntParams["F_BUMPED_LIGHTMAP"] = 1;
                }

                return material;
            }

            public void AddOverlays(BspModelMesh model)
            {
                foreach (var overlay in model.Overlays)
                {
                    if (overlay.Vertices.Length == 0)
                    {
                        continue;
                    }

                    var target = FindScene(overlay.Vertices[0].Position);

                    if (target == null)
                    {
                        continue;
                    }

                    var name = $"{bsp.Name}/overlay{overlay.OverlayIndex}/{overlay.Material}";

                    AddMeshNode(target, name, ResolveOverlay(overlay.Material), overlay.Vertices, overlay.Indices, Matrix4x4.Identity, overlay: true);
                }
            }

            public void AddBrushEntities(BspModelMesh[] models)
            {
                foreach (var entity in bsp.Entities)
                {
                    var modelName = entity.GetString("model");

                    if (modelName.Length < 2 || modelName[0] != '*' || !int.TryParse(modelName.AsSpan(1), out var modelIndex))
                    {
                        continue;
                    }

                    if (modelIndex <= 0 || modelIndex >= models.Length)
                    {
                        continue;
                    }

                    if (IsHiddenBrushEntity(entity))
                    {
                        continue;
                    }

                    var transform = EntityTransformHelper.ToRigidTransformationMatrix(entity.Angles, entity.Origin);
                    var modelBounds = bsp.Models[modelIndex];
                    var center = Vector3.Transform((modelBounds.Min + modelBounds.Max) * 0.5f, transform);
                    var target = FindScene(center);

                    if (target == null)
                    {
                        continue;
                    }

                    CurrentEntity = entity;
                    AddModel(models[modelIndex], transform, splitByLighting: false, target);
                    CurrentEntity = null;
                }
            }

            private Scene? FindScene(Vector3 position)
                => Source1MapLighting.SelectScene(lighting?.SkyBounds, scene, skyScene, position);

            public void AddModel(BspModelMesh model, Matrix4x4 transform, bool splitByLighting, Scene? target = null)
            {
                target ??= scene;

                foreach (var (materialName, materialMesh) in model.Materials)
                {
                    if (materialMesh.Indices.Length == 0 || IsToolMaterial(materialName))
                    {
                        continue;
                    }

                    var name = $"{bsp.Name}/model{model.ModelIndex}/{materialName}";
                    var material = Resolve(materialName);

                    if (splitByLighting && lighting != null)
                    {
                        AddSplitMeshNodes(name, material, materialMesh, transform, lighting);
                    }
                    else
                    {
                        AddMeshNode(target, name, material, materialMesh.Vertices, materialMesh.Indices, transform);
                    }
                }
            }

            // Triangles inside the 3D sky box area go to the sky scene, and each scene's are grouped by the nearest cube map
            private void AddSplitMeshNodes(string name, RenderMaterial material, BspMaterialMesh materialMesh, Matrix4x4 transform, Source1MapLighting mapLighting)
            {
                var skyBounds = mapLighting.SkyBounds;
                var usesEnvMap = Source1MapLighting.UsesEnvMap(material);

                if (skyBounds == null && !usesEnvMap)
                {
                    AddMeshNode(scene, name, material, materialMesh.Vertices, materialMesh.Indices, transform);
                    return;
                }

                var groups = new Dictionary<int, List<int>>();
                var skyGroups = new Dictionary<int, List<int>>();
                var vertices = materialMesh.Vertices;
                var indices = materialMesh.Indices;

                for (var i = 0; i + 2 < indices.Length; i += 3)
                {
                    var a = vertices[indices[i]].Position;
                    var b = vertices[indices[i + 1]].Position;
                    var c = vertices[indices[i + 2]].Position;
                    var center = (a + b + c) / 3f;
                    var target = groups;

                    if (Source1MapLighting.IsInSky(skyBounds, center))
                    {
                        if (skyScene == null)
                        {
                            continue;
                        }

                        target = skyGroups;
                    }

                    var cubemap = usesEnvMap ? mapLighting.FindCubemap(center, vertices[indices[i]].Normal) : -1;

                    if (!target.TryGetValue(cubemap, out var triangles))
                    {
                        triangles = [];
                        target.Add(cubemap, triangles);
                    }

                    triangles.Add(i);
                }

                var triangleCount = indices.Length / 3;

                AddSplitGroups(scene, name, material, materialMesh, groups, triangleCount, transform, mapLighting);

                if (skyScene != null)
                {
                    AddSplitGroups(skyScene, $"{name}/sky", material, materialMesh, skyGroups, triangleCount, transform, mapLighting);
                }
            }

            private void AddSplitGroups(Scene target, string name, RenderMaterial material, BspMaterialMesh materialMesh, Dictionary<int, List<int>> groups, int triangleCount, Matrix4x4 transform, Source1MapLighting mapLighting)
            {
                foreach (var (cubemap, triangles) in groups)
                {
                    var partMaterial = material;

                    if (cubemap >= 0)
                    {
                        partMaterial = BindLightmaps(mapLighting.GetInstance(material, null, mapLighting.GetCubemapOrigin(cubemap), useAmbientCube: false));
                    }

                    var partName = groups.Count == 1 ? name : $"{name}/cubemap{cubemap}";

                    if (triangles.Count == triangleCount && groups.Count == 1)
                    {
                        AddMeshNode(target, partName, partMaterial, materialMesh.Vertices, materialMesh.Indices, transform);
                        continue;
                    }

                    var (partVertices, partIndices) = CreateSubMesh(materialMesh, triangles);

                    AddMeshNode(target, partName, partMaterial, CollectionsMarshal.AsSpan(partVertices), CollectionsMarshal.AsSpan(partIndices), transform);
                }
            }

            private void AddMeshNode(Scene target, string name, RenderMaterial material, ReadOnlySpan<BspVertex> sourceVertices, ReadOnlySpan<int> indices, Matrix4x4 transform, bool overlay = false)
            {
                var vertices = new WorldVertex[sourceVertices.Length];
                var bounds = new AABB();

                for (var i = 0; i < vertices.Length; i++)
                {
                    var position = sourceVertices[i].Position;

                    vertices[i] = new WorldVertex(sourceVertices[i]);
                    bounds = i == 0 ? new AABB(position, position) : bounds.Encapsulate(position);
                }

                var buffers = WorldVertex.InputLayout.CreateBuffers<WorldVertex, int>(vertices, indices);
                var mesh = RenderableMesh.CreateMesh(name, material, buffers, bounds, target.RendererContext);
                mesh.PlaceDrawCall(mesh.DrawCallsOpaque[0], forceOverlay: overlay);

                var node = new MeshSceneNode(target, mesh)
                {
                    Name = name,
                    Transform = transform,
                };

                target.Add(node, false);

                if (CurrentEntity != null)
                {
                    EntityNodeAdded?.Invoke(CurrentEntity, node);
                }
            }
        }

        /// <summary>Gets whether an entity does not draw its model when the map starts, by its render mode, effects, or because it starts disabled.</summary>
        /// <param name="entity">The entity.</param>
        internal static bool IsHiddenAtSpawn(BspEntity entity)
        {
            const int RenderModeEnvironmental = 6;
            const int RenderModeNone = 10;
            const int NoDrawEffect = 32;
            const int WallToggleStartOff = 1;

            var renderMode = entity.GetInt("rendermode");

            if (renderMode is RenderModeEnvironmental or RenderModeNone || (entity.GetInt("effects") & NoDrawEffect) != 0)
            {
                return true;
            }

            var className = entity.ClassName;

            if (className == "func_wall_toggle")
            {
                return (entity.GetInt("spawnflags") & WallToggleStartOff) != 0;
            }

            var startsDisabled = className.StartsWith("prop_dynamic", StringComparison.Ordinal)
                || className is "func_brush" or "item_teamflag";

            return startsDisabled && entity.GetInt("startdisabled") != 0;
        }

        private static bool IsHiddenBrushEntity(BspEntity entity)
        {
            var className = entity.ClassName;

            if (className.StartsWith("trigger_", StringComparison.Ordinal)
                || className.StartsWith("func_areaportal", StringComparison.Ordinal)
                || className.StartsWith("func_nav_", StringComparison.Ordinal))
            {
                return true;
            }

            switch (className)
            {
                case "func_occluder":
                case "func_nobuild":
                case "func_clip_vphysics":
                case "func_respawnroom":
                case "func_regenerate":
                case "func_capturezone":
                case "func_passtime_goal":
                case "func_vehicleclip":
                case "func_precipitation":
                case "func_nogrenades":
                case "func_changeclass":
                case "func_flag_alert":
                case "func_respawnflag":
                case "func_powerupvolume":
                    return true;
            }

            return IsHiddenAtSpawn(entity);
        }

        private static bool IsToolMaterial(string name)
            => name.StartsWith("tools/", StringComparison.OrdinalIgnoreCase);

        private static (List<BspVertex> Vertices, List<int> Indices) CreateSubMesh(BspMaterialMesh source, List<int> triangles)
        {
            var vertices = new List<BspVertex>();
            var indices = new List<int>(triangles.Count * 3);
            var remap = new Dictionary<int, int>();

            foreach (var start in triangles)
            {
                for (var corner = 0; corner < 3; corner++)
                {
                    var original = source.Indices[start + corner];

                    if (!remap.TryGetValue(original, out var mapped))
                    {
                        mapped = vertices.Count;
                        remap.Add(original, mapped);
                        vertices.Add(source.Vertices[original]);
                    }

                    indices.Add(mapped);
                }
            }

            return (vertices, indices);
        }

        private static void AddStaticProps(Scene scene, Scene? skyScene, BspFile bsp, Func<Scene, BspStaticProp, int, Matrix4x4, SceneNode?> createProp, AABB? skyBounds)
        {
            var props = bsp.StaticProps.Props;

            for (var index = 0; index < props.Count; index++)
            {
                var prop = props[index];

                if (prop.ModelName.Length == 0)
                {
                    continue;
                }

                var target = Source1MapLighting.SelectScene(skyBounds, scene, skyScene, prop.Origin);

                if (target == null)
                {
                    continue;
                }

                var scale = prop.Scale > 0f ? prop.Scale : 1f;
                var transform = Matrix4x4.CreateScale(scale) * EntityTransformHelper.ToRigidTransformationMatrix(prop.Angles, prop.Origin);

                var node = createProp(target, prop, index, transform);

                if (node == null)
                {
                    continue;
                }

                if (prop.ColorModulation != 0xffffffff)
                {
                    var color = new Color32(prop.ColorModulation);
                    node.Tint = new Vector3(color.R, color.G, color.B) / 255f;
                }

                target.Add(node, false);
            }
        }

        private static RenderTexture CreateLightmapTexture(LightmapAtlas atlas, int page)
        {
            var texture = new RenderTexture(TextureTarget.Texture2D, atlas.Width, atlas.Height, 1, 1, $"Source1Lightmap{page}");

            GL.TextureStorage2D(texture.Handle, 1, SizedInternalFormat.Rgba16f, atlas.Width, atlas.Height);
            GL.TextureSubImage2D(texture.Handle, 0, 0, 0, atlas.Width, atlas.Height, PixelFormat.Rgba, PixelType.HalfFloat, atlas.Pages[page]);

            texture.SetFiltering(TextureMinFilter.Linear, TextureMagFilter.Linear);
            texture.SetWrapMode(RsTextureAddressMode.Clamp);

            return texture;
        }

        /// <summary>Creates the 2D sky of the <c>skyname</c> of the map from its six <c>materials/skybox/</c> textures.</summary>
        /// <param name="scene">The scene the sky is for.</param>
        /// <param name="fileSystem">Where the sky textures are read from.</param>
        /// <param name="skyName">The sky name, such as <c>sky_day01_01</c>.</param>
        /// <param name="highDynamicRange">
        /// Whether to use the <c>$hdrcompressedtexture</c> of the faces, as the engine does for a map with HDR lighting.
        /// The low dynamic range faces are used when a face has none.
        /// </param>
        /// <returns>The sky, or null when a face is missing or is high dynamic range.</returns>
        public static SceneSkybox2D? CreateSkybox(Scene scene, Source1FileSystem fileSystem, string skyName, bool highDynamicRange)
        {
            ArgumentNullException.ThrowIfNull(scene);
            ArgumentNullException.ThrowIfNull(fileSystem);

            if (skyName.Length == 0)
            {
                return null;
            }

            var skyTextures = new SkyTexture[SkyFaces.Length];

            for (var i = 0; i < SkyFaces.Length; i++)
            {
                if (GetSkyTexture(fileSystem, skyName, SkyFaces[i].Suffix) is not { } skyTexture)
                {
                    scene.RendererContext.Logger.LogWarning("Sky texture of materials/skybox/{Name}{Face}.vmt was not found", skyName, SkyFaces[i].Suffix);
                    return null;
                }

                skyTextures[i] = skyTexture;
                highDynamicRange &= skyTexture.CompressedHdrPath != null;
            }

            var bitmaps = new SKBitmap[SkyFaces.Length];
            var transforms = new Matrix4x4[SkyFaces.Length];

            try
            {
                var size = 0;

                for (var i = 0; i < SkyFaces.Length; i++)
                {
                    var suffix = SkyFaces[i].Suffix;
                    var path = highDynamicRange ? skyTextures[i].CompressedHdrPath! : skyTextures[i].Path;
                    using var stream = fileSystem.OpenFile(path);

                    if (stream == null)
                    {
                        scene.RendererContext.Logger.LogWarning("Sky texture {Path} was not found", path);
                        return null;
                    }

                    var vtf = new VtfFile(stream);

                    if (vtf.IsHighDynamicRange)
                    {
                        scene.RendererContext.Logger.LogWarning("Sky texture {Name}{Face} is high dynamic range", skyName, suffix);
                        return null;
                    }

                    bitmaps[i] = vtf.DecodeMip();
                    transforms[i] = skyTextures[i].Transform;
                    size = Math.Max(size, bitmaps[i].Width);
                }

                var cube = highDynamicRange
                    ? CreateCompressedHdrSkyCube(size, bitmaps, transforms)
                    : CreateSkyCube(size, bitmaps, transforms);
                var material = new RenderMaterial(scene.RendererContext.ShaderLoader.LoadShader("sky.vfx"));
                material.Textures[SkyTextureSamplerName] = cube;

                return new SceneSkybox2D(material);
            }
            finally
            {
                foreach (var bitmap in bitmaps)
                {
                    bitmap?.Dispose();
                }
            }
        }

        // A decal is as large as its base texture in world units, times $decalscale
        private static Vector2? GetDecalSize(Source1FileSystem fileSystem, string name)
        {
            try
            {
                var vmt = VmtFile.Load(Source1Paths.ToMaterialPath(name), fileSystem.OpenFile);
                var baseTexture = vmt.Values.String("$basetexture");

                if (baseTexture == null)
                {
                    return null;
                }

                using var stream = fileSystem.OpenFile(Source1Paths.ToTexturePath(baseTexture));

                if (stream == null)
                {
                    return null;
                }

                var header = VtfFile.ReadHeader(stream);
                var scale = vmt.Values.Float("$decalscale", 1f);

                return new Vector2(header.Width, header.Height) * scale;
            }
            catch (Exception e) when (e is InvalidDataException or IOException)
            {
                return null;
            }
        }

        private static SkyTexture? GetSkyTexture(Source1FileSystem fileSystem, string skyName, string suffix)
        {
            try
            {
                var vmt = VmtFile.Load($"materials/skybox/{skyName}{suffix}.vmt", fileSystem.OpenFile);
                var baseTexture = vmt.Values.String("$basetexture");

                if (baseTexture == null)
                {
                    return null;
                }

                var compressedHdrTexture = vmt.Values.String("$hdrcompressedtexture");
                var compressedHdrPath = compressedHdrTexture == null ? null : Source1Paths.ToTexturePath(compressedHdrTexture);

                return new SkyTexture(Source1Paths.ToTexturePath(baseTexture), compressedHdrPath, vmt.Values.Transform("$basetexturetransform"));
            }
            catch (Exception e) when (e is InvalidDataException or IOException)
            {
                return null;
            }
        }

        /// <summary>Resamples six sky faces into a cube map.</summary>
        /// <param name="size">The width and height of a cube face.</param>
        /// <param name="bitmaps">The faces in the order of their suffixes <c>ft</c>, <c>bk</c>, <c>lf</c>, <c>rt</c>, <c>up</c> and <c>dn</c>.</param>
        /// <param name="texCoordTransforms">The <c>$basetexturetransform</c> of each face, or null to use the whole bitmap for the whole face.</param>
        internal static RenderTexture CreateSkyCube(int size, SKBitmap[] bitmaps, Matrix4x4[]? texCoordTransforms = null)
        {
            var texture = new RenderTexture(TextureTarget.TextureCubeMap, size, size, 1, 1, "Source1Sky");
            GL.TextureStorage2D(texture.Handle, 1, SizedInternalFormat.Srgb8Alpha8, size, size);

            var pixels = new byte[size * size * 4];

            for (var glFace = 0; glFace < 6; glFace++)
            {
                var texels = ResampleCubeFace(glFace, size, bitmaps, texCoordTransforms);

                for (var i = 0; i < texels.Length; i++)
                {
                    var texel = texels[i];
                    var offset = i * 4;

                    pixels[offset] = texel.Blue;
                    pixels[offset + 1] = texel.Green;
                    pixels[offset + 2] = texel.Red;
                    pixels[offset + 3] = 255;
                }

                GL.TextureSubImage3D(texture.Handle, 0, 0, 0, glFace, size, size, 1, PixelFormat.Bgra, PixelType.UnsignedByte, pixels);
            }

            texture.SetFiltering(TextureMinFilter.Linear, TextureMagFilter.Linear);
            texture.SetWrapMode(RsTextureAddressMode.Clamp, RsTextureAddressMode.Clamp, RsTextureAddressMode.Clamp);

            return texture;
        }

        // A compressed HDR sky texel is a linear color times a scale in its alpha, which the sky shader multiplies by 8
        private static RenderTexture CreateCompressedHdrSkyCube(int size, SKBitmap[] bitmaps, Matrix4x4[] texCoordTransforms)
        {
            const float CompressedHdrScale = 8f;

            var texture = new RenderTexture(TextureTarget.TextureCubeMap, size, size, 1, 1, "Source1SkyHdr");
            GL.TextureStorage2D(texture.Handle, 1, SizedInternalFormat.Rgba16f, size, size);

            var pixels = new Half[size * size * 4];

            for (var glFace = 0; glFace < 6; glFace++)
            {
                var texels = ResampleCubeFace(glFace, size, bitmaps, texCoordTransforms);

                for (var i = 0; i < texels.Length; i++)
                {
                    var texel = texels[i];
                    var scale = texel.Alpha / 255f * CompressedHdrScale / 255f;
                    var offset = i * 4;

                    pixels[offset] = (Half)(texel.Red * scale);
                    pixels[offset + 1] = (Half)(texel.Green * scale);
                    pixels[offset + 2] = (Half)(texel.Blue * scale);
                    pixels[offset + 3] = Half.One;
                }

                GL.TextureSubImage3D(texture.Handle, 0, 0, 0, glFace, size, size, 1, PixelFormat.Rgba, PixelType.HalfFloat, pixels);
            }

            texture.SetFiltering(TextureMinFilter.Linear, TextureMagFilter.Linear);
            texture.SetWrapMode(RsTextureAddressMode.Clamp, RsTextureAddressMode.Clamp, RsTextureAddressMode.Clamp);

            return texture;
        }

        // The texel of the sky faces that each texel of a cube face looks at
        private static SKColor[] ResampleCubeFace(int glFace, int size, SKBitmap[] bitmaps, Matrix4x4[]? texCoordTransforms)
        {
            // Every texel of a cube face looks furthest along the face's own axis, so one sky face fills all of it
            var faceIndex = FindSkyFace(GetCubeDirection(glFace, 0f, 0f));
            var face = SkyFaces[faceIndex];
            var bitmap = bitmaps[faceIndex];
            var sourcePixels = bitmap.Pixels;
            var texCoordTransform = texCoordTransforms == null ? Matrix4x4.Identity : texCoordTransforms[faceIndex];
            var texels = new SKColor[size * size];

            for (var y = 0; y < size; y++)
            {
                var v = (2f * (y + 0.5f) / size) - 1f;

                for (var x = 0; x < size; x++)
                {
                    var u = (2f * (x + 0.5f) / size) - 1f;
                    var direction = GetCubeDirection(glFace, u, v);
                    var forward = Vector3.Dot(direction, face.Forward);
                    var s = Vector3.Dot(direction, face.Right) / forward;
                    var t = Vector3.Dot(direction, face.Up) / forward;
                    var texCoord = Vector2.Transform(new Vector2((s + 1f) * 0.5f, (1f - t) * 0.5f), texCoordTransform);
                    var sourceX = Math.Clamp((int)(texCoord.X * bitmap.Width), 0, bitmap.Width - 1);
                    var sourceY = Math.Clamp((int)(texCoord.Y * bitmap.Height), 0, bitmap.Height - 1);

                    texels[(y * size) + x] = sourcePixels[(sourceY * bitmap.Width) + sourceX];
                }
            }

            return texels;
        }

        private static Vector3 GetCubeDirection(int glFace, float u, float v) => glFace switch
        {
            0 => new Vector3(1f, -v, -u),
            1 => new Vector3(-1f, -v, u),
            2 => new Vector3(u, 1f, v),
            3 => new Vector3(u, -1f, -v),
            4 => new Vector3(u, -v, 1f),
            _ => new Vector3(-u, -v, -1f),
        };

        private static int FindSkyFace(Vector3 direction)
        {
            var best = 0;
            var bestDot = float.MinValue;

            for (var i = 0; i < SkyFaces.Length; i++)
            {
                var dot = Vector3.Dot(direction, SkyFaces[i].Forward);

                if (dot > bestDot)
                {
                    bestDot = dot;
                    best = i;
                }
            }

            return best;
        }
    }
}
