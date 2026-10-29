using System.Linq;
using OpenTK.Graphics.OpenGL;
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

        private readonly List<FogVolume> volumes = [];
        private readonly List<SceneLight> lightCandidates = [];
        private readonly RendererContext rendererContext;
        private RenderTexture? volume;
        private Shader? shader;

        // Anisotropy is left out. Nothing in the game's fog shaders reads it
        private float scattering;
        private float drawDistance = 600f;
        private float fadeInStart = 20f;
        private float fadeInEnd = 100f;

        private readonly float[] volumeBias = new float[MaxVolumes * 4];
        private readonly float[] volumeScale = new float[MaxVolumes * 4];
        private readonly float[] volumeExponent = new float[MaxVolumes * 4];
        private readonly float[] lightPosition = new float[MaxLights * 4];
        private readonly float[] lightDirection = new float[MaxLights * 4];
        private readonly float[] lightColor = new float[MaxLights * 4];
        private readonly float[] lightFalloff = new float[MaxLights * 4];

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
        }

        /// <summary>Lights and accumulates the fog for the view that is bound.</summary>
        /// <param name="scene">The scene whose lights light the fog.</param>
        /// <param name="cameraPosition">The position of the view the fog is built for.</param>
        public void Render(Scene scene, Vector3 cameraPosition)
        {
            if (!HasController || volumes.Count == 0)
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
            shader.SetUniform4Array("g_vFogVolumeExponent", volumeCount, volumeExponent);
            shader.SetUniform("g_nFogLightCount", lightCount);

            if (lightCount > 0)
            {
                shader.SetUniform4Array("g_vFogLightPosition_Spot", lightCount, lightPosition);
                shader.SetUniform4Array("g_vFogLightDirection_OuterCos", lightCount, lightDirection);
                shader.SetUniform4Array("g_vFogLightColor_InnerCos", lightCount, lightColor);
                shader.SetUniform4Array("g_vFogLightFalloff", lightCount, lightFalloff);
            }

            GL.BindImageTexture(0, volume.Handle, 0, true, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba16f);
            GL.DispatchCompute(MathUtils.DivideRoundUp(GridWidth, 8), MathUtils.DivideRoundUp(GridHeight, 8), 1);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
        }

        private int StoreVolumes()
        {
            var count = Math.Min(volumes.Count, MaxVolumes);

            for (var i = 0; i < count; i++)
            {
                var fogVolume = volumes[i];
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

                volumeExponent[i * 4] = fogVolume.FalloffExponent;
            }

            return count;
        }

        private int StoreLights(Scene scene, Vector3 cameraPosition)
        {
            lightCandidates.Clear();

            // TODO: directional lights, shadows and cookies
            foreach (var light in scene.AllNodes.OfType<SceneLight>())
            {
                // Only fog lighting modes 1 and 3 light the fog
                var litFog = light.FogLighting is 1 or 3;

                if (!light.Enabled || !litFog || light.FogContributionStrength == 0f || light.Type == SceneLight.LightType.Directional)
                {
                    continue;
                }

                // Only the lights that reach into the fog
                var reach = drawDistance + light.Range;

                if (Vector3.DistanceSquared(light.Position, cameraPosition) > reach * reach)
                {
                    continue;
                }

                lightCandidates.Add(light);
            }

            lightCandidates.Sort((a, b) => Vector3.DistanceSquared(a.Position, cameraPosition).CompareTo(Vector3.DistanceSquared(b.Position, cameraPosition)));

            var count = Math.Min(lightCandidates.Count, MaxLights);

            for (var i = 0; i < count; i++)
            {
                var light = lightCandidates[i];
                var isSpot = light.Type == SceneLight.LightType.Spot;

                // The distance falloff uses the attenuation terms as they are, the color carries their scale
                var linear = light.AttenuationLinear;
                var quadratic = light.AttenuationQuadratic;
                var range = light.GetAttenuatedRange();
                var hasBias = light.AttenuationConstant == 0f && range > 0f;
                var falloffAtRange = (quadratic * range * range) + (linear * range) + 1.1920929e-7f;

                var color = ColorSpace.SrgbGammaToLinear(light.Color)
                    * (light.Brightness * light.BrightnessScale * light.AttenuationColorScale * light.StyleBrightness * light.FogContributionStrength);

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
            }

            return count;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            volume?.Delete();
            volume = null;
        }
    }
}
