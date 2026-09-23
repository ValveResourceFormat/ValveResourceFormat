using ValveResourceFormat.Renderer.SceneEnvironment;

namespace ValveResourceFormat.Renderer.World
{
    /// <summary>
    /// Represents the fog data for the current scene.
    /// </summary>
    public class WorldFogInfo
    {
        //private const int VOLFOG_BBOX_LIMIT = 128;

        // If we were to do this correctly, we would bin all of these entities and pick the best one at runtime.
        // We don't use triggers though, so there isn't much of a need. So we're only storing one.
        /// <summary>Gets or sets a value indicating whether gradient fog is active.</summary>
        public bool GradientFogActive { get; set; }
        /// <summary>Gets or sets a value indicating whether cubemap fog is active.</summary>
        public bool CubeFogActive { get; set; }
        //public bool VolumetricFogActive { get; set; }

        // For now we're only using one gradient fog
        /// <summary>Gets or sets the active gradient fog parameters.</summary>
        public SceneGradientFog? GradientFog { get; set; }
        /// <summary>Gets or sets the active cubemap fog parameters.</summary>
        public SceneCubemapFog? CubemapFog { get; set; }

        /*
        // VOLUMETRIC FOG
        // Volumetric fog is a vital part of the look of Half-Life: Alyx, and it's very much possible to implement... but it's hard.
        public void LoadVolumetricFogController(EntityLump.Entity entity)
        {
            VolumetricFogActive = true;
            var fogIrradianceVolume = entity.GetStringProperty("fogirradiancevolume");
            //var anisotropy = entity.GetFloatProperty("anisotropy"); // according to the decompiled code, i think this is unused
            var drawDistance = entity.GetFloatProperty("drawdistance");
            var fogStrength = entity.GetFloatProperty("fogstrength");
            var boxMins = entity.GetStringProperty("box_mins");
            var boxMaxs = entity.GetStringProperty("box_maxs");
            var indirectVoxelDim = float.Parse(entity.GetStringProperty("indirectvoxeldim"));
            var indirectStrength = entity.GetFloatProperty("indirectstrength");
            var indirectEnabled = entity.GetBooleanProperty("indirectenabled");

            //var indirDimX = float.Parse(entity.GetStringProperty("indirectvoxeldimx"));
            //var indirDimY = float.Parse(entity.GetStringProperty("indirectvoxeldimy"));
            //var indirDimZ = float.Parse(entity.GetStringProperty("indirectvoxeldimz"));
        }


        public class FogVolume
        {
            public int Shape { get; set; }
            public float Strength { get; set; }
            public float Exponent { get; set; }
            public Matrix4x4 Transform { get; set; }
        }

        private readonly List<FogVolume> fogVolumes = new();
        public void LoadFogVolume(EntityLump.Entity entity)
        {
            if (fogVolumes.Count < VOLFOG_BBOX_LIMIT)
            {
                var fogVol = new FogVolume
                {
                    Shape = int.Parse(entity.GetStringProperty("shape")),
                    Strength = entity.GetFloatProperty("fogstrength"),
                    Exponent = entity.GetFloatProperty("falloffexponent"),
                    //Transform = SceneLightingInfo.BoxToTransform(entity)
                };

                fogVolumes.Add(fogVol);
            }
            else
            {
                Console.WriteLine($"Tried to go over limit of {VOLFOG_BBOX_LIMIT} fog volumes that could be loaded in the current map.");
            }
        }*/

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
        /// Copies the active fog state into the provided view constants buffer.
        /// </summary>
        /// <param name="viewConstants">The view constants buffer to update.</param>
        /// <param name="viewerFogEnabled">Whether fog rendering is enabled in the viewer settings.</param>
        /// <param name="fogSpace">Converts authored distances and heights into the view's space.</param>
        public void SetFogUniforms(Buffers.ViewConstants viewConstants, bool viewerFogEnabled, FogSpace fogSpace)
        {
            viewConstants.GradientFogActive = viewerFogEnabled && GradientFogActive;
            viewConstants.CubeFogActive = viewerFogEnabled && CubeFogActive;

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
    }
}
