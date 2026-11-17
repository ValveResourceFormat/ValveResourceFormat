using System.Diagnostics;
using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer;

internal sealed class DepthPyramid(RendererContext rendererContext)
{
    // Size of the larger axis. The other axis follows the viewport aspect
    private const int MaxDimension = 512;

    // Threads per axis of a shader tile, which reduces through LevelsPerDispatch levels
    private const int TileSize = 16;
    private const int LevelsPerDispatch = 5;

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
        var maxMipLevel = int.Log2(Math.Max(targetWidth, targetHeight));

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

        DispatchLevels(npotDownsampleShader, pyramid, 0);

        downsampleShader.Use();

        for (var firstLevel = LevelsPerDispatch; firstLevel < pyramid.NumMipLevels; firstLevel += LevelsPerDispatch)
        {
            GL.BindImageTexture(1, pyramid.Handle, firstLevel - 1, false, 0, TextureAccess.ReadOnly, SizedInternalFormat.R32f);
            DispatchLevels(downsampleShader, pyramid, firstLevel);
        }

        GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
    }

    private static void DispatchLevels(Shader shader, RenderTexture pyramid, int firstLevel)
    {
        var levelCount = Math.Min(LevelsPerDispatch, pyramid.NumMipLevels - firstLevel);
        var width = MathUtils.MipLevelSize(pyramid.Width, firstLevel);
        var height = MathUtils.MipLevelSize(pyramid.Height, firstLevel);

        shader.SetUniform("g_nDestDepthWidth", width);
        shader.SetUniform("g_nDestDepthHeight", height);
        shader.SetUniform("g_nDestLevelCount", levelCount);

        for (var i = 0; i < LevelsPerDispatch; i++)
        {
            // Units past the last level rebind it, the shader skips their writes
            var level = firstLevel + Math.Min(i, levelCount - 1);
            GL.BindImageTexture(2 + i, pyramid.Handle, level, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.R32f);
        }

        GL.DispatchCompute(MathUtils.DivideRoundUp(width, TileSize), MathUtils.DivideRoundUp(height, TileSize), 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit);
    }

    public void Delete()
    {
        Texture?.Delete();
        Texture = null;
    }
}
