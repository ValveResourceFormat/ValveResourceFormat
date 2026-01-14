using System.Diagnostics;
using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer
{
    public class PostProcessRenderer
    {
        private readonly RendererContext RendererContext;
        private Shader? shaderMSAAResolve;
        private Shader? shaderPostProcess;
        private Shader? shaderDownsampleBloomThreshold;

        public RenderTexture? BlueNoise { get; set; }
        private readonly Random random = new();

        public PostProcessState State { get; set; }
        public bool Enabled { get; set; } = true;
        public float TonemapScalar { get; set; }
        public bool ColorCorrectionEnabled { get; set; } = true;

        private Framebuffer? MSAAResolveFrameBuffer;
        private Framebuffer? BloomDownsampleFrameBuffer;

        private RenderTexture.AttachmentFormat PostProcessrameBufferAttachmentFormat;
        private int BloomBufferDownsampleAmount = 4;

        private int BloomSampler = -1;

        public PostProcessRenderer(RendererContext rendererContext)
        {
            this.RendererContext = rendererContext;
            PostProcessrameBufferAttachmentFormat = new(PixelInternalFormat.Rgba16f, PixelFormat.Rgba, PixelType.Float);
        }

        private void CreateFramebuffer(ref Framebuffer? framebuffer)
        {
            framebuffer = Framebuffer.Prepare(nameof(framebuffer), 2, 2, 0, PostProcessrameBufferAttachmentFormat, null);
            framebuffer.CreateColorAttachment(PostProcessrameBufferAttachmentFormat, 2, 2, FramebufferAttachment.ColorAttachment0);
            framebuffer.Initialize();
            framebuffer.CheckStatus_ThrowIfIncomplete();
        }
        public void Load()
        {
            shaderMSAAResolve = RendererContext.ShaderLoader.LoadShader("vrf.msaa_resolve");
            shaderPostProcess = RendererContext.ShaderLoader.LoadShader("vrf.post_processing");
            shaderDownsampleBloomThreshold = RendererContext.ShaderLoader.LoadShader("vrf.downsample_bloomthreshold");

            CreateFramebuffer(ref MSAAResolveFrameBuffer);
            CreateFramebuffer(ref BloomDownsampleFrameBuffer);

            BloomSampler = GL.GenSampler();

            GL.SamplerParameter(BloomSampler, SamplerParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.SamplerParameter(BloomSampler, SamplerParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            GL.SamplerParameter(BloomSampler, SamplerParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.SamplerParameter(BloomSampler, SamplerParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

            GL.SamplerParameter(BloomSampler, SamplerParameterName.TextureLodBias, 0.0f);
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
            shader.SetUniform1("g_flWhitePointScale", 1.0f / TonemapSettings.ApplyTonemapping(TonemapSettings.WhitePoint));
        }

        // In CS2 Blue Noise is done optionally in msaa_resolve

        // we should have a shared FullscreenQuadRenderer class
        public void Render(Framebuffer colorBufferRead, Framebuffer colorBufferDraw, bool flipY)
        {
            Debug.Assert(shaderMSAAResolve != null);
            Debug.Assert(shaderPostProcess != null);
            Debug.Assert(shaderDownsampleBloomThreshold != null);
            Debug.Assert(BlueNoise != null);
            Debug.Assert(MSAAResolveFrameBuffer != null);
            Debug.Assert(BloomDownsampleFrameBuffer != null);

            GL.DepthMask(false);
            GL.Disable(EnableCap.DepthTest);

            // MSAA resolve
            MSAAResolveFrameBuffer.BindAndClear(FramebufferTarget.DrawFramebuffer);
            MSAAResolveFrameBuffer.Resize(colorBufferRead.Width, colorBufferRead.Height);

            shaderMSAAResolve.Use();

            shaderMSAAResolve.SetTexture(0, "g_tColorBuffer", colorBufferRead.GetColorRenderTexture(FramebufferAttachment.ColorAttachment0)!);
            shaderMSAAResolve.SetUniform1("g_bFlipY", flipY);
            shaderMSAAResolve.SetUniform1("g_nNumSamplesMSAA", colorBufferRead.NumSamples);

            GL.BindVertexArray(RendererContext.MeshBufferCache.EmptyVAO);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

            // bloom downsample and threshold
            var msaaResolvedTexture = MSAAResolveFrameBuffer.GetColorRenderTexture()!;

            msaaResolvedTexture.SetWrapMode(TextureWrapMode.ClampToEdge);
            msaaResolvedTexture.SetFiltering(TextureMinFilter.Linear, TextureMagFilter.Linear);

            shaderDownsampleBloomThreshold.Use();
            shaderDownsampleBloomThreshold.SetTexture(0, "inputTexture", msaaResolvedTexture);

            msaaResolvedTexture.SetWrapMode(TextureWrapMode.ClampToEdge);
            msaaResolvedTexture.SetFiltering(TextureMinFilter.Linear, TextureMagFilter.Linear);

            BloomDownsampleFrameBuffer.Resize(MSAAResolveFrameBuffer.Width / 4, MSAAResolveFrameBuffer.Height / 4);
            BloomDownsampleFrameBuffer.Bind(FramebufferTarget.DrawFramebuffer);

            GL.BindVertexArray(RendererContext.MeshBufferCache.EmptyVAO);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

            // Post Process
            colorBufferDraw.Bind(FramebufferTarget.DrawFramebuffer);

            shaderPostProcess.Use();

            GL.Viewport(0, 0, colorBufferRead.Width, colorBufferRead.Height);

            shaderPostProcess.SetTexture(0, "g_tColorCorrection", MSAAResolveFrameBuffer.GetColorRenderTexture(FramebufferAttachment.ColorAttachment0));
            shaderPostProcess.SetTexture(1, "g_tColorCorrectionLUT", State.ColorCorrectionLUT ?? RendererContext.MaterialLoader.GetErrorTexture()); // todo: error postprocess texture
            shaderPostProcess.SetTexture(2, "g_tBlueNoise", BlueNoise);
            shaderPostProcess.SetTexture(3, "g_tStencilBuffer", colorBufferRead.Stencil!);
            shaderPostProcess.SetUniform1("g_bFlipY", flipY);

            shaderPostProcess.SetSampler("bloomSampler", BloomSampler);

            shaderPostProcess.SetUniform1("g_bPostProcessEnabled", Enabled);

            shaderPostProcess.SetUniform1("g_flToneMapScalarLinear", TonemapScalar);
            SetPostProcessUniforms(shaderPostProcess, State.TonemapSettings);

            var invDimensions = 1.0f / State.ColorCorrectionLutDimensions;
            var invRange = new Vector2(1.0f - invDimensions, 0.5f * invDimensions);
            shaderPostProcess.SetUniform2("g_vColorCorrectionColorRange", invRange);
            shaderPostProcess.SetUniform1("g_flColorCorrectionDefaultWeight", (State.NumLutsActive > 0 && ColorCorrectionEnabled) ? State.ColorCorrectionWeight : 0f);

            GL.BindVertexArray(RendererContext.MeshBufferCache.EmptyVAO);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

            GL.UseProgram(0);
            GL.BindVertexArray(0);

            GL.DepthMask(true);
            GL.Enable(EnableCap.DepthTest);
        }
    }
}
