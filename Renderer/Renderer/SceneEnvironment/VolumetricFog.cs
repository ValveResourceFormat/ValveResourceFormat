using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer.Buffers;
using ValveResourceFormat.Renderer.Materials;
using ValveResourceFormat.Renderer.Shaders;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.SceneEnvironment
{
    /// <summary>
    /// The volumetric fog of a scene, from <c>env_volumetric_fog_controller</c> and its <c>env_volumetric_fog_volume</c>s.
    /// Each frame it lights the fog in a grid of the screen and accumulates the light along each ray, for the shaders to
    /// read as <c>scatter + color * transmittance</c>.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/hlvr/client/C_EnvVolumetricFogController">C_EnvVolumetricFogController</seealso>
    /// <seealso href="https://s2v.app/SchemaExplorer/hlvr/client/C_EnvVolumetricFogVolume">C_EnvVolumetricFogVolume</seealso>
    public sealed class VolumetricFog : IDisposable
    {
        /// <summary>Width of the grid in cells.</summary>
        public const int GridWidth = 240;

        /// <summary>Height of the grid in cells.</summary>
        public const int GridHeight = 160;

        /// <summary>Number of slices of the grid, from the camera outwards, of a controller that does not set it.</summary>
        public const int DefaultGridDepth = 64;

        private const int MaxVolumes = 128;
        private const int MaxLights = 64;
        private const int MaxBarnLights = 64;

        // Cost units of lights the 8 bit fog takes: a light costs one, and a light the fog shadows costs one per cube face
        private const int LightBudget = 32;

        /// <summary>Size in texels of the tile a fog light shadow map is rendered into.</summary>
        public const int ShadowTileSize = 128;

        /// <summary>Number of tiles along a side of the fog shadow atlas.</summary>
        public const int ShadowAtlasTiles = 16;

        /// <summary>Size in texels of the square fog shadow atlas.</summary>
        public const int ShadowAtlasSize = ShadowTileSize * ShadowAtlasTiles;

        private const int MaxShadowTiles = ShadowAtlasTiles * ShadowAtlasTiles;

        // Cap on the shadow maps rendered in a frame, so a turn of the camera spreads the work over frames
        private const int MaxNewShadowFacesPerFrame = 16;
        private const int CubeFaceCount = 6;
        private const float ShadowNearPlane = 1f;

        // The texture units the baked light and the shadow atlas of the fog are bound to while the fog is built
        private const int IrradianceTextureUnit = 30;
        private const int ShadowTextureUnit = 29;

        // Cube face order of a point light's shadow faces: +X, -X, +Y, -Y, +Z, -Z
        private static readonly Vector3[] CubeFaceDirections = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];

        // Scale of the controller's strength to the fog's extinction per unit of distance
        private const float ScatteringScale = 0.0022f;

        // The slices of controllers that set a first slice thickness follow an exponential distribution, with this
        // many slices when the controller gives no count, and a limit on the count and the thickness
        private const int WarpedGridDepth = 128;
        private const int MinWarpedGridDepth = 32;
        private const int MaxWarpedGridDepth = 384;
        private const float MinFirstSliceThickness = 0.5f;
        private const float MaxFirstSliceThickness = 8f;
        private const float DefaultWarp = 7f;
        private const float MaxWarp = 256f;

        // Lowest density of fog that no volume covers, for fog the engine keeps in floats and does not space exponentially
        private const float MinDensity = 0.001f;

        /// <summary>The falloff shape of a volume that fades from its top and sides.</summary>
        public const int ShapeHeight = 2;

        /// <summary>One <c>env_volumetric_fog_volume</c>.</summary>
        /// <param name="LocalToWorld">Placement of the volume, without scale.</param>
        /// <param name="Mins">Corner of the box with the smallest coordinates, in the volume's space.</param>
        /// <param name="Maxs">Corner of the box with the largest coordinates, in the volume's space.</param>
        /// <param name="Strength">Density in the center of the volume.</param>
        /// <param name="FalloffExponent">Exponent of the falloff towards the edge.</param>
        /// <param name="Shape">0 for a falloff towards the faces of the box, 1 for a radial one, 2 for a falloff from the top and the sides.</param>
        /// <param name="HeightFogDepth">Distance below the top over which a height shaped volume fades.</param>
        /// <param name="HeightFogEdgeWidth">Distance from the sides over which a height shaped volume fades.</param>
        /// <param name="IndirectStrength">Strength of the baked light in the volume, or a negative value to leave the controller's.</param>
        /// <param name="Tint">Linear color the volume gives the fog, or <see langword="null"/> to leave the controller's.</param>
        /// <param name="FollowsRotation">Whether the box turns with the angles of the entity, rather than staying along the axes.</param>
        public readonly record struct FogVolume(
            Matrix4x4 LocalToWorld,
            Vector3 Mins,
            Vector3 Maxs,
            float Strength,
            float FalloffExponent,
            int Shape,
            float HeightFogDepth,
            float HeightFogEdgeWidth,
            float IndirectStrength,
            Vector3? Tint,
            bool FollowsRotation);

        // Matches FogVolume_t, 96 bytes
        [StructLayout(LayoutKind.Sequential)]
        private struct FogVolumeGpu
        {
            // The rows of the world to unit box transform, one per axis
            public Vector4 UnitX;
            public Vector4 UnitY;
            public Vector4 UnitZ;
            // Shape, strength, falloff exponent and indirect strength
            public Vector4 Params;
            // Height scale, edge scale and 1 for a tint
            public Vector4 HeightParams;
            public Vector4 Tint;
        }

        // Matches FogLight_t, 80 bytes
        [StructLayout(LayoutKind.Sequential)]
        private struct FogLightGpu
        {
            // Position, and 1 for a spot light
            public Vector4 Position;
            // Direction, and the cosine of the outer cone angle
            public Vector4 Direction;
            // Color, and the cosine of the inner cone angle
            public Vector4 Color;
            // The distance terms, the range squared and the bias
            public Vector4 Falloff;
            // Index in the lighting constants or -1, 1 when the fog shadows the light with the shadow map of the scene,
            // and the first shadow face of the light or -1
            public Vector4 Params;
        }

        // Matches FogShadowFace_t, 80 bytes
        [StructLayout(LayoutKind.Sequential)]
        private struct FogShadowFaceGpu
        {
            public Matrix4x4 WorldToShadow;
            // Center and half size of the tile in the atlas, zero size for a face that is not rendered
            public Vector4 OffsetScale;
        }

        /// <summary>One shadow map of a fog light, rendered into a tile of the fog shadow atlas.</summary>
        /// <param name="Eye">Position of the light.</param>
        /// <param name="WorldToProjection">Transform to render the map with, which leaves a gutter around the tile.</param>
        /// <param name="Region">The tile in the atlas.</param>
        public readonly record struct ShadowFace(Vector3 Eye, Matrix4x4 WorldToProjection, ShadowAtlasRegion Region);

        // Key ranks the lights of 8 bit storage, the lights of the other storage go by their distance
        private readonly record struct FogLightCandidate(SceneLight Light, float Scale, float DistanceSquared, float Key, int Order);

        // The baked fog lives in three nested cubes around the camera, of 256 cells per side. The smallest cube has
        // cells of drawDistance / 649.6, the outermost cells 5.2 times that
        private const float ClipmapCellsPerSide = 256f;
        private const float ClipmapDrawDistanceInCells = 649.6f;
        private const float ClipmapOuterCellScale = 5.2f;

        // Fills the scene when its controller has no volume entities
        private static readonly FogVolume[] DefaultVolumes = [new FogVolume(Matrix4x4.Identity, new Vector3(-50000f), new Vector3(50000f), 1f, 0.125f, 0, 0f, 0f, -1f, null, false)];

        private readonly List<FogVolume> volumes = [];
        private readonly FogVolumeGpu[] volumeGpu = new FogVolumeGpu[MaxVolumes];
        private StorageBuffer? volumeBuffer;
        private RenderTexture? irradiance;
        private int gridDepth = DefaultGridDepth;
        private float warp;
        private float densityFloor;
        private Vector3 tint = Vector3.One;
        private float indirectStrength;
        private Vector3 irradianceMins;
        private Vector3 irradianceMaxs;
        private readonly List<FogLightCandidate> lightCandidates = [];
        private readonly RendererContext rendererContext;
        private RenderTexture? volume;
        private Shader? shader;

        // Anisotropy is left out. Nothing in the game's fog shaders reads it
        private int staticLightCount;
        private bool lightStorageIs8Bit = true;
        private bool fogShadowNeedsCastShadows;
        private float scattering;
        private float drawDistance = 600f;
        private float fadeInStart = 20f;
        private float fadeInEnd = 100f;

        private readonly FogLightGpu[] lightGpu = new FogLightGpu[MaxLights];
        private StorageBuffer? lightBuffer;

        // The frustum of the fog and the box around it, as of the last collection of lights
        private readonly Frustum fogFrustum = new();
        private readonly Frustum shadowFaceFrustum = new();
        private AABB fogBounds;

        // The tiles a light's shadow maps were rendered into, kept while the light stays in use. The casters are static
        // geometry, so a map stays valid until the light or its tile changes.
        private sealed class ShadowLightCache
        {
            public readonly int[] Tiles = new int[CubeFaceCount];
            public int RenderedMask;
            public int LastUsedFrame;
            public Vector3 Position;
            public Vector3 Direction;
            public float SpotAngle;
            public float Far;

            public void Clear()
            {
                Array.Fill(Tiles, -1);
                RenderedMask = 0;
            }
        }

        private readonly Dictionary<SceneLight, ShadowLightCache> shadowCache = [];
        private readonly Stack<ShadowLightCache> shadowCachePool = [];
        private readonly Stack<int> freeShadowTiles = new(MaxShadowTiles);
        private readonly ShadowLightCache?[] candidateShadowCache = new ShadowLightCache?[MaxLights];
        private readonly int[] candidateShadowMask = new int[MaxLights];
        private int shadowFrame;
        private int shadowLayoutVersion = -1;

        // The shadow maps to render this frame, and the faces the shader reads. A point light takes six faces in the
        // shader's buffer, of which only the ones that were rendered are valid
        private readonly ShadowFace[] shadowFaces = new ShadowFace[MaxNewShadowFacesPerFrame];
        private readonly FogShadowFaceGpu[] shadowFaceGpu = new FogShadowFaceGpu[LightBudget * CubeFaceCount];
        private readonly int[] candidateShadowFace = new int[MaxLights];
        private int shadowFaceCount;
        private int shadowSlotCount;
        private StorageBuffer? shadowFaceBuffer;
        private bool lightsPrepared;

        // x: slot in the barn light buffer, y: factor for the light's color, z: 1 when the fog shadows the light
        private readonly float[] barnLightParams = new float[MaxBarnLights * 4];

        /// <summary>Gets a value indicating whether the scene has a fog controller.</summary>
        public bool HasController { get; private set; }

        /// <summary>Gets the texture the fog is accumulated into, or <see langword="null"/> before the first frame.</summary>
        public RenderTexture? Volume => volume;

        /// <summary>Gets the number of slices of the grid.</summary>
        public int GridDepth => gridDepth;

        /// <summary>Gets the distance of the first slice from the camera, and the distance from it to the last slice.</summary>
        public Vector2 NearRange
        {
            get
            {
                var near = drawDistance / gridDepth;
                return new Vector2(near, drawDistance - near);
            }
        }

        /// <summary>
        /// Gets the warp of the slice distribution and one over its log2, or zero for slices that grow with the square
        /// of their index. With a warp, a slice at <c>s</c> of the grid lies at <c>(warp + 1)^s - 1</c> over the warp
        /// of the way from the first slice to the last.
        /// </summary>
        public Vector2 DepthWarp => warp > 0f ? new Vector2(warp, 1f / MathF.Log2(warp + 1f)) : Vector2.Zero;

        /// <summary>Gets the fog volumes of the scene.</summary>
        public List<FogVolume> Volumes => volumes;

        /// <summary>
        /// Gets or sets how many <c>env_volumetric_fog_volume</c> entities the scene has, including disabled ones.
        /// A scene with none gets a default volume.
        /// </summary>
        public int VolumeEntityCount { get; set; }

        /// <summary>Initializes the fog of a scene.</summary>
        /// <param name="rendererContext">The context to load the fog shader from.</param>
        public VolumetricFog(RendererContext rendererContext)
        {
            this.rendererContext = rendererContext;

            for (var tile = MaxShadowTiles - 1; tile >= 0; tile--)
            {
                freeShadowTiles.Push(tile);
            }
        }

        /// <summary>Takes the fog settings of an <c>env_volumetric_fog_controller</c>.</summary>
        /// <param name="entity">The controller's keyvalues.</param>
        /// <param name="origin">The position of the controller, which its box is relative to.</param>
        public void SetController(EntityLump.Entity entity, Vector3 origin)
        {
            HasController = true;

            scattering = entity.GetFloatProperty("fogstrength", 1f) * ScatteringScale;
            drawDistance = entity.GetFloatProperty("drawdistance", 600f);
            fadeInStart = entity.GetFloatProperty("fadeinstart", 20f);
            fadeInEnd = entity.GetFloatProperty("fadeinend", 100f);

            // Only the newer controllers have a tint. Their games keep the fog lighting in floats, older games in 8 bits
            var hasTint = entity.TryGetValue("tintcolor", out _);
            lightStorageIs8Bit = !hasTint;
            tint = hasTint ? ColorSpace.SrgbGammaToLinear(entity.GetColor32Property("tintcolor")) : Vector3.One;

            // The shader of a controller that sets the first slice thickness starts from zero density, as does the 8 bit fog
            var hasSliceThickness = SetSlices(entity);
            densityFloor = lightStorageIs8Bit || hasSliceThickness ? 0f : MinDensity;
            SetIndirect(entity, origin);
        }

        // A controller that sets the thickness of the first slice spaces its slices exponentially, so that they are thin near
        // the camera. The warp that gives the first slice that thickness is searched, over the range from the fade in start.
        // Returns whether the controller sets the thickness.
        private bool SetSlices(EntityLump.Entity entity)
        {
            if (!entity.TryGetValue("volume_first_slice_thickness", out _))
            {
                return false;
            }

            var depthOverride = entity.GetInt32Property("volume_depth_override");
            gridDepth = depthOverride > 0 ? Math.Clamp(depthOverride, MinWarpedGridDepth, MaxWarpedGridDepth) : WarpedGridDepth;

            var thickness = entity.GetFloatProperty("volume_first_slice_thickness");
            warp = DefaultWarp;

            if (thickness != 0f)
            {
                thickness = Math.Clamp(thickness, MinFirstSliceThickness, MaxFirstSliceThickness);
                warp = SolveWarp(drawDistance - fadeInStart, thickness, gridDepth);
            }

            warp = Math.Clamp(warp, 1f, MaxWarp);

            return true;
        }

        private static float SolveWarp(float range, float thickness, int depth)
        {
            float FirstSlice(float candidate) => range * (MathF.Pow(candidate + 1f, 1f / depth) - 1f) / candidate;

            var low = 1f;
            var high = 2f;

            while (FirstSlice(high) > thickness && high < 1e6f)
            {
                high *= 2f;
            }

            for (var i = 0; i < 20; i++)
            {
                var middle = (high + low) * 0.5f;
                var slice = FirstSlice(middle);

                if (MathF.Abs(slice - thickness) < 0.01f)
                {
                    break;
                }

                if (thickness <= slice)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }

            return (high + low) * 0.5f;
        }

        // The baked light of the fog covers the box of the controller
        private void SetIndirect(EntityLump.Entity entity, Vector3 origin)
        {
            irradiance = null;
            indirectStrength = 0f;

            var textureName = entity.GetStringProperty("fogirradiancevolume");

            if (!entity.GetBooleanProperty("indirectenabled") || string.IsNullOrEmpty(textureName))
            {
                return;
            }

            irradianceMins = origin + entity.GetVector3Property("box_mins");
            irradianceMaxs = origin + entity.GetVector3Property("box_maxs");
            indirectStrength = entity.GetFloatProperty("indirectstrength", 1f);

            irradiance = rendererContext.MaterialLoader.GetTexture(textureName);
            irradiance.SetWrapMode(RsTextureAddressMode.Clamp);
            irradiance.SetFiltering(TextureMinFilter.Linear, TextureMagFilter.Linear);
        }

        /// <summary>Lights and accumulates the fog for the view that is bound.</summary>
        /// <param name="scene">The scene whose lights light the fog.</param>
        /// <param name="camera">The view the fog is built for.</param>
        /// <param name="shadowDepth">The shadow atlas of the scene's spot lights, or <see langword="null"/> if it has none.</param>
        /// <param name="fogShadowDepth">The atlas the <see cref="ShadowFaces"/> were rendered into, or <see langword="null"/> if there are none.</param>
        public void Render(Scene scene, Camera camera, RenderTexture? shadowDepth, RenderTexture? fogShadowDepth)
        {
            var cameraPosition = camera.Location;
            var hasPreparedLights = lightsPrepared;
            lightsPrepared = false;

            if (!CanRender)
            {
                return;
            }

            if (!hasPreparedLights)
            {
                PrepareLights(scene, camera, withShadows: false);
            }

            shader ??= rendererContext.ShaderLoader.LoadShader("volumetric_fog");

            if (volume == null)
            {
                volume = RenderTexture.Create3D(TextureTarget.Texture3D, GridWidth, GridHeight, gridDepth, ImageFormat.RGBA16161616F, 1, "VolumetricFog");
                volume.SetWrapMode(RsTextureAddressMode.Clamp);
                volume.SetFiltering(TextureMinFilter.Linear, TextureMagFilter.Linear);

                // Until it is built, the fog lets everything through
                GL.ClearTexImage(volume.Handle, 0, PixelFormat.Rgba, PixelType.Float, [0f, 0f, 0f, 1f]);
            }

            var volumeCount = StoreVolumes();
            var lightCount = StoreLights(scene);
            var barnLightCount = StoreBarnLights(scene, cameraPosition);
            var nearRange = NearRange;

            // Fades in from the start distance to the end distance, or not at all without a range
            var fadeStart = fadeInStart < 0f ? fadeInStart + fadeInEnd : fadeInStart;
            var fadeScale = 0f;
            var fadeBias = 1f;

            if (fadeInEnd > 0f && fadeStart < fadeInEnd)
            {
                fadeScale = 1f / (fadeInEnd - fadeStart);
                fadeBias = -fadeStart * fadeScale;
            }

            shader.Use();
            shader.SetUniform("g_vFogGridSize", new Vector3(GridWidth, GridHeight, gridDepth));
            shader.SetUniform("g_vFogNearRange", nearRange);
            shader.SetUniform("g_vFogDepthWarp", DepthWarp);
            shader.SetUniform("g_vFogScatteringFadeIn", new Vector4(scattering, 0f, fadeScale, fadeBias));
            shader.SetUniform("g_vFogTint_IndirectStrength", new Vector4(tint, indirectStrength));
            shader.SetUniform("g_nFogVolumeCount", volumeCount);
            shader.SetUniform("g_flFogDensityFloor", densityFloor);

            if (irradiance != null)
            {
                var scale = Vector3.One / (irradianceMaxs - irradianceMins);
                shader.SetUniform("g_vFogIrradianceScale", scale);
                shader.SetUniform("g_vFogIrradianceBias", -irradianceMins * scale);
                GL.BindTextureUnit(IrradianceTextureUnit, irradiance.Handle);
            }

            volumeBuffer!.BindBufferBase();
            shader.SetUniform("g_nFogLightCount", lightCount);
            shader.SetUniform("g_nFogStaticLightCount", staticLightCount);
            shader.SetUniform("g_nFogLightStorageIs8Bit", lightStorageIs8Bit ? 1 : 0);

            var clipmapExtent = ClipmapCellsPerSide / 2f * ClipmapOuterCellScale * drawDistance / ClipmapDrawDistanceInCells;
            shader.SetUniform("g_vFogClipmapCenterExtent", new Vector4(cameraPosition, clipmapExtent));

            lightBuffer!.BindBufferBase();

            shader.SetUniform("g_nFogBarnLightCount", barnLightCount);

            if (barnLightCount > 0)
            {
                shader.SetUniform4Array("g_vFogBarnLight", barnLightCount, barnLightParams);
            }

            scene.LightingInfo.BindLightCookieVolume();

            if (shadowDepth != null)
            {
                GL.BindTextureUnit((int)ReservedTextureSlots.BarnLightShadowDepth, shadowDepth.Handle);
            }

            if (fogShadowDepth != null && shadowSlotCount > 0)
            {
                shadowFaceBuffer ??= StorageBuffer.Allocate<FogShadowFaceGpu>(ReservedBufferSlots.FogShadowFaces, "FogShadowFaces", LightBudget * CubeFaceCount, BufferUsage.Dynamic);
                shadowFaceBuffer.Update<FogShadowFaceGpu>(shadowFaceGpu.AsSpan(0, shadowSlotCount), 0);
                shadowFaceBuffer.BindBufferBase();
                GL.BindTextureUnit(ShadowTextureUnit, fogShadowDepth.Handle);
            }

            GL.BindImageTexture(0, volume.Handle, 0, true, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba16f);
            GL.DispatchCompute(MathUtils.DivideRoundUp(GridWidth, 8), MathUtils.DivideRoundUp(GridHeight, 8), 1);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
        }

        private int StoreVolumes()
        {
            var source = volumes.Count == 0 ? DefaultVolumes.AsSpan() : CollectionsMarshal.AsSpan(volumes);
            var count = Math.Min(source.Length, MaxVolumes);

            for (var i = 0; i < count; i++)
            {
                var fogVolume = source[i];
                var size = fogVolume.Maxs - fogVolume.Mins;
                var scale = Vector3.One / size;

                // The volume to world transform without its rotation is a move to the origin of the entity
                var worldToLocal = Matrix4x4.CreateTranslation(-fogVolume.LocalToWorld.Translation);

                if (fogVolume.FollowsRotation && Matrix4x4.Invert(fogVolume.LocalToWorld, out var inverse))
                {
                    worldToLocal = inverse;
                }

                var worldToUnit = worldToLocal * Matrix4x4.CreateTranslation(-fogVolume.Mins) * Matrix4x4.CreateScale(scale);
                var heightScale = 0f;
                var edgeScale = 0f;

                if (fogVolume.Shape == ShapeHeight)
                {
                    heightScale = size.Z / fogVolume.HeightFogDepth;
                    edgeScale = MathF.Sqrt((size.X * size.X) + (size.Y * size.Y)) / fogVolume.HeightFogEdgeWidth;
                }

                var hasTint = fogVolume.Tint != null;

                volumeGpu[i] = new FogVolumeGpu
                {
                    UnitX = new Vector4(worldToUnit.M11, worldToUnit.M21, worldToUnit.M31, worldToUnit.M41),
                    UnitY = new Vector4(worldToUnit.M12, worldToUnit.M22, worldToUnit.M32, worldToUnit.M42),
                    UnitZ = new Vector4(worldToUnit.M13, worldToUnit.M23, worldToUnit.M33, worldToUnit.M43),
                    Params = new Vector4(fogVolume.Shape, fogVolume.Strength, fogVolume.FalloffExponent, fogVolume.IndirectStrength),
                    HeightParams = new Vector4(heightScale, edgeScale, hasTint ? 1f : 0f, 0f),
                    Tint = new Vector4(fogVolume.Tint ?? Vector3.One, 0f),
                };
            }

            if (volumeBuffer == null)
            {
                volumeBuffer = StorageBuffer.Allocate<FogVolumeGpu>(ReservedBufferSlots.FogVolumes, "FogVolumes", MaxVolumes, BufferUsage.Dynamic);
            }

            volumeBuffer.Update<FogVolumeGpu>(volumeGpu.AsSpan(0, count), 0);

            return count;
        }

        private bool CanRender => HasController && (volumes.Count > 0 || VolumeEntityCount == 0);

        /// <summary>Gets whether the fog has lights with shadow maps of their own, which need the fog shadow atlas.</summary>
        public bool UsesShadowAtlas => CanRender && lightStorageIs8Bit;

        /// <summary>Gets the shadow maps picked by the last <see cref="PrepareShadows"/> that are not in the fog shadow atlas yet, to render into it.</summary>
        public ReadOnlySpan<ShadowFace> ShadowFaces => shadowFaces.AsSpan(0, shadowFaceCount);

        /// <summary>
        /// Picks the lights of the view and places the shadow maps of the ones the fog shadows, for the next
        /// <see cref="Render"/> to use.
        /// </summary>
        /// <param name="scene">The scene whose lights light the fog.</param>
        /// <param name="camera">The view the fog is built for.</param>
        public void PrepareShadows(Scene scene, Camera camera)
        {
            if (!CanRender)
            {
                shadowFaceCount = 0;
                shadowSlotCount = 0;
                return;
            }

            // The static layout changes with the geometry the maps were rendered from
            if (shadowLayoutVersion != scene.IndirectLayoutVersion)
            {
                shadowLayoutVersion = scene.IndirectLayoutVersion;
                InvalidateShadows();
            }

            PrepareLights(scene, camera, withShadows: true);
            lightsPrepared = true;
        }

        private void PrepareLights(Scene scene, Camera camera, bool withShadows)
        {
            lightCandidates.Clear();
            shadowFaceCount = 0;
            shadowSlotCount = 0;
            fogShadowNeedsCastShadows = scene.LightingInfo.LightmapGameVersionNumber >= 2;

            if (lightStorageIs8Bit)
            {
                CollectViewLights(scene, camera);
                ApplyLightBudget();
            }
            else
            {
                CollectNearbyLights(scene, camera.Location);
            }

            Array.Fill(candidateShadowFace, -1);

            // Only mode 1 lights of 8 bit storage have a shadow map of their own
            if (withShadows && lightStorageIs8Bit)
            {
                AssignShadowFaces();
            }
        }

        private int StoreLights(Scene scene)
        {
            var candidateCount = Math.Min(lightCandidates.Count, MaxLights);
            var count = 0;

            for (var candidateIndex = 0; candidateIndex < candidateCount; candidateIndex++)
            {
                var (light, scale, _, _, _) = lightCandidates[candidateIndex];

                // TODO: The shader has no directional or ortho light. The sun filling the fog evenly with its color veils every
                // outdoor view, and an ortho light needs an orthographic shadow map.
                if (light.Type == SceneLight.LightType.Directional)
                {
                    continue;
                }

                var i = count;
                var isSpot = light.Type == SceneLight.LightType.Spot;

                // The distance falloff uses the attenuation terms as they are, the color carries their scale
                var linear = light.AttenuationLinear;
                var quadratic = light.AttenuationQuadratic;
                var range = light.FogRange;
                var hasBias = light.AttenuationConstant == 0f && range > 0f;
                var falloffAtRange = (quadratic * range * range) + (linear * range) + 1.1920929e-7f;

                var color = ColorSpace.SrgbGammaToLinear(light.Color)
                    * (light.Brightness * light.BrightnessScale * light.AttenuationColorScale * light.StyleBrightness * scale);

                // With 8 bit storage only mode 1 shadows the light. A light without a face in the fog shadow atlas
                // reads the shadow map of the scene, if it has one.
                var isShadowed = lightStorageIs8Bit ? HasFogShadow(light) : light.FogLighting is 1 or 2;

                lightGpu[i] = new FogLightGpu
                {
                    Position = new Vector4(light.Position, isSpot ? 1f : 0f),
                    Direction = new Vector4(light.Direction, MathF.Cos(float.DegreesToRadians(light.SpotOuterAngle))),
                    Color = new Vector4(color, MathF.Cos(float.DegreesToRadians(light.SpotInnerAngle))),
                    Falloff = new Vector4(linear, quadratic, range * range, hasBias ? 1f / falloffAtRange : 0f),
                    Params = new Vector4(scene.LightingInfo.GetStoredLightIndex(light), isShadowed ? 1f : 0f, candidateShadowFace[candidateIndex], 0f),
                };

                count++;
            }

            if (lightBuffer == null)
            {
                lightBuffer = StorageBuffer.Allocate<FogLightGpu>(ReservedBufferSlots.FogLights, "FogLights", MaxLights, BufferUsage.Dynamic);
            }

            lightBuffer.Update<FogLightGpu>(lightGpu.AsSpan(0, count), 0);

            // Fog lighting modes 1 and 3 of 8 bit storage are baked into the clipmaps of the fog, so those lights are static
            staticLightCount = lightStorageIs8Bit ? count : 0;

            return count;
        }

        // The lights of the view the fog is built for: the ones that reach into the frustum of the fog,
        // ranked by priority and then by how close the light is to the camera
        private void CollectViewLights(Scene scene, Camera camera)
        {
            var cameraPosition = camera.Location;
            UpdateFogFrustum(camera);

            var staticNodes = scene.StaticNodes;

            for (var i = 0; i < staticNodes.Count; i++)
            {
                if (staticNodes[i] is SceneLight light)
                {
                    AddViewLight(light, cameraPosition);
                }
            }

            var dynamicNodes = scene.DynamicNodes;

            for (var i = 0; i < dynamicNodes.Count; i++)
            {
                if (dynamicNodes[i] is SceneLight light)
                {
                    AddViewLight(light, cameraPosition);
                }
            }

            lightCandidates.Sort(static (a, b) =>
            {
                var byPriority = b.Light.Priority.CompareTo(a.Light.Priority);

                if (byPriority != 0)
                {
                    return byPriority;
                }

                var byKey = b.Key.CompareTo(a.Key);

                return byKey != 0 ? byKey : a.Order.CompareTo(b.Order);
            });
        }

        private void AddViewLight(SceneLight light, Vector3 cameraPosition)
        {
            if (!light.Enabled || light.FogLighting is not (1 or 3) || light.FogContributionStrength == 0f)
            {
                return;
            }

            float key;
            var fade = 1f;
            var isDirectional = light.Type == SceneLight.LightType.Directional && light.Entity != SceneLight.EntityType.Ortho;

            if (isDirectional)
            {
                key = 1f;
            }
            else
            {
                var length = light.PrecomputedMaxRange > 0f ? light.PrecomputedMaxRange : light.Range;

                var bounds = GetFogLightBounds(light, length);

                if (!fogFrustum.Intersects(bounds))
                {
                    return;
                }

                fade = light.GetFogDistanceFade(bounds, cameraPosition);

                if (fade <= 0f)
                {
                    return;
                }

                if (light.Type == SceneLight.LightType.Point)
                {
                    key = 0.5f / Vector3.DistanceSquared(cameraPosition, light.Position) * fade;
                }
                else
                {
                    var distanceSquared = MathUtils.DistanceSquaredToSegment(cameraPosition, light.Position, light.Position + (light.Direction * length));
                    key = 0.5f / MathF.Max(distanceSquared, 1f) * fade;
                }
            }

            lightCandidates.Add(new FogLightCandidate(light, light.FogContributionStrength * fade, 0f, key, lightCandidates.Count));
        }

        // Walks the ranked lights and keeps them while the budget lasts. A light the fog shadows pays for each cube face
        // that reaches into the fog and keeps only the faces it could pay for, so it is left out with none. Once the
        // budget is spent every later light is left out, whatever it costs. The kept faces are left in candidateShadowMask.
        private void ApplyLightBudget()
        {
            var units = 0;
            var keptCount = 0;

            for (var candidateIndex = 0; candidateIndex < lightCandidates.Count && units < LightBudget; candidateIndex++)
            {
                var candidate = lightCandidates[candidateIndex];
                var light = candidate.Light;
                var keptMask = 0;

                if (HasFogShadow(light) && light.Type == SceneLight.LightType.Point)
                {
                    var far = GetShadowFar(light);

                    for (var face = 0; face < CubeFaceCount; face++)
                    {
                        if (!FaceReachesFog(light.Position, face, far))
                        {
                            continue;
                        }

                        if (units < LightBudget)
                        {
                            keptMask |= 1 << face;
                        }

                        units++;
                    }

                    if (keptMask == 0)
                    {
                        continue;
                    }
                }
                else
                {
                    if (HasFogShadow(light) && light.Type == SceneLight.LightType.Spot && light.Entity != SceneLight.EntityType.Ortho)
                    {
                        keptMask = 1;
                    }

                    units++;
                }

                lightCandidates[keptCount] = candidate;
                candidateShadowMask[keptCount] = keptMask;
                keptCount++;
            }

            lightCandidates.RemoveRange(keptCount, lightCandidates.Count - keptCount);
        }

        // The VR light binner shadows every mode 1 light, the other one only when the light also casts shadows
        private bool HasFogShadow(SceneLight light)
            => light.FogLighting == 1 && (!fogShadowNeedsCastShadows || light.CastShadows == 1);

        private bool FaceReachesFog(Vector3 position, int face, float far)
        {
            shadowFaceFrustum.Update(CreateCubeFaceMatrix(position, face, far));

            return fogFrustum.Intersects(GetCubeFaceBounds(position, face, far)) && shadowFaceFrustum.Intersects(fogBounds);
        }

        private static float GetShadowFar(SceneLight light) => MathF.Max(light.FogRange, ShadowNearPlane + 1f);

        // The lights that reach into the fog, the baked ones first and each group nearest first
        private void CollectNearbyLights(Scene scene, Vector3 cameraPosition)
        {
            var staticNodes = scene.StaticNodes;

            for (var i = 0; i < staticNodes.Count; i++)
            {
                if (staticNodes[i] is SceneLight light)
                {
                    AddNearbyLight(light, cameraPosition);
                }
            }

            var dynamicNodes = scene.DynamicNodes;

            for (var i = 0; i < dynamicNodes.Count; i++)
            {
                if (dynamicNodes[i] is SceneLight light)
                {
                    AddNearbyLight(light, cameraPosition);
                }
            }

            lightCandidates.Sort(static (a, b) =>
            {
                var byDistance = a.DistanceSquared.CompareTo(b.DistanceSquared);

                return byDistance != 0 ? byDistance : a.Order.CompareTo(b.Order);
            });
        }

        private void AddNearbyLight(SceneLight light, Vector3 cameraPosition)
        {
            if (!light.Enabled || light.FogLighting is < 1 or > 4)
            {
                return;
            }

            // The sun lights the fog too, but filling the fog evenly with its color veils every outdoor view
            if (light.Entity == SceneLight.EntityType.Ortho || light.Type == SceneLight.LightType.Directional)
            {
                return;
            }

            var reach = drawDistance + light.FogRange;
            var distanceSquared = Vector3.DistanceSquared(light.Position, cameraPosition);

            if (distanceSquared > reach * reach)
            {
                return;
            }

            lightCandidates.Add(new FogLightCandidate(light, 1f, distanceSquared, 0f, lightCandidates.Count));
        }

        // The camera frustum cut to the range of the fog, from its first slice to its draw distance, and the box around it
        private void UpdateFogFrustum(Camera camera)
        {
            var forward = camera.Forward;
            var forwardOffset = Vector3.Dot(forward, camera.Location);
            var nearDistance = drawDistance / gridDepth;

            camera.ViewFrustum.Planes.CopyTo(fogFrustum.Planes, 0);
            fogFrustum.Planes[4] = new Plane(-forward, forwardOffset + drawDistance);
            fogFrustum.Planes[5] = new Plane(forward, -forwardOffset - nearDistance);

            var projection = camera.ProjectionMatrix;
            var halfWidthPerDistance = camera.Right / projection.M11;
            var halfHeightPerDistance = camera.Up / projection.M22;

            var bounds = new AABB(new Vector3(float.MaxValue), new Vector3(float.MinValue));

            ReadOnlySpan<float> distances = [nearDistance, drawDistance];

            foreach (var distance in distances)
            {
                var center = camera.Location + (forward * distance);
                var x = halfWidthPerDistance * distance;
                var y = halfHeightPerDistance * distance;

                bounds = bounds.Encapsulate(center + x + y);
                bounds = bounds.Encapsulate(center + x - y);
                bounds = bounds.Encapsulate(center - x + y);
                bounds = bounds.Encapsulate(center - x - y);
            }

            fogBounds = bounds;
        }

        /// <summary>
        /// Makes every shadow map render again. Call it when the atlas the maps were rendered into is created anew.
        /// </summary>
        public void InvalidateShadows()
        {
            foreach (var cache in shadowCache.Values)
            {
                cache.RenderedMask = 0;
            }
        }

        // Gives the lights the fog shadows a slot each in the order the lights are ranked in, and queues the shadow
        // maps that are not in the atlas yet. A spot light has one map, a point light one per cube face that reaches
        // into the fog. A map stays in its tile while its light is in use, and the lights that were not picked for a
        // while give up their tiles to new ones, so a light that does not fit is left unshadowed.
        // The casters are static geometry, so a map that was rendered stays valid. Dynamic casters would not be
        // reflected in it.
        private void AssignShadowFaces()
        {
            var candidateCount = Math.Min(lightCandidates.Count, MaxLights);
            shadowFrame++;

            for (var candidateIndex = 0; candidateIndex < candidateCount; candidateIndex++)
            {
                candidateShadowCache[candidateIndex] = null;

                if (candidateShadowMask[candidateIndex] == 0)
                {
                    continue;
                }

                var light = lightCandidates[candidateIndex].Light;
                var far = GetShadowFar(light);

                if (!shadowCache.TryGetValue(light, out var cache))
                {
                    cache = RentShadowCache();
                    shadowCache.Add(light, cache);
                }

                cache.LastUsedFrame = shadowFrame;

                if (cache.Position != light.Position || cache.Direction != light.Direction || cache.SpotAngle != light.SpotOuterAngle || cache.Far != far)
                {
                    cache.Position = light.Position;
                    cache.Direction = light.Direction;
                    cache.SpotAngle = light.SpotOuterAngle;
                    cache.Far = far;
                    cache.RenderedMask = 0;
                }

                candidateShadowCache[candidateIndex] = cache;
            }

            for (var candidateIndex = 0; candidateIndex < candidateCount; candidateIndex++)
            {
                var cache = candidateShadowCache[candidateIndex];

                if (cache == null)
                {
                    continue;
                }

                var light = lightCandidates[candidateIndex].Light;
                var isSpot = light.Type == SceneLight.LightType.Spot;
                var faceCount = isSpot ? 1 : CubeFaceCount;

                candidateShadowFace[candidateIndex] = shadowSlotCount;

                for (var face = 0; face < faceCount; face++)
                {
                    var bit = 1 << face;
                    var isNew = false;

                    // A face the budget did not pay for reads as lit
                    if ((candidateShadowMask[candidateIndex] & bit) == 0)
                    {
                        shadowFaceGpu[shadowSlotCount++] = default;
                        continue;
                    }

                    if ((cache.RenderedMask & bit) == 0 && shadowFaceCount < MaxNewShadowFacesPerFrame)
                    {
                        if (cache.Tiles[face] < 0)
                        {
                            cache.Tiles[face] = AllocateShadowTile();
                        }

                        if (cache.Tiles[face] >= 0)
                        {
                            cache.RenderedMask |= bit;
                            isNew = true;
                        }
                    }

                    if ((cache.RenderedMask & bit) == 0)
                    {
                        shadowFaceGpu[shadowSlotCount++] = default;
                        continue;
                    }

                    var matrix = isSpot ? CreateSpotShadowMatrix(light, cache.Far) : CreateCubeFaceMatrix(cache.Position, face, cache.Far);
                    AddShadowFace(cache.Position, matrix, cache.Tiles[face], isNew);
                }
            }
        }

        // Writes the slot of the shader's buffer that is next, and queues the map to render into its tile
        private void AddShadowFace(Vector3 eye, Matrix4x4 worldToFrustum, int tile, bool render)
        {
            var region = new ShadowAtlasRegion(tile % ShadowAtlasTiles * ShadowTileSize, tile / ShadowAtlasTiles * ShadowTileSize, ShadowTileSize, ShadowTileSize);

            // The map is rendered with the matrix that leaves a gutter, and read with the one that does not
            var renderMatrix = worldToFrustum;
            var offsetScale = ShadowMapper.ComputeShadowOffsetScale(region, ShadowAtlasSize, ref renderMatrix);

            if (render)
            {
                shadowFaces[shadowFaceCount++] = new ShadowFace(eye, renderMatrix, region);
            }

            shadowFaceGpu[shadowSlotCount++] = new FogShadowFaceGpu { WorldToShadow = worldToFrustum, OffsetScale = offsetScale };
        }

        private ShadowLightCache RentShadowCache()
        {
            if (!shadowCachePool.TryPop(out var cache))
            {
                cache = new ShadowLightCache();
            }

            cache.Clear();
            return cache;
        }

        // Takes a free tile, or the tiles of the light that was picked the longest ago and is not in use this frame
        private int AllocateShadowTile()
        {
            while (freeShadowTiles.Count == 0)
            {
                SceneLight? oldestLight = null;
                ShadowLightCache? oldest = null;

                foreach (var (light, cache) in shadowCache)
                {
                    if (cache.LastUsedFrame != shadowFrame && (oldest == null || cache.LastUsedFrame < oldest.LastUsedFrame))
                    {
                        oldestLight = light;
                        oldest = cache;
                    }
                }

                if (oldestLight == null || oldest == null)
                {
                    return -1;
                }

                shadowCache.Remove(oldestLight);

                foreach (var tile in oldest.Tiles)
                {
                    if (tile >= 0)
                    {
                        freeShadowTiles.Push(tile);
                    }
                }

                shadowCachePool.Push(oldest);
            }

            return freeShadowTiles.Pop();
        }

        private static Matrix4x4 CreateSpotShadowMatrix(SceneLight light, float far)
        {
            var forward = light.Direction;
            var up = MathF.Abs(forward.Z) > 0.99f ? Vector3.UnitX : Vector3.UnitZ;

            return SceneLight.CreateSpotShadowMatrix(light.Position, forward, up, light.SpotOuterAngle, far);
        }

        private static Matrix4x4 CreateCubeFaceMatrix(Vector3 position, int face, float far)
        {
            var direction = CubeFaceDirections[face];
            var up = GetCubeFaceUp(face);

            var view = Matrix4x4.CreateLookAtLeftHanded(position, position + direction, up);
            var projection = Matrix4x4.CreatePerspectiveLeftHanded(2f * ShadowNearPlane, 2f * ShadowNearPlane, ShadowNearPlane, far);

            return view * projection;
        }

        private static Vector3 GetCubeFaceUp(int face) => face < 4 ? Vector3.UnitZ : Vector3.UnitX;

        // A box around the pyramid a cube face reaches, from the light to the square at the range
        private static AABB GetCubeFaceBounds(Vector3 position, int face, float far)
        {
            var direction = CubeFaceDirections[face];
            var up = GetCubeFaceUp(face);
            var right = Vector3.Cross(up, direction);

            var center = position + (direction * far);
            var x = right * far;
            var y = up * far;

            return new AABB(position, position)
                .Encapsulate(center + x + y)
                .Encapsulate(center + x - y)
                .Encapsulate(center - x + y)
                .Encapsulate(center - x - y);
        }

        // A box around the volume the light reaches, which for a spot or ortho light is its range along its direction
        private static AABB GetFogLightBounds(SceneLight light, float length)
        {
            if (light.Type == SceneLight.LightType.Point)
            {
                return new AABB(light.Position, length);
            }

            var end = light.Position + (light.Direction * length);
            float spread;

            if (light.Entity == SceneLight.EntityType.Ortho)
            {
                spread = MathF.Sqrt((light.SizeParams.X * light.SizeParams.X) + (light.SizeParams.Y * light.SizeParams.Y));
            }
            else
            {
                var halfAngle = MathF.Min(float.DegreesToRadians(light.SpotOuterAngle), float.DegreesToRadians(85f));
                spread = length * MathF.Tan(halfAngle);
            }

            var min = Vector3.Min(light.Position, end) - new Vector3(spread);
            var max = Vector3.Max(light.Position, end) + new Vector3(spread);

            return new AABB(min, max);
        }

        // Barn, rect and omni2 lights light the fog through the faces the scene binned for its own shading, in the
        // order it ranks them, so a light that is not on screen does not light the fog
        private int StoreBarnLights(Scene scene, Vector3 cameraPosition)
        {
            var faces = scene.LightingInfo.BinnedBarnLightFaces;
            var count = 0;

            for (var slot = 0; slot < faces.Length && count < MaxBarnLights; slot++)
            {
                var light = faces[slot].Light;

                if (!light.BarnFog)
                {
                    continue;
                }

                var intensity = light.BarnFogIntensity;

                if (intensity <= 0f)
                {
                    continue;
                }

                var reach = drawDistance + light.Range;

                if (Vector3.DistanceSquared(light.Position, cameraPosition) > reach * reach)
                {
                    continue;
                }

                var offset = count * 4;
                barnLightParams[offset] = slot;
                barnLightParams[offset + 1] = intensity;
                barnLightParams[offset + 2] = light.BarnFogHasShadows ? 1f : 0f;
                count++;
            }

            return count;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            volume?.Delete();
            volume = null;
            volumeBuffer?.Delete();
            volumeBuffer = null;
            lightBuffer?.Delete();
            lightBuffer = null;
            shadowFaceBuffer?.Delete();
            shadowFaceBuffer = null;
        }
    }
}
