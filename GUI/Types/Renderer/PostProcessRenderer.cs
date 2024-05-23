using GUI.Utils;
using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer
{
    internal class PostProcessRenderer
    {
        private VrfGuiContext guiContext;
        private int vao;

        // To prevent shader compilation stutter, we must keep both shader combinations loaded [Richard Leadbetter voice]
        private Shader shaderNoLUT;
        private Shader shaderLUT;

        private readonly Random random = new();
        public PostProcessRenderer(VrfGuiContext guiContext)
        {
            this.guiContext = guiContext;
        }

        public void Load()
        {
            // does number of samples ever change?
            var NoLutArguments = new Dictionary<string, byte>();
            var LUTArguments = new Dictionary<string, byte> { { "F_COLOR_CORRECTION_LUT", 1 } };
            shaderNoLUT = guiContext.ShaderLoader.LoadShader("vrf.post_process", NoLutArguments);
            shaderLUT = guiContext.ShaderLoader.LoadShader("vrf.post_process", LUTArguments);


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
        public void Render(PostProcessState postProcessState, Framebuffer colorBuffer, RenderTexture blueNoise, float tonemapScalar)
        {
            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);

            GL.Disable(EnableCap.Blend);
            GL.Disable(EnableCap.CullFace);
            GL.Disable(EnableCap.DepthTest);

            var usesLut = (postProcessState.NumLutsActive > 0);

            var shader = usesLut ? shaderLUT : shaderNoLUT;

            GL.UseProgram(shader.Program);

            // Bind textures
            shader.SetTexture(0, "g_tColorBuffer", colorBuffer.Color);
            shader.SetTexture(1, "g_tBlueNoise", blueNoise);
            if (usesLut)
            {
                // use to debug handle
                shader.SetTexture(2, "g_tColorCorrection", postProcessState.ColorCorrectionLUT);
                shader.SetUniform1("g_flColorCorrectionDefaultWeight", postProcessState.ColorCorrectionWeight);

                var invDimensions = (1.0f / postProcessState.ColorCorrectionLutDimensions);
                var invRange = new Vector2(1.0f - invDimensions, 0.5f * invDimensions);
                shader.SetUniform2("g_vColorCorrectionColorRange", invRange);
            }

            shader.SetUniform1("g_flToneMapScalarLinear", tonemapScalar);

            SetPostProcessUniforms(shader, postProcessState.TonemapSettings);

            GL.BindVertexArray(vao);
            GL.DrawArrays(PrimitiveType.TriangleFan, 0, 4);

            GL.EnableVertexAttribArray(0);
            GL.BindVertexArray(0);
            GL.UseProgram(0);

            GL.DepthMask(true);
            GL.Disable(EnableCap.Blend);
            GL.Enable(EnableCap.CullFace);
            GL.Enable(EnableCap.DepthTest);
        }
    }
}
