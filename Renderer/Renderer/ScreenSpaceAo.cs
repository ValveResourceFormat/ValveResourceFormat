using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// Screen space ambient occlusion computed from the resolved depth of the depth prepass, with a ground truth
/// ambient occlusion filter. The surfaces that accept it darken their indirect light by it.
/// </summary>
internal sealed class ScreenSpaceAo(RendererContext rendererContext)
{
    // The mips of the filtered depth that the horizon search steps down to
    private const int DepthMipCount = 5;

    private const int DenoisePassCount = 3;

    private const float DenoiseBlurBeta = 1.2f;

    // The passes before the last one blur less and leave the result in the scale the AO is stored in
    private const float IntermediateDenoiseBlurFactor = 0.2f;

    private Shader? prefilterShader;
    private Shader? mainShader;
    private Shader? denoiseShader;

    private RenderTexture? workingDepth;
    private RenderTexture? aoTerm;
    private RenderTexture? edges;

    /// <summary>Gets the occlusion of the last <see cref="Run"/>, 1 where nothing occludes.</summary>
    public RenderTexture? Result { get; private set; }

    private void LoadShaders()
    {
        if (prefilterShader != null)
        {
            return;
        }

        prefilterShader = rendererContext.ShaderLoader.LoadShader("xegtao.vfx", ("D_PASS", 0));
        mainShader = rendererContext.ShaderLoader.LoadShader("xegtao.vfx", ("D_PASS", 1));
        denoiseShader = rendererContext.ShaderLoader.LoadShader("xegtao.vfx", ("D_PASS", 2));
    }

    private void EnsureSize(int width, int height)
    {
        if (Result != null && Result.Width == width && Result.Height == height)
        {
            return;
        }

        Delete();

        workingDepth = RenderTexture.Create(width, height, ImageFormat.R32F, DepthMipCount, nameof(workingDepth));
        workingDepth.SetFiltering(TextureMinFilter.NearestMipmapNearest, TextureMagFilter.Nearest);
        workingDepth.SetWrapMode(RsTextureAddressMode.Clamp);
        workingDepth.SetBaseMaxLevel(0, DepthMipCount - 1);

        aoTerm = RenderTexture.Create(width, height, ImageFormat.R32F, nameof(aoTerm));
        aoTerm.SetFiltering(TextureMinFilter.Nearest, TextureMagFilter.Nearest);

        edges = RenderTexture.Create(width, height, ImageFormat.RGBA16161616F, nameof(edges));
        edges.SetFiltering(TextureMinFilter.Nearest, TextureMagFilter.Nearest);

        Result = RenderTexture.Create(width, height, ImageFormat.R32F, nameof(Result));
        Result.SetFiltering(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
        Result.SetWrapMode(RsTextureAddressMode.Clamp);
    }

    /// <summary>Computes the occlusion of the depth, filling <see cref="Result"/>. The view constants are those of the view the depth was drawn with.</summary>
    /// <param name="sceneDepth">The resolved window space depth of the view.</param>
    public void Run(RenderTexture sceneDepth)
    {
        // Too small for the depth mips, which a view this size does not show anything in
        if (Math.Min(sceneDepth.Width, sceneDepth.Height) < (1 << DepthMipCount))
        {
            return;
        }

        LoadShaders();

        using var _ = new GLDebugGroup("Screen Space Ambient Occlusion");

        EnsureSize(sceneDepth.Width, sceneDepth.Height);

        var width = sceneDepth.Width;
        var height = sceneDepth.Height;
        var pixelSize = new Vector2(1f / width, 1f / height);

        prefilterShader!.Use();
        prefilterShader.SetTexture(0, "g_tSourceDepth", sceneDepth);

        for (var mip = 0; mip < DepthMipCount; mip++)
        {
            GL.BindImageTexture(mip, workingDepth!.Handle, mip, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.R32f);
        }

        GL.DispatchCompute(MathUtils.DivideRoundUp(width, 16), MathUtils.DivideRoundUp(height, 16), 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        mainShader!.Use();
        mainShader.SetTexture(0, "g_tWorkingDepth", workingDepth!);
        mainShader.SetUniform("g_vPixelSize", pixelSize);

        GL.BindImageTexture(5, aoTerm!.Handle, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.R32f);
        GL.BindImageTexture(6, edges!.Handle, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba16f);

        GL.DispatchCompute(MathUtils.DivideRoundUp(width, 8), MathUtils.DivideRoundUp(height, 8), 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        denoiseShader!.Use();
        denoiseShader.SetTexture(1, "g_tEdges", edges);

        // The passes alternate between the two textures, ending in the result
        var denoiseInput = aoTerm!;
        var denoiseOutput = Result!;

        for (var pass = 0; pass < DenoisePassCount; pass++)
        {
            var isFinalPass = pass == DenoisePassCount - 1;

            denoiseShader.SetTexture(0, "g_tAoTerm", denoiseInput);
            denoiseShader.SetUniform("g_flDenoiseBlurBeta", isFinalPass ? DenoiseBlurBeta : DenoiseBlurBeta * IntermediateDenoiseBlurFactor);
            denoiseShader.SetUniform("g_bFinalPass", isFinalPass);

            GL.BindImageTexture(7, denoiseOutput.Handle, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.R32f);

            GL.DispatchCompute(MathUtils.DivideRoundUp(width, 8), MathUtils.DivideRoundUp(height, 8), 1);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

            (denoiseInput, denoiseOutput) = (denoiseOutput, denoiseInput);
        }
    }

    public void Delete()
    {
        workingDepth?.Delete();
        aoTerm?.Delete();
        edges?.Delete();
        Result?.Delete();

        workingDepth = null;
        aoTerm = null;
        edges = null;
        Result = null;
    }
}
