using System.Diagnostics;
using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer
{
    public class PostProcessRenderer
    {
        private readonly RendererContext RendererContext;
        private Shader? shaderMSAAResolve;
        private Shader? shaderPostProcess;
        private Shader? shaderPostProcessBloom;
        private Framebuffer? MsaaResolveFramebuffer;

        public RenderTexture? BlueNoise { get; set; }
        private readonly Random random = new();

        public float AverageLuminance { get; set; }
        public PostProcessState State { get; set; }
        public bool Enabled { get; set; } = true;
        public bool ColorCorrectionEnabled { get; set; } = true;

        public List<float> ExposureHistory { get; } = new(10);
        public float CustomExposure { get; set; } = -1;
        public float CurrentExposure { get; private set; } = 1.0f;
        public float TargetExposure { get; private set; }
        public float TonemapScalar { get; set; }
        public BloomRenderer Bloom { get; private set; }

        public static Framebuffer.AttachmentFormat DefaultColorFormat => new(PixelInternalFormat.Rgba16f, PixelFormat.Rgba, PixelType.Float);

        public PostProcessRenderer(RendererContext rendererContext)
        {
            RendererContext = rendererContext;
            Bloom = new BloomRenderer(rendererContext, this);
        }

        public void Load()
        {
            shaderMSAAResolve = RendererContext.ShaderLoader.LoadShader("vrf.msaa_resolve");
            shaderPostProcess = RendererContext.ShaderLoader.LoadShader("vrf.post_processing", ("D_BLOOM", 0));
            shaderPostProcessBloom = RendererContext.ShaderLoader.LoadShader("vrf.post_processing", ("D_BLOOM", 1));

            MsaaResolveFramebuffer = Framebuffer.Prepare("Multi Sampling Resolve", 2, 2, 0, DefaultColorFormat, null);
            MsaaResolveFramebuffer.Initialize();

            Bloom.Load();
        }

        private void SetPostProcessUniforms(Shader shader, TonemapSettings TonemapSettings)
        {
            // Randomize dither offset every frame
            var ditherOffset = new Vector2(random.NextSingle(), random.NextSingle());

            // Dither by one 255th of frame color originally. Modified to be twice that, because it looks better.
            shader.SetUniform4("g_vBlueNoiseDitherParams", new Vector4(ditherOffset, 1.0f / 256.0f, 2.0f / 255.0f));

            shader.SetUniform1("g_flExposureBiasScaleFactor", MathF.Pow(2.0f, TonemapSettings.ExposureBias));
            shader.SetUniform1("g_flShoulderStrength", TonemapSettings.ShoulderStrength);
            shader.SetUniform1("g_flLinearStrength", TonemapSettings.LinearStrength);
            shader.SetUniform1("g_flLinearAngle", TonemapSettings.LinearAngle);
            shader.SetUniform1("g_flToeStrength", TonemapSettings.ToeStrength);
            shader.SetUniform1("g_flToeNum", TonemapSettings.ToeNum);
            shader.SetUniform1("g_flToeDenom", TonemapSettings.ToeDenom);

            var tonemappedWhitePoint = TonemapSettings.ApplyTonemapping(TonemapSettings.WhitePoint);
            shader.SetUniform1("g_flWhitePoint", TonemapSettings.WhitePoint);
            shader.SetUniform1("g_flWhitePointScale", 1.0f / tonemappedWhitePoint);
        }

        public void Render(Framebuffer colorBufferRead, Framebuffer colorBufferDraw, bool flipY)
        {
            Debug.Assert(shaderMSAAResolve != null);
            Debug.Assert(shaderPostProcess != null && shaderPostProcessBloom != null);

            Debug.Assert(BlueNoise != null);
            Debug.Assert(MsaaResolveFramebuffer != null);

            GL.DepthMask(false);
            GL.Disable(EnableCap.DepthTest);

            using (new GLDebugGroup("MSAA Resolve"))
            {
                MsaaResolveFramebuffer.Resize(colorBufferRead.Width, colorBufferRead.Height);
                MsaaResolveFramebuffer.BindAndClear(FramebufferTarget.DrawFramebuffer);

                shaderMSAAResolve.Use();

                shaderMSAAResolve.SetTexture(0, "g_tColorBuffer", colorBufferRead.Color);
                shaderMSAAResolve.SetUniform1("g_bFlipY", flipY);
                shaderMSAAResolve.SetUniform1("g_nNumSamplesMSAA", colorBufferRead.NumSamples);

                GL.BindVertexArray(RendererContext.MeshBufferCache.EmptyVAO);
                GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            }

            using (new GLDebugGroup("Tonemapping, Color Correction, Bloom"))
            {
                colorBufferDraw.Bind(FramebufferTarget.DrawFramebuffer);

                var postProcessShader = State.HasBloom == true ? shaderPostProcessBloom : shaderPostProcess;

                if (State.HasBloom)
                {
                    Bloom.Render(MsaaResolveFramebuffer);
                }

                colorBufferDraw.Bind(FramebufferTarget.DrawFramebuffer);
                postProcessShader.Use();
                GL.Viewport(0, 0, colorBufferRead.Width, colorBufferRead.Height);

                postProcessShader.SetTexture(0, "g_tColorBuffer", MsaaResolveFramebuffer.Color);
                postProcessShader.SetTexture(1, "g_tColorCorrectionLUT", State.ColorCorrectionLUT ?? RendererContext.MaterialLoader.GetErrorTexture()); // todo: error postprocess texture
                postProcessShader.SetTexture(2, "g_tBlueNoise", BlueNoise);
                postProcessShader.SetTexture(3, "g_tStencilBuffer", colorBufferRead.Stencil!);

                if (State.HasBloom)
                {
                    postProcessShader.SetTexture(4, "g_tBloom", Bloom.AccumulationResult);
                    // these seems to all be needed at once due to transitions between post process volumes, we dont do that yet
                    // NormalizedBloomStrengths seems to act as a blending factor "how much of each bloom mode do we have right now"
                    var bloomStrengths = new Vector3(State.BloomSettings.AddBloomStrength, State.BloomSettings.ScreenBloomStrength, State.BloomSettings.BlurBloomStrength);
                    var normalizedStrenghts = Vector3.Normalize(bloomStrengths);
                    postProcessShader.SetUniform3("g_vNormalizedBloomStrengths", normalizedStrenghts);
                    postProcessShader.SetUniform3("g_vUnNormalizedBloomStrengths", bloomStrengths);
                }
                postProcessShader.SetUniform1("g_bFlipY", flipY);

                postProcessShader.SetUniform1("g_bPostProcessEnabled", Enabled);

                postProcessShader.SetUniform1("g_flToneMapScalarLinear", TonemapScalar);
                SetPostProcessUniforms(postProcessShader, State.TonemapSettings);

                var invDimensions = 1.0f / State.ColorCorrectionLutDimensions;
                var invRange = new Vector2(1.0f - invDimensions, 0.5f * invDimensions);
                postProcessShader.SetUniform2("g_vColorCorrectionColorRange", invRange);
                postProcessShader.SetUniform1("g_flColorCorrectionDefaultWeight", (State.NumLutsActive > 0 && ColorCorrectionEnabled) ? State.ColorCorrectionWeight : 0f);

                GL.BindVertexArray(RendererContext.MeshBufferCache.EmptyVAO);
                GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            }

            GL.UseProgram(0);
            GL.BindVertexArray(0);

            GL.DepthMask(true);
            GL.Enable(EnableCap.DepthTest);
        }

        public void CalculateTonemapScalar(float deltaTime)
        {
            var exposure = 1.0f;

            if (CustomExposure != -1)
            {
                TonemapScalar = CustomExposure;
                State = State with { ExposureSettings = State.ExposureSettings with { AutoExposureEnabled = false } };
                return;
            }

            exposure = AutoAdjustExposure(exposure, deltaTime);

            exposure *= MathF.Pow(2.0f, State.ExposureSettings.ExposureCompensation);
            TonemapScalar = exposure;
        }

        private float AutoAdjustExposure(float exposure, float deltaTime)
        {
            if (!State.ExposureSettings.AutoExposureEnabled)
            {
                return exposure;
            }

            // Implement auto-exposure logic
            var rawScalar = 0.18f / AverageLuminance;
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

            var (min, max) = (settings.ExposureMin, settings.ExposureMax);
            var clampedScalar = Math.Clamp(rawScalar, min, max);
            if (ExposureHistory.Count == 10)
            {
                var weightedSum = 0.0f;
                var weightTotal = 0.0f;

                for (var i = 0; i < 10; i++)
                {
                    var weight = Math.Abs(5 - i) * 0.2f;
                    weightTotal += weight;
                    weightedSum += weight * ExposureHistory[i];
                }

                clampedScalar = Math.Clamp(weightedSum / weightTotal, min, max);
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

            adaptRate *= deltaTime;

            var newScalar = MathF.Pow(2, logCurrent + adaptRate);
            newScalar = adaptRate >= 0.0
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
