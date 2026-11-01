using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL;
using ValveKeyValue;
using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace ValveResourceFormat.Renderer.SceneEnvironment
{
    /// <summary>
    /// Filmic tonemapping curve parameters for HDR-to-LDR conversion.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/materialsystem2/PostProcessingTonemapParameters_t">PostProcessingTonemapParameters_t</seealso>
    public readonly struct TonemapSettings
    {
        /// <summary>Gets the exposure bias in log2 stops; converted to linear via exp2 before GPU upload.</summary>
        public float ExposureBias { get; init; } // converted to linear via exp2. Kept as exposurebias for blending purposes

        /// <summary>Gets the shoulder strength of the Uncharted 2 tonemapping curve.</summary>
        public float ShoulderStrength { get; init; }

        /// <summary>Gets the linear section strength of the tonemapping curve.</summary>
        public float LinearStrength { get; init; }

        /// <summary>Gets the linear section angle of the tonemapping curve.</summary>
        public float LinearAngle { get; init; }

        /// <summary>Gets the toe strength of the tonemapping curve.</summary>
        public float ToeStrength { get; init; }

        /// <summary>Gets the toe numerator of the tonemapping curve.</summary>
        public float ToeNum { get; init; }

        /// <summary>Gets the toe denominator of the tonemapping curve.</summary>
        public float ToeDenom { get; init; }

        /// <summary>Gets the white point in stops, the scene value that displays as white being two to its power.</summary>
        public float WhitePoint { get; init; }

        /// <summary>
        /// The scale the shader applies to the exposed scene before the curve.
        /// </summary>
        public const float PreTonemapScale = 2.8f;

        /// <summary>Gets the white point the curve clamps to and is normalized against: two to the power of <see cref="WhitePoint"/>.</summary>
        public readonly float EffectiveWhitePoint => MathF.Pow(2f, WhitePoint);
        // The following params aren't used, I think?
        /*float LuminanceSource; // CS2
        float ExposureBiasShadows; // CS2
        float ExposureBiasHighlights; // CS2
        float MinShadowLum; // CS2
        float MaxShadowLum; // CS2
        float MinHighlightLum; // CS2
        float MaxHighlightLum; // CS2
        */

        /// <summary>Initializes a new <see cref="TonemapSettings"/> with default filmic curve values.</summary>
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

        /// <summary>The nearly linear curve the game uses where no post processing gives one, when its game sets it to.</summary>
        public static TonemapSettings Linear()
        {
            return new TonemapSettings()
            {
                ExposureBias = 0.0f,
                ShoulderStrength = 0.0f,
                LinearStrength = 0.001f,
                LinearAngle = 0.001f,
                ToeStrength = 1.0f,
                ToeNum = 1.0f,
                ToeDenom = 1.0f,
                WhitePoint = 1.5f,
            };
        }
        /// <summary>Initializes a new <see cref="TonemapSettings"/> by reading values from a KV object.</summary>
        /// <param name="tonemapParams">The KV object containing tonemapping parameters.</param>
        public TonemapSettings(KVObject tonemapParams)
        {
            ExposureBias = tonemapParams.GetFloatProperty("m_flExposureBias");
            ShoulderStrength = tonemapParams.GetFloatProperty("m_flShoulderStrength");
            LinearStrength = tonemapParams.GetFloatProperty("m_flLinearStrength");
            LinearAngle = tonemapParams.GetFloatProperty("m_flLinearAngle");
            ToeStrength = tonemapParams.GetFloatProperty("m_flToeStrength");
            ToeNum = tonemapParams.GetFloatProperty("m_flToeNum");
            ToeDenom = tonemapParams.GetFloatProperty("m_flToeDenom");
            WhitePoint = tonemapParams.GetFloatProperty("m_flWhitePoint");
        }
        /// <summary>Linearly interpolates between two <see cref="TonemapSettings"/> by a given weight.</summary>
        /// <param name="weight">Blend factor in the range [0, 1]; 0 returns <paramref name="TonemapSettings1"/>, 1 returns <paramref name="TonemapSettings2"/>.</param>
        /// <param name="TonemapSettings1">The first (start) tonemapping settings.</param>
        /// <param name="TonemapSettings2">The second (end) tonemapping settings.</param>
        /// <returns>A new <see cref="TonemapSettings"/> whose fields are component-wise lerped between the two inputs.</returns>
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
        /// <summary>Applies the Uncharted 2 filmic tonemapping curve to a single input value.</summary>
        /// <param name="inputValue">The linear HDR value to tonemap.</param>
        /// <returns>The tonemapped LDR output value.</returns>
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
        /// <summary>Gets or sets whether automatic eye adaptation is active.</summary>
        public bool AutoExposureEnabled { get; set; }

        /// <summary>Gets the minimum allowed exposure value.</summary>
        public float ExposureMin { get; init; }

        /// <summary>Gets the maximum allowed exposure value.</summary>
        public float ExposureMax { get; init; }

        /// <summary>Gets the rate at which exposure adapts toward brighter scenes.</summary>
        public float ExposureSpeedUp { get; init; }

        /// <summary>Gets the rate at which exposure adapts toward darker scenes.</summary>
        public float ExposureSpeedDown { get; init; }

        /// <summary>Gets the luminance smoothing range used to dampen rapid exposure changes.</summary>
        public float ExposureSmoothingRange { get; init; }

        /// <summary>Gets the exposure compensation bias added on top of the adapted value.</summary>
        public float ExposureCompensation { get; init; }

        /// <summary>Initializes a new <see cref="ExposureSettings"/> with default values.</summary>
        public ExposureSettings()
        {
            AutoExposureEnabled = false;
            ExposureMin = 0.25f;
            ExposureMax = 8.0f;
            ExposureSpeedUp = 1.0f;
            ExposureSpeedDown = 2.0f;
            ExposureSmoothingRange = 100f;
            ExposureCompensation = 0.0f;
        }

        /// <summary>Creates an <see cref="ExposureSettings"/> populated from entity key-value properties.</summary>
        public static ExposureSettings LoadFromEntity(Entity entity)
        {
            var def = new ExposureSettings();
            var settings = new ExposureSettings
            {
                ExposureMin = GetExposureBound(entity, "minexposure", "minlogexposure", def.ExposureMin),
                ExposureMax = GetExposureBound(entity, "maxexposure", "maxlogexposure", def.ExposureMax),
                ExposureSpeedUp = entity.GetFloatProperty("exposurespeedup", def.ExposureSpeedUp),
                ExposureSpeedDown = entity.GetFloatProperty("exposurespeeddown", def.ExposureSpeedDown),
                ExposureCompensation = entity.GetFloatProperty("exposurecompensation", def.ExposureCompensation),
                ExposureSmoothingRange = entity.GetFloatProperty("exposuresmoothingrange", def.ExposureSmoothingRange),
                AutoExposureEnabled = entity.GetBooleanProperty("enableexposure"), // todo: test where this is enabled/disabled
            };

            return settings;
        }

        // A positive linear bound wins; otherwise the log2 bound applies
        private static float GetExposureBound(Entity entity, string linearKey, string logKey, float defaultValue)
        {
            var linear = entity.GetFloatProperty(linearKey);
            if (linear > 0f)
            {
                return linear;
            }

            if (entity.ContainsKey(logKey))
            {
                return MathF.Pow(2f, entity.GetFloatProperty(logKey));
            }

            return defaultValue;
        }

        /// <summary>Linearly interpolates the numeric fields between two exposure settings.</summary>
        /// <param name="weight">Blend factor in the range [0, 1]; 0 returns <paramref name="a"/>, 1 returns <paramref name="b"/>.</param>
        /// <param name="a">The first (start) exposure settings.</param>
        /// <param name="b">The second (end) exposure settings.</param>
        public static ExposureSettings Blend(float weight, in ExposureSettings a, in ExposureSettings b)
        {
            return new ExposureSettings
            {
                AutoExposureEnabled = a.AutoExposureEnabled || b.AutoExposureEnabled,
                ExposureMin = float.Lerp(a.ExposureMin, b.ExposureMin, weight),
                ExposureMax = float.Lerp(a.ExposureMax, b.ExposureMax, weight),
                ExposureSpeedUp = float.Lerp(a.ExposureSpeedUp, b.ExposureSpeedUp, weight),
                ExposureSpeedDown = float.Lerp(a.ExposureSpeedDown, b.ExposureSpeedDown, weight),
                ExposureSmoothingRange = float.Lerp(a.ExposureSmoothingRange, b.ExposureSmoothingRange, weight),
                ExposureCompensation = float.Lerp(a.ExposureCompensation, b.ExposureCompensation, weight),
            };
        }
    }

    /// <summary>
    /// Bloom compositing blend modes.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/materialsystem2/BloomBlendMode_t">BloomBlendMode_t</seealso>
    public enum BloomBlendType
    {
        /// <summary>Additively blends the bloom layer over the scene.</summary>
        BLOOM_BLEND_ADD,
        /// <summary>Screen blends the bloom layer with the scene.</summary>
        BLOOM_BLEND_SCREEN,
        /// <summary>Blurs the bloom layer before blending with the scene.</summary>
        BLOOM_BLEND_BLUR
    }

    /// <summary>
    /// Bloom effect parameters including threshold, intensity, and per-mip blur settings.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/materialsystem2/PostProcessingBloomParameters_t">PostProcessingBloomParameters_t</seealso>
    public readonly struct BloomSettings
    {
        /// <summary>Gets the compositing blend mode used to apply bloom.</summary>
        public BloomBlendType BlendMode { get; init; } = BloomBlendType.BLOOM_BLEND_ADD;

        /// <summary>Gets the additive bloom strength.</summary>
        public float AddBloomStrength { get; init; } = 1;

        /// <summary>Gets the screen-blend bloom strength.</summary>
        public float ScreenBloomStrength { get; init; } = 0;

        /// <summary>Gets the blur-blend bloom strength.</summary>
        public float BlurBloomStrength { get; init; } = 0;

        /// <summary>Gets the luminance threshold above which pixels contribute to bloom.</summary>
        public float BloomThreshold { get; init; } = 1.05f;

        /// <summary>Gets the width of the soft threshold knee for bloom.</summary>
        public float BloomThresholdWidth { get; init; } = 1.661f;

        /// <summary>Gets the strength multiplier applied to skybox pixels in bloom.</summary>
        public float SkyboxBloomStrength { get; init; } = 1;

        /// <summary>Gets the minimum luminance value that begins contributing to bloom.</summary>
        public float BloomStartValue { get; init; } = 1;

        /// <summary>Number of bloom blur buffers (1/2 through 1/32 resolution).</summary>
        public const int NumBlurBuffers = 5;

        /// <summary>Fixed per-mip blur weight storage. Inline so bloom states copy and blend without allocating.</summary>
        [InlineArray(NumBlurBuffers)]
        public struct BlurWeights
        {
            private float element0;
        }

        /// <summary>Fixed per-mip blur tint storage. Inline so bloom states copy and blend without allocating.</summary>
        [InlineArray(NumBlurBuffers)]
        public struct BlurTints
        {
            private Vector3 element0;
        }

        /// <summary>Gets the per-mip blur weights for the five blur buffers (1/2 through 1/32 resolution).</summary>
        public BlurWeights BlurWeight { get; init; }

        /// <summary>Gets the per-mip color tints for the five blur buffers.</summary>
        public BlurTints BlurTint { get; init; }

        /// <summary>Initializes bloom settings with the default per-mip weights and tints.</summary>
        public BloomSettings()
        {
            var weights = new BlurWeights();
            var tints = new BlurTints();

            for (var i = 0; i < NumBlurBuffers; i++)
            {
                weights[i] = 0.2f;
                tints[i] = Vector3.One;
            }

            BlurWeight = weights;
            BlurTint = tints;
        }

        /// <summary>Gets the effective bloom strength for the current <see cref="BlendMode"/>.</summary>
        public float BloomStrength =>
            BlendMode switch
            {
                BloomBlendType.BLOOM_BLEND_ADD => AddBloomStrength,
                BloomBlendType.BLOOM_BLEND_SCREEN => ScreenBloomStrength,
                BloomBlendType.BLOOM_BLEND_BLUR => BlurBloomStrength,
                _ => throw new InvalidOperationException("Invalid bloom blend type")
            };

        /// <summary>Linearly interpolates between two bloom settings. The three mode strengths blend
        /// independently, which is also how the composite shader weighs the modes against each other.</summary>
        /// <param name="weight">Blend factor in the range [0, 1]; 0 returns <paramref name="a"/>, 1 returns <paramref name="b"/>.</param>
        /// <param name="a">The first (start) bloom settings.</param>
        /// <param name="b">The second (end) bloom settings.</param>
        public static BloomSettings Blend(float weight, in BloomSettings a, in BloomSettings b)
        {
            var blurWeight = new BlurWeights();
            var blurTint = new BlurTints();

            for (var i = 0; i < NumBlurBuffers; i++)
            {
                blurWeight[i] = float.Lerp(a.BlurWeight[i], b.BlurWeight[i], weight);
                blurTint[i] = Vector3.Lerp(a.BlurTint[i], b.BlurTint[i], weight);
            }

            return new BloomSettings
            {
                BlendMode = weight < 0.5f ? a.BlendMode : b.BlendMode,
                AddBloomStrength = float.Lerp(a.AddBloomStrength, b.AddBloomStrength, weight),
                ScreenBloomStrength = float.Lerp(a.ScreenBloomStrength, b.ScreenBloomStrength, weight),
                BlurBloomStrength = float.Lerp(a.BlurBloomStrength, b.BlurBloomStrength, weight),
                BloomThreshold = float.Lerp(a.BloomThreshold, b.BloomThreshold, weight),
                BloomThresholdWidth = float.Lerp(a.BloomThresholdWidth, b.BloomThresholdWidth, weight),
                SkyboxBloomStrength = float.Lerp(a.SkyboxBloomStrength, b.SkyboxBloomStrength, weight),
                BloomStartValue = float.Lerp(a.BloomStartValue, b.BloomStartValue, weight),
                BlurWeight = blurWeight,
                BlurTint = blurTint,
            };
        }

        /// <summary>Parses a <see cref="BloomSettings"/> from a KV object.</summary>
        public static BloomSettings ParseFromKVObject(KVObject data)
        {
            var blurWeightData = data.GetFloatArray("m_flBlurWeight");
            var blurTintData = data.GetArray("m_vBlurTint").Select(v => v.ToVector3()).ToArray();

            Debug.Assert(blurWeightData.Length == NumBlurBuffers);
            Debug.Assert(blurTintData.Length == NumBlurBuffers);

            var blurWeight = new BlurWeights();
            var blurTint = new BlurTints();

            for (var i = 0; i < NumBlurBuffers; i++)
            {
                blurWeight[i] = blurWeightData[i];
                blurTint[i] = blurTintData[i];
            }

            return new BloomSettings
            {
                BlendMode = data.GetEnumValue<BloomBlendType>("m_blendMode"),
                AddBloomStrength = data.GetFloatProperty("m_flBloomStrength"),
                ScreenBloomStrength = data.GetFloatProperty("m_flScreenBloomStrength"),
                BlurBloomStrength = data.GetFloatProperty("m_flBlurBloomStrength"),
                BloomThreshold = data.GetFloatProperty("m_flBloomThreshold"),
                BloomThresholdWidth = data.GetFloatProperty("m_flBloomThresholdWidth"),
                SkyboxBloomStrength = data.GetFloatProperty("m_flSkyboxBloomStrength"),
                BloomStartValue = data.GetFloatProperty("m_flBloomStartValue"),
                BlurWeight = blurWeight,
                BlurTint = blurTint,
            };
        }
    }

    /// <summary>
    /// Vignette that darkens the screen outside of a rounded shape around its center.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/materialsystem2/PostProcessingVignetteParameters_t">PostProcessingVignetteParameters_t</seealso>
    public readonly struct VignetteSettings
    {
        /// <summary>Gets the darkening strength. Only negative values, which darken, are applied.</summary>
        public float Strength { get; init; }

        /// <summary>Gets the center of the vignette in screen space, from -1 to 1.</summary>
        public Vector2 Center { get; init; }

        /// <summary>Gets the distance from the center at which the darkening starts.</summary>
        public float Radius { get; init; }

        /// <summary>Gets how round the shape is: 0 is closest to a rectangle, 1 is an ellipse.</summary>
        public float Roundness { get; init; }

        /// <summary>Gets the distance over which the darkening fades in.</summary>
        public float Feather { get; init; }

        /// <summary>Gets the color the darkened area is tinted with.</summary>
        public Vector3 Tint { get; init; }

        /// <summary>Gets the multiplier of the fully vignetted area. Meaningful only for a negative <see cref="Strength"/>.</summary>
        public float Scale => 1f + Strength;

        /// <summary>Gets the exponent of the norm the distance from the center is measured in.</summary>
        public float Exponent => 1f / float.Lerp(0.125f, 0.5f, Roundness);

        /// <summary>Linearly interpolates between two vignette settings.</summary>
        /// <param name="weight">Blend factor in the range [0, 1]; 0 returns <paramref name="a"/>, 1 returns <paramref name="b"/>.</param>
        /// <param name="a">The first (start) vignette settings.</param>
        /// <param name="b">The second (end) vignette settings.</param>
        public static VignetteSettings Blend(float weight, in VignetteSettings a, in VignetteSettings b) => new()
        {
            Strength = float.Lerp(a.Strength, b.Strength, weight),
            Center = Vector2.Lerp(a.Center, b.Center, weight),
            Radius = float.Lerp(a.Radius, b.Radius, weight),
            Roundness = float.Lerp(a.Roundness, b.Roundness, weight),
            Feather = float.Lerp(a.Feather, b.Feather, weight),
            Tint = Vector3.Lerp(a.Tint, b.Tint, weight),
        };

        /// <summary>Parses a <see cref="VignetteSettings"/> from a KV object.</summary>
        public static VignetteSettings ParseFromKVObject(KVObject data)
        {
            return new VignetteSettings
            {
                Strength = data.GetFloatProperty("m_flVignetteStrength"),
                Center = data.GetSubCollection("m_vCenter").ToVector2(),
                Radius = data.GetFloatProperty("m_flRadius"),
                Roundness = data.GetFloatProperty("m_flRoundness"),
                Feather = data.GetFloatProperty("m_flFeather"),
                Tint = data.GetSubCollection("m_vColorTint").ToVector3(),
            };
        }
    }

    /// <summary>
    /// Combined post-processing state including tonemapping, bloom, and color grading.
    /// </summary>
    public struct PostProcessState()
    {
        /// <summary>Gets or sets the active tonemapping curve settings.</summary>
        public TonemapSettings TonemapSettings { get; set; }

        /// <summary>Gets or sets the active bloom effect settings.</summary>
        public BloomSettings BloomSettings { get; set; }

        /// <summary>Gets or sets the active automatic exposure settings.</summary>
        public ExposureSettings ExposureSettings { get; set; }

        /// <summary>Gets or sets whether bloom is active in this state.</summary>
        public bool HasBloom { get; set; } = false;

        /// <summary>Gets or sets the active vignette settings.</summary>
        public VignetteSettings VignetteSettings { get; set; }

        /// <summary>Gets or sets whether a vignette is active in this state.</summary>
        public bool HasVignette { get; set; } = false;

        // for blending colorcorrectionluts this would be a List with weights, right?
        /// <summary>Gets or sets the color correction 3D LUT texture, or <see langword="null"/> if none.</summary>
        public RenderTexture? ColorCorrectionLUT { get; set; }

        /// <summary>Gets or sets the blend weight for the color correction LUT.</summary>
        public float ColorCorrectionWeight { get; set; } = 1.0f;

        /// <summary>Gets or sets the resolution of the color correction LUT cube (e.g. 32 for a 32x32x32 LUT).</summary>
        public int ColorCorrectionLutDimensions { get; set; } = 32;

        /// <summary>Gets or sets the number of LUT layers currently active.</summary>
        public int NumLutsActive { get; set; }

        /// <summary>Gets a default <see cref="PostProcessState"/> with filmic tonemapping and no bloom.</summary>
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
        /// <summary>Gets or sets the exposure settings contributed by this controller.</summary>
        public ExposureSettings ControllerExposureSettings { get; set; }
    }

    // TODO: make a parent TriggerSceneNode class?
    /// <summary>
    /// Spatial volume that applies post-processing effects to the camera view.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CPostProcessingVolume">CPostProcessingVolume</seealso>
    public class ScenePostProcessVolume(Scene scene) : SceneNode(scene)
    {
        /// <summary>Gets the fade time in seconds when transitioning into this volume.</summary>
        public float FadeTime { get; init; } = 1.0f;

        /// <summary>Gets whether this volume overrides auto-exposure settings.</summary>
        public bool UseExposure { get; init; }

        /// <summary>Gets whether this is the global master post-process volume.</summary>
        public bool IsMaster { get; init; }

        /// <summary>Gets or sets whether this volume is enabled. A disabled volume contributes nothing.</summary>
        public bool IsEnabled { get; set; } = true;

        // Don't skip if no postprocess resource. Could still affect exposure
        /// <summary>Gets or sets the parsed post-processing resource for this volume.</summary>
        public PostProcessing? PostProcessingResource { get; set; }

        /// <summary>Gets or sets the model used as the trigger volume shape.</summary>
        public Model? ModelVolume { get; set; }

        /// <summary>Gets or sets the collider built from the volume model's physics, used to test whether the camera is inside.</summary>
        public EntityCollider? Collider { get; set; }

        /// <summary>
        /// Gets or sets the current blend weight of this volume, crossfaded towards presence over
        /// <see cref="FadeTime"/>. The master volume does not use this; it is the base state.
        /// </summary>
        public float Weight { get; set; }

        /// <summary>Whether this resource uses the newer post-Half-Life: Alyx format, detected by local contrast parameters.</summary>
        public bool IsPostHLA { get; set; }

        /// <summary>Gets or sets whether this volume has tonemapping curve data.</summary>
        public bool HasTonemap { get; set; }

        /// <summary>Gets or sets whether this volume has bloom data.</summary>
        public bool HasBloom { get; set; }

        /// <summary>Gets or sets the tonemapping settings applied by this volume.</summary>
        public TonemapSettings PostProcessTonemapSettings { get; set; } = new();

        /// <summary>Gets or sets the bloom settings applied by this volume.</summary>
        public BloomSettings BloomSettings { get; set; } = new();

        /// <summary>Gets or sets the exposure settings applied by this volume.</summary>
        public ExposureSettings ExposureSettings { get; set; } = new();

        /// <summary>Gets or sets the color correction LUT texture for this volume.</summary>
        public RenderTexture? ColorCorrectionLUT { get; set; }

        /// <summary>Gets or sets the resolution of the color correction LUT cube.</summary>
        public int ColorCorrectionLutDimensions { get; set; }

        /// <summary>Gets or sets whether this volume has vignette data.</summary>
        public bool HasVignette { get; set; }

        /// <summary>Gets or sets the vignette settings applied by this volume.</summary>
        public VignetteSettings VignetteSettings { get; set; }

        // Local Contrast requires a scene blur, so that's off the table for the time being.

        /// <summary>
        /// Reads tonemapping, bloom, and color correction data from the given post-processing resource.
        /// </summary>
        /// <param name="resource">The post-processing resource to load.</param>
        public void LoadPostProcessResource(PostProcessing resource)
        {
            PostProcessingResource = resource;

            var tonemapParams = PostProcessingResource.GetTonemapParams();

            if (tonemapParams != null)
            {
                HasTonemap = true;
                PostProcessTonemapSettings = new TonemapSettings(tonemapParams);
            }

            var bloomParams = PostProcessingResource.GetBloomParams();

            if (bloomParams != null)
            {
                HasBloom = true;
                BloomSettings = BloomSettings.ParseFromKVObject(bloomParams);
            }

            var vignetteParams = PostProcessingResource.GetVignetteParams();

            if (vignetteParams != null)
            {
                HasVignette = true;
                VignetteSettings = VignetteSettings.ParseFromKVObject(vignetteParams);
            }

            // HLA has slightly different behavior. TODO
            IsPostHLA = PostProcessingResource.Data.ContainsKey("m_bHasLocalContrastParams");

            // Create color correction texture from raw data
            if (resource.HasColorCorrection())
            {
                var resolution = resource.GetColorCorrectionLUTDimension();
                var data = resource.GetColorCorrectionLUT();

                ColorCorrectionLutDimensions = resolution;

                ColorCorrectionLUT = RenderTexture.Create3D(TextureTarget.Texture3D, resolution, resolution, resolution, ImageFormat.RGBA8888, 1, nameof(ColorCorrectionLUT));
                ColorCorrectionLUT.SetWrapMode(RsTextureAddressMode.Clamp);
                ColorCorrectionLUT.SetFiltering(TextureMinFilter.Linear, TextureMagFilter.Linear);
                GL.TextureSubImage3D(ColorCorrectionLUT.Handle, 0, 0, 0, 0, resolution, resolution, resolution, PixelFormat.Rgba, PixelType.UnsignedByte, data);
            }
        }
    }
}
