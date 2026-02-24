using System.Diagnostics;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL;
using Vector2i = OpenTK.Mathematics.Vector2i;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// Post-processing renderer that creates bloom effects using multi-pass Gaussian blur.
/// </summary>
public class BloomRenderer
{
    private Shader? firstDownsampleBloomThreshold;
    private Shader? downsample;
    private Shader? horizontalBlur;
    private Shader? verticalBlur;
    private Shader? firstUpsample;
    private Shader? upsample;

    private Framebuffer? Ping;
    private Framebuffer? Pong;
    private Framebuffer? Accumulation;

    public RenderTexture? AccumulationResult { get; private set; }

    public const int BloomMipCount = 4;
    private readonly RendererContext RendererContext;
    private readonly PostProcessRenderer PostProcessRenderer;

    public BloomRenderer(RendererContext rendererContext, PostProcessRenderer postProcessRenderer)
    {
        RendererContext = rendererContext;
        PostProcessRenderer = postProcessRenderer;
    }

    public void Load()
    {
        firstDownsampleBloomThreshold = RendererContext.ShaderLoader.LoadShader("vrf.downsample_bloomthreshold");
        downsample = RendererContext.ShaderLoader.LoadShader("vrf.gaussian_bloom_blur");
        horizontalBlur = RendererContext.ShaderLoader.LoadShader("vrf.gaussian_bloom_blur", ("D_BLUR_PASS", 1), ("D_BLUR_PASS_HORIZONTAL", 1));
        verticalBlur = RendererContext.ShaderLoader.LoadShader("vrf.gaussian_bloom_blur", ("D_BLUR_PASS", 1), ("D_BLUR_PASS_HORIZONTAL", 0));
        upsample = RendererContext.ShaderLoader.LoadShader("vrf.gaussian_bloom_blur", ("D_BLUR_PASS", 2));
        firstUpsample = RendererContext.ShaderLoader.LoadShader("vrf.gaussian_bloom_blur", ("D_BLUR_PASS", 3));

        Ping = CreateFramebuffer("BloomPing");
        Pong = CreateFramebuffer("BloomPong");
        Accumulation = CreateFramebuffer("BloomAccumulation", BloomMipCount);

        AccumulationResult = Accumulation!.Color!;
    }

    private static Framebuffer CreateFramebuffer(string name, int mips = 1)
    {
        var framebuffer = Framebuffer.Prepare(name, 4, 4, 0, PostProcessRenderer.DefaultColorFormat, null);
        framebuffer.NumMips = mips;
        framebuffer.Initialize();
        framebuffer.CheckStatus_ThrowIfIncomplete();
        return framebuffer;
    }

    public void Render(Framebuffer input)
    {
        Debug.Assert(firstDownsampleBloomThreshold != null);
        Debug.Assert(downsample != null);
        Debug.Assert(horizontalBlur != null);
        Debug.Assert(verticalBlur != null);
        Debug.Assert(upsample != null);
        Debug.Assert(firstUpsample != null);

        Debug.Assert(Accumulation != null && Accumulation.Color != null);
        Debug.Assert(Ping != null && Ping.Color != null);
        Debug.Assert(Pong != null && Pong.Color != null);

        Vector2i maxBloomRes = new(input.Width, input.Height);

        // Start at 1/4 resolution
        maxBloomRes /= 4;

        static bool InvalidSize(Vector2i size) => size.X < 16 || size.Y < 16;

        // skip bloom if the resolution is too small
        if (InvalidSize(maxBloomRes))
        {
            Accumulation.BindAndClear();
            return;
        }

        var settings = PostProcessRenderer.State.BloomSettings;
        var tonemapScalar = PostProcessRenderer.TonemapScalar;

        Accumulation.Resize(maxBloomRes.X, maxBloomRes.Y);
        Ping.Resize(maxBloomRes.X, maxBloomRes.Y);
        Pong.Resize(maxBloomRes.X, maxBloomRes.Y);

        // todo: only set these when size actually changes (texture re-allocation)
        Accumulation.Color.SetFiltering(TextureMinFilter.LinearMipmapLinear, TextureMagFilter.Linear);
        Accumulation.Color.SetWrapMode(TextureWrapMode.ClampToEdge);

        Ping.Color.SetFiltering(TextureMinFilter.Linear, TextureMagFilter.Linear);
        Ping.Color.SetWrapMode(TextureWrapMode.ClampToEdge);
        Pong.Color.SetFiltering(TextureMinFilter.Linear, TextureMagFilter.Linear);
        Pong.Color.SetWrapMode(TextureWrapMode.ClampToEdge);

        using (new GLDebugGroup("Bloom Downsample Threshold Pass"))
        {
            var inputTexture = input.Color;
            Debug.Assert(inputTexture != null && inputTexture.Target == TextureTarget.Texture2D);

            firstDownsampleBloomThreshold.Use();
            firstDownsampleBloomThreshold.SetTexture(0, "inputTexture", inputTexture);
            inputTexture.SetFiltering(TextureMinFilter.Linear, TextureMagFilter.Linear);
            inputTexture.SetWrapMode(TextureWrapMode.ClampToEdge);

            Ping.Bind(FramebufferTarget.DrawFramebuffer);
            GL.Viewport(0, 0, Ping.Width, Ping.Height);

            var thresholdParams = new Vector2(settings.BloomThreshold / settings.BloomThresholdWidth * -1, 1 / settings.BloomThresholdWidth);
            firstDownsampleBloomThreshold.SetUniform1("bloomScale", settings.BloomStrength);
            firstDownsampleBloomThreshold.SetUniform1("g_flToneMapScalarLinear", tonemapScalar);
            firstDownsampleBloomThreshold.SetUniform2("thresholdParams", thresholdParams);

            GL.BindVertexArray(RendererContext.MeshBufferCache.EmptyVAO);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        }

        var lastWrittenMip = 0;
        var downsampledSize = maxBloomRes;

        for (var i = 0; i < BloomMipCount; i++)
        {
            using var _ = new GLDebugGroup(i switch { 0 => "Bloom Accumulation 0", 1 => "Bloom Accumulation 1", 2 => "Bloom Accumulation 2", 3 => "Bloom Accumulation 3", _ => "Bloom Accumulation" });

            if (InvalidSize(downsampledSize))
            {
                break;
            }

            if (i != 0)
            {
                Ping.BindAndClear();
                Pong.BindAndClear();

                // cheap downsample from previous mip
                // Accumulation.AttachColorMipLevel(i - 1);
                RenderTexture(downsample, Accumulation, Ping, downsampledSize, i - 1);
            }

            // blur horizontal
            RenderTexture(horizontalBlur, Ping, Pong, maxBloomRes);

            // blur vertical
            RenderTexture(verticalBlur, Pong, Ping, maxBloomRes);

            // write to bloom accumulation buffer
            Accumulation.AttachColorMipLevel(i);
            RenderTexture(downsample, Ping, Accumulation, maxBloomRes, i);

            lastWrittenMip = i;
            downsampledSize /= 2;
        }

        // loop through mips backwards, from lowest res to highest
        for (var i = lastWrittenMip; i >= 1; i--)
        {
            using var _ = new GLDebugGroup(i switch { 1 => "Bloom Upsample 1", 2 => "Bloom Upsample 2", 3 => "Bloom Upsample 3", 4 => "Bloom Upsample 4", _ => "Bloom Upsample" });
            var isFirstUpsample = i == lastWrittenMip;
            var isLastUpsample = i == 1;

            var currentMipSize = Accumulation.GetMipSize(i - 1);
            var invTexSize = Vector2.One / new Vector2(currentMipSize.X, currentMipSize.Y);

            Accumulation.Bind(FramebufferTarget.DrawFramebuffer);

            // render into next higher res mip
            Accumulation.AttachColorMipLevel(i - 1);
            GL.Viewport(0, 0, currentMipSize.X, currentMipSize.Y);

            // first combine needs two blur+tint values, for first and second mip
            // subsequent combines only need one, for current mip
            var upsampleShader = isFirstUpsample
                ? firstUpsample
                : upsample;

            upsampleShader.Use();
            upsampleShader.SetTexture(0, "g_tSource", Accumulation.Color);
            upsampleShader.SetUniform2("g_vTexelSize", invTexSize);
            upsampleShader.SetUniform1("g_nCurrentMip", (float)i);

            if (isFirstUpsample)
            {
                var prevBlurTint = settings.BlurTint[i + 1] * settings.BlurWeight[i + 1];
                upsampleShader.SetUniform3("g_vPrevMipBlurTint", prevBlurTint);
            }

            var blurTint = settings.BlurTint[i] * settings.BlurWeight[i];

            // last merge
            // there are 5 blur+tint values, but only 4 mips to combine, seems like s2 bloom used to start
            // at half res, and downsample to 5 mips at some point, but right now it looks like they just
            // combine the last two blur + tint combos into a single one for last mip merge
            if (isLastUpsample)
            {
                blurTint += settings.BlurTint[i - 1] * settings.BlurWeight[i - 1];
            }

            upsampleShader.SetUniform3("g_vCurMipBlurTint", blurTint);

            GL.BindVertexArray(RendererContext.MeshBufferCache.EmptyVAO);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        }

        Accumulation.AttachColorMipLevel(0);
    }

    /// <summary>
    /// Render a texture from ping to pong using the provided screenspace shader.
    /// </summary>
    private void RenderTexture(Shader shader, Framebuffer ping, Framebuffer pong, Vector2i size, int mip = 0)
    {
        var texSize = new Vector2(size.X, size.Y);
        var invTexSize = Vector2.One / new Vector2(size.X, size.Y);

        pong.Bind(FramebufferTarget.DrawFramebuffer);

        Debug.Assert(ping.Color != null);
        GL.Viewport(0, 0, size.X, size.Y);

        shader.Use();

        shader.SetUniform2("g_vTexelSize", invTexSize);
        shader.SetUniform2("g_vTextureSize", texSize);
        shader.SetUniform1("g_nCurrentMip", (float)mip);
        shader.SetTexture(0, "g_tSource", ping.Color);

        GL.BindVertexArray(RendererContext.MeshBufferCache.EmptyVAO);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
    }
}
