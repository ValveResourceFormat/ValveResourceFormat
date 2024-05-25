using GUI.Utils;
using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer
{
    internal class PostProcessRenderer
    {
        private VrfGuiContext guiContext;
        private int vao;
        private Shader shader;

        public RenderTexture BlueNoise;
        private readonly Random random = new();

        public PostProcessState State { get; set; }
        public float TonemapScalar { get; set; }

        public PostProcessRenderer(VrfGuiContext guiContext)
        {
            this.guiContext = guiContext;
        }

        public void Load()
        {
            shader = guiContext.ShaderLoader.LoadShader("vrf.post_process");
            GL.CreateVertexArrays(1, out vao);
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
        public void Render(Framebuffer colorBuffer)
        {
            GL.DepthMask(false);
            GL.Disable(EnableCap.DepthTest);

            GL.UseProgram(shader.Program);

            // Bind textures
            shader.SetTexture(0, "g_tColorBuffer", colorBuffer.Color);
            shader.SetTexture(1, "g_tColorCorrection", State.ColorCorrectionLUT ?? guiContext.MaterialLoader.GetErrorTexture()); // todo: error postprocess texture
            shader.SetTexture(2, "g_tBlueNoise", BlueNoise);

            shader.SetUniform1("g_nNumSamplesMSAA", colorBuffer.NumSamples);

            shader.SetUniform1("g_flToneMapScalarLinear", TonemapScalar);
            SetPostProcessUniforms(shader, State.TonemapSettings);

            var invDimensions = 1.0f / State.ColorCorrectionLutDimensions;
            var invRange = new Vector2(1.0f - invDimensions, 0.5f * invDimensions);
            shader.SetUniform2("g_vColorCorrectionColorRange", invRange);
            shader.SetUniform1("g_flColorCorrectionDefaultWeight", (State.NumLutsActive > 0) ? State.ColorCorrectionWeight : 0f);

            GL.BindVertexArray(vao);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

            GL.EnableVertexAttribArray(0);
            GL.BindVertexArray(0);
            GL.UseProgram(0);

            GL.DepthMask(true);
            GL.Enable(EnableCap.DepthTest);
        }
    }
}
