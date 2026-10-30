using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Source1;
using ValveResourceFormat.Source1.Bsp;
using ValveResourceFormat.Source1.Material;
using ValveResourceFormat.Source1.Texture;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Source1
{
    /// <summary>
    /// Turns the sprite entities of a Source 1 map into scene nodes: <c>env_sprite</c>, <c>env_sprite_oriented</c>,
    /// <c>env_glow</c> and <c>env_lightglow</c>, batched into meshes of quads per material and map area.
    /// </summary>
    internal static class Source1SpriteBuilder
    {
        private const string SpriteShaderName = "sprite";
        private const string LightGlowMaterial = "sprites/light_glow02_add_noz";

        // The quads of glows grow with the distance, so their reach cannot be known up front
        private const float BoundsPadding = 256f;

        private const int StartOnFlag = 1;

        // The radius of what has to be in view for a glow to show, when the entity names none
        private const float DefaultProxySize = 2f;

        // The distance at which a glow of a scale of one is as wide as the texture, as the quad is sized per distance
        private const float GlowSizeDistance = 200f;
        private const float LightGlowSizeDistance = 100f;

        private const int FadeNone = 0;
        private const int FadeGlow = 1;
        private const int FadeLightGlow = 2;

        private enum RenderMode
        {
            Normal = 0,
            TransColor = 1,
            TransTexture = 2,
            Glow = 3,
            TransAlpha = 4,
            TransAdd = 5,
            Environmental = 6,
            TransAddFrameBlend = 7,
            TransAlphaAdd = 8,
            WorldGlow = 9,
            None = 10,
        }

        private enum Orientation
        {
            ParallelUpright = 0,
            FacingUpright = 1,
            Parallel = 2,
            Oriented = 3,
            ParallelOriented = 4,
            FacingCamera = 5,
        }

        private readonly record struct SpriteInfo(Vector2 Size, Orientation Orientation, Vector2 Origin);

        private readonly record struct MaterialKey(string Name, RenderMode RenderMode);

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct SpriteVertex
        {
            [VertexAttribute(VertexSlot.Position)] public readonly Vector3 Position;
            [VertexAttribute(VertexSlot.TexCoord)] public readonly Vector2 TexCoord;
            [VertexAttribute(VertexSlot.Color)] public readonly Vector4 Color;
            [VertexAttribute("vSpriteBillboard")] public readonly Vector3 Billboard;
            [VertexAttribute("vSpriteParams")] public readonly Vector3 Params;
            [VertexAttribute("vSpriteFade")] public readonly Vector4 Fade;

            public static readonly VertexInputLayout InputLayout = Source1VertexLayout.Create<SpriteVertex>("vSpriteBillboard", "vSpriteParams", "vSpriteFade");

            public SpriteVertex(Vector3 position, Vector2 texCoord, Vector4 color, Vector3 billboard, Vector3 parameters, Vector4 fade)
            {
                Position = position;
                TexCoord = texCoord;
                Color = color;
                Billboard = billboard;
                Params = parameters;
                Fade = fade;
            }
        }

        private sealed class Builder
        {
            private readonly Scene scene;
            private readonly Scene? skyScene;
            private readonly BspFile bsp;
            private readonly Func<string, RenderMaterial> resolveMaterial;
            private readonly AABB? skyBounds;
            private readonly Dictionary<string, SpriteInfo?> sprites = [];
            private readonly Dictionary<MaterialKey, RenderMaterial> materials = [];
            private readonly Source1MeshBatchSet<SpriteVertex> batches = new();

            public Builder(Scene scene, Scene? skyScene, BspFile bsp, Func<string, RenderMaterial> resolveMaterial, AABB? skyBounds)
            {
                this.scene = scene;
                this.skyScene = skyScene;
                this.bsp = bsp;
                this.resolveMaterial = resolveMaterial;
                this.skyBounds = skyBounds;
            }

            public void AddEntities()
            {
                foreach (var entity in bsp.Entities)
                {
                    switch (entity.ClassName)
                    {
                        case "env_sprite":
                        case "env_glow":
                            AddSprite(entity, isOrientedClass: false);
                            break;
                        case "env_sprite_oriented":
                            AddSprite(entity, isOrientedClass: true);
                            break;
                        case "env_lightglow":
                            AddLightGlow(entity);
                            break;
                    }
                }
            }

            public void AddBatches()
            {
                batches.AddTo($"{bsp.Name}/sprites", SpriteVertex.InputLayout);
            }

            private void AddSprite(BspEntity entity, bool isOrientedClass)
            {
                var materialName = entity.GetString("model");

                if (!materialName.EndsWith(".vmt", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                // A named sprite only shows from the start when it is told to
                if (Source1MapSceneBuilder.IsHiddenAtSpawn(entity)
                    || (entity.TargetName.Length > 0 && (entity.GetInt("spawnflags") & StartOnFlag) == 0))
                {
                    return;
                }

                var target = Source1MapLighting.SelectScene(skyBounds, scene, skyScene, entity.Origin);

                if (target == null || GetSprite(materialName) is not { } sprite)
                {
                    return;
                }

                var renderMode = (RenderMode)entity.GetInt("rendermode");

                if (GetMaterial(materialName, renderMode) is not { } material)
                {
                    return;
                }

                var angles = GetSpriteAngles(entity, isOrientedClass);
                var orientation = sprite.Orientation;

                // A sprite with a roll turns in the view plane
                if (angles.Z != 0f && orientation == Orientation.Parallel)
                {
                    orientation = Orientation.ParallelOriented;
                }

                var scale = entity.GetFloat("scale", 1f);

                if (scale <= 0f)
                {
                    scale = 1f;
                }

                var hasColor = renderMode != RenderMode.Normal;
                var color = hasColor ? GetColor(entity) : Vector4.One;
                var distanceScale = renderMode == RenderMode.Glow ? 1f / GlowSizeDistance : 0f;
                var isGlow = renderMode is RenderMode.Glow or RenderMode.WorldGlow;
                var parameters = new Vector3(float.DegreesToRadians(angles.Z), distanceScale, isGlow ? FadeGlow : FadeNone);
                var fade = new Vector4(Vector3.Zero, isGlow ? entity.GetFloat("GlowProxySize", DefaultProxySize) : 0f);

                AddQuad(target, material, entity.Origin, sprite, orientation, angles, scale, color, parameters, fade);
            }

            private void AddLightGlow(BspEntity entity)
            {
                var target = Source1MapLighting.SelectScene(skyBounds, scene, skyScene, entity.Origin);
                var width = entity.GetFloat("HorizontalGlowSize");
                var height = entity.GetFloat("VerticalGlowSize");

                if (target == null || width <= 0f || height <= 0f || GetMaterial(LightGlowMaterial, RenderMode.Glow) is not { } material)
                {
                    return;
                }

                var color = GetColor(entity);
                var fade = new Vector4(
                    entity.GetFloat("MinDist"),
                    entity.GetFloat("MaxDist"),
                    entity.GetFloat("OuterMaxDist"),
                    entity.GetFloat("GlowProxySize", DefaultProxySize));
                var parameters = new Vector3(0f, 1f / LightGlowSizeDistance, FadeLightGlow);

                // The glow is sized by half widths, centered on the light, as a sprite of the same size would
                var sprite = new SpriteInfo(new Vector2(width, height) * 2f, Orientation.FacingCamera, new Vector2(0.5f));

                AddQuad(target, material, entity.Origin, sprite, sprite.Orientation, Vector3.Zero, 1f, color, parameters, fade);
            }

            // The color and the opacity of a sprite, as the vertex color of its quad. The colors are gamma space values.
            private static Vector4 GetColor(BspEntity entity)
            {
                var color = new Vector3(1f);

                if (entity.Properties.ContainsKey("rendercolor"))
                {
                    color = Vector3.Clamp(entity.GetVector3("rendercolor") / 255f, Vector3.Zero, Vector3.One);
                }

                var opacity = Math.Clamp(entity.GetInt("renderamt", 255), 0, 255) / 255f;
                var hdrScale = entity.GetFloat("HDRColorScale", 1f);

                return new Vector4(ColorSpace.SrgbGammaToLinear(color) * hdrScale, opacity);
            }

            private void AddQuad(Scene target, RenderMaterial material, Vector3 origin, SpriteInfo sprite, Orientation orientation, Vector3 angles, float scale, Vector4 color, Vector3 parameters, Vector4 fade)
            {
                // The corner offsets of the sprite from its origin, which is at the center unless the material says otherwise
                var left = -sprite.Size.X * sprite.Origin.X * scale;
                var right = left + (sprite.Size.X * scale);
                var up = sprite.Size.Y * sprite.Origin.Y * scale;
                var down = up - (sprite.Size.Y * scale);

                // The texture is sampled half a texel in from the edge
                var minU = 0.5f / sprite.Size.X;
                var minV = 0.5f / sprite.Size.Y;

                var corners = new (float X, float Y, Vector2 TexCoord)[]
                {
                    (left, down, new Vector2(minU, 1f - minV)),
                    (left, up, new Vector2(minU, minV)),
                    (right, up, new Vector2(1f - minU, minV)),
                    (right, down, new Vector2(1f - minU, 1f - minV)),
                };

                var batch = batches.Get(target, material, origin);
                var reach = MathUtils.Max(MathF.Abs(left), MathF.Abs(right), MathF.Abs(up), MathF.Abs(down)) + BoundsPadding;
                var fixedAxes = orientation == Orientation.Oriented ? new BspDetailProps.Basis(angles) : default;
                var indices = new int[corners.Length];

                for (var i = 0; i < corners.Length; i++)
                {
                    var corner = corners[i];
                    var position = origin;
                    var offset = new Vector3(corner.X, corner.Y, (float)orientation);

                    // An oriented sprite is placed in the world, the others are turned toward the view by the shader
                    if (orientation == Orientation.Oriented)
                    {
                        position += (fixedAxes.Right * corner.X) + (fixedAxes.Up * corner.Y);
                        offset = new Vector3(0f, 0f, (float)orientation);
                    }

                    var vertex = new SpriteVertex(position, corner.TexCoord, color, offset, parameters, fade);
                    indices[i] = batch.AddVertex(vertex, position, reach);
                }

                batch.AddQuad(indices[0], indices[1], indices[2], indices[3]);
            }

            // A sprite turns its yaw into a roll, and the oriented kind faces away from the way it points
            private static Vector3 GetSpriteAngles(BspEntity entity, bool isOrientedClass)
            {
                var angles = entity.Angles;

                if (isOrientedClass)
                {
                    angles.Y = (angles.Y + 180f) % 360f;
                    return angles;
                }

                if (angles.Y != 0f && angles.Z == 0f)
                {
                    angles.Z = angles.Y;
                    angles.Y = 0f;
                }

                return angles;
            }

            private SpriteInfo? GetSprite(string materialName)
            {
                var path = Source1Paths.ToMaterialPath(materialName);

                if (sprites.TryGetValue(path, out var cached))
                {
                    return cached;
                }

                var info = LoadSprite(path);
                sprites.Add(path, info);

                return info;
            }

            private SpriteInfo? LoadSprite(string path)
            {
                var fileSystem = scene.RendererContext.Source1FileSystem;

                if (fileSystem == null)
                {
                    return null;
                }

                try
                {
                    var vmt = VmtFile.Load(path, fileSystem.OpenFile);
                    var baseTexture = vmt.Values.String("$basetexture");

                    if (string.IsNullOrWhiteSpace(baseTexture))
                    {
                        return null;
                    }

                    using var stream = fileSystem.OpenFile(Source1Paths.ToTexturePath(baseTexture));

                    if (stream == null)
                    {
                        return null;
                    }

                    var header = VtfFile.ReadHeader(stream);
                    var origin = vmt.Values.Pair("$spriteorigin", new Vector2(0.5f));
                    var orientation = ParseOrientation(vmt.Values.String("$spriteorientation"));

                    return new SpriteInfo(new Vector2(header.Width, header.Height), orientation, origin);
                }
                catch (Exception e) when (e is InvalidDataException or IOException)
                {
                    return null;
                }
            }

            private static Orientation ParseOrientation(string? name)
            {
                return name?.ToLowerInvariant() switch
                {
                    "facing_upright" => Orientation.FacingUpright,
                    "vp_parallel" => Orientation.Parallel,
                    "oriented" => Orientation.Oriented,
                    "vp_parallel_oriented" => Orientation.ParallelOriented,
                    _ => Orientation.ParallelUpright,
                };
            }

            // Each render mode blends differently, so a material has an instance per mode
            private RenderMaterial? GetMaterial(string materialName, RenderMode renderMode)
            {
                var key = new MaterialKey(Source1Paths.ToMaterialPath(materialName), renderMode);

                if (materials.TryGetValue(key, out var cached))
                {
                    return cached;
                }

                var material = CreateMaterial(resolveMaterial(materialName), renderMode);

                if (material != null)
                {
                    materials.Add(key, material);
                }

                return material;
            }

            private RenderMaterial? CreateMaterial(RenderMaterial source, RenderMode renderMode)
            {
                if (!source.Material.TextureParams.ContainsKey("g_tColor"))
                {
                    return null;
                }

                var isAdditive = renderMode is RenderMode.Glow or RenderMode.WorldGlow or RenderMode.TransAdd or RenderMode.TransAddFrameBlend;
                var isBlended = isAdditive || renderMode is RenderMode.TransColor or RenderMode.TransTexture or RenderMode.TransAlpha or RenderMode.TransAlphaAdd;

                return Source1MeshBatch.CreateMaterial(scene.RendererContext.MaterialLoader, source, SpriteShaderName, material =>
                {
                    material.IntParams["F_TRANSLUCENT"] = isBlended ? 1 : 0;
                    material.IntParams["F_ADDITIVE_BLEND"] = isAdditive ? 1 : 0;
                    material.IntParams["F_ALPHA_TEST"] = 0;

                    // A glow is checked for being in view by the shader, instead of being depth tested
                    material.IntParams["F_DISABLE_Z_BUFFERING"] = renderMode is RenderMode.Glow or RenderMode.WorldGlow ? 1 : 0;
                });
            }
        }

        /// <summary>Adds the sprite entities of a map to the scene.</summary>
        /// <param name="scene">The scene to add the nodes to.</param>
        /// <param name="skyScene">The scene of the 3D sky box, or null to leave out the sprites of that area.</param>
        /// <param name="bsp">The map.</param>
        /// <param name="resolveMaterial">Loads the material of a material name such as <c>sprites/glow01</c>.</param>
        /// <param name="skyBounds">The area of the 3D sky box, or null.</param>
        public static void Build(Scene scene, Scene? skyScene, BspFile bsp, Func<string, RenderMaterial> resolveMaterial, AABB? skyBounds)
        {
            ArgumentNullException.ThrowIfNull(scene);
            ArgumentNullException.ThrowIfNull(bsp);
            ArgumentNullException.ThrowIfNull(resolveMaterial);

            var builder = new Builder(scene, skyScene, bsp, resolveMaterial, skyBounds);
            builder.AddEntities();
            builder.AddBatches();
        }
    }
}
