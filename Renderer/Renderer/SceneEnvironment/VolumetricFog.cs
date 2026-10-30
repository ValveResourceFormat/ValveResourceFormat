using System.Linq;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
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

        /// <summary>Number of slices of the grid, from the camera outwards.</summary>
        public const int GridDepth = 64;

        // TODO: The game takes up to 128 volumes, which needs a buffer instead of uniform arrays
        private const int MaxVolumes = 64;
        private const int MaxLights = 64;

        // Scale of the controller's strength to the fog's extinction per unit of distance
        private const float ScatteringScale = 0.0022f;

        /// <summary>One <c>env_volumetric_fog_volume</c>.</summary>
        /// <param name="Mins">Corner of the box with the smallest coordinates.</param>
        /// <param name="Maxs">Corner of the box with the largest coordinates.</param>
        /// <param name="Strength">Density in the center of the volume.</param>
        /// <param name="FalloffExponent">Exponent of the falloff towards the edge.</param>
        /// <param name="IsSphere">Whether the falloff is radial, rather than towards the faces of the box.</param>
        public readonly record struct FogVolume(Vector3 Mins, Vector3 Maxs, float Strength, float FalloffExponent, bool IsSphere);

        private readonly record struct FogLightCandidate(SceneLight Light, float Scale, float DistanceSquared, bool IsStatic);

        // The baked fog lives in three nested cubes around the camera, of 256 cells per side. The smallest cube has
        // cells of drawDistance / 649.6, the outermost cells 5.2 times that
        private const float ClipmapCellsPerSide = 256f;
        private const float ClipmapDrawDistanceInCells = 649.6f;
        private const float ClipmapOuterCellScale = 5.2f;

        // Fills the scene when its controller has no volume entities
        private static readonly FogVolume[] DefaultVolumes = [new FogVolume(new Vector3(-50000f), new Vector3(50000f), 1f, 0.125f, false)];

        private readonly List<FogVolume> volumes = [];
        private readonly List<FogLightCandidate> lightCandidates = [];
        private readonly RendererContext rendererContext;
        private RenderTexture? volume;
        private Shader? shader;

        // Anisotropy is left out. Nothing in the game's fog shaders reads it
        private int staticLightCount;
        private bool lightStorageIs8Bit = true;
        private float scattering;
        private float drawDistance = 600f;
        private float fadeInStart = 20f;
        private float fadeInEnd = 100f;

        private readonly float[] volumeBias = new float[MaxVolumes * 4];
        private readonly float[] volumeScale = new float[MaxVolumes * 4];
        private readonly float[] volumeExponent = new float[MaxVolumes];
        private readonly float[] lightPosition = new float[MaxLights * 4];
        private readonly float[] lightDirection = new float[MaxLights * 4];
        private readonly float[] lightColor = new float[MaxLights * 4];
        private readonly float[] lightFalloff = new float[MaxLights * 4];
        private readonly float[] lightParams = new float[MaxLights * 4];

        /// <summary>Gets a value indicating whether the scene has a fog controller.</summary>
        public bool HasController { get; private set; }

        /// <summary>Gets the texture the fog is accumulated into, or <see langword="null"/> before the first frame.</summary>
        public RenderTexture? Volume => volume;

        /// <summary>Gets the distance of the first slice from the camera, and the distance from it to the last slice.</summary>
        public Vector2 NearRange
        {
            get
            {
                var near = drawDistance / GridDepth;
                return new Vector2(near, drawDistance - near);
            }
        }

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
        }

        /// <summary>Takes the fog settings of an <c>env_volumetric_fog_controller</c>.</summary>
        /// <param name="entity">The controller's keyvalues.</param>
        public void SetController(EntityLump.Entity entity)
        {
            HasController = true;

            // TODO: IndirectEnabled lights the fog with the baked fogirradiancevolume texture
            scattering = entity.GetFloatProperty("fogstrength", 1f) * ScatteringScale;
            drawDistance = entity.GetFloatProperty("drawdistance", 600f);
            fadeInStart = entity.GetFloatProperty("fadeinstart", 20f);
            fadeInEnd = entity.GetFloatProperty("fadeinend", 100f);

            // Only the newer controllers have a tint. Their games keep the fog lighting in floats, older games in 8 bits
            lightStorageIs8Bit = !entity.TryGetValue("tintcolor", out _);
        }

        /// <summary>Lights and accumulates the fog for the view that is bound.</summary>
        /// <param name="scene">The scene whose lights light the fog.</param>
        /// <param name="cameraPosition">The position of the view the fog is built for.</param>
        /// <param name="shadowDepth">The shadow atlas of the scene's spot lights, or <see langword="null"/> if it has none.</param>
        public void Render(Scene scene, Vector3 cameraPosition, RenderTexture? shadowDepth)
        {
            if (!HasController || (volumes.Count == 0 && VolumeEntityCount > 0))
            {
                return;
            }

            shader ??= rendererContext.ShaderLoader.LoadShader("volumetric_fog");

            if (volume == null)
            {
                volume = RenderTexture.Create3D(TextureTarget.Texture3D, GridWidth, GridHeight, GridDepth, ImageFormat.RGBA16161616F, 1, "VolumetricFog");
                volume.SetWrapMode(RsTextureAddressMode.Clamp);
                volume.SetFiltering(TextureMinFilter.Linear, TextureMagFilter.Linear);

                // Until it is built, the fog lets everything through
                GL.ClearTexImage(volume.Handle, 0, PixelFormat.Rgba, PixelType.Float, [0f, 0f, 0f, 1f]);
            }

            var volumeCount = StoreVolumes();
            var lightCount = StoreLights(scene, cameraPosition);
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
            shader.SetUniform("g_vFogGridSize", new Vector3(GridWidth, GridHeight, GridDepth));
            shader.SetUniform("g_vFogNearRange", nearRange);
            shader.SetUniform("g_vFogScatteringFadeIn", new Vector4(scattering, 0f, fadeScale, fadeBias));
            shader.SetUniform("g_nFogVolumeCount", volumeCount);
            shader.SetUniform4Array("g_vFogVolumeBoundsBias", volumeCount, volumeBias);
            shader.SetUniform4Array("g_vFogVolumeBoundsScale", volumeCount, volumeScale);
            shader.SetUniform4Array("g_vFogVolumeExponent", MathUtils.DivideRoundUp(volumeCount, 4), volumeExponent);
            shader.SetUniform("g_nFogLightCount", lightCount);
            shader.SetUniform("g_nFogStaticLightCount", staticLightCount);
            shader.SetUniform("g_nFogLightStorageIs8Bit", lightStorageIs8Bit ? 1 : 0);

            var clipmapExtent = lightStorageIs8Bit
                ? ClipmapCellsPerSide / 2f * ClipmapOuterCellScale * drawDistance / ClipmapDrawDistanceInCells
                : 0f;
            shader.SetUniform("g_vFogClipmapCenterExtent", new Vector4(cameraPosition, clipmapExtent));

            if (lightCount > 0)
            {
                shader.SetUniform4Array("g_vFogLightPosition_Spot", lightCount, lightPosition);
                shader.SetUniform4Array("g_vFogLightDirection_OuterCos", lightCount, lightDirection);
                shader.SetUniform4Array("g_vFogLightColor_InnerCos", lightCount, lightColor);
                shader.SetUniform4Array("g_vFogLightFalloff", lightCount, lightFalloff);
                shader.SetUniform4Array("g_vFogLightParams", lightCount, lightParams);
            }

            scene.LightingInfo.BindLightCookieVolume();

            if (shadowDepth != null)
            {
                GL.BindTextureUnit((int)ReservedTextureSlots.BarnLightShadowDepth, shadowDepth.Handle);
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
                var bias = -fogVolume.Mins * scale;

                volumeBias[i * 4] = bias.X;
                volumeBias[i * 4 + 1] = bias.Y;
                volumeBias[i * 4 + 2] = bias.Z;
                volumeBias[i * 4 + 3] = fogVolume.IsSphere ? 1f : 0f;

                volumeScale[i * 4] = scale.X;
                volumeScale[i * 4 + 1] = scale.Y;
                volumeScale[i * 4 + 2] = scale.Z;
                volumeScale[i * 4 + 3] = fogVolume.Strength;

                volumeExponent[i] = fogVolume.FalloffExponent;
            }

            return count;
        }

        private int StoreLights(Scene scene, Vector3 cameraPosition)
        {
            lightCandidates.Clear();

            foreach (var light in scene.AllNodes.OfType<SceneLight>())
            {
                // Fog lighting modes 1 to 4 light the fog. With 8 bit storage the live modes 2 and 4 come after every
                // other light of the view, past where the game's loop over them stops, so only 1 and 3 do.
                var litFog = lightStorageIs8Bit
                    ? light.FogLighting is 1 or 3
                    : light.FogLighting is >= 1 and <= 4;

                if (!light.Enabled || !litFog)
                {
                    continue;
                }

                // TODO: The game adds an ortho light to the fog only when it has a shadow map, which needs an orthographic one.
                // The sun lights the fog too, but filling the fog evenly with its color veils every outdoor view.
                if (light.Entity == SceneLight.EntityType.Ortho || light.Type == SceneLight.LightType.Directional)
                {
                    continue;
                }

                var scale = GetLightScale(light, cameraPosition);

                if (scale == 0f)
                {
                    continue;
                }

                // Only the lights that reach into the fog
                var reach = drawDistance + light.FogRange;
                var distanceSquared = Vector3.DistanceSquared(light.Position, cameraPosition);

                if (distanceSquared > reach * reach)
                {
                    continue;
                }

                lightCandidates.Add(new FogLightCandidate(light, scale, distanceSquared, IsStaticFogLight(light)));
            }

            // The baked lights come first, each group nearest first
            lightCandidates.Sort(static (a, b) =>
            {
                if (a.IsStatic != b.IsStatic)
                {
                    return a.IsStatic ? -1 : 1;
                }

                return a.DistanceSquared.CompareTo(b.DistanceSquared);
            });

            var count = Math.Min(lightCandidates.Count, MaxLights);
            staticLightCount = 0;

            for (var i = 0; i < count; i++)
            {
                var (light, scale, _, isStatic) = lightCandidates[i];
                var isSpot = light.Type == SceneLight.LightType.Spot;

                // The distance falloff uses the attenuation terms as they are, the color carries their scale
                var linear = light.AttenuationLinear;
                var quadratic = light.AttenuationQuadratic;
                var range = light.FogRange;
                var hasBias = light.AttenuationConstant == 0f && range > 0f;
                var falloffAtRange = (quadratic * range * range) + (linear * range) + 1.1920929e-7f;

                var color = ColorSpace.SrgbGammaToLinear(light.Color)
                    * (light.Brightness * light.BrightnessScale * light.AttenuationColorScale * light.StyleBrightness * scale);

                if (isStatic)
                {
                    staticLightCount++;
                }

                var offset = i * 4;
                lightPosition[offset] = light.Position.X;
                lightPosition[offset + 1] = light.Position.Y;
                lightPosition[offset + 2] = light.Position.Z;
                lightPosition[offset + 3] = isSpot ? 1f : 0f;

                lightDirection[offset] = light.Direction.X;
                lightDirection[offset + 1] = light.Direction.Y;
                lightDirection[offset + 2] = light.Direction.Z;
                lightDirection[offset + 3] = MathF.Cos(float.DegreesToRadians(light.SpotOuterAngle));

                lightColor[offset] = color.X;
                lightColor[offset + 1] = color.Y;
                lightColor[offset + 2] = color.Z;
                lightColor[offset + 3] = MathF.Cos(float.DegreesToRadians(light.SpotInnerAngle));

                lightFalloff[offset] = linear;
                lightFalloff[offset + 1] = quadratic;
                lightFalloff[offset + 2] = range * range;
                lightFalloff[offset + 3] = hasBias ? 1f / falloffAtRange : 0f;

                // Only modes 1 and 2 shadow the light
                var isShadowed = light.FogLighting is 1 or 2;

                lightParams[offset] = scene.LightingInfo.GetStoredLightIndex(light);
                lightParams[offset + 1] = isShadowed ? 1f : 0f;
            }

            return count;
        }

        // Fog lighting modes 1 and 3 are baked into the fog's clipmaps, 2 and 4 are lit live.
        // Without 8 bit storage there is no such split.
        private bool IsStaticFogLight(SceneLight light) => lightStorageIs8Bit && light.FogLighting is 1 or 3;

        private float GetLightScale(SceneLight light, Vector3 cameraPosition)
        {
            if (!lightStorageIs8Bit)
            {
                return 1f;
            }

            return light.FogContributionStrength * light.GetDistanceFade(cameraPosition);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            volume?.Delete();
            volume = null;
        }
    }
}
