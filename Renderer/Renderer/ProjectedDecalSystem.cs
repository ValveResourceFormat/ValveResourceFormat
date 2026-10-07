using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
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
        /// <summary>The most decals kept at once, one cull batch. Adding past it removes the oldest that is not permanent.</summary>
        public const int MaxDecals = 320;

        private const float GrazingIncidenceCutoff = 0.55f;
        private const float GrazingIncidenceVariance = 0.1f;

        // None of these are in the decal data, so they are chosen rather than known
        private const float MinDecalSize = 0.5f;

        private const string KnifeDecalGroup = "ManhackCut";

        private const uint FlagFlipU = 1 << 0;
        private const uint FlagHidden = 1 << 1;

        // Matches the feature flags in projected_decals.frag.slang, which go by the material's own names
        // for them. The low two bits are the blend mode.
        [Flags]
        private enum MaterialFlags : uint
        {
            None = 0,
            AlphaCutoff = 1 << 2,
            CutoffAngle = 1 << 3,
            NormalMap = 1 << 4,
            Parallax = 1 << 5,
            Specular = 1 << 6,
            OcclusionMap = 1 << 7,
            Triplanar = 1 << 8,
            BloodAging = 1 << 9,
        }

        private const string TriplanarFeature = "F_TRIPLANAR_MAPPING";

        // The material features that are no more than a switch in the shader
        private static readonly (string Feature, MaterialFlags Flag)[] FeatureFlags =
        [
            ("F_ALPHA_MODE", MaterialFlags.AlphaCutoff),
            (TriplanarFeature, MaterialFlags.Triplanar),
            ("F_SPECULAR_DIRECT", MaterialFlags.Specular),
            ("F_SPECULAR", MaterialFlags.Specular),
            ("F_BLOOD_AGING", MaterialFlags.BloodAging),
        ];

        // Matches ProjectedDecal_t, 64 bytes
        [StructLayout(LayoutKind.Sequential)]
        private struct DecalGpu
        {
            public OpenTK.Mathematics.Matrix3x4 WorldToDecal;
            public uint MaterialIndex;
            public uint Flags;
            public uint Tint;
            public float PlaceTime;
        }

        // Matches DecalTexture_t, 16 bytes. Where a decal finds one of its textures in the array of its kind.
        [StructLayout(LayoutKind.Sequential)]
        private struct TextureGpu
        {
            // How much of the array layer the decal's part of the texture covers
            public Vector2 Scale;
            // The layer in the low 16 bits, then the last mip level the texture has in four
            public uint Layer;
            // Where that part starts in the layer, in texels: x in the low 16 bits, y in the high
            public uint Offset;
        }

        // Matches ProjectedDecalMaterialData_t, 128 bytes. Derived parameters are evaluated here so the shader
        // only reads them.
        [StructLayout(LayoutKind.Sequential)]
        private struct MaterialGpu
        {
            public TextureGpu Color;
            public TextureGpu Normal;
            public TextureGpu Occlusion;
            public TextureGpu Height;
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
            public required ProjectedDecalDefinition Definition { get; init; }
            public required MaterialGpu Parameters { get; init; }
            public required ProjectedDecalTextureArray.Layer Color { get; init; }
            public ProjectedDecalTextureArray.Layer? Normal { get; init; }
            public ProjectedDecalTextureArray.Layer? Occlusion { get; init; }
            public ProjectedDecalTextureArray.Layer? Height { get; init; }
        }

        private readonly record struct ProjectedDecal(int MaterialIndex, uint Flags, Vector4 Tint, BaseEntity? Parent, Matrix4x4 LocalTransform, float PlaceTime, bool IsPermanent);

        // What a permanent decal gives as the time it was added: long enough ago that anything aging has settled
        private const float PermanentPlaceTime = -1e9f;

        private readonly Scene scene;

        private readonly List<ProjectedDecal> decals = [];
        private readonly List<Matrix4x4> boxTransforms = [];
        private readonly DecalGpu[] decalGpuData = new DecalGpu[MaxDecals];

        private readonly List<DecalMaterial> materials = [];
        private readonly Dictionary<string, int> materialsByPath = new(StringComparer.OrdinalIgnoreCase);

        // BC7 mode 6 with every endpoint zero, which is transparent black
        private static readonly byte[] Bc7ClearBlock = [0x40, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

        // Occlusion 1 in red, metalness 0 in green
        private static readonly byte[] OcclusionClearBlock = [0xFF, 0xFF, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

        // Height 1, level with the surface
        private static readonly byte[] HeightClearBlock = [0xFF, 0xFF, 0, 0, 0, 0, 0, 0];

        private readonly ProjectedDecalTextureArray colorArray = new("ProjectedDecalColor", VTexFormat.BC7, ImageFormat.BC7, srgb: true, Bc7ClearBlock);
        private readonly ProjectedDecalTextureArray normalArray = new("ProjectedDecalNormal", VTexFormat.BC7, ImageFormat.BC7, srgb: false, Bc7ClearBlock);
        private readonly ProjectedDecalTextureArray occlusionArray = new("ProjectedDecalOcclusion", VTexFormat.ATI2N, ImageFormat.ATI2N, srgb: false, OcclusionClearBlock);
        private readonly ProjectedDecalTextureArray heightArray = new("ProjectedDecalHeight", VTexFormat.ATI1N, ImageFormat.ATI1N, srgb: false, HeightClearBlock);

        private ImpactDecalTable? impactDecals;

        private StorageBuffer? decalBuffer;
        private StorageBuffer? materialBuffer;
        private int materialBufferCapacity;
        private bool decalsDirty;
        private bool tintsDirty;
        private float time;
        private bool materialsDirty;
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

        /// <summary>
        /// Registers a kind of decal. Its textures are loaded here, and join every other decal's in the
        /// one draw, whether they are files of their own or parts of an atlas. The color and normal
        /// textures have to be BC7, the occlusion texture ATI2N and the height texture ATI1N.
        /// </summary>
        /// <param name="definition">What the decal looks like.</param>
        /// <returns>A handle to add decals with, or -1 when the color texture could not be loaded.</returns>
        public int Register(ProjectedDecalDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(definition);

            if (AddTexture(colorArray, definition.ColorTexture) is not { } color)
            {
                return -1;
            }

            var normal = AddTexture(normalArray, definition.NormalTexture);
            var occlusion = AddTexture(occlusionArray, definition.OcclusionTexture);
            var height = AddTexture(heightArray, definition.HeightTexture);

            var flags = MaterialFlags.None;

            foreach (var (feature, flag) in FeatureFlags)
            {
                if (definition.Features.Contains(feature))
                {
                    flags |= flag;
                }
            }

            if (definition.CutoffAngle != null)
            {
                flags |= MaterialFlags.CutoffAngle;
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

            var cutoffAngle = definition.CutoffAngle ?? 60f;
            var cutoffBias = MathF.Cos(float.DegreesToRadians(MathF.Min(180f, cutoffAngle + definition.CutoffAngleSoftness)));
            var cutoffRange = MathF.Cos(float.DegreesToRadians(cutoffAngle)) - cutoffBias;

            materials.Add(new DecalMaterial
            {
                Definition = definition,
                Color = color,
                Normal = normal,
                Occlusion = occlusion,
                Height = height,
                Parameters = new MaterialGpu
                {
                    Fade = new Vector4(
                        cutoffBias,
                        1f / MathF.Max(cutoffRange, 1e-4f),
                        1f / MathF.Max(definition.DepthFade, 1e-4f),
                        1f / MathF.Max(definition.AlphaCutoffSoftness, 1e-4f)),
                    Parallax = new Vector4(
                        definition.HeightScale,
                        definition.ParallaxMinSamples,
                        definition.ParallaxMaxSamples,
                        definition.ParallaxLodThreshold),
                    Emissive = new Vector4(definition.EmissiveColor, definition.AdditiveAmount),
                    Flags = (uint)definition.BlendMode | (uint)flags,
                    Roughness = definition.Roughness,
                },
            });

            materialsDirty = true;

            return materials.Count - 1;
        }

        /// <summary>
        /// Registers the decal a projected decal material describes, or finds it if it was registered before.
        /// </summary>
        /// <param name="materialPath">The material.</param>
        /// <param name="sequenceName">A sequence of the material's sprite sheet, for a material that holds many decals in one.</param>
        /// <returns>A handle to add decals with, or -1 when the material could not be loaded.</returns>
        public int RegisterMaterial(string materialPath, string? sequenceName = null)
        {
            var key = string.IsNullOrEmpty(sequenceName) ? materialPath : $"{materialPath}:{sequenceName}";

            if (materialsByPath.TryGetValue(key, out var index))
            {
                return index;
            }

            var definition = ProjectedDecalDefinition.FromMaterial(scene.RendererContext.FileLoader, materialPath, sequenceName);
            index = definition == null ? -1 : Register(definition);

            if (index < 0)
            {
                scene.RendererContext.Logger.LogWarning("Projected decal {Decal} could not be loaded", key);
            }

            materialsByPath[key] = index;

            return index;
        }

        /// <summary>Adds a decal.</summary>
        /// <param name="materialPath">A projected decal material.</param>
        /// <param name="boxTransform">Maps a unit cube centred on the origin to the decal box, see <see cref="CreateBoxTransform"/>.</param>
        /// <param name="tint">Linear color and opacity multiplier.</param>
        /// <param name="flipU">Whether to mirror the texture horizontally.</param>
        /// <param name="parent">An entity the decal moves with, or null for the static world.</param>
        /// <param name="permanent">Whether the decal neither fades nor makes way for newer ones, as one placed with a map.</param>
        /// <returns>Whether the material could be loaded and the decal was added.</returns>
        public bool Add(string materialPath, Matrix4x4 boxTransform, Vector4 tint, bool flipU = false, BaseEntity? parent = null, bool permanent = false)
            => Add(RegisterMaterial(materialPath), boxTransform, tint, flipU, parent, permanent);

        /// <summary>Adds a decal.</summary>
        /// <param name="decal">A handle from <see cref="Register"/> or <see cref="RegisterMaterial"/>.</param>
        /// <param name="boxTransform">Maps a unit cube centred on the origin to the decal box, see <see cref="CreateBoxTransform"/>.</param>
        /// <param name="tint">Linear color and opacity multiplier.</param>
        /// <param name="flipU">Whether to mirror the texture horizontally.</param>
        /// <param name="parent">An entity the decal moves with, or null for the static world.</param>
        /// <param name="permanent">Whether the decal neither fades nor makes way for newer ones, as one placed with a map.</param>
        /// <returns>Whether the handle was valid and there was room for the decal.</returns>
        public bool Add(int decal, Matrix4x4 boxTransform, Vector4 tint, bool flipU = false, BaseEntity? parent = null, bool permanent = false)
        {
            var materialIndex = decal;

            if ((uint)materialIndex >= (uint)materials.Count)
            {
                return false;
            }

            if (decals.Count >= MaxDecals)
            {
                var oldest = decals.FindIndex(static existing => !existing.IsPermanent);

                if (oldest < 0)
                {
                    return false;
                }

                RemoveDecalAt(oldest);
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

            decals.Add(new ProjectedDecal(materialIndex, flipU ? FlagFlipU : 0, tint, parent, localTransform,
                permanent ? PermanentPlaceTime : time, permanent));
            boxTransforms.Add(boxTransform);
            decalsDirty = true;

            return true;
        }

        /// <summary>
        /// Fades decals out as they age and drops them once gone. Moves decals with the entities they were
        /// added on, hides them while their entity is not drawn, and drops them once it is removed.
        /// Call once a frame, before the cull batch reads <see cref="BoxTransforms"/>.
        /// </summary>
        /// <param name="currentTime">The time the view constants carry this frame, which the decals age against.</param>
        public void Update(float currentTime)
        {
            time = currentTime;

            for (var i = decals.Count - 1; i >= 0; i--)
            {
                var decal = decals[i];
                var material = materials[decal.MaterialIndex].Definition;

                if (!decal.IsPermanent && time - decal.PlaceTime >= material.FadeStartTime + material.FadeDuration)
                {
                    RemoveDecalAt(i);
                    continue;
                }

                // A removal already has every decal written again
                if (!decalsDirty)
                {
                    var tint = GetFadedTint(decal);

                    if (decalGpuData[i].Tint != tint)
                    {
                        decalGpuData[i].Tint = tint;
                        tintsDirty = true;
                    }
                }

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

        // Fading is the opacity of the decal itself, which alpha cutoff materials read as how far they have eroded
        private uint GetFadedTint(in ProjectedDecal decal)
        {
            var material = materials[decal.MaterialIndex].Definition;
            var fadeTime = time - decal.PlaceTime - material.FadeStartTime;
            var tint = decal.Tint;

            if (fadeTime > 0f && !decal.IsPermanent)
            {
                tint.W *= 1f - Math.Clamp(fadeTime / MathF.Max(material.FadeDuration, 1e-4f), 0f, 1f);
            }

            return Color32.FromVector4Clamped(tint).PackedValue;
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

            // Jittered per shot, so hits near the cutoff leave either mark
            var grazingCutoff = GrazingIncidenceCutoff + (Random.Shared.NextSingle() * 2f - 1f) * GrazingIncidenceVariance;
            var isGrazing = -Vector3.Dot(normal, direction) < grazingCutoff;
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

        /// <summary>Adds a decal picked from a decal group, turned at random on the surface.</summary>
        /// <param name="groupName">A group in <c>scripts/decalgroups.vdata</c> or <c>scripts/decals_subrect.txt</c>.</param>
        /// <param name="position">The position on the surface.</param>
        /// <param name="normal">The surface normal.</param>
        /// <param name="parent">The entity the surface belongs to, which the decal moves with, or null for the static world.</param>
        /// <param name="sizeOverride">The width and height of the decal, or zero for the size its material gives.</param>
        /// <returns>Whether a decal was added.</returns>
        public bool SpawnGroupDecal(string groupName, Vector3 position, Vector3 normal, BaseEntity? parent = null, float sizeOverride = 0f)
        {
            impactDecals ??= ImpactDecalTable.Load(scene.RendererContext.FileLoader);

            normal = Vector3.Normalize(normal);
            var up = RotateAround(normal, GetOrthogonal(normal), Random.Shared.NextSingle() * MathF.Tau);

            return SpawnFromGroup(groupName, position, normal, up, parent, sizeOverride);
        }

        private bool SpawnFromGroup(string? groupName, Vector3 position, Vector3 normal, Vector3 up, BaseEntity? parent, float sizeOverride = 0f)
        {
            if (impactDecals?.PickOption(groupName, Random.Shared) is not { } option)
            {
                return false;
            }

            return Spawn(RegisterMaterial(option.Material, option.Sequence), position, normal, up, parent, sizeOverride);
        }

        /// <summary>
        /// Adds a decal on a surface, at the size its definition gives, varied as much as the definition allows
        /// and mirrored at random.
        /// </summary>
        /// <param name="decal">A handle from <see cref="Register"/> or <see cref="RegisterMaterial"/>.</param>
        /// <param name="position">The position on the surface.</param>
        /// <param name="normal">The surface normal.</param>
        /// <param name="up">The direction of the texture's top edge, flattened onto the surface.</param>
        /// <param name="parent">The entity the surface belongs to, which the decal moves with, or null for the static world.</param>
        /// <param name="sizeOverride">The width and height of the decal, or zero for the size its definition gives.</param>
        /// <returns>Whether the handle was valid and the decal was added.</returns>
        public bool Spawn(int decal, Vector3 position, Vector3 normal, Vector3 up, BaseEntity? parent = null, float sizeOverride = 0f)
        {
            if ((uint)decal >= (uint)materials.Count)
            {
                return false;
            }

            var random = Random.Shared;
            var definition = materials[decal].Definition;

            var width = definition.Width;
            var height = definition.Height;
            var depth = definition.Depth;

            if (sizeOverride > 0f)
            {
                width = height = sizeOverride;

                // A triplanar decal wraps around what is inside its box, so its depth follows its size
                if (definition.Features.Contains(TriplanarFeature))
                {
                    depth = sizeOverride;
                }
            }

            // Variances are in world units: a knife scuff 25 wide varies by 6, a bullet hole 5 wide by 0.5
            var sizeOffset = Vary(definition.SizeVariance);
            width = MathF.Max(width + sizeOffset, MinDecalSize);
            height = MathF.Max(height + sizeOffset + Vary(definition.HeightVariance), MinDecalSize);
            depth = MathF.Max(depth + Vary(definition.DepthVariance), MinDecalSize);

            normal = Vector3.Normalize(normal);
            var transform = CreateBoxTransform(position + normal * definition.DepthOffset, normal, up, new Vector3(width, height, depth));

            return Add(decal, transform, Vector4.One, flipU: random.Next(2) == 0, parent);

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

        private ProjectedDecalTextureArray.Layer? AddTexture(ProjectedDecalTextureArray array, string? path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            var layer = array.Add(scene.RendererContext.FileLoader, path);

            if (layer == null)
            {
                scene.RendererContext.Logger.LogWarning("Projected decal texture {Path} was skipped, it is missing or not {Format}",
                    path, array.Format);
            }

            // A texture larger than the rest regrows its array, which changes every material's scale into it
            materialsDirty = true;

            return layer;
        }

        private static TextureGpu GetTextureGpu(ProjectedDecalTextureArray.Layer? layer, ProjectedDecalTextureArray array, Vector4 rect)
        {
            if (layer is not { } found || array.Width == 0 || array.Height == 0)
            {
                return new TextureGpu { Scale = Vector2.One };
            }

            // The texture sits in the corner of its layer, and the decal may use only a part of the texture.
            // That part starts on a whole texel, which sixteen bits hold exactly for any size an array can be.
            var offsetX = (uint)Math.Clamp(MathF.Round(rect.X * found.Width), 0f, ushort.MaxValue);
            var offsetY = (uint)Math.Clamp(MathF.Round(rect.Y * found.Height), 0f, ushort.MaxValue);
            var lastMip = (uint)Math.Clamp(found.MipCount - 1, 0, 15);

            return new TextureGpu
            {
                Scale = new Vector2((rect.Z - rect.X) * found.Width / array.Width, (rect.W - rect.Y) * found.Height / array.Height),
                Layer = (uint)found.Index | (lastMip << 16),
                Offset = offsetX | (offsetY << 16),
            };
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

                    var rect = material.Definition.TextureRect;

                    parameters.Color = GetTextureGpu(material.Color, colorArray, rect);
                    parameters.Normal = GetTextureGpu(material.Normal, normalArray, rect);
                    parameters.Occlusion = GetTextureGpu(material.Occlusion, occlusionArray, rect);
                    parameters.Height = GetTextureGpu(material.Height, heightArray, rect);

                    materialGpuData[i] = parameters;
                }

                materialBuffer.Update<MaterialGpu>(materialGpuData.AsSpan(), 0);
            }

            if (decalsDirty)
            {
                decalsDirty = false;
                tintsDirty = false;

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
                        Tint = GetFadedTint(decal),
                        PlaceTime = decal.PlaceTime,
                    };
                }

                decalBuffer.Update<DecalGpu>(decalGpuData.AsSpan(0, decals.Count), 0);
            }
            else if (tintsDirty && decalBuffer != null)
            {
                tintsDirty = false;
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
        /// <c>g_tSceneDepth</c> texture beforehand, see <see cref="SceneViewState.WantsSceneDepth"/>, and this
        /// scene's cull masks bound.
        /// </summary>
        /// <param name="context">The render context of the main scene.</param>
        /// <param name="translucentSurfaces">Draws them over the translucent surfaces in front of the opaque
        /// scene instead, which needs <c>g_tTranslucentSceneDepth</c> filled and the translucent layer drawn.</param>
        public void Render(Scene.RenderContext context, bool translucentSurfaces = false)
        {
            if (decals.Count > 0)
            {
                Draw(context, translucentSurfaces);
            }
        }

        /// <summary>
        /// Loads the decal tables and draws both decal passes with no decals in them, so that the first
        /// decal waits for neither a shader to compile nor the driver to specialize it. Does nothing in
        /// a game without decal groups.
        /// </summary>
        /// <param name="context">The render context of the main scene, during the prewarm frame.</param>
        public void Prewarm(Scene.RenderContext context)
        {
            impactDecals ??= ImpactDecalTable.Load(scene.RendererContext.FileLoader);

            if (!impactDecals.HasDecalGroups)
            {
                return;
            }

            Draw(context, translucentSurfaces: false);
            Draw(context, translucentSurfaces: true);
        }

        private void Draw(Scene.RenderContext context, bool translucentSurfaces)
        {
            if (context.ReplacementShader != null || context.View is not { LightBinner: var binner })
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

            // The shader writes what to add, and in its second output how much of the destination to keep.
            // CS2 blends its liquid decals the same way.
            passState.SetBlend(RsBlendMode.One, RsBlendMode.Src1Color);

            using (renderState.Scope(in passState))
            {
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
            tintsDirty = false;
            materialsDirty = false;
            parentedCount = 0;
            shader = null;
        }

        /// <summary>Releases every GPU resource, the same as <see cref="Clear"/>.</summary>
        public void Delete() => Clear();
    }
}
