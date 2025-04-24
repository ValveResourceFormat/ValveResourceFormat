#nullable disable

namespace GUI.Types.Renderer
{
    /// <summary>
    /// Represents the fog data for the current scene.
    /// </summary>
    class WorldFogInfo
    {
        //private const int VOLFOG_BBOX_LIMIT = 128;

        // If we were to do this correctly, we would bin all of these entities and pick the best one at runtime.
        // We don't use triggers though, so there's isn't much of a need. So we're only storing one.
        public bool GradientFogActive { get; set; }
        public bool CubeFogActive { get; set; }
        //public bool VolumetricFogActive { get; set; }

        // For now we're only using one gradient fog
        public SceneGradientFog GradientFog { get; set; }
        public SceneCubemapFog CubemapFog { get; set; }
        public RenderTexture DefaultFogTexture { get; set; }

        /*
        // VOLUMETRIC FOG
        // Volumetric fog is a vital part of the look of Half-Life: Alyx, and it's very much possible to implement... but it's hard.
        public void LoadVolumetricFogController(EntityLump.Entity entity)
        {
            VolumetricFogActive = true;
            var fogIrradianceVolume = entity.GetProperty<string>("fogirradiancevolume");
            //var anisotropy = entity.GetProperty<float>("anisotropy"); // according to the decompiled code, i think this is unused
            var drawDistance = entity.GetProperty<float>("drawdistance");
            var fogStrength = entity.GetProperty<float>("fogstrength");
            var boxMins = entity.GetProperty<string>("box_mins");
            var boxMaxs = entity.GetProperty<string>("box_maxs");
            var indirectVoxelDim = float.Parse(entity.GetProperty<string>("indirectvoxeldim"));
            var indirectStrength = entity.GetProperty<float>("indirectstrength");
            var indirectEnabled = entity.GetProperty<bool>("indirectenabled");

            //var indirDimX = float.Parse(entity.GetProperty<string>("indirectvoxeldimx"));
            //var indirDimY = float.Parse(entity.GetProperty<string>("indirectvoxeldimy"));
            //var indirDimZ = float.Parse(entity.GetProperty<string>("indirectvoxeldimz"));
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
                    Shape = int.Parse(entity.GetProperty<string>("shape")),
                    Strength = entity.GetProperty<float>("fogstrength"),
                    Exponent = entity.GetProperty<float>("falloffexponent"),
                    //Transform = SceneLightingInfo.BoxToTransform(entity)
                };

                fogVolumes.Add(fogVol);
            }
            else
            {
                Console.WriteLine($"Tried to go over limit of {VOLFOG_BBOX_LIMIT} fog volumes that could be loaded in the current map.");
            }
        }*/

        public void SetFogUniforms(UniformBuffers.ViewConstants viewConstants, bool viewerFogEnabled)
        {
            viewConstants.GradientFogActive = viewerFogEnabled && GradientFogActive;
            viewConstants.CubeFogActive = viewerFogEnabled && CubeFogActive;

            if (GradientFogActive)
            {
                viewConstants.GradientFogBiasAndScale = GradientFog.GetBiasAndScale();
                viewConstants.GradientFogColor_Opacity = GradientFog.Color_Opacity;
                viewConstants.GradientFogExponents = GradientFog.Exponents;
                viewConstants.GradientFogCullingParams = GradientFog.CullingParams;
            }

            if (CubeFogActive)
            {
                viewConstants.CubeFog_Offset_Scale_Bias_Exponent = CubemapFog.OffsetScaleBiasExponent();
                viewConstants.CubeFog_Height_Offset_Scale_Exponent_Log2Mip = CubemapFog.Height_OffsetScaleExponentLog2Mip();
                viewConstants.CubeFogCullingParams_ExposureBias_MaxOpacity = CubemapFog.CullingParams_Opacity();
                viewConstants.CubeFogSkyWsToOs = CubemapFog.Transform;
            }
        }
    }
}
