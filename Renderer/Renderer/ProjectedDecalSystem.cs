using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer.Buffers;
using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.Renderer.Materials;
using ValveResourceFormat.Renderer.Shaders;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;
using PrimitiveType = OpenTK.Graphics.OpenGL.PrimitiveType;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Decals projected onto whatever the resolved scene depth holds inside their box, such as bullet
    /// impacts. The boxes are binned with the tiled light culling, and one full screen pass walks the
    /// decals reaching each pixel, with every decal material and texture held on the GPU.
    /// </summary>
    public sealed class ProjectedDecalSystem
    {
        /// <summary>The most decals kept at once, one cull batch. Adding past it removes the oldest.</summary>
        public const int MaxDecals = 320;

        // None of these are in the decal data, so they are chosen rather than known
        private const float GrazingIncidenceCosine = 0.5f;
        private const float DefaultDecalDepth = 12f;
        private const float DefaultDecalSize = 8f;
        private const float MinDecalSize = 0.5f;

        private const string KnifeDecalGroup = "ManhackCut";

        private const uint FlagFlipU = 1;
        private const uint FlagHidden = 2;

        // Matches the DECAL_ flags in projected_decals.frag.slang; the low two bits are the blend mode
        [Flags]
        private enum MaterialFlags : uint
        {
            None = 0,
            AlphaCutoff = 4,
            CutoffAngle = 8,
            NormalMap = 16,
            Parallax = 32,
            Specular = 64,
            OcclusionMap = 128,
        }

        // Matches ProjectedDecal_t, 64 bytes
        [StructLayout(LayoutKind.Sequential)]
        private struct DecalGpu
        {
            public OpenTK.Mathematics.Matrix3x4 WorldToDecal;
            public uint MaterialIndex;
            public uint Flags;
            public uint Tint;
            public uint Padding;
        }

        // Matches ProjectedDecalMaterial_t, 128 bytes. Derived parameters are evaluated here so the shader
        // only reads them.
        [StructLayout(LayoutKind.Sequential)]
        private struct MaterialGpu
        {
            public Vector4 ColorScaleLayer;
            public Vector4 NormalScaleLayer;
            public Vector4 OcclusionScaleLayer;
            public Vector4 HeightScaleLayer;
            public Vector4 Fade;
            public Vector4 Parallax;
            public Vector4 Emissive;
            public uint Flags;
            public float Roughness;
            public float Padding0;
            public float Padding1;
        }

        private sealed class DecalMaterial
        {
            public required Material Data { get; init; }
            public required MaterialGpu Parameters { get; init; }
            public required ProjectedDecalTextureArray.Layer Color { get; init; }
            public ProjectedDecalTextureArray.Layer? Normal { get; init; }
            public ProjectedDecalTextureArray.Layer? Occlusion { get; init; }
            public ProjectedDecalTextureArray.Layer? Height { get; init; }
            public bool IsMultiply { get; init; }
        }

        private readonly record struct ProjectedDecal(int MaterialIndex, uint Flags, Vector4 Tint, BaseEntity? Parent, Matrix4x4 LocalTransform);

        // BC7 mode 6 with every endpoint zero, which is transparent black
        private static readonly byte[] Bc7ClearBlock = [0x40, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

        // Occlusion 1 in red, metalness 0 in green
        private static readonly byte[] OcclusionClearBlock = [0xFF, 0xFF, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

        // Height 1, level with the surface
        private static readonly byte[] HeightClearBlock = [0xFF, 0xFF, 0, 0, 0, 0, 0, 0];

        private readonly Scene scene;

        private readonly List<ProjectedDecal> decals = [];
        private readonly List<Matrix4x4> boxTransforms = [];
        private readonly DecalGpu[] decalGpuData = new DecalGpu[MaxDecals];

        private readonly List<DecalMaterial> materials = [];
        private readonly Dictionary<string, int> materialsByPath = new(StringComparer.OrdinalIgnoreCase);

        private readonly ProjectedDecalTextureArray colorArray = new("ProjectedDecalColor", VTexFormat.BC7, ImageFormat.BC7, srgb: true, Bc7ClearBlock);
        private readonly ProjectedDecalTextureArray normalArray = new("ProjectedDecalNormal", VTexFormat.BC7, ImageFormat.BC7, srgb: false, Bc7ClearBlock);
        private readonly ProjectedDecalTextureArray occlusionArray = new("ProjectedDecalOcclusion", VTexFormat.ATI2N, ImageFormat.ATI2N, srgb: false, OcclusionClearBlock);
        private readonly ProjectedDecalTextureArray heightArray = new("ProjectedDecalHeight", VTexFormat.ATI1N, ImageFormat.ATI1N, srgb: false, HeightClearBlock);

        private ImpactDecalTable? impactDecals;

        private StorageBuffer? decalBuffer;
        private StorageBuffer? materialBuffer;
        private int materialBufferCapacity;
        private bool decalsDirty;
        private bool materialsDirty;
        private bool hasMultiplyDecals;
        private int parentedCount;
        private Shader? shader;

        /// <summary>Initializes a decal system for a scene.</summary>
        public ProjectedDecalSystem(Scene scene)
        {
            this.scene = scene;
        }

        /// <summary>Gets the number of decals currently alive.</summary>
        public int Count => decals.Count;

        /// <summary>Gets each decal's box transform, in the order the cull batch and the decal pass index them.</summary>
        internal ReadOnlySpan<Matrix4x4> BoxTransforms => CollectionsMarshal.AsSpan(boxTransforms);

        /// <summary>Adds a decal.</summary>
        /// <param name="materialPath">A <c>csgo_projected_decals</c> material.</param>
        /// <param name="boxTransform">Maps a unit cube centred on the origin to the decal box, see <see cref="CreateBoxTransform"/>.</param>
        /// <param name="tint">Linear color and opacity multiplier.</param>
        /// <param name="flipU">Whether to mirror the texture horizontally.</param>
        /// <param name="parent">An entity the decal moves with, or null for the static world.</param>
        /// <returns>Whether the material could be loaded and the decal was added.</returns>
        public bool Add(string materialPath, Matrix4x4 boxTransform, Vector4 tint, bool flipU = false, BaseEntity? parent = null)
        {
            var materialIndex = GetMaterialIndex(materialPath);

            if (materialIndex < 0)
            {
                return false;
            }

            if (decals.Count >= MaxDecals)
            {
                RemoveDecalAt(0);
            }

            // The world never moves, so a decal on it has nothing to follow
            if (parent is WorldEntity)
            {
                parent = null;
            }

            var localTransform = boxTransform;

            if (parent != null)
            {
                // Traces answer against the collider's tick state, so the hit is local to that frame
                var parentFrame = parent.Collider?.Transform ?? GetRigidTransform(parent);

                if (Matrix4x4.Invert(parentFrame, out var worldToParent))
                {
                    localTransform = boxTransform * worldToParent;
                    parentedCount++;
                }
                else
                {
                    parent = null;
                }
            }

            decals.Add(new ProjectedDecal(materialIndex, flipU ? FlagFlipU : 0, tint, parent, localTransform));
            boxTransforms.Add(boxTransform);
            decalsDirty = true;

            return true;
        }

        /// <summary>
        /// Moves decals with the entities they were added on, hides them while their entity is not drawn,
        /// and drops them once it is removed. Call once a frame, before the cull batch reads <see cref="BoxTransforms"/>.
        /// </summary>
        public void UpdateParentedDecals()
        {
            if (parentedCount == 0)
            {
                return;
            }

            for (var i = decals.Count - 1; i >= 0; i--)
            {
                var decal = decals[i];

                if (decal.Parent is not { } parent)
                {
                    continue;
                }

                if (parent.IsRemoved)
                {
                    RemoveDecalAt(i);
                    continue;
                }

                // The drawn transform rather than the tick state, so the decal moves as smoothly as the entity
                var boxTransform = decal.LocalTransform * GetRigidTransform(parent);
                var flags = parent.IsDrawn ? decal.Flags & ~FlagHidden : decal.Flags | FlagHidden;

                if (boxTransform == boxTransforms[i] && flags == decal.Flags)
                {
                    continue;
                }

                boxTransforms[i] = boxTransform;
                decals[i] = decal with { Flags = flags };

                decalsDirty = true;
            }
        }

        private void RemoveDecalAt(int index)
        {
            if (decals[index].Parent != null)
            {
                parentedCount--;
            }

            decals.RemoveAt(index);
            boxTransforms.RemoveAt(index);
            decalsDirty = true;
        }

        // The drawn transform without the entity's scale, which is the rigid frame its collider moves in
        private static Matrix4x4 GetRigidTransform(BaseEntity entity)
        {
            var scale = entity.EntityScale;

            if (scale.X == 0f || scale.Y == 0f || scale.Z == 0f)
            {
                return entity.Transform;
            }

            return Matrix4x4.CreateScale(Vector3.One / scale) * entity.Transform;
        }

        /// <summary>
        /// Adds the decal a bullet leaves where it hits a surface, picked from the impact decal group of
        /// the surface property.
        /// </summary>
        /// <param name="position">The hit position.</param>
        /// <param name="normal">The surface normal at the hit.</param>
        /// <param name="direction">The direction the bullet travels.</param>
        /// <param name="surfacePropertyHash">The hash of the hit surface property, or zero for the default surface.</param>
        /// <param name="parent">The entity that was hit, which the decal moves with, or null for the static world.</param>
        /// <returns>Whether a decal was added.</returns>
        public bool SpawnImpactDecal(Vector3 position, Vector3 normal, Vector3 direction, uint surfacePropertyHash, BaseEntity? parent = null)
        {
            impactDecals ??= ImpactDecalTable.Load(scene.RendererContext.FileLoader);

            direction = Vector3.Normalize(direction);
            normal = FaceAgainst(normal, direction);

            var isGrazing = -Vector3.Dot(normal, direction) < GrazingIncidenceCosine;
            var groupName = impactDecals.FindDecalGroup(surfacePropertyHash, isGrazing);

            // Grazing marks streak toward the bottom of their texture, so their top faces back along the shot
            var up = isGrazing
                ? -direction
                : RotateAround(normal, GetOrthogonal(normal), Random.Shared.NextSingle() * MathF.Tau);

            return SpawnFromGroup(groupName, position, normal, up, parent);
        }

        /// <summary>
        /// Adds the scuff a knife leaves where it hits a surface. Surfaces that take no bullet decals take
        /// no scuff either.
        /// </summary>
        /// <param name="position">The hit position.</param>
        /// <param name="normal">The surface normal at the hit.</param>
        /// <param name="direction">The direction the knife swings toward.</param>
        /// <param name="surfacePropertyHash">The hash of the hit surface property, or zero for the default surface.</param>
        /// <param name="parent">The entity that was hit, which the decal moves with, or null for the static world.</param>
        /// <returns>Whether a decal was added.</returns>
        public bool SpawnKnifeDecal(Vector3 position, Vector3 normal, Vector3 direction, uint surfacePropertyHash, BaseEntity? parent = null)
        {
            impactDecals ??= ImpactDecalTable.Load(scene.RendererContext.FileLoader);

            if (string.IsNullOrEmpty(impactDecals.FindDecalGroup(surfacePropertyHash, isGrazing: false)))
            {
                return false;
            }

            direction = Vector3.Normalize(direction);
            normal = FaceAgainst(normal, direction);

            // The scuff texture is wider than tall, so keep its width level on walls, and across the view
            // on floors and ceilings, where the top of the texture faces away from the camera
            var up = MathF.Abs(normal.Z) < 0.7f
                ? Vector3.UnitZ
                : direction;

            return SpawnFromGroup(KnifeDecalGroup, position, normal, up, parent);
        }

        private bool SpawnFromGroup(string? groupName, Vector3 position, Vector3 normal, Vector3 up, BaseEntity? parent)
        {
            var random = Random.Shared;
            var materialPath = impactDecals?.PickMaterial(groupName, random);

            if (materialPath == null)
            {
                return false;
            }

            var materialIndex = GetMaterialIndex(materialPath);

            if (materialIndex < 0)
            {
                return false;
            }

            var attributes = materials[materialIndex].Data.FloatAttributes;

            var height = attributes.GetValueOrDefault("DecalWorldHeight", attributes.GetValueOrDefault("DecalWorldWidth", DefaultDecalSize));
            var width = attributes.GetValueOrDefault("DecalWorldWidth", height);
            var depth = attributes.GetValueOrDefault("DecalDepth", DefaultDecalDepth);
            var depthOffset = attributes.GetValueOrDefault("DecalDepthOffset");

            // Variances are in world units: a knife scuff 25 wide varies by 6, a bullet hole 5 wide by 0.5
            var sizeOffset = Vary(attributes.GetValueOrDefault("DecalSizeVariance"));
            width = MathF.Max(width + sizeOffset, MinDecalSize);
            height = MathF.Max(height + sizeOffset + Vary(attributes.GetValueOrDefault("DecalHeightVariance")), MinDecalSize);
            depth = MathF.Max(depth + Vary(attributes.GetValueOrDefault("DecalDepthVariance")), MinDecalSize);

            var transform = CreateBoxTransform(position + normal * depthOffset, normal, up, new Vector3(width, height, depth));

            return Add(materialPath, transform, Vector4.One, flipU: random.Next(2) == 0, parent);

            float Vary(float variance) => (random.NextSingle() * 2f - 1f) * variance;
        }

        /// <summary>
        /// Builds the transform mapping a unit cube onto a decal box. The texture spans the box's X and Y axes
        /// with its top toward <paramref name="up"/>, and projects along Z, which points out of the surface.
        /// </summary>
        /// <param name="center">The box centre.</param>
        /// <param name="normal">The projection axis, pointing out of the surface.</param>
        /// <param name="up">The direction of the texture's top edge, flattened onto the surface.</param>
        /// <param name="size">The width, height and depth of the box.</param>
        public static Matrix4x4 CreateBoxTransform(Vector3 center, Vector3 normal, Vector3 up, Vector3 size)
        {
            var axisZ = Vector3.Normalize(normal);
            var axisY = up - axisZ * Vector3.Dot(up, axisZ);

            axisY = axisY.LengthSquared() > 1e-8f
                ? Vector3.Normalize(axisY)
                : GetOrthogonal(axisZ);

            var axisX = Vector3.Cross(axisY, axisZ);

            axisX *= size.X;
            axisY *= size.Y;
            axisZ *= size.Z;

            return new Matrix4x4(
                axisX.X, axisX.Y, axisX.Z, 0f,
                axisY.X, axisY.Y, axisY.Z, 0f,
                axisZ.X, axisZ.Y, axisZ.Z, 0f,
                center.X, center.Y, center.Z, 1f);
        }

        private static Vector3 GetOrthogonal(Vector3 direction)
        {
            var reference = MathF.Abs(direction.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX;
            return Vector3.Normalize(Vector3.Cross(direction, reference));
        }

        private static Vector3 RotateAround(Vector3 axis, Vector3 vector, float angle)
        {
            return Vector3.Transform(vector, Quaternion.CreateFromAxisAngle(axis, angle));
        }

        private static Vector3 FaceAgainst(Vector3 normal, Vector3 direction)
        {
            normal = Vector3.Normalize(normal);
            return Vector3.Dot(normal, direction) > 0f ? -normal : normal;
        }

        private int GetMaterialIndex(string materialPath)
        {
            if (!materialsByPath.TryGetValue(materialPath, out var index))
            {
                index = LoadMaterial(materialPath);
                materialsByPath[materialPath] = index;
            }

            return index;
        }

        private int LoadMaterial(string materialPath)
        {
            using var resource = scene.RendererContext.FileLoader.LoadFileCompiled(materialPath);

            if (resource?.DataBlock is not Material data)
            {
                return -1;
            }

            var intParams = data.IntParams;
            var floatParams = data.FloatParams;

            if (AddTexture(colorArray, data, "g_tColor") is not { } color)
            {
                return -1;
            }

            var normal = intParams.GetValueOrDefault("F_NORMAL_MAP") == 1 ? AddTexture(normalArray, data, "g_tNormal") : null;
            var occlusion = AddTexture(occlusionArray, data, "g_tAmbientOcclusion");
            var height = intParams.GetValueOrDefault("F_PARALLAX") == 1 ? AddTexture(heightArray, data, "g_tHeight") : null;

            var flags = MaterialFlags.None;

            if (intParams.GetValueOrDefault("F_ALPHA_MODE") == 1)
            {
                flags |= MaterialFlags.AlphaCutoff;
            }

            if (intParams.GetValueOrDefault("F_CUTOFF_ANGLE") == 1)
            {
                flags |= MaterialFlags.CutoffAngle;
            }

            if (intParams.GetValueOrDefault("F_SPECULAR_DIRECT") == 1)
            {
                flags |= MaterialFlags.Specular;
            }

            if (normal != null)
            {
                flags |= MaterialFlags.NormalMap;
            }

            if (occlusion != null)
            {
                flags |= MaterialFlags.OcclusionMap;
            }

            if (height != null)
            {
                flags |= MaterialFlags.Parallax;
            }

            var blendMode = (uint)Math.Clamp(intParams.GetValueOrDefault("F_BLEND_MODE"), 0L, 3L);

            var cutoffAngle = floatParams.GetValueOrDefault("g_flCutoffAngle", 60f);
            var cutoffBias = MathF.Cos(float.DegreesToRadians(MathF.Min(180f, cutoffAngle + floatParams.GetValueOrDefault("g_flCutoffAngleSoftness", 5f))));
            var cutoffRange = MathF.Cos(float.DegreesToRadians(cutoffAngle)) - cutoffBias;

            var emissiveTint = data.VectorParams.GetValueOrDefault("g_vEmissiveTint", Vector4.One).AsVector3();
            var emissiveScale = MathF.Pow(2f, floatParams.GetValueOrDefault("g_flEmissiveBrightness"));

            materials.Add(new DecalMaterial
            {
                Data = data,
                Color = color,
                Normal = normal,
                Occlusion = occlusion,
                Height = height,
                IsMultiply = blendMode is 1 or 3,
                Parameters = new MaterialGpu
                {
                    Fade = new Vector4(
                        cutoffBias,
                        1f / MathF.Max(cutoffRange, 1e-4f),
                        1f / MathF.Max(floatParams.GetValueOrDefault("g_flDecalZAlphaScale", 0.01f), 1e-4f),
                        1f / MathF.Max(floatParams.GetValueOrDefault("g_flAlphaCutoffSoftness", 0.1f), 1e-4f)),
                    Parallax = new Vector4(
                        floatParams.GetValueOrDefault("g_flHeightMapScale", 0.02f),
                        intParams.GetValueOrDefault("g_nMinSamples", 8),
                        intParams.GetValueOrDefault("g_nMaxSamples", 32),
                        intParams.GetValueOrDefault("g_nLODThreshold", 4)),
                    Emissive = new Vector4(
                        ColorSpace.SrgbGammaToLinear(emissiveTint) * emissiveScale,
                        floatParams.GetValueOrDefault("g_flAdditiveAmount", 1f)),
                    Flags = blendMode | (uint)flags,
                    Roughness = MathF.Max(0.01f, 1f - floatParams.GetValueOrDefault("g_flGlossiness", 0.5f)),
                },
            });

            materialsDirty = true;

            return materials.Count - 1;
        }

        private ProjectedDecalTextureArray.Layer? AddTexture(ProjectedDecalTextureArray array, Material data, string parameter)
        {
            if (!data.TextureParams.TryGetValue(parameter, out var path))
            {
                return null;
            }

            var layer = array.Add(scene.RendererContext.FileLoader, path);

            // A texture larger than the rest regrows its array, which changes every material's scale into it
            materialsDirty = true;

            return layer;
        }

        private static Vector4 GetScaleLayer(ProjectedDecalTextureArray.Layer? layer, ProjectedDecalTextureArray array)
        {
            if (layer is not { } found || array.Width == 0 || array.Height == 0)
            {
                return new Vector4(1f, 1f, 0f, 0f);
            }

            return new Vector4((float)found.Width / array.Width, (float)found.Height / array.Height, found.Index, 0f);
        }

        private void UploadBuffers()
        {
            if (materialsDirty)
            {
                materialsDirty = false;

                if (materialBuffer == null || materialBufferCapacity < materials.Count)
                {
                    materialBuffer?.Delete();
                    materialBufferCapacity = Math.Max(16, (int)BitOperations.RoundUpToPowerOf2((uint)materials.Count));
                    materialBuffer = StorageBuffer.Allocate<MaterialGpu>(ReservedBufferSlots.ProjectedDecalMaterials, "ProjectedDecalMaterials", materialBufferCapacity, BufferUsage.Dynamic);
                }

                var materialGpuData = new MaterialGpu[materials.Count];

                for (var i = 0; i < materials.Count; i++)
                {
                    var material = materials[i];
                    var parameters = material.Parameters;

                    parameters.ColorScaleLayer = GetScaleLayer(material.Color, colorArray);
                    parameters.NormalScaleLayer = GetScaleLayer(material.Normal, normalArray);
                    parameters.OcclusionScaleLayer = GetScaleLayer(material.Occlusion, occlusionArray);
                    parameters.HeightScaleLayer = GetScaleLayer(material.Height, heightArray);

                    materialGpuData[i] = parameters;
                }

                materialBuffer.Update<MaterialGpu>(materialGpuData.AsSpan(), 0);
            }

            if (decalsDirty)
            {
                decalsDirty = false;
                hasMultiplyDecals = false;

                decalBuffer ??= StorageBuffer.Allocate<DecalGpu>(ReservedBufferSlots.ProjectedDecals, "ProjectedDecals", MaxDecals, BufferUsage.Dynamic);

                for (var i = 0; i < decals.Count; i++)
                {
                    var decal = decals[i];
                    Matrix4x4.Invert(boxTransforms[i], out var worldToDecal);

                    decalGpuData[i] = new DecalGpu
                    {
                        WorldToDecal = worldToDecal.To3x4(),
                        MaterialIndex = (uint)decal.MaterialIndex,
                        Flags = decal.Flags,
                        Tint = Color32.FromVector4Clamped(decal.Tint).PackedValue,
                    };

                    hasMultiplyDecals |= materials[decal.MaterialIndex].IsMultiply;
                }

                decalBuffer.Update<DecalGpu>(decalGpuData.AsSpan(0, decals.Count), 0);
            }
        }

        private Shader LoadShader()
        {
            var arguments = new Dictionary<string, byte>(scene.RenderAttributes);

            // Decals have no env map binding of their own, and only the probe atlas is bound scene wide
            arguments.Remove("S_SCENE_CUBEMAP_TYPE");

            if (scene.LightingInfo.HasValidLightProbes && scene.LightingInfo.LightProbeType == LightProbeType.ProbeAtlas)
            {
                arguments["D_BAKED_LIGHTING_FROM_PROBE"] = 1;
            }

            return scene.RendererContext.ShaderLoader.LoadShader("projected_decals", arguments);
        }

        /// <summary>
        /// Draws the decals over the opaque scene. Needs the scene depth resolved into the reserved
        /// <c>g_tSceneDepth</c> texture beforehand, see <see cref="Scene.WantsSceneDepth"/>, and this
        /// scene's cull masks bound.
        /// </summary>
        /// <param name="context">The render context of the main scene.</param>
        /// <param name="translucentSurfaces">Draws them over the translucent surfaces in front of the opaque
        /// scene instead, which needs <c>g_tTranslucentSceneDepth</c> filled and the translucent layer drawn.</param>
        public void Render(Scene.RenderContext context, bool translucentSurfaces = false)
        {
            if (decals.Count == 0 || context.ReplacementShader != null)
            {
                return;
            }

            using var _ = new GLDebugGroup(translucentSurfaces ? "Projected Decals (Translucent)" : "Projected Decals");

            // The samplers always need an array of the right kind bound, even one no decal uses
            colorArray.EnsureCreated();
            normalArray.EnsureCreated();
            occlusionArray.EnsureCreated();
            heightArray.EnsureCreated();

            UploadBuffers();

            shader ??= LoadShader();

            var passShader = shader.WithCombo("D_TRANSLUCENT_SCENE_DEPTH", translucentSurfaces ? (byte)1 : (byte)0);
            var binner = scene.LightBinner;

            passShader.Use();
            passShader.SetUniform1("uDecalTileBase", binner.DecalTileBase);
            passShader.SetUniform1("uDecalBinBase", binner.DecalBinBase);
            passShader.SetUniform1("uDecalCullWords", binner.DecalCullWords);
            passShader.SetUniform1("uDecalCount", (uint)binner.DecalSlotCount);

            // The global volume, for pixels no volume contains
            if (scene.ProbeAtlasVolumes is [.., var globalProbe])
            {
                passShader.SetUniform1("uLightProbeIndex", (uint)globalProbe.ShaderIndex);
            }

            foreach (var (slot, _, texture) in context.Textures)
            {
                GL.BindTextureUnit((int)slot, texture.Handle);
            }

            scene.LightingInfo.BindLightmapTextures();

            var textureUnit = RenderMaterial.TextureUnitStart;
            passShader.SetTexture(textureUnit++, "uDecalColor", colorArray.ArrayTexture);
            passShader.SetTexture(textureUnit++, "uDecalNormal", normalArray.ArrayTexture);
            passShader.SetTexture(textureUnit++, "uDecalOcclusion", occlusionArray.ArrayTexture);
            passShader.SetTexture(textureUnit, "uDecalHeight", heightArray.ArrayTexture);

            decalBuffer?.BindBufferBase();
            materialBuffer?.BindBufferBase();

            GL.BindVertexArray(scene.RendererContext.MeshBufferCache.EmptyVAO);

            var renderState = GraphicsContext.RenderState;
            var passState = renderState.CurrentPass;

            passState.Rasterizer.CullMode = RsCullMode.None;
            passState.DepthStencil.DepthTestEnable = false;
            passState.DepthStencil.DepthWriteEnable = false;
            passState.BlendEnable = true;
            passState.ColorWriteMask = RsColorWriteEnableBits.R | RsColorWriteEnableBits.G | RsColorWriteEnableBits.B;

            // The shader writes what to add and how much of the destination to keep
            passState.SetBlend(RsBlendMode.One, RsBlendMode.SrcAlpha);

            using (renderState.Scope(in passState))
            {
                passShader.SetUniform1("uDecalPass", 0u);
                GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            }

            if (!hasMultiplyDecals)
            {
                return;
            }

            // Mod2x and liquid decals multiply per channel, which the pass above cannot express
            passState.SetBlend(RsBlendMode.Zero, RsBlendMode.SrcColor);

            using (renderState.Scope(in passState))
            {
                passShader.SetUniform1("uDecalPass", 1u);
                GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            }
        }

        /// <summary>Removes every decal and releases the materials, textures and buffers they used.</summary>
        public void Clear()
        {
            decals.Clear();
            boxTransforms.Clear();
            materials.Clear();
            materialsByPath.Clear();
            impactDecals = null;

            colorArray.Delete();
            normalArray.Delete();
            occlusionArray.Delete();
            heightArray.Delete();

            decalBuffer?.Delete();
            materialBuffer?.Delete();
            decalBuffer = null;
            materialBuffer = null;
            materialBufferCapacity = 0;

            decalsDirty = false;
            materialsDirty = false;
            hasMultiplyDecals = false;
            parentedCount = 0;
            shader = null;
        }

        /// <summary>Releases every GPU resource, the same as <see cref="Clear"/>.</summary>
        public void Delete() => Clear();
    }
}
