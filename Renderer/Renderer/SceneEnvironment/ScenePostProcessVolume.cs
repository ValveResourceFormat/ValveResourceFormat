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

        /// <summary>Gets the white point value; passed through the tonemapper before GPU upload.</summary>
        public float WhitePoint { get; init; } // This is run through the tonemapper before being given to the shader

        /// <summary>
        /// The scale the shader applies to the exposed scene before the curve.
        /// </summary>
        public const float PreTonemapScale = 2.8f;

        /// <summary>Gets the white point the curve is actually normalized against: <see cref="WhitePoint"/> scaled by <see cref="PreTonemapScale"/>.</summary>
        public readonly float EffectiveWhitePoint => WhitePoint * PreTonemapScale;
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

        /// <summary>Returns a <see cref="TonemapSettings"/> intended for linear (non-filmic) tonemapping. Note: <see cref="ApplyTonemapping"/> returns a constant 0 with these values, so they are not a true passthrough curve.</summary>
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
        /// <summary>Initializes a new <see cref="TonemapSettings"/> by reading values from a KV object.</summary>
        /// <param name="tonemapParams">The KV object containing tonemapping parameters.</param>
        public TonemapSettings(KVObject tonemapParams)
        {
            ExposureBias = (float)tonemapParams.GetDoubleProperty("m_flExposureBias");
            ShoulderStrength = (float)tonemapParams.GetDoubleProperty("m_flShoulderStrength");
            LinearStrength = (float)tonemapParams.GetDoubleProperty("m_flLinearStrength");
            LinearAngle = (float)tonemapParams.GetDoubleProperty("m_flLinearAngle");
            ToeStrength = (float)tonemapParams.GetDoubleProperty("m_flToeStrength");
            ToeNum = (float)tonemapParams.GetDoubleProperty("m_flToeNum");
            ToeDenom = (float)tonemapParams.GetDoubleProperty("m_flToeDenom");
            WhitePoint = (float)tonemapParams.GetDoubleProperty("m_flWhitePoint");
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

        /// <summary>
        /// Runs the full tonemap the shader performs: the pre-curve scale and clamp, the curve, and the
        /// white point normalization.
        /// </summary>
        /// <param name="sceneValue">The exposed linear scene value.</param>
        /// <returns>The displayed value, in the 0-1 range before the sRGB encode.</returns>
        public readonly float Display(float sceneValue)
        {
            var effectiveWhitePoint = EffectiveWhitePoint;
            var curveInput = MathF.Min(sceneValue * PreTonemapScale, effectiveWhitePoint);

            return ApplyTonemapping(curveInput) / ApplyTonemapping(effectiveWhitePoint);
        }

        /// <summary>
        /// Inverts <see cref="Display"/>: the exposed scene value that displays as <paramref name="displayValue"/>.
        /// </summary>
        /// <param name="displayValue">The desired displayed value, in the 0-1 range the shader outputs.</param>
        /// <returns>
        /// The exposed linear scene value that displays as <paramref name="displayValue"/>,
        /// or <see cref="float.NaN"/> if this curve cannot be inverted.
        /// </returns>
        public readonly float InvertTonemapping(float displayValue)
        {
            // Solving ApplyTonemapping(x) = displayValue * ApplyTonemapping(EffectiveWhitePoint) for x, after
            // clearing the curve's denominator, leaves a quadratic in x with these coefficients. g is the
            // curve's numerator/denominator ratio before the ToeNum/ToeDenom shift. x is the curve input,
            // so the result is divided by the pre-curve scale to land back in scene space.
            var g = (displayValue * ApplyTonemapping(EffectiveWhitePoint)) + (ToeNum / ToeDenom);

            var a = ShoulderStrength * (1f - g);
            var b = LinearStrength * (LinearAngle - g);
            var c = ToeStrength * (ToeNum - (g * ToeDenom));

            float result;

            if (a == 0f)
            {
                // No shoulder, so the curve is a straight line.
                result = -c / b;
            }
            else
            {
                var discriminant = (b * b) - (4f * a * c);

                if (discriminant < 0f)
                {
                    return float.NaN;
                }

                var root = MathF.Sqrt(discriminant);
                result = (-b + root) / (2f * a);

                if (result <= 0f)
                {
                    result = (-b - root) / (2f * a);
                }
            }

            return (float.IsFinite(result) && result > 0f) ? result / PreTonemapScale : float.NaN;
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
            ExposureSpeedDown = 1.0f;
            ExposureSmoothingRange = 100f;
            ExposureCompensation = 0.0f;
        }

        /// <summary>Creates an <see cref="ExposureSettings"/> populated from entity key-value properties.</summary>
        public static ExposureSettings LoadFromEntity(Entity entity)
        {
            var def = new ExposureSettings();
            var settings = new ExposureSettings
            {
                ExposureMin = entity.ContainsKey("minlogexposure")
                    ? MathF.Pow(2, entity.GetFloatProperty("minlogexposure"))
                    : entity.GetFloatProperty("minexposure", def.ExposureMin),
                ExposureMax = entity.ContainsKey("maxlogexposure")
                    ? MathF.Pow(2, entity.GetFloatProperty("maxlogexposure"))
                    : entity.GetFloatProperty("maxexposure", def.ExposureMax),
                ExposureSpeedUp = entity.GetFloatProperty("exposurespeedup", def.ExposureSpeedUp),
                ExposureSpeedDown = entity.GetFloatProperty("exposurespeeddown", def.ExposureSpeedDown),
                ExposureCompensation = entity.GetFloatProperty("exposurecompensation", def.ExposureCompensation),
                ExposureSmoothingRange = entity.GetFloatProperty("exposuresmoothingrange", def.ExposureSmoothingRange),
                AutoExposureEnabled = entity.GetBooleanProperty("enableexposure"), // todo: test where this is enabled/disabled
            };

            return settings;
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
    public class ScenePostProcessVolume(Scene scene) : SceneNode(scene)
    {
        /// <summary>Gets the fade time in seconds when transitioning into this volume.</summary>
        public float FadeTime { get; init; } = 1.0f;

        /// <summary>Gets whether this volume overrides auto-exposure settings.</summary>
        public bool UseExposure { get; init; }

        /// <summary>Gets whether this is the global master post-process volume.</summary>
        public bool IsMaster { get; init; }

        /// <summary>Gets whether this volume starts disabled and contributes nothing.</summary>
        public bool StartDisabled { get; init; }

        // Don't skip if no postprocess resource. Could still affect exposure
        /// <summary>Gets or sets the parsed post-processing resource for this volume.</summary>
        public PostProcessing? PostProcessingResource { get; set; }

        /// <summary>Gets or sets the model used as the trigger volume shape.</summary>
        public Model? ModelVolume { get; set; } // dumb

        /// <summary>Gets or sets the collider built from the volume model's physics, used to test whether the camera is inside.</summary>
        public EntityCollider? Collider { get; set; }

        /// <summary>
        /// Gets or sets the current blend weight of this volume, crossfaded towards presence over
        /// <see cref="FadeTime"/>. The master volume does not use this; it is the base state.
        /// </summary>
        public float Weight { get; set; }

        /// <summary>Gets or sets whether this post-processing resource uses the newer post-Half-Life: Alyx format (detected via the presence of local contrast parameters).</summary>
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

            // HLA has slightly different behavior. TODO
            IsPostHLA = PostProcessingResource.Data.ContainsKey("m_bHasLocalContrastParams");

            // Create color correction texture from raw data
            if (resource.HasColorCorrection())
            {
                var resolution = resource.GetColorCorrectionLUTDimension();
                var data = resource.GetColorCorrectionLUT();

                ColorCorrectionLutDimensions = resolution;

                ColorCorrectionLUT = new RenderTexture(TextureTarget.Texture3D, resolution, resolution, resolution, 1, nameof(ColorCorrectionLUT));
                ColorCorrectionLUT.SetWrapMode(TextureWrapMode.ClampToEdge);
                ColorCorrectionLUT.SetFiltering(TextureMinFilter.Linear, TextureMagFilter.Linear);
                GL.TextureStorage3D(ColorCorrectionLUT.Handle, 1, SizedInternalFormat.Rgba8, resolution, resolution, resolution);
                GL.TextureSubImage3D(ColorCorrectionLUT.Handle, 0, 0, 0, 0, resolution, resolution, resolution, PixelFormat.Rgba, PixelType.UnsignedByte, data);
            }
        }
    }
}
