using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.EntityLump;

#nullable disable

namespace GUI.Types.Renderer
{
    struct TonemapSettings
    {
        public float ExposureBias; // converted to linear via exp2. Kept as exposurebias for blending purposes
        public float ShoulderStrength;
        public float LinearStrength;
        public float LinearAngle;
        public float ToeStrength;
        public float ToeNum;
        public float ToeDenom;
        public float WhitePoint; // This is ran through the tonemapper before being given to the shader
        // The following params aren't used, I think?
        /*float LuminanceSource; // CS2
        float ExposureBiasShadows; // CS2
        float ExposureBiasHighlights; // CS2
        float MinShadowLum; // CS2
        float MaxShadowLum; // CS2
        float MinHighlightLum; // CS2
        float MaxHighlightLum; // CS2
        */

        // Default settings
        public TonemapSettings()
        {
            ExposureBias = 0.0f;
            ShoulderStrength = 0.15f;
            LinearStrength = 0.5f;
            LinearAngle = 0.1f;
            ToeStrength = 0.2f;
            ToeNum = 0.02f;
            ToeDenom = 0.3f;
            WhitePoint = 4.0f;
        }
        public static TonemapSettings Linear()
        {
            return new TonemapSettings()
            {
                ExposureBias = 0.0f,
                ShoulderStrength = 0.0f,
                LinearStrength = 0.0f,
                LinearAngle = 0.0f,
                ToeStrength = 1.0f,
                ToeNum = 1.0f,
                ToeDenom = 1.0f,
                WhitePoint = 2.83f,
            };
        }
        public TonemapSettings(KVObject tonemapParams)
        {
            // no "Unchecked" equivalent for KVObject
            ExposureBias = (float)tonemapParams.GetProperty<double>("m_flExposureBias");
            ShoulderStrength = (float)tonemapParams.GetProperty<double>("m_flShoulderStrength");
            LinearStrength = (float)tonemapParams.GetProperty<double>("m_flLinearStrength");
            LinearAngle = (float)tonemapParams.GetProperty<double>("m_flLinearAngle");
            ToeStrength = (float)tonemapParams.GetProperty<double>("m_flToeStrength");
            ToeNum = (float)tonemapParams.GetProperty<double>("m_flToeNum");
            ToeDenom = (float)tonemapParams.GetProperty<double>("m_flToeDenom");
            WhitePoint = (float)tonemapParams.GetProperty<double>("m_flWhitePoint");
        }
        /// <summary>
        /// Lerp between two tonemap settings based on a weight
        /// </summary>
        /// <param name="weight"></param>
        /// <param name="TonemapSettings1"></param>
        /// <param name="TonemapSettings2"></param>
        /// <returns></returns>
        public static TonemapSettings BlendTonemapSettings(float weight, TonemapSettings TonemapSettings1, TonemapSettings TonemapSettings2)
        {
            return new TonemapSettings()
            {
                ExposureBias = float.Lerp(TonemapSettings1.ExposureBias, TonemapSettings2.ExposureBias, weight),
                ShoulderStrength = float.Lerp(TonemapSettings1.ShoulderStrength, TonemapSettings2.ShoulderStrength, weight),
                LinearStrength = float.Lerp(TonemapSettings1.LinearStrength, TonemapSettings2.LinearStrength, weight),
                LinearAngle = float.Lerp(TonemapSettings1.LinearAngle, TonemapSettings2.LinearAngle, weight),
                ToeStrength = float.Lerp(TonemapSettings1.ToeStrength, TonemapSettings2.ToeStrength, weight),
                ToeNum = float.Lerp(TonemapSettings1.ToeNum, TonemapSettings2.ToeNum, weight),
                ToeDenom = float.Lerp(TonemapSettings1.ToeDenom, TonemapSettings2.ToeDenom, weight),
                WhitePoint = float.Lerp(TonemapSettings1.WhitePoint, TonemapSettings2.WhitePoint, weight),
            };
        }
        /// <summary>
        /// CPU version of the Uncharted 2 tonemapper (the tonemapping S2 uses) used to calculate White Point
        /// </summary>
        /// <param name="inputValue"></param>
        /// <returns></returns>
        public readonly float ApplyTonemapping(float inputValue) // apply exposure bias too?
        {
            var num = inputValue * (inputValue * ShoulderStrength + (LinearAngle * LinearStrength)) + (ToeStrength * ToeNum);
            var denom = inputValue * (inputValue * ShoulderStrength + LinearStrength) + (ToeStrength * ToeDenom);
            return (num / denom) - (ToeNum / ToeDenom);
        }
    };
    struct ExposureSettings
    {
        public bool AutoExposureEnabled;

        public float ExposureMin;
        public float ExposureMax;

        public float ExposureSpeedUp;
        public float ExposureSpeedDown;

        public float ExposureCompensation;

        public ExposureSettings()
        {
            AutoExposureEnabled = false;
            ExposureMin = 0.25f;
            ExposureMax = 8.0f;
            ExposureSpeedUp = 1.0f;
            ExposureSpeedDown = 1.0f;
            ExposureCompensation = 0.0f;
        }

        public void LoadFromEntity(Entity entity)
        {
            // todo: These changed to minlogexposure maxlogexposure
            ExposureMin = entity.GetPropertyUnchecked("minexposure", ExposureMin);
            ExposureMax = entity.GetPropertyUnchecked("maxexposure", ExposureMax);
            ExposureSpeedUp = entity.GetPropertyUnchecked("exposurespeedup", ExposureSpeedUp);
            ExposureSpeedDown = entity.GetPropertyUnchecked("exposurespeeddown", ExposureSpeedDown);
            ExposureCompensation = entity.GetPropertyUnchecked("exposurecompensation", ExposureCompensation);
        }
    };
    struct PostProcessState()
    {
        public TonemapSettings TonemapSettings { get; set; } = new();
        public ExposureSettings ExposureSettings { get; set; } = new();

        // for blending colorcorrectionluts this would be a List with weights, right?
        public RenderTexture ColorCorrectionLUT { get; set; }
        public float ColorCorrectionWeight { get; set; } = 1.0f;
        public int ColorCorrectionLutDimensions { get; set; } = 32;
        public int NumLutsActive { get; set; }
    };

    class SceneTonemapController(Scene scene) : SceneNode(scene)
    {
        public ExposureSettings ControllerExposureSettings { get; set; }
    }

    // make a parent TriggerSceneNode class?
    class ScenePostProcessVolume(Scene scene) : SceneNode(scene)
    {
        public float FadeTime;
        public bool UseExposure;

        public bool IsMaster;

        // Don't skip if no postprocess resource. Could still affect exposure
        public PostProcessing PostProcessingResource;
        public Model ModelVolume; // dumb

        public bool IsPostHLA;

        public bool HasTonemap;
        public TonemapSettings PostProcessTonemapSettings { get; set; } = new();
        public ExposureSettings ExposureSettings { get; set; } = new();

        public RenderTexture ColorCorrectionLUT { get; set; }
        public int ColorCorrectionLutDimensions { get; set; }

        // Bloom isn't implemented yet.
        // But the BlurWeight array corresponds to the 1/2, 1/4, 1/8, 1/16, and 1/32 resolution blur strengths respectively.
        // Same goes for BlurTint.

        // Local Contrast requires a scene blur, so that's off the table for the time being.

        // Vignette could easily be added but I haven't actually found any vpost file that uses it yet.


        // For the time being we're only going to support one LUT at a time, as that's all that can be used.
        // The most accurate method would be to go the valve route of using
        // a compute shader to blend LUTs (up to 8) at the start of every frame.

        // Additionally, due to collision detection being An Absolute Pain, we can't
        // use local post process volumes, and can only use the master.

        //
        public void LoadPostProcessResource(PostProcessing resource)
        {
            PostProcessingResource = resource;

            var tonemapParams = PostProcessingResource.GetTonemapParams();

            if (tonemapParams != null)
            {
                HasTonemap = true;
                PostProcessTonemapSettings = new TonemapSettings(tonemapParams);
            }

            // HLA has slightly different behavior. TODO
            IsPostHLA = PostProcessingResource.Data.ContainsKey("m_bHasLocalContrastParams");

            // Create color correction texture from raw data
            if (resource.HasColorCorrection())
            {
                var resolution = resource.GetColorCorrectionLUTDimension();
                var data = resource.GetColorCorrectionLUT();

                ColorCorrectionLutDimensions = resolution;

                ColorCorrectionLUT = new RenderTexture(TextureTarget.Texture3D, resolution, resolution, resolution, 1);
                ColorCorrectionLUT.SetWrapMode(TextureWrapMode.ClampToEdge);
                ColorCorrectionLUT.SetFiltering(TextureMinFilter.Linear, TextureMagFilter.Linear);
                GL.TextureStorage3D(ColorCorrectionLUT.Handle, 1, SizedInternalFormat.Rgba8, resolution, resolution, resolution);
                // COLOR CORRECTION DEBUG
#if false
                Log.Info(nameof(ScenePostProcessVolume), "CREATING COLOR CORRECTION TEXTURE");
                Log.Info(nameof(ScenePostProcessVolume), $"Dimensions: {dimensions} x {dimensions} x {dimensions}");
                Log.Info(nameof(ScenePostProcessVolume), $"Total Size in Bytes: {data.Length}");
                Log.Info(nameof(ScenePostProcessVolume), $"Number of Blocks: {data.Length / bytesPerPixel}");
                Log.Info(nameof(ScenePostProcessVolume), $"LUT Texture Handle: {ColorCorrectionLUT.Handle}");

                Log.Info(nameof(ScenePostProcessVolume), "PRINTING FIRST 32X32 BLOCK");
                for (var i = 0; i < ((dimensions * dimensions) / bytesPerPixel); i++)
                {
                    var pixelValues = new string("");
                    for (var k = 0; k < bytesPerPixel; k++)
                    {
                        if (k == 0)
                        {
                            pixelValues = $"{data[i * 4 + k] / 255.0f} ";
                        }
                        else
                        {
                            pixelValues = pixelValues.Insert(pixelValues.Length - 1, $"{data[i * 4 + k] / 255.0f} ");
                        }
                    }
                    Log.Info(nameof(ScenePostProcessVolume), pixelValues);
                }
#endif

                GL.TextureSubImage3D(ColorCorrectionLUT.Handle, 0, 0, 0, 0, resolution, resolution, resolution, PixelFormat.Rgba, PixelType.UnsignedByte, data);
            }
        }
    }
}
