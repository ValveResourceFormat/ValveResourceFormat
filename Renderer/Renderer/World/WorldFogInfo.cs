using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer.SceneEnvironment;

namespace ValveResourceFormat.Renderer.World
{
    /// <summary>
    /// Represents the fog data for the current scene.
    /// </summary>
    public class WorldFogInfo
    {
        // If we were to do this correctly, we would bin all of these entities and pick the best one at runtime.
        // We don't use triggers though, so there isn't much of a need. So we're only storing one.
        /// <summary>Gets or sets a value indicating whether gradient fog is active.</summary>
        public bool GradientFogActive { get; set; }
        /// <summary>Gets or sets a value indicating whether cubemap fog is active.</summary>
        public bool CubeFogActive { get; set; }

        // For now we're only using one gradient fog
        /// <summary>Gets or sets the active gradient fog parameters.</summary>
        public SceneGradientFog? GradientFog { get; set; }
        /// <summary>Gets or sets the active cubemap fog parameters.</summary>
        public SceneCubemapFog? CubemapFog { get; set; }

        /// <summary>
        /// Gets or sets the volumetric fog of the scene, or <see langword="null"/> when it has none.
        /// </summary>
        public VolumetricFog? Volumetric { get; set; }

        /// <summary>
        /// Gets or sets the fog of an <c>env_fog_controller</c> the 2D sky is fogged with, or
        /// <see langword="null"/> to leave the sky without it.
        /// </summary>
        public LinearFog? LinearFog { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether what is drawn takes the <see cref="LinearFog"/> as its gradient fog,
        /// as in Dota, where the fog controller fogs the whole scene.
        /// </summary>
        public bool ObjectsUseLinearFog { get; set; }

        /// <summary>
        /// Sets this to the fog a 3D sky view draws with: the sky map's own gradient fog when it has an
        /// active one, otherwise the world's, and always the world's cubemap fog.
        /// </summary>
        /// <param name="world">The fog of the map the sky belongs to.</param>
        /// <param name="sky">The fog of the sky map.</param>
        internal void SetToSkyView(WorldFogInfo world, WorldFogInfo sky)
        {
            var ownGradientFog = sky.GradientFogActive;

            GradientFogActive = ownGradientFog || world.GradientFogActive;
            GradientFog = ownGradientFog ? sky.GradientFog : world.GradientFog;
            CubeFogActive = world.CubeFogActive;
            CubemapFog = world.CubemapFog;
        }

        /// <summary>
        /// Sets this to the fog a <c>sky_camera_volume</c> sky is drawn with: the world cubemap fog only. Gradient
        /// fog is found by the world groups a view draws, and a volume sky has none of its own.
        /// </summary>
        /// <param name="world">The fog of the map the sky belongs to.</param>
        internal void SetToVolumeSkyView(WorldFogInfo world)
        {
            GradientFogActive = false;
            GradientFog = null;
            CubeFogActive = world.CubeFogActive;
            CubemapFog = world.CubemapFog;
        }

        /// <summary>
        /// Binds the volumetric fog volume to its texture unit, for the views that read it.
        /// </summary>
        internal void BindVolumetricFog()
        {
            if (Volumetric?.Volume is { } volume)
            {
                GL.BindTextureUnit((int)ReservedTextureSlots.FogVolume, volume.Handle);
            }
        }

        /// <summary>
        /// Binds the gradient fog texture to its texture unit, for the shaders that fog with one.
        /// </summary>
        internal void BindGradientFogTexture()
        {
            if (GradientFogActive && GradientFog?.Texture is { } texture)
            {
                GL.BindTextureUnit((int)ReservedTextureSlots.GradientFogTexture, texture.Handle);
            }
        }

        /// <summary>
        /// Copies the active fog state into the provided view constants buffer.
        /// </summary>
        /// <param name="viewConstants">The view constants buffer to update.</param>
        /// <param name="viewerFogEnabled">Whether fog rendering is enabled in the viewer settings.</param>
        /// <param name="fogSpace">Converts authored distances and heights into the view's space.</param>
        public void SetFogUniforms(ViewConstants viewConstants, bool viewerFogEnabled, FogSpace fogSpace)
        {
            viewConstants.VolumetricFogActive = viewerFogEnabled && Volumetric is { Volume: not null };

            if (ObjectsUseLinearFog && LinearFog is { MaxDensity: > 0f } linearFog)
            {
                SetLinearFogUniforms(viewConstants, viewerFogEnabled, linearFog, fogSpace);
                return;
            }

            viewConstants.GradientFogActive = viewerFogEnabled && GradientFogActive;
            viewConstants.CubeFogActive = viewerFogEnabled && CubeFogActive;

            if (Volumetric != null)
            {
                viewConstants.VolumetricFogNearRange = Volumetric.NearRange;
                viewConstants.VolumetricFogDepthWarp = Volumetric.DepthWarp;
            }

            if (GradientFogActive && GradientFog != null)
            {
                viewConstants.GradientFogBiasAndScale = GradientFog.GetBiasAndScale(fogSpace);
                viewConstants.GradientFogColor_Opacity = GradientFog.Color_Opacity;
                viewConstants.GradientFogExponents = GradientFog.Exponents;
                viewConstants.GradientFogCullingParams = GradientFog.CullingParams(fogSpace);
            }

            if (CubeFogActive && CubemapFog != null)
            {
                viewConstants.CubeFog_Offset_Scale_Bias_Exponent = CubemapFog.OffsetScaleBiasExponent(fogSpace);
                viewConstants.CubeFog_Height_Offset_Scale_Exponent_Log2Mip = CubemapFog.Height_OffsetScaleExponentLog2Mip(fogSpace);
                viewConstants.CubeFogCullingParams_ExposureBias_MaxOpacity = CubemapFog.CullingParams_Opacity(fogSpace);
                viewConstants.CubeFogSkyWsToOs = CubemapFog.Transform;
            }
        }

        // The gradient fog of the shaders covers a pixel by pow(saturate(distance * scale + bias), exponent) times its
        // opacity. The height term is a constant 1 here, so the culling height is unused. Without a range after the
        // start every pixel is fully in the fog, which the culling distance must not skip.
        private static void SetLinearFogUniforms(ViewConstants viewConstants, bool viewerFogEnabled, LinearFog fog, FogSpace fogSpace)
        {
            var start = fogSpace.Distance(fog.Start);
            var end = fogSpace.Distance(fog.End);
            var scale = end > start ? 1f / (end - start) : 0f;
            var bias = end > start ? -start * scale : 1f;

            viewConstants.GradientFogActive = viewerFogEnabled;
            viewConstants.CubeFogActive = false;
            viewConstants.GradientFogBiasAndScale = new Vector4(bias, 1f, scale, 0f);
            viewConstants.GradientFogColor_Opacity = new Vector4(fog.Color, fog.MaxDensity);
            viewConstants.GradientFogExponents = new Vector2(fog.Exponent, 1f);
            // A negative start fogs everything, which squaring would turn into a positive culling distance
            var nearestFogged = MathF.Max(start, 0f);
            var cullingDistance = end > start ? nearestFogged * nearestFogged : float.MinValue;
            viewConstants.GradientFogCullingParams = new Vector2(cullingDistance, float.PositiveInfinity);
        }
    }
}
