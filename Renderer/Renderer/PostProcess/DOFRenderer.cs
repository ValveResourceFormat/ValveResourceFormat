using System.Diagnostics;
using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer.PostProcess;

/// <summary>Depth-of-field post-processing renderer using a bokeh scatter approach.</summary>
public class DOFRenderer
{
    /// <summary>Golden angle in radians used to distribute bokeh samples in a spiral pattern.</summary>
    public const float GOLDEN_ANGLE = 2.39996322f; // constant to 2.39996322 i think?
    /// <summary>Maximum number of spiral bokeh samples.</summary>
    public const int MAX_DOF_SAMPLES = 256; // constant to 2.39996322 i think?

    /// <summary>Gets or sets the world-space depth at which the near blur becomes fully blurred.</summary>
    public float NearBlurry { get; set; } = -100;
    /// <summary>Gets or sets the world-space depth at which the near blur becomes fully sharp.</summary>
    public float NearCrisp { get; set; }

    /// <summary>Gets or sets the world-space depth at which the far blur begins.</summary>
    public float FarCrisp { get; set; } = 180f;
    /// <summary>Gets or sets the world-space depth at which the far blur is fully blurred.</summary>
    public float FarBlurry { get; set; } = 2000f;

    /// <summary>Gets or sets the maximum bokeh radius in pixels (r_dof2_maxblursize).</summary>
    public float MaxBlurSize { get; set; } = 5.0f;
    /// <summary>Gets or sets the initial bokeh spiral radius scale (r_dof2_radiusscale).</summary>
    public float RadScale { get; set; } = 0.25f;

    /// <summary>Gets or sets the world-space focal distance from the camera.</summary>
    public float FocalDistance { get; set; } = 100f;

    private readonly float[] Offsets = new float[MAX_DOF_SAMPLES * 4];

    /// <summary>Gets or sets a value indicating whether depth-of-field is active.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets the framebuffer that holds the DOF-blurred output.</summary>
    public Framebuffer? BlurredResult { get; private set; }
    /// <summary>Gets the DOF shader parameters used for the most recently rendered frame.</summary>
    public Dof2InputParams CurrentDofParams { get; private set; }

    private Shader? DOF;
    /// <summary>Gets or sets the MSAA sample count passed to the DOF resolve shader.</summary>
    public byte MsaaSamples { get; set; }
    /// <summary>Gets a lazily-created MSAA resolve shader variant that encodes circle-of-confusion in the alpha channel.</summary>
    public Lazy<Shader> MsaaResolveDof => new(() => RendererContext.ShaderLoader.LoadShader("vrf.msaa_resolve", ("D_DOF", 1), ("D_MSAA_SAMPLES", MsaaSamples)));

    private readonly RendererContext RendererContext;

    /// <summary>Packed per-frame shader inputs for the DOF pass.</summary>
    public record struct Dof2InputParams
    {
        /// <summary>Gets or sets the near CoC scale factor.</summary>
        public float NearScale { get; set; }
        /// <summary>Gets or sets the near CoC bias.</summary>
        public float NearBias { get; set; }
        /// <summary>Gets or sets the far CoC scale factor.</summary>
        public float FarScale { get; set; }
        /// <summary>Gets or sets the far CoC bias.</summary>
        public float FarBias { get; set; }

        /// <summary>Gets or sets the maximum bokeh radius in pixels (r_dof2_maxblursize convar).</summary>
        public float MaxBlurSize { get; set; } // r_dof2_maxblursize convar (5.0)
        /// <summary>Gets or sets the bokeh spiral radius scale (r_dof2_radiusscale convar).</summary>
        public float RadScale { get; set; } // r_dof2_radiusscale convar (0.25)

        // rt size
        /// <summary>Gets or sets the render target width in pixels.</summary>
        public int Width { get; set; }
        /// <summary>Gets or sets the render target height in pixels.</summary>
        public int Height { get; set; }
    }

    /// <summary>
    /// Initializes a new <see cref="DOFRenderer"/> using the given renderer context.
    /// </summary>
    /// <param name="rendererContext">The renderer context providing shader loading and mesh buffer access.</param>
    public DOFRenderer(RendererContext rendererContext)
    {
        RendererContext = rendererContext;
    }

    /// <summary>
    /// Applies depth-of-field blur. Returns the blurred color texture.
    /// </summary>
    public RenderTexture Render(RenderTexture input)
    {
        if (DOF == null)
        {
            DOF = RendererContext.ShaderLoader.LoadShader("vrf.dof2");
            BlurredResult = Framebuffer.Prepare("Depth Of Field", 2, 2, 0, PostProcessRenderer.DefaultColorFormat, null);
            BlurredResult.Initialize();
        }

        Debug.Assert(DOF != null);
        Debug.Assert(BlurredResult != null);

        using (new GLDebugGroup("Depth Of Field"))
        {
            DOF.Use();

            BlurredResult.Resize(input.Width, input.Height);
            BlurredResult.BindAndClear(FramebufferTarget.DrawFramebuffer);

            SetShaderParams();

            DOF.SetTexture(0, "g_tInputColor_CoC", input);

            GL.BindVertexArray(RendererContext.MeshBufferCache.EmptyVAO);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        }

        return BlurredResult.Color!;
    }

    /// <summary>
    /// Sets depth and lens-plane uniforms on the MSAA resolve shader for circle-of-confusion encoding.
    /// </summary>
    /// <param name="shader">The MSAA resolve shader to configure.</param>
    /// <param name="camera">The active camera providing view/projection matrices and position.</param>
    /// <param name="msaaDepth">The MSAA depth texture used to reconstruct world-space depth.</param>
    public void SetDofResolveShaderUniforms(Shader shader, Camera camera, RenderTexture msaaDepth)
    {
        shader.SetTexture(2, "g_tSceneDepth", msaaDepth);

        Matrix4x4.Invert(camera.ViewProjectionMatrix, out var invViewProjMatrix);
        shader.SetUniform4x4("g_invViewProjMatrix", invViewProjMatrix);

        var focalPoint = camera.Location + camera.Forward * FocalDistance;
        var d = -Vector3.Dot(camera.Forward, focalPoint);

        var lensPlane = new Vector4(camera.Forward, d);
        shader.SetUniform4("g_vLensPlane", lensPlane);

        shader.SetUniform4("g_vNearFarScaleBias", new Vector4(CurrentDofParams.NearScale, CurrentDofParams.NearBias, CurrentDofParams.FarScale, CurrentDofParams.FarBias));
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

        input.Width = BlurredResult!.Width;
        input.Height = BlurredResult!.Height;

        return input;
    }

    /// <summary>
    /// Uploads DOF shader uniforms if the current parameters have changed since the last call.
    /// </summary>
    public void SetShaderParams()
    {
        var newParams = CreateInputParams();

        if (CurrentDofParams.Equals(newParams))
        {
            return;
        }

        CurrentDofParams = newParams;

        var g_vInvRenderTargetDim = new Vector2(1.0f / BlurredResult!.Width, 1.0f / BlurredResult.Height);

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

