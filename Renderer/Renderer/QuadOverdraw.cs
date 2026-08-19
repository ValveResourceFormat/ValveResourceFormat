using System.Diagnostics;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.Renderer.Buffers;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// Quad overdraw debug visualization.
/// </summary>
///
/// <remarks>
/// The scene is rendered with a replacement shader that counts, per 2x2 pixel quad, how
/// many primitives were shaded into it, using an atomic lock so a primitive spanning
/// several pixels of the same quad counts once.
///
/// The first render only fills out the depth buffer, the second one renders overdraw
/// </remarks>
public class QuadOverdraw(RendererContext rendererContext)
{
    private Shader? sceneShader;
    private Shader? visualizeShader;

    // Two uints per quad: a scoreboard lock and the count, zeroed each frame
    private StorageBuffer? quadBuffer;
    private ClearBufferMask savedClearMask;
    private RenderState savedPassState;

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
    /// Sizes the count buffer to the framebuffer, zeroes it, binds it to its slot, and puts
    /// <see cref="SceneShader"/> in depth prime mode. Call before the first scene render of the frame.
    /// </summary>
    /// <param name="width">Framebuffer width in pixels.</param>
    /// <param name="height">Framebuffer height in pixels.</param>
    public void Prepare(int width, int height)
    {
        // All variants: skinned meshes draw with a skinning variant of this shader (see MeshBatchRenderer)
        SceneShader.SetUniform1AllVariants("bCountQuads", 0u);

        // one entry per 2x2 pixel quad
        var quadWidth = (width + 1) / 2;
        var quadHeight = (height + 1) / 2;
        var elementCount = quadWidth * quadHeight * 2;

        if (quadBuffer == null || quadBuffer.Size != elementCount * sizeof(uint))
        {
            quadBuffer?.Delete();
            quadBuffer = StorageBuffer.Allocate<uint>(ReservedBufferSlots.QuadOverdraw, nameof(ReservedBufferSlots.QuadOverdraw), elementCount, BufferUsageHint.DynamicCopy);
        }

        // zero is both the unlocked lock value and the starting count
        quadBuffer.Clear();
        quadBuffer.BindBufferBase();
    }

    /// <summary>
    /// Switches from the depth prime render to the counting render by enabling counting in <see cref="SceneShader"/>.
    /// </summary>
    /// <param name="framebuffer">The framebuffer the scene renders into.</param>
    public void BeginCountingPass(Framebuffer framebuffer)
    {
        savedClearMask = framebuffer.ClearMask;
        framebuffer.ClearMask &= ~ClearBufferMask.DepthBufferBit;

        savedPassState = rendererContext.RenderState.CurrentPass;
        var countingState = savedPassState;
        countingState.DepthStencil.DepthFunc = RsComparison.CloserEqual;
        rendererContext.RenderState.SetPassBaseline(in countingState);

        SceneShader.SetUniform1AllVariants("bCountQuads", 1u);
    }

    /// <summary>Restores the depth state changed by <see cref="BeginCountingPass"/>.</summary>
    /// <param name="framebuffer">The framebuffer passed to <see cref="BeginCountingPass"/>.</param>
    public void EndCountingPass(Framebuffer framebuffer)
    {
        framebuffer.ClearMask = savedClearMask;
        rendererContext.RenderState.SetPassBaseline(in savedPassState);
    }

    /// <summary>
    /// Replaces the bound framebuffer contents with the overdraw heat map and legend.
    /// Call after the scene rendered with <see cref="SceneShader"/> as the replacement shader.
    /// </summary>
    public void Render()
    {
        Debug.Assert(quadBuffer != null, $"{nameof(Prepare)} must be called before {nameof(Render)}");

        visualizeShader ??= rendererContext.ShaderLoader.LoadShader("visualize_quad_overdraw");

        using var _ = new GLDebugGroup("Quad Overdraw Visualization");

        // The counts were written as buffer stores, the fullscreen pass reads them.
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit);

        visualizeShader.Use();

        using var overdrawState = rendererContext.RenderState.Scope(depthTest: false, depthWrite: false);

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
