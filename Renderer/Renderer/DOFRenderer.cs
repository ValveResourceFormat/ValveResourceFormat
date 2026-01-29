using System.Diagnostics;
using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer;

public class DOFRenderer
{
    public const float GOLDEN_ANGLE = 2.39996322f; // constant to 2.39996322 i think?
    public const int MAX_DOF_SAMPLES = 256; // constant to 2.39996322 i think?

    public float NearBlurry { get; set; } = -100;
    public float NearCrisp { get; set; }

    public float FarCrisp { get; set; } = 180f;
    public float FarBlurry { get; set; } = 2000f;

    public float MaxBlurSize { get; set; } = 5.0f;
    public float RadScale { get; set; } = 0.25f;

    public float FocalDistance { get; set; } = 100f;

    private readonly float[] Offsets = new float[MAX_DOF_SAMPLES * 4];

    public bool DOFEnabled { get; set; }

    public Framebuffer? DOFFrameBuffer { get; private set; }
    public Dof2InputParams CurrentDofParams { get; private set; }

    private Shader? DOF;

    private readonly RendererContext RendererContext;

    public record struct Dof2InputParams
    {
        public float NearScale { get; set; }
        public float NearBias { get; set; }
        public float FarScale { get; set; }
        public float FarBias { get; set; }

        public float MaxBlurSize { get; set; } // r_dof2_maxblursize convar (5.0)
        public float RadScale { get; set; } // r_dof2_radiusscale convar (0.25)

        // rt size
        public int Width { get; set; }
        public int Height { get; set; }
    }

    public DOFRenderer(RendererContext rendererContext)
    {
        RendererContext = rendererContext;
    }

    public void Load()
    {
        DOFFrameBuffer = Framebuffer.Prepare("Depth Of Field", 2, 2, 0, PostProcessRenderer.DefaultColorFormat, null);
        DOFFrameBuffer.Initialize();

        DOF = RendererContext.ShaderLoader.LoadShader("vrf.dof2");
    }

    public void Render(Framebuffer input)
    {
        Debug.Assert(DOF != null);
        Debug.Assert(DOFFrameBuffer != null);

        using (new GLDebugGroup("Depth Of Field"))
        {
            DOF.Use();

            DOFFrameBuffer.Resize(input.Width, input.Height);
            DOFFrameBuffer.BindAndClear(FramebufferTarget.DrawFramebuffer);

            SetShaderParams();

            DOF.SetTexture(0, "g_tInputColor_CoC", input.Color);

            GL.BindVertexArray(RendererContext.MeshBufferCache.EmptyVAO);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        }
    }

    private Dof2InputParams CreateInputParams()
    {
        var input = new Dof2InputParams();

        var nearRange = NearCrisp - NearBlurry;
        input.NearScale = -1.0f / nearRange;
        input.NearBias = (NearBlurry / nearRange) + 1.0f;

        var farRange = FarBlurry - FarCrisp;
        input.FarScale = 1.0f / farRange;
        input.FarBias = -FarCrisp / farRange;

        input.MaxBlurSize = MaxBlurSize;
        input.RadScale = RadScale;

        input.Width = DOFFrameBuffer!.Width;
        input.Height = DOFFrameBuffer!.Height;

        return input;
    }

    public void SetShaderParams()
    {
        var newParams = CreateInputParams();

        if (CurrentDofParams.Equals(newParams))
        {
            return;
        }

        CurrentDofParams = newParams;

        var g_vInvRenderTargetDim = new Vector2(1.0f / DOFFrameBuffer!.Width, 1.0f / DOFFrameBuffer.Height);

        DOF!.SetUniform1("flMaxBlurSize", CurrentDofParams.MaxBlurSize);

        var angle = 0.0f;
        var radius = CurrentDofParams.RadScale;
        var sample = 0;
        while (sample < 256 && radius < CurrentDofParams.MaxBlurSize)
        {
            var x = MathF.Cos(angle) * g_vInvRenderTargetDim.X * radius;
            var y = MathF.Sin(angle) * g_vInvRenderTargetDim.Y * radius;

            var currentSampleIndex = sample * 4;
            Offsets[currentSampleIndex] = x;
            Offsets[currentSampleIndex + 1] = y;
            Offsets[currentSampleIndex + 2] = radius - 0.5f;
            Offsets[currentSampleIndex + 3] = radius + 0.5f;

            angle += GOLDEN_ANGLE;
            radius += CurrentDofParams.RadScale / radius;

            sample++;
        }

        DOF.SetUniform4Array("g_vSampleOffsetsRadMinMax", Offsets.Length, Offsets);
        DOF.SetUniform1("g_nSamples", sample);
    }
}

