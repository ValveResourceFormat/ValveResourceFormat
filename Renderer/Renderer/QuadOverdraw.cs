using System.Diagnostics;
using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// Quad overdraw debug visualization.
/// </summary>
///
/// <remarks>
/// The frame is drawn once for depth, then again through each material's overdraw mode to count primitives
/// per 2x2 pixel quad.
/// </remarks>
public class QuadOverdraw(RendererContext rendererContext)
{
    private Shader? sceneShader;
    private Shader? visualizeShader;

    // Two uints per quad: a scoreboard lock and the count, zeroed each frame
    private StorageBuffer? quadBuffer;
    private ClearBufferMask savedClearMask;
    private RenderState savedPassState;

    /// <summary>Gets the counting shader used by materials whose own shader has no overdraw mode.</summary>
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

    /// <summary>Sizes, zeroes and binds the count buffer. Call before the first scene render of the frame.</summary>
    public void Prepare(int width, int height)
    {
        // one entry per 2x2 pixel quad
        var quadWidth = (width + 1) / 2;
        var quadHeight = (height + 1) / 2;
        var elementCount = quadWidth * quadHeight * 2;

        if (quadBuffer == null || quadBuffer.Size != elementCount * sizeof(uint))
        {
            quadBuffer?.Delete();
            quadBuffer = StorageBuffer.Allocate<uint>(ReservedBufferSlots.QuadOverdraw, nameof(ReservedBufferSlots.QuadOverdraw), elementCount, BufferUsage.GpuOnly);
        }

        // zero is both the unlocked lock value and the starting count
        quadBuffer.Clear();
        quadBuffer.BindBufferBase();
    }

    /// <summary>Keeps the first render's depth and has the counting render test against it.</summary>
    public void BeginCountingPass(Framebuffer framebuffer)
    {
        savedClearMask = framebuffer.ClearMask;
        framebuffer.ClearMask &= ~ClearBufferMask.DepthBufferBit;

        savedPassState = GraphicsContext.RenderState.CurrentPass;
        var countingState = savedPassState;
        countingState.DepthStencil.DepthFunc = RsComparison.CloserEqual;
        GraphicsContext.RenderState.SetPassBaseline(in countingState);
    }

    /// <summary>Restores the depth state changed by <see cref="BeginCountingPass"/>.</summary>
    public void EndCountingPass(Framebuffer framebuffer)
    {
        framebuffer.ClearMask = savedClearMask;
        GraphicsContext.RenderState.SetPassBaseline(in savedPassState);
    }

    /// <summary>Replaces the framebuffer contents with the overdraw heat map. Call after the counting render.</summary>
    public void Render()
    {
        Debug.Assert(quadBuffer != null, $"{nameof(Prepare)} must be called before {nameof(Render)}");

        visualizeShader ??= rendererContext.ShaderLoader.LoadShader("visualize_quad_overdraw");

        using var _ = new GLDebugGroup("Quad Overdraw Visualization");

        // The counts were written as buffer stores, the fullscreen pass reads them.
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit);

        visualizeShader.Use();

        using var overdrawState = GraphicsContext.RenderState.Scope(depthTest: false, depthWrite: false);

        GL.BindVertexArray(rendererContext.MeshBufferCache.EmptyVAO);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
    }

    /// <summary>Releases the GPU buffer owned by this visualization.</summary>
    public void Dispose()
    {
        quadBuffer?.Delete();
        quadBuffer = null;
    }
}
