using System.Diagnostics;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Filmic tonemapping curve parameters for HDR-to-LDR conversion.
    /// </summary>
    public readonly struct TonemapSettings
    {
        public float ExposureBias { get; init; } // converted to linear via exp2. Kept as exposurebias for blending purposes
        public float ShoulderStrength { get; init; }
        public float LinearStrength { get; init; }
        public float LinearAngle { get; init; }
        public float ToeStrength { get; init; }
        public float ToeNum { get; init; }
        public float ToeDenom { get; init; }
        public float WhitePoint { get; init; } // This is ran through the tonemapper before being given to the shader
        // The following params aren't used, I think?
        /*float LuminanceSource; // CS2
        float ExposureBiasShadows; // CS2
        float ExposureBiasHighlights; // CS2
        float MinShadowLum; // CS2
        float MaxShadowLum; // CS2
        float MinHighlightLum; // CS2
        float MaxHighlightLum; // CS2
        */

        // Default settings (filmic)
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
    }

    /// <summary>
    /// Automatic exposure adjustment parameters for adaptive brightness.
    /// </summary>
    public struct ExposureSettings
    {
        public bool AutoExposureEnabled { get; set; }

        public float ExposureMin { get; init; }
        public float ExposureMax { get; init; }

        public float ExposureSpeedUp { get; init; }
        public float ExposureSpeedDown { get; init; }
        public float ExposureSmoothingRange { get; init; }

        public float ExposureCompensation { get; init; }

        public ExposureSettings()
        {
            AutoExposureEnabled = false;
            ExposureMin = 0.25f;
            ExposureMax = 8.0f;
            ExposureSpeedUp = 1.0f;
            ExposureSpeedDown = 1.0f;
            ExposureSmoothingRange = 100f;
            ExposureCompensation = 0.0f;
        }

        public static ExposureSettings LoadFromEntity(Entity entity)
        {
            var def = new ExposureSettings();
            var settings = new ExposureSettings
            {
                ExposureMin = entity.ContainsKey("minlogexposure")
                    ? MathF.Pow(2, entity.GetPropertyUnchecked<float>("minlogexposure"))
                    : entity.GetPropertyUnchecked("minexposure", def.ExposureMin),
                ExposureMax = entity.ContainsKey("maxlogexposure")
                    ? MathF.Pow(2, entity.GetPropertyUnchecked<float>("maxlogexposure"))
                    : entity.GetPropertyUnchecked("maxexposure", def.ExposureMax),
                ExposureSpeedUp = entity.GetPropertyUnchecked("exposurespeedup", def.ExposureSpeedUp),
                ExposureSpeedDown = entity.GetPropertyUnchecked("exposurespeeddown", def.ExposureSpeedDown),
                ExposureCompensation = entity.GetPropertyUnchecked("exposurecompensation", def.ExposureCompensation),
                ExposureSmoothingRange = entity.GetPropertyUnchecked("exposuresmoothingrange", def.ExposureSmoothingRange),
                AutoExposureEnabled = entity.GetProperty<bool>("enableexposure"), // todo: test where this is enabled/disabled
            };

            settings.AutoExposureEnabled = settings.AutoExposureEnabled && settings.ExposureMax > settings.ExposureMin;
            return settings;
        }
    }

    /// <summary>
    /// Bloom compositing blend modes.
    /// </summary>
    public enum BloomBlendType
    {
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
        BLOOM_BLEND_ADD,
        BLOOM_BLEND_SCREEN,
        BLOOM_BLEND_BLUR
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
    }

    /// <summary>
    /// Bloom effect parameters including threshold, intensity, and per-mip blur settings.
    /// </summary>
    public readonly struct BloomSettings()
    {
        public BloomBlendType BlendMode { get; init; } = BloomBlendType.BLOOM_BLEND_ADD;
        public float AddBloomStrength { get; init; } = 1;
        public float ScreenBloomStrength { get; init; } = 0;
        public float BlurBloomStrength { get; init; } = 0;
        public float BloomThreshold { get; init; } = 1.05f;
        public float BloomThresholdWidth { get; init; } = 1.661f;
        public float SkyboxBloomStrength { get; init; } = 1;
        public float BloomStartValue { get; init; } = 1;
        public float[] BlurWeight { get; init; } = [0.2f, 0.2f, 0.2f, 0.2f, 0.2f];
        public Vector3[] BlurTint { get; init; } = [Vector3.One, Vector3.One, Vector3.One, Vector3.One, Vector3.One];

        public float BloomStrength =>
            BlendMode switch
            {
                BloomBlendType.BLOOM_BLEND_ADD => AddBloomStrength,
                BloomBlendType.BLOOM_BLEND_SCREEN => ScreenBloomStrength,
                BloomBlendType.BLOOM_BLEND_BLUR => BlurBloomStrength,
                _ => throw new InvalidOperationException("Invalid bloom blend type")
            };


        public static BloomSettings ParseFromKVObject(KVObject data)
        {
            var settings = new BloomSettings();

            const int NumBlurBuffers = 5;
            var blurWeight = data.GetFloatArray("m_flBlurWeight");
            var blurTint = data.GetArray("m_vBlurTint", v => v.ToVector3());

            Debug.Assert(blurWeight.Length == NumBlurBuffers);
            Debug.Assert(blurTint.Length == NumBlurBuffers);

            for (var i = 0; i < NumBlurBuffers; i++)
            {
                settings.BlurWeight[i] = blurWeight[i];
                settings.BlurTint[i] = blurTint[i];
            }

            settings = settings with
            {
                BlendMode = data.GetEnumValue<BloomBlendType>("m_blendMode"),
                AddBloomStrength = data.GetFloatProperty("m_flBloomStrength"),
                ScreenBloomStrength = data.GetFloatProperty("m_flScreenBloomStrength"),
                BlurBloomStrength = data.GetFloatProperty("m_flBlurBloomStrength"),
                BloomThreshold = data.GetFloatProperty("m_flBloomThreshold"),
                BloomThresholdWidth = data.GetFloatProperty("m_flBloomThresholdWidth"),
                SkyboxBloomStrength = data.GetFloatProperty("m_flSkyboxBloomStrength"),
                BloomStartValue = data.GetFloatProperty("m_flBloomStartValue"),
                BlurWeight = data.GetFloatArray("m_flBlurWeight"),
            };

            return settings;
        }
    }

    /// <summary>
    /// Combined post-processing state including tonemapping, bloom, and color grading.
    /// </summary>
    public struct PostProcessState()
    {
        public TonemapSettings TonemapSettings { get; set; }
        public BloomSettings BloomSettings { get; set; }
        public ExposureSettings ExposureSettings { get; set; }

        public bool HasBloom { get; set; } = false;

        // for blending colorcorrectionluts this would be a List with weights, right?
        public RenderTexture? ColorCorrectionLUT { get; set; }
        public float ColorCorrectionWeight { get; set; } = 1.0f;
        public int ColorCorrectionLutDimensions { get; set; } = 32;
        public int NumLutsActive { get; set; }

        public static PostProcessState Default { get; } = new()
        {
            TonemapSettings = new TonemapSettings(),
            BloomSettings = new BloomSettings(),
            ExposureSettings = new ExposureSettings(),
        };
    }

    /// <summary>
    /// Scene node for controlling global tonemapping and exposure parameters.
    /// </summary>
    public class SceneTonemapController(Scene scene) : SceneNode(scene)
    {
        public ExposureSettings ControllerExposureSettings { get; set; }
    }

    // TODO: make a parent TriggerSceneNode class?
    /// <summary>
    /// Spatial volume that applies post-processing effects to the camera view.
    /// </summary>
    public class ScenePostProcessVolume(Scene scene) : SceneNode(scene)
    {
        public float FadeTime { get; init; }
        public bool UseExposure { get; init; }

        public bool IsMaster { get; init; }

        // Don't skip if no postprocess resource. Could still affect exposure
        public PostProcessing? PostProcessingResource { get; set; }
        public Model? ModelVolume { get; set; } // dumb

        public bool IsPostHLA { get; set; }

        public bool HasBloom { get; set; }
        public TonemapSettings PostProcessTonemapSettings { get; set; } = new();
        public BloomSettings BloomSettings { get; set; } = new();
        public ExposureSettings ExposureSettings { get; set; } = new();

        public RenderTexture? ColorCorrectionLUT { get; set; }
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
                PostProcessTonemapSettings = new TonemapSettings(tonemapParams);
            }

            var bloomParams = PostProcessingResource.GetBloomParams();

            if (bloomParams != null)
            {
                HasBloom = true;
                BloomSettings = BloomSettings.ParseFromKVObject(bloomParams);
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
                ColorCorrectionLUT.SetLabel(nameof(ColorCorrectionLUT));
                ColorCorrectionLUT.SetWrapMode(TextureWrapMode.ClampToEdge);
                ColorCorrectionLUT.SetFiltering(TextureMinFilter.Linear, TextureMagFilter.Linear);
                GL.TextureStorage3D(ColorCorrectionLUT.Handle, 1, SizedInternalFormat.Rgba8, resolution, resolution, resolution);
                GL.TextureSubImage3D(ColorCorrectionLUT.Handle, 0, 0, 0, 0, resolution, resolution, resolution, PixelFormat.Rgba, PixelType.UnsignedByte, data);
            }
        }
    }
}
