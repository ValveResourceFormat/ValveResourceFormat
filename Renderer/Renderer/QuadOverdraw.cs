using System.Diagnostics;
using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// Quad overdraw debug visualization.
/// </summary>
/// 
/// <remarks>
/// The scene is rendered with a replacement shader that counts, per 2x2 pixel quad, how
/// many primitives were shaded into it, using an atomic lock image so a primitive spanning
/// several pixels of the same quad counts once.
///
/// The first render only fills out the depth buffer, the second one renders overdraw
/// </remarks>
public class QuadOverdraw(RendererContext rendererContext)
{
    /// <summary>
    /// Image unit for the lock image. Units 1 and 2 are used by the MSAA resolve and depth pyramid compute passes.
    /// </summary>
    public const int LockImageUnit = 3;

    /// <summary>
    /// Image unit for the count image. Units 1 and 2 are used by the MSAA resolve and depth pyramid compute passes.
    /// </summary>
    public const int CountImageUnit = 4;

    private Shader? sceneShader;
    private Shader? visualizeShader;

    private RenderTexture? quadLock;
    private RenderTexture? quadCount;
    private ClearBufferMask savedClearMask;

    /// <summary>Gets the replacement shader that counts quad overdraw while the scene renders.</summary>
    public Shader SceneShader => sceneShader ??= rendererContext.ShaderLoader.LoadShader("quad_overdraw");

    /// <summary>Gets whether the current render mode has activated the quad overdraw visualization.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Loads the shaders up front so the render mode is registered for the mode dropdown.</summary>
    public void Load()
    {
        sceneShader ??= rendererContext.ShaderLoader.LoadShader("quad_overdraw");
        visualizeShader ??= rendererContext.ShaderLoader.LoadShader("visualize_quad_overdraw");
    }

    /// <summary>Updates <see cref="IsActive"/> based on the active render mode name.</summary>
    public void SetRenderMode(string renderMode)
    {
        IsActive = SceneShader.RenderModes.Contains(renderMode);
    }

    /// <summary>
    /// Sizes the count images to the framebuffer, resets them, binds them to their image
    /// units, and puts <see cref="SceneShader"/> in depth prime mode. Call before the first
    /// scene render of the frame.
    /// </summary>
    /// <param name="width">Framebuffer width in pixels.</param>
    /// <param name="height">Framebuffer height in pixels.</param>
    public void Prepare(int width, int height)
    {
        SceneShader.SetUniform1("bCountQuads", false);

        // one texel per 2x2 pixel quad
        var quadWidth = (width + 1) / 2;
        var quadHeight = (height + 1) / 2;

        if (quadLock == null || quadLock.Width != quadWidth || quadLock.Height != quadHeight)
        {
            quadLock?.Delete();
            quadCount?.Delete();

            quadLock = RenderTexture.Create(quadWidth, quadHeight, SizedInternalFormat.R32ui);
            quadLock.SetLabel("QuadOverdrawLock");

            quadCount = RenderTexture.Create(quadWidth, quadHeight, SizedInternalFormat.R32ui);
            quadCount.SetLabel("QuadOverdrawCount");
            quadCount.SetFiltering(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
        }

        var unlocked = uint.MaxValue;
        var zero = 0u;
        GL.ClearTexImage(quadLock.Handle, 0, PixelFormat.RedInteger, PixelType.UnsignedInt, ref unlocked);
        GL.ClearTexImage(quadCount!.Handle, 0, PixelFormat.RedInteger, PixelType.UnsignedInt, ref zero);

        GL.BindImageTexture(LockImageUnit, quadLock.Handle, 0, false, 0, TextureAccess.ReadWrite, SizedInternalFormat.R32ui);
        GL.BindImageTexture(CountImageUnit, quadCount.Handle, 0, false, 0, TextureAccess.ReadWrite, SizedInternalFormat.R32ui);
    }

    /// <summary>
    /// Switches from the depth prime render to the counting render by enabling counting in <see cref="SceneShader"/>.
    /// </summary>
    /// <param name="framebuffer">The framebuffer the scene renders into.</param>
    public void BeginCountingPass(Framebuffer framebuffer)
    {
        savedClearMask = framebuffer.ClearMask;
        framebuffer.ClearMask &= ~ClearBufferMask.DepthBufferBit;

        GL.DepthFunc(DepthFunction.Gequal);
        SceneShader.SetUniform1("bCountQuads", true);
    }

    /// <summary>Restores the depth state changed by <see cref="BeginCountingPass"/>.</summary>
    /// <param name="framebuffer">The framebuffer passed to <see cref="BeginCountingPass"/>.</param>
    public void EndCountingPass(Framebuffer framebuffer)
    {
        framebuffer.ClearMask = savedClearMask;
        GL.DepthFunc(DepthFunction.Greater);
    }

    /// <summary>
    /// Replaces the bound framebuffer contents with the overdraw heat map and legend.
    /// Call after the scene rendered with <see cref="SceneShader"/> as the replacement shader.
    /// </summary>
    public void Render()
    {
        Debug.Assert(quadCount != null, $"{nameof(Prepare)} must be called before {nameof(Render)}");

        visualizeShader ??= rendererContext.ShaderLoader.LoadShader("visualize_quad_overdraw");

        using var _ = new GLDebugGroup("Quad Overdraw Visualization");

        // The counts were written as image stores, the fullscreen pass samples them.
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        visualizeShader.Use();
        visualizeShader.SetTexture(0, "g_tQuadOverdraw", quadCount);

        GL.Disable(EnableCap.DepthTest);
        GL.DepthMask(false);

        GL.BindVertexArray(rendererContext.MeshBufferCache.EmptyVAO);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

        GL.DepthMask(true);
        GL.Enable(EnableCap.DepthTest);
    }

    /// <summary>Releases the GPU textures owned by this visualization.</summary>
    public void Dispose()
    {
        quadLock?.Delete();
        quadCount?.Delete();
        quadLock = null;
        quadCount = null;
    }
}
