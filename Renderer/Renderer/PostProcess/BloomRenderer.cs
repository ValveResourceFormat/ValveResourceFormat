using System.Diagnostics;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer.SceneEnvironment;
using Vector2i = OpenTK.Mathematics.Vector2i;

namespace ValveResourceFormat.Renderer.PostProcess;

/// <summary>
/// Post-processing renderer that creates bloom effects using multi-pass Gaussian blur.
/// </summary>
public class BloomRenderer
{
    private Shader? downsampleBloomThreshold;
    private Shader? downsampleBloomThresholdExposed;
    private Shader? downsample;
    private Shader? horizontalBlur;
    private Shader? verticalBlur;
    private Shader? upsample;
    private Shader? upsampleTent;
    private Shader? finalUpsample;

    private Framebuffer? MipChain;
    private Framebuffer? Output;

    /// <summary>Gets the texture containing the final composited bloom result after the upsample passes.</summary>
    public RenderTexture? AccumulationResult => Output!.Color!;

    /// <summary>Number of mip levels in the bloom mip chain, starting at quarter resolution.</summary>
    public const int BloomMipCount = 4;

    /// <summary>
    /// Radius of the tent filter that upsamples each mip into the one above it, in texels of the
    /// larger mip. Zero upsamples with a 2x2 box instead.
    /// </summary>
    private const float TentFilterRadius = 3.1f;

    private readonly RendererContext RendererContext;
    private readonly PostProcessRenderer PostProcessRenderer;

    /// <summary>
    /// Initializes a new <see cref="BloomRenderer"/> using the given renderer context and post-process renderer.
    /// </summary>
    /// <param name="rendererContext">The renderer context providing shader loading and mesh buffer access.</param>
    /// <param name="postProcessRenderer">The owning post-process renderer, used for shared state and format.</param>
    public BloomRenderer(RendererContext rendererContext, PostProcessRenderer postProcessRenderer)
    {
        RendererContext = rendererContext;
        PostProcessRenderer = postProcessRenderer;
    }

    private Game Game => RendererContext.FileLoader.GameInfo.Game;

    // Deadlock and Half-Life: Alyx weigh the threshold by the exposed luminance but bloom the unexposed colour
    /// <summary>
    /// Gets whether the scene is exposed before it is thresholded, which leaves the bloom in exposed space.
    /// </summary>
    public bool ThresholdsExposedScene => Game is Game.Csgo or Game.Steampal;

    // Deadlock keeps the effects target in floating point, and Dota 2 decodes its sRGB writes when it reads them back.
    // TODO: The effects bloom comes out uniformly about 8% darker than in Counter-Strike 2, for a reason not yet found
    /// <summary>
    /// Gets whether the effects bloom layer blooms sRGB encoded, as stored in an 8-bit target and read back
    /// without decoding.
    /// </summary>
    public bool EncodesEffectsBloom => Game is Game.Csgo or Game.Steampal;

    /// <summary>Loads bloom shaders and allocates the mip chain and output framebuffers.</summary>
    public void Load()
    {
        downsampleBloomThreshold = RendererContext.ShaderLoader.LoadShader("downsample_bloomthreshold");
        downsampleBloomThresholdExposed = RendererContext.ShaderLoader.LoadShader("downsample_bloomthreshold", ("D_EXPOSED_THRESHOLD", 1));
        downsample = RendererContext.ShaderLoader.LoadShader("gaussian_bloom_blur");
        horizontalBlur = RendererContext.ShaderLoader.LoadShader("gaussian_bloom_blur", ("D_BLUR_PASS", 1));
        verticalBlur = RendererContext.ShaderLoader.LoadShader("gaussian_bloom_blur", ("D_BLUR_PASS", 2));
        upsample = RendererContext.ShaderLoader.LoadShader("gaussian_bloom_blur", ("D_BLUR_PASS", 3));
        upsampleTent = RendererContext.ShaderLoader.LoadShader("gaussian_bloom_blur", ("D_BLUR_PASS", 3), ("D_TENT_FILTER", 1));
        finalUpsample = RendererContext.ShaderLoader.LoadShader("gaussian_bloom_blur", ("D_BLUR_PASS", 4));

        MipChain = CreateFramebuffer("BloomMipChain", BloomMipCount);
        Output = CreateFramebuffer("BloomOutput");
    }

    private static Framebuffer CreateFramebuffer(string name, int mips = 1)
    {
        var framebuffer = Framebuffer.Prepare(name, 4, 4, 0, PostProcessRenderer.DefaultColorFormat, null);
        framebuffer.NumMips = mips;
        framebuffer.Initialize();
        framebuffer.SetColorSamplerState(
            mips > 1 ? TextureMinFilter.LinearMipmapLinear : TextureMinFilter.Linear,
            TextureMagFilter.Linear,
            RsTextureAddressMode.Clamp
        );
        return framebuffer;
    }

    /// <summary>
    /// Renders multi-pass bloom from the resolved scene color into <see cref="AccumulationResult"/>.
    /// </summary>
    /// <param name="input">The resolved scene color, thresholded before it blooms.</param>
    /// <param name="effectsBloom">Geometry drawn only to glow, bloomed whole without a threshold, or null.</param>
    /// <param name="settings">The bloom settings to apply.</param>
    /// <param name="sceneWeight">How much of the thresholded scene blooms: 0 blooms only <paramref name="effectsBloom"/>.</param>
    public void Render(RenderTexture input, RenderTexture? effectsBloom, BloomSettings settings, float sceneWeight)
    {
        Debug.Assert(MipChain != null && MipChain.Color != null);
        Debug.Assert(Output != null && Output.Color != null);

        var quarterRes = new Vector2i(input.Width / 4, input.Height / 4);

        // skip bloom if the resolution is too small
        if (quarterRes.X < 16 || quarterRes.Y < 16)
        {
            Output.BindAndClear();
            return;
        }

        MipChain.Resize(quarterRes.X, quarterRes.Y);
        Output.Resize(quarterRes.X, quarterRes.Y);

        GL.BindVertexArray(RendererContext.MeshBufferCache.EmptyVAO);

        RenderThreshold(input, effectsBloom, settings, sceneWeight);

        for (var level = 0; level < BloomMipCount; level++)
        {
            RenderDownsampleAndBlur(level);
        }

        RenderUpsample(settings);

        MipChain.AttachColorMipLevel(0);
    }

    private void RenderThreshold(RenderTexture input, RenderTexture? effectsBloom, BloomSettings settings, float sceneWeight)
    {
        using var _ = new GLDebugGroup("Bloom Downsample Threshold Pass");

        Debug.Assert(input.Target == TextureTarget.Texture2D);
        Debug.Assert(downsampleBloomThreshold != null && downsampleBloomThresholdExposed != null);

        // TODO: Half-Life: Alyx downsamples with 16 point taps weighted by 1 / (max(rgb) + 1) and thresholds after
        // decoding the sum, and Deadlock scales the threshold luminance and colour by 20 times the self-illum mask
        var shader = ThresholdsExposedScene ? downsampleBloomThresholdExposed : downsampleBloomThreshold;

        // The scene blooms by the strongest of the three modes, the composite weighs the modes against each other
        var scalesSceneByStrongestMode = Game is Game.Csgo or Game.Steampal or Game.Citadel;
        var bloomScale = scalesSceneByStrongestMode
            ? MathF.Max(MathF.Max(settings.AddBloomStrength, settings.ScreenBloomStrength), settings.BlurBloomStrength)
            : settings.BloomStrength;

        var thresholdWidth = MathF.Max(settings.BloomThresholdWidth, 1e-4f);
        var thresholdParams = new Vector2(-settings.BloomThreshold / thresholdWidth, 1f / thresholdWidth);

        shader.Use();
        shader.SetTexture(0, "inputTexture", input);
        shader.SetUniform("g_flBloomScale", bloomScale);
        shader.SetUniform("g_flToneMapScalarLinear", PostProcessRenderer.TonemapScalar);
        shader.SetUniform("g_flExposureBiasScaleFactor", MathF.Pow(2f, PostProcessRenderer.State.TonemapSettings.ExposureBias));
        shader.SetUniform("g_flThresholdParams", thresholdParams);
        shader.SetUniform("g_flSceneBloomWeight", sceneWeight);
        shader.SetUniform("g_bEffectsBloom", effectsBloom != null);
        // The effects bloom layer is clamped to [0, 1] where it is stored in an 8-bit target rather than in floating point.
        // TODO: Dota 2 blooms the scene without a threshold, which is not modelled
        var clampsEffectsBloom = Game is Game.Csgo or Game.Steampal or Game.Dota;

        shader.SetUniform("g_bEffectsBloomClamped", clampsEffectsBloom);
        shader.SetUniform("g_bEffectsBloomEncoded", EncodesEffectsBloom);

        if (effectsBloom != null)
        {
            shader.SetTexture(1, "g_tEffectsBloom", effectsBloom);
        }

        BindTarget(MipChain!, 0);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
    }

    /// <summary>
    /// Fills a mip of the chain with a box downsample of the mip above it, the first mip already holds the
    /// thresholded scene, then blurs it horizontally into the output and vertically back.
    /// </summary>
    private void RenderDownsampleAndBlur(int level)
    {
        using var _ = new GLDebugGroup(level switch { 0 => "Bloom Blur 0", 1 => "Bloom Blur 1", 2 => "Bloom Blur 2", 3 => "Bloom Blur 3", _ => "Bloom Blur" });

        Debug.Assert(downsample != null && horizontalBlur != null && verticalBlur != null);

        var size = MipChain!.GetMipSize(level);
        var lastTexelCenter = new Vector2(size.X - 0.5f, size.Y - 0.5f);

        if (level > 0)
        {
            var sourceSize = MipChain.GetMipSize(level - 1);

            downsample.Use();
            downsample.SetTexture(0, "g_tSource", MipChain.Color!);
            downsample.SetUniform("g_nSourceMip", (float)(level - 1));
            downsample.SetUniform("g_vTexelSize", new Vector4(1f / sourceSize.X, 1f / sourceSize.Y, 0f, 0f));

            BindTarget(MipChain, level);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        }

        horizontalBlur.Use();
        horizontalBlur.SetTexture(0, "g_tSource", MipChain.Color!);
        horizontalBlur.SetUniform("g_nSourceMip", (float)level);
        horizontalBlur.SetUniform("g_vTexelSize", new Vector4(1f / size.X, 1f / size.Y, lastTexelCenter.X, lastTexelCenter.Y));

        // The output is free until the final upsample, its top left corner holds the horizontal pass
        Output!.Bind(FramebufferTarget.DrawFramebuffer);
        GL.Viewport(0, 0, size.X, size.Y);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

        verticalBlur.Use();
        verticalBlur.SetTexture(0, "g_tSource", Output.Color!);
        verticalBlur.SetUniform("g_nSourceMip", 0f);
        verticalBlur.SetUniform("g_vTexelSize", new Vector4(1f / Output.Width, 1f / Output.Height, lastTexelCenter.X, lastTexelCenter.Y));

        BindTarget(MipChain, level);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
    }

    /// <summary>
    /// Sums the blurred mips from the smallest up, weighing each by its blur weight and tint. The mip
    /// chain starts at quarter resolution, so the two largest of the five weights both go to the first mip.
    /// </summary>
    private void RenderUpsample(BloomSettings settings)
    {
        Debug.Assert(upsample != null && upsampleTent != null && finalUpsample != null);

        // Deadlock and Half-Life: Alyx upsample with a 2x2 box
        var upsampleShader = ThresholdsExposedScene && TentFilterRadius > 0f ? upsampleTent : upsample;

        using (GraphicsContext.RenderState.Scope(blend: true, srcBlend: RsBlendMode.One, dstBlend: RsBlendMode.Src1Color))
        {
            for (var level = BloomMipCount - 2; level >= 1; level--)
            {
                using var _ = new GLDebugGroup(level switch { 1 => "Bloom Upsample 1", 2 => "Bloom Upsample 2", _ => "Bloom Upsample" });

                var prevMipBlurTint = Vector3.One;

                if (level == BloomMipCount - 2)
                {
                    prevMipBlurTint = GetBlurTint(settings, BloomMipCount);
                }

                upsampleShader.Use();
                upsampleShader.SetTexture(0, "g_tSource", MipChain!.Color!);
                upsampleShader.SetUniform("g_nSourceMip", (float)(level + 1));
                upsampleShader.SetUniform("g_vCombineUVScaleAndClamp", GetCombineUVScaleAndClamp(level));
                upsampleShader.SetUniform("g_flTentFilterRadius", TentFilterRadius);
                upsampleShader.SetUniform("g_vPrevMipBlurTint", prevMipBlurTint);
                upsampleShader.SetUniform("g_vCurrMipBlurTint", GetBlurTint(settings, level + 1));

                BindTarget(MipChain, level);
                GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            }
        }

        using (new GLDebugGroup("Bloom Upsample 0"))
        {
            var size = MipChain!.GetMipSize(0);
            var lastTexelCenter = new Vector2((size.X - 0.5f) / size.X, (size.Y - 0.5f) / size.Y);
            var curMipBlurTint = GetBlurTint(settings, 0) + GetBlurTint(settings, 1);

            finalUpsample.Use();
            finalUpsample.SetTexture(0, "g_tSource", MipChain.Color!);
            finalUpsample.SetUniform("g_nSourceMip", 1f);
            finalUpsample.SetUniform("g_nSource2Mip", 0f);
            finalUpsample.SetUniform("g_vCombineUVScaleAndClamp", GetCombineUVScaleAndClamp(0));
            finalUpsample.SetUniform("g_vCombineUVScaleAndClamp2", new Vector4(0f, 0f, lastTexelCenter.X, lastTexelCenter.Y));
            finalUpsample.SetUniform("g_vCurrMipBlurTint", curMipBlurTint);

            Output!.Bind(FramebufferTarget.DrawFramebuffer);
            GL.Viewport(0, 0, size.X, size.Y);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        }
    }

    /// <summary>
    /// Scales pixels of <paramref name="level"/> to uv, and clamps the uv to the last texel centre of the mip below.
    /// </summary>
    private Vector4 GetCombineUVScaleAndClamp(int level)
    {
        var size = MipChain!.GetMipSize(level);
        var sourceSize = MipChain.GetMipSize(level + 1);

        return new Vector4(
            1f / size.X,
            1f / size.Y,
            (sourceSize.X - 0.5f) / sourceSize.X,
            (sourceSize.Y - 0.5f) / sourceSize.Y);
    }

    private static Vector3 GetBlurTint(in BloomSettings settings, int index) => settings.BlurTint[index] * settings.BlurWeight[index];

    private static void BindTarget(Framebuffer framebuffer, int level)
    {
        var size = framebuffer.GetMipSize(level);

        framebuffer.Bind(FramebufferTarget.DrawFramebuffer);
        framebuffer.AttachColorMipLevel(level);
        GL.Viewport(0, 0, size.X, size.Y);
    }
}
