using System.Diagnostics;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.World;

namespace ValveResourceFormat.Renderer.PostProcess
{
    /// <summary>
    /// Post-processing renderer for tonemapping, color grading, and adaptive exposure.
    /// </summary>
    public class PostProcessRenderer
    {
        private readonly RendererContext RendererContext;
        private Shader? shaderMsaaResolve;
        private Shader? shaderDepthResolve;
        private Shader? shaderPostProcess;
        private Shader? shaderPostProcessBloom;
        private Shader? shaderCombineLuts;
        private RenderTexture? combinedLut;
        private static readonly string[] LutSamplerNames =
            ["g_tColorCorrection0", "g_tColorCorrection1", "g_tColorCorrection2", "g_tColorCorrection3"];
        private readonly OutlineRenderer Outline;

        /// <summary>Gets or sets the blue noise texture used for dithering in the tonemap pass.</summary>
        public RenderTexture? BlueNoise { get; set; }
        private readonly Random random = new();

        /// <summary>Gets or sets the scene average luminance used for auto-exposure calculations.</summary>
        public float AverageLuminance { get; set; }
        /// <summary>Gets or sets the current post-processing state (tonemap, bloom, exposure settings).</summary>
        public PostProcessState State { get; set; }
        /// <summary>Gets or sets a value indicating whether post-processing is active.</summary>
        public bool Enabled { get; set; } = true;
        /// <summary>Gets or sets a value indicating whether color correction LUT application is active.</summary>
        public bool ColorCorrectionEnabled { get; set; } = true;
        /// <summary>Gets or sets a value indicating whether any scene objects require outline rendering this frame.</summary>
        public bool HasOutlineObjects { get; set; }

        /// <summary>Gets or sets the multisampled coverage mask produced by the outline geometry pass.</summary>
        public RenderTexture? OutlineMask { get; set; }

        /// <summary>Gets the per-frame raw exposure scalar history used for temporal smoothing.</summary>
        public List<float> ExposureHistory { get; } = new(10);

        /// <summary>Gets or sets a manually overridden exposure value; set to -1 to use auto-exposure.</summary>
        public float CustomExposure { get; set; } = -1;

        /// <summary>Gets or sets the display gamma, the game's brightness setting. 2.2 is identity; lower is brighter.</summary>
        public float FullScreenGamma { get; set; } = 2.2f;

        /// <summary>
        /// Gets or sets the viewer's exposure compensation in stops, added on top of whatever the post
        /// process volume authors. Applied after the exposure clamp, so it still works on the many scenes
        /// that pin exposure to an authored bound.
        /// </summary>
        public float ExposureCompensation { get; set; }

        /// <summary>The displayed luminance that auto-exposure places the scene's log-average on.</summary>
        private const float MiddleGrey = 0.18f;

        /// <summary>
        /// Gets the pre-tonemap scene luminance auto-exposure aims for: the value the active tonemap
        /// curve displays as <see cref="MiddleGrey"/>.
        /// </summary>
        public float ExposureTargetLuminance { get; private set; } = MiddleGrey;
        /// <summary>Gets the smoothed exposure value applied in the current frame.</summary>
        public float CurrentExposure { get; private set; } = 1.0f;
        /// <summary>Gets the target exposure value that <see cref="CurrentExposure"/> is adapting towards.</summary>
        public float TargetExposure { get; private set; }
        /// <summary>Gets or sets the final linear tonemap scalar passed to the post-process shader.</summary>
        public float TonemapScalar { get; set; }
        /// <summary>Gets the bloom renderer used for the multi-pass Gaussian bloom effect.</summary>
        public BloomRenderer Bloom { get; private set; }
        /// <summary>Gets the depth-of-field renderer.</summary>
        public DOFRenderer DOF { get; private set; }

        /// <summary>Gets the HDR color attachment format used by post-process framebuffers.</summary>
        public static ImageFormat DefaultColorFormat => ImageFormat.RGBA16161616F;

        /// <summary>
        /// Initializes a new <see cref="PostProcessRenderer"/> using the given renderer context.
        /// </summary>
        /// <param name="rendererContext">The renderer context providing shader loading and mesh buffer access.</param>
        public PostProcessRenderer(RendererContext rendererContext)
        {
            RendererContext = rendererContext;
            Bloom = new BloomRenderer(rendererContext, this);
            DOF = new DOFRenderer(rendererContext);
            Outline = new OutlineRenderer(rendererContext);
        }

        /// <summary>
        /// Loads all post-processing shaders and initializes sub-renderers for the given MSAA sample count.
        /// </summary>
        /// <param name="msaaSamples">The MSAA sample count used to select shader variants.</param>
        public void Load(int msaaSamples)
        {
            var msaa = (byte)msaaSamples;
            shaderMsaaResolve = RendererContext.ShaderLoader.LoadShader("msaa_resolve", ("D_MSAA_SAMPLES", msaa));
            shaderDepthResolve = RendererContext.ShaderLoader.LoadShader("depth_resolve", ("D_MSAA_SAMPLES", msaa));
            shaderPostProcess = RendererContext.ShaderLoader.LoadShader("post_processing", ("D_BLOOM", 0));
            shaderPostProcessBloom = RendererContext.ShaderLoader.LoadShader("post_processing", ("D_BLOOM", 1));
            shaderCombineLuts = RendererContext.ShaderLoader.LoadShader("combine_luts");

            DOF.MsaaSamples = msaa;
            Bloom.Load();
            Outline.Load();
        }

        /// <summary>
        /// Resolves MSAA color and/or depth from <paramref name="source"/> using compute shaders.
        /// Color and depth are written to standalone <see cref="RenderTexture"/> targets.
        /// Uses Karis average for HDR-aware color resolve, min filter for depth (conservative for reverse-Z).
        /// </summary>
        public void ResolveMsaa(Framebuffer source, RenderTexture destColor, RenderTexture destDepth,
            bool resolveColor, bool resolveDepth)
        {
            Debug.Assert(shaderMsaaResolve != null && shaderDepthResolve != null);

            var groupsX = (destColor.Width + 7) / 8;
            var groupsY = (destColor.Height + 7) / 8;

            if (resolveColor)
            {
                shaderMsaaResolve.Use();
                shaderMsaaResolve.SetTexture(0, "g_tSourceMsaa", source.Color);
                GL.BindImageTexture(1, destColor.Handle, 0, false, 0,
                    TextureAccess.WriteOnly, SizedInternalFormat.Rgba16f);
                GL.DispatchCompute(groupsX, groupsY, 1);
            }

            if (resolveDepth)
            {
                shaderDepthResolve.Use();
                shaderDepthResolve.SetTexture(0, "g_tSourceDepthMsaa", source.Depth);
                GL.BindImageTexture(1, destDepth.Handle, 0, false, 0,
                    TextureAccess.WriteOnly, SizedInternalFormat.R32f);
                GL.DispatchCompute(groupsX, groupsY, 1);
            }

            GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);
        }

        /// <summary>
        /// Resolves the frame's weighted color correction LUTs into <see cref="PostProcessState.ColorCorrectionLUT"/>.
        /// A single full-weight LUT passes through; anything else runs the combine compute shader, which
        /// weighs up to four LUTs and fills the remainder with the neutral LUT.
        /// </summary>
        /// <param name="luts">The LUTs contributing this frame with their blend weights.</param>
        public void ResolveColorCorrection(List<WeightedLut> luts)
        {
            if (luts.Count == 0)
            {
                State = State with { ColorCorrectionLUT = null, NumLutsActive = 0 };
                return;
            }

            if (luts.Count == 1 && luts[0].Weight >= 0.999f)
            {
                State = State with
                {
                    ColorCorrectionLUT = luts[0].Lut,
                    ColorCorrectionLutDimensions = luts[0].Dimensions,
                    NumLutsActive = 1,
                };
                return;
            }

            Debug.Assert(shaderCombineLuts != null);

            var dimensions = luts[0].Dimensions;

            if (combinedLut == null || combinedLut.Width != dimensions)
            {
                combinedLut?.Delete();
                combinedLut = new RenderTexture(TextureTarget.Texture3D, dimensions, dimensions, dimensions, 1, "CombinedColorCorrectionLUT");
                combinedLut.SetWrapMode(TextureWrapMode.ClampToEdge);
                combinedLut.SetFiltering(TextureMinFilter.Linear, TextureMagFilter.Linear);
                GL.TextureStorage3D(combinedLut.Handle, 1, SizedInternalFormat.Rgba8, dimensions, dimensions, dimensions);
            }

            var weights = Vector4.Zero;
            var totalWeight = 0f;

            shaderCombineLuts.Use();

            for (var i = 0; i < WorldPostProcessInfo.MaxBlendedLuts; i++)
            {
                // The combine shader texel-fetches, so every input has to share the output's dimensions;
                // a mismatched LUT drops out and its share goes to the neutral remainder.
                var valid = i < luts.Count && luts[i].Dimensions == dimensions;
                var entry = valid ? luts[i] : luts[0];
                var weight = valid ? entry.Weight : 0f;

                weights[i] = weight;
                totalWeight += weight;
                shaderCombineLuts.SetTexture(i, LutSamplerNames[i], entry.Lut);
            }

            shaderCombineLuts.SetUniform("g_vColorCorrectionWeights0", weights);
            shaderCombineLuts.SetUniform("g_flIdentityWeight", MathF.Max(0f, 1f - totalWeight));

            GL.BindImageTexture(0, combinedLut.Handle, 0, true, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba8);

            var groups = (dimensions + 3) / 4;
            GL.DispatchCompute(groups, groups, groups);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);

            State = State with
            {
                ColorCorrectionLUT = combinedLut,
                ColorCorrectionLutDimensions = dimensions,
                NumLutsActive = luts.Count,
            };
        }

        private void SetPostProcessUniforms(Shader shader, TonemapSettings TonemapSettings)
        {
            // Randomize dither offset every frame
            var ditherOffset = new Vector2(random.NextSingle(), random.NextSingle());

            // Dither by one 255th of frame color originally. Modified to be twice that, because it looks better.
            shader.SetUniform("g_vBlueNoiseDitherParams", new Vector4(ditherOffset, 1.0f / 256.0f, 2.0f / 255.0f));

            shader.SetUniform("g_flExposureBiasScaleFactor", MathF.Pow(2.0f, TonemapSettings.ExposureBias));
            shader.SetUniform("g_flShoulderStrength", TonemapSettings.ShoulderStrength);
            shader.SetUniform("g_flLinearStrength", TonemapSettings.LinearStrength);
            shader.SetUniform("g_flLinearAngle", TonemapSettings.LinearAngle);
            shader.SetUniform("g_flToeStrength", TonemapSettings.ToeStrength);
            shader.SetUniform("g_flToeNum", TonemapSettings.ToeNum);
            shader.SetUniform("g_flToeDenom", TonemapSettings.ToeDenom);

            var effectiveWhitePoint = TonemapSettings.EffectiveWhitePoint;
            var tonemappedWhitePoint = TonemapSettings.ApplyTonemapping(effectiveWhitePoint);
            shader.SetUniform("g_flWhitePoint", effectiveWhitePoint);
            shader.SetUniform("g_flWhitePointScale", 1.0f / tonemappedWhitePoint);
            shader.SetUniform("g_flFullScreenGamma", FullScreenGamma);
        }

        /// <summary>
        /// Resolves MSAA, applies DOF/bloom, tonemaps, and writes the final LDR image to <paramref name="colorBufferDraw"/>.
        /// </summary>
        public void Render(Framebuffer colorBufferRead, Framebuffer colorBufferDraw,
            RenderTexture resolveTarget, Camera camera, bool flipY)
        {
            Debug.Assert(shaderMsaaResolve != null);
            Debug.Assert(shaderPostProcess != null && shaderPostProcessBloom != null);

            Debug.Assert(BlueNoise != null);

            using var _ = RendererContext.RenderState.Scope(depthTest: false, depthWrite: false);

            using (new GLDebugGroup("MSAA Resolve"))
            {
                var msaaResolveShader = DOF.Enabled ? DOF.MsaaResolveDof : shaderMsaaResolve;

                msaaResolveShader.Use();
                msaaResolveShader.SetTexture(0, "g_tSourceMsaa", colorBufferRead.Color);
                GL.BindImageTexture(1, resolveTarget.Handle, 0, false, 0,
                    TextureAccess.WriteOnly, SizedInternalFormat.Rgba16f);
                msaaResolveShader.SetUniform("g_bFlipY", flipY);

                if (DOF.Enabled)
                {
                    DOF.SetDofResolveShaderUniforms(msaaResolveShader, camera, colorBufferRead.Depth!);
                }

                var groupsX = (resolveTarget.Width + 7) / 8;
                var groupsY = (resolveTarget.Height + 7) / 8;
                GL.DispatchCompute(groupsX, groupsY, 1);
                GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);
            }

            RenderTexture resolvedScene = resolveTarget;

            if (DOF.Enabled)
            {
                resolvedScene = DOF.Render(resolveTarget);
            }

            using (new GLDebugGroup("Tonemapping, Color Correction, Bloom"))
            {
                colorBufferDraw.Bind(FramebufferTarget.DrawFramebuffer);

                var postProcessShader = State.HasBloom == true ? shaderPostProcessBloom : shaderPostProcess;

                if (State.HasBloom)
                {
                    Bloom.Render(resolvedScene);
                }

                colorBufferDraw.Bind(FramebufferTarget.DrawFramebuffer);
                postProcessShader.Use();
                GL.Viewport(0, 0, colorBufferRead.Width, colorBufferRead.Height);

                postProcessShader.SetTexture(0, "g_tColorBuffer", resolvedScene);
                postProcessShader.SetTexture(2, "g_tColorCorrectionLUT",
                    State.ColorCorrectionLUT ?? RendererContext.MaterialLoader.GetDefaultVolume());

                // Bound here too, in case post processing runs before the scene binds it.
                postProcessShader.SetTexture((int)ReservedTextureSlots.BlueNoise, "g_tBlueNoise", BlueNoise);

                if (State.HasBloom)
                {
                    postProcessShader.SetTexture(4, "g_tBloom", Bloom.AccumulationResult);
                    // these seem to all be needed at once due to transitions between post process volumes, we don't do that yet
                    // NormalizedBloomStrengths seems to act as a blending factor "how much of each bloom mode do we have right now"
                    var bloomStrengths = new Vector3(State.BloomSettings.AddBloomStrength, State.BloomSettings.ScreenBloomStrength, State.BloomSettings.BlurBloomStrength);
                    var normalizedStrenghts = Vector3.Normalize(bloomStrengths);
                    postProcessShader.SetUniform("g_vNormalizedBloomStrengths", normalizedStrenghts);
                    postProcessShader.SetUniform("g_vUnNormalizedBloomStrengths", bloomStrengths);
                }
                postProcessShader.SetUniform("g_bFlipY", flipY);

                postProcessShader.SetUniform("g_bPostProcessEnabled", Enabled);

                postProcessShader.SetUniform("g_flToneMapScalarLinear", TonemapScalar);
                SetPostProcessUniforms(postProcessShader, State.TonemapSettings);

                var invDimensions = 1.0f / State.ColorCorrectionLutDimensions;
                var invRange = new Vector2(1.0f - invDimensions, 0.5f * invDimensions);
                postProcessShader.SetUniform("g_vColorCorrectionColorRange", invRange);
                postProcessShader.SetUniform("g_flColorCorrectionDefaultWeight", (State.NumLutsActive > 0 && ColorCorrectionEnabled) ? State.ColorCorrectionWeight : 0f);

                GL.BindVertexArray(RendererContext.MeshBufferCache.EmptyVAO);
                GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            }

            if (HasOutlineObjects)
            {
                using var outlineGroup = new GLDebugGroup("Outline Edge");
                Debug.Assert(OutlineMask != null);
                Outline.Render(OutlineMask, colorBufferRead.NumSamples, flipY);
            }
        }

        /// <summary>
        /// Updates <see cref="TonemapScalar"/> based on auto-exposure logic or <see cref="CustomExposure"/>.
        /// </summary>
        /// <param name="deltaTime">Elapsed time in seconds since the last frame, used for exposure adaptation speed.</param>
        public void CalculateTonemapScalar(float deltaTime)
        {
            var exposure = 1.0f;

            if (CustomExposure != -1)
            {
                TonemapScalar = CustomExposure;
                State = State with { ExposureSettings = State.ExposureSettings with { AutoExposureEnabled = false } };
                ReportTonemapStats();
                return;
            }

            exposure = AutoAdjustExposure(exposure, deltaTime);

            exposure *= MathF.Pow(2.0f, State.ExposureSettings.ExposureCompensation + ExposureCompensation);
            TonemapScalar = exposure;

            ReportTonemapStats();
        }

        /// <summary> Publishes tonemapping state to the render stats.</summary>
        private void ReportTonemapStats()
        {
            var settings = State.ExposureSettings;
            var stats = PerfStats.Active;

            stats.Set(Metric.SceneLuminance, AverageLuminance);
            stats.Set(Metric.TonemapScalar, TonemapScalar);
            stats.Set(Metric.FullScreenGamma, FullScreenGamma);
            stats.Set(Metric.ExposureTargetLuminance, ExposureTargetLuminance);
            stats.Set(Metric.Exposure, CurrentExposure);
            stats.Set(Metric.ExposureMin, settings.AutoExposureEnabled ? settings.ExposureMin : 0f);
            stats.Set(Metric.ExposureMax, settings.AutoExposureEnabled ? settings.ExposureMax : 0f);
        }

        private float AutoAdjustExposure(float exposure, float deltaTime)
        {
            if (!State.ExposureSettings.AutoExposureEnabled)
            {
                return exposure;
            }

            var curveInput = State.TonemapSettings.InvertTonemapping(MiddleGrey);
            ExposureTargetLuminance = float.IsNaN(curveInput) ? MiddleGrey : curveInput;

            var rawScalar = ExposureTargetLuminance / AverageLuminance;
            if (!float.IsFinite(rawScalar))
            {
                return exposure;
            }

            if (ExposureHistory.Count >= 10)
            {
                ExposureHistory.RemoveAt(0);
            }

            ExposureHistory.Add(rawScalar);

            var settings = State.ExposureSettings;
            // CurrentExposure is persistent between frames

            // Sequential min-then-max clamp: authored data contains locked (min == max) and even
            // inverted ranges
            var (min, max) = (settings.ExposureMin, settings.ExposureMax);
            var clampedScalar = MathF.Min(MathF.Max(rawScalar, min), max);
            if (ExposureHistory.Count == 10)
            {
                var weightedSum = 0.0f;
                var weightTotal = 0.0f;

                for (var i = 0; i < 10; i++)
                {
                    var weight = Math.Abs(5 - i) * 0.2f; // might be (5 - Math.Abs(5 - i))
                    weightTotal += weight;
                    weightedSum += weight * ExposureHistory[i];
                }

                clampedScalar = MathF.Min(MathF.Max(weightedSum * (1.0f / weightTotal), min), max);
            }

            if (!float.IsFinite(clampedScalar))
            {
                return CurrentExposure;
            }

            TargetExposure = clampedScalar;

            //if (Unknown > 0.0)
            //    TargetExposure *= Unknown;

            if (settings.ExposureSpeedUp == 0.0)
            {
                CurrentExposure = TargetExposure;
                return TargetExposure;
            }

            var adaptRate = CurrentExposure < TargetExposure ? settings.ExposureSpeedUp : settings.ExposureSpeedDown;

            var logCurrent = MathF.Log2(CurrentExposure);
            var logTarget = MathF.Log2(TargetExposure);
            var logDiff = MathF.Abs(logCurrent - logTarget);

            if (logDiff < settings.ExposureSmoothingRange)
            {
                adaptRate = MathF.Min(logDiff * 0.5f, adaptRate);
            }

            if (CurrentExposure > TargetExposure)
            {
                adaptRate = -adaptRate;
            }

            // Decide the clamp direction before the deltaTime multiply
            var adaptingUpward = adaptRate >= 0.0;
            adaptRate *= deltaTime;

            var newScalar = MathF.Pow(2, logCurrent + adaptRate);
            newScalar = adaptingUpward
                ? MathF.Min(newScalar, TargetExposure)
                : MathF.Max(newScalar, TargetExposure);

            if (!float.IsFinite(newScalar))
            {
                newScalar = TargetExposure;
            }

            CurrentExposure = newScalar;
            return newScalar;
        }
    }
}
