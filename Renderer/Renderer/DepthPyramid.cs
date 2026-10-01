using System.Diagnostics;
using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer;

internal sealed class DepthPyramid(RendererContext rendererContext)
{
    // The other side follows the viewport aspect
    private const int MaxDimension = 512;

    private Shader? downsampleShader;
    private Shader? npotDownsampleShader;

    public RenderTexture? Texture { get; private set; }

    public void LoadShaders()
    {
        downsampleShader = rendererContext.ShaderLoader.LoadShader("depth_pyramid");
        npotDownsampleShader = rendererContext.ShaderLoader.LoadShader("depth_pyramid", ("D_NPOT_DOWNSAMPLE", 1));
    }

    public void EnsureSize(int width, int height)
    {
        var scale = Math.Min(1f, MaxDimension / (float)Math.Max(width, height));

        static int NearestPowerOfTwo(float value)
            => 1 << Math.Max(0, (int)MathF.Round(MathF.Log2(MathF.Max(value, 1f))));

        var targetWidth = NearestPowerOfTwo(width * scale);
        var targetHeight = NearestPowerOfTwo(height * scale);

        if (Texture != null && Texture.Width == targetWidth && Texture.Height == targetHeight)
        {
            return;
        }

        Texture?.Delete();

        // Mips needed to take the larger axis down to 1
        var maxMipLevel = (int)Math.Log2(Math.Max(targetWidth, targetHeight));

        Texture = RenderTexture.Create(targetWidth, targetHeight, ImageFormat.R32F, maxMipLevel + 1, "DepthPyramid");
        Texture.SetBaseMaxLevel(0, maxMipLevel);
    }

    public void Generate(RenderTexture depthSource)
    {
        var pyramid = Texture;

        if (pyramid == null || downsampleShader == null || npotDownsampleShader == null)
        {
            return;
        }

        using var _ = new GLDebugGroup("Generate Depth Pyramid");

        Debug.Assert(depthSource.Target == TextureTarget.Texture2D);

        // Downsample from non power of two depth source
        npotDownsampleShader.Use();
        npotDownsampleShader.SetTexture(0, "g_tSourceDepthNpot", depthSource);
        npotDownsampleShader.SetUniform("g_nSourceDepthWidth", depthSource.Width);
        npotDownsampleShader.SetUniform("g_nSourceDepthHeight", depthSource.Height);

        npotDownsampleShader.SetUniform("g_nDestDepthWidth", pyramid.Width);
        npotDownsampleShader.SetUniform("g_nDestDepthHeight", pyramid.Height);

        GL.BindImageTexture(2, pyramid.Handle, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.R32f);

        GL.DispatchCompute(MathUtils.DivideRoundUp(pyramid.Width, 8), MathUtils.DivideRoundUp(pyramid.Height, 8), 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit);

        // Generate mip levels down to 1x1
        downsampleShader.Use();

        for (var mipLevel = 1; mipLevel < pyramid.NumMipLevels; mipLevel++)
        {
            var destWidth = MathUtils.MipLevelSize(pyramid.Width, mipLevel);
            var destHeight = MathUtils.MipLevelSize(pyramid.Height, mipLevel);

            downsampleShader.SetUniform("g_nDestDepthWidth", destWidth);
            downsampleShader.SetUniform("g_nDestDepthHeight", destHeight);

            GL.BindImageTexture(1, pyramid.Handle, mipLevel - 1, false, 0, TextureAccess.ReadOnly, SizedInternalFormat.R32f);
            GL.BindImageTexture(2, pyramid.Handle, mipLevel, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.R32f);

            GL.DispatchCompute(MathUtils.DivideRoundUp(destWidth, 8), MathUtils.DivideRoundUp(destHeight, 8), 1);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit);
        }

        GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
    }

    public void Delete()
    {
        Texture?.Delete();
        Texture = null;
    }
}
