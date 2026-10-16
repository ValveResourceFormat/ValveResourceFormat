using System.Diagnostics;
using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer.PostProcess;

/// <summary>
/// Fullscreen pass that draws an outline by running edge detection over the outline coverage mask.
/// </summary>
public class OutlineRenderer(RendererContext rendererContext)
{
    // Where the selection is seen, and fainter where the scene hides it. The image is already display encoded here.
    private static readonly Vector4 OutlineColor = new(255f / 255f, 183f / 255f, 45f / 255f, 1f);
    private static readonly Vector4 OccludedColor = new(228f / 255f, 210f / 255f, 176f / 255f, 0.5f);

    private Shader? outlineEdge;

    /// <summary>Loads the outline edge detection shader.</summary>
    public void Load()
    {
        outlineEdge = rendererContext.ShaderLoader.LoadShader("outline_post");
    }

    /// <summary>
    /// Execute the outline post-pass. Caller must ensure the destination framebuffer is bound.
    /// </summary>
    public void Render(RenderTexture outlineMask, int numSamples)
    {
        Debug.Assert(outlineEdge != null);

        outlineEdge.Use();

        outlineEdge.SetUniform("g_nNumSamplesMSAA", numSamples);
        outlineEdge.SetUniform("g_vOutlineColor", OutlineColor);
        outlineEdge.SetUniform("g_vOccludedColor", OccludedColor);

        outlineEdge.SetTexture(0, "g_tOutlineMask", outlineMask);

        using var _ = GraphicsContext.RenderState.Scope(blend: true);

        GL.BindVertexArray(rendererContext.MeshBufferCache.EmptyVAO);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
    }
}
