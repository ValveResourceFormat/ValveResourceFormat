using System.Diagnostics;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.CompiledShader;

namespace ValveResourceFormat.Renderer.PostProcess;

/// <summary>
/// Fullscreen pass that draws an outline by running edge detection over the outline coverage mask.
/// </summary>
public class OutlineRenderer(RendererContext rendererContext)
{
    private Shader? outlineEdge;

    /// <summary>Loads the outline edge detection shader.</summary>
    public void Load()
    {
        outlineEdge = rendererContext.ShaderLoader.LoadShader("outline_post");
    }

    /// <summary>
    /// Execute the outline post-pass. Caller must ensure the destination framebuffer is bound.
    /// </summary>
    public void Render(RenderTexture outlineMask, int numSamples, bool flipY)
    {
        Debug.Assert(outlineEdge != null);

        outlineEdge.Use();

        outlineEdge.SetUniform("g_bFlipY", flipY);
        outlineEdge.SetUniform("g_nNumSamplesMSAA", numSamples);

        outlineEdge.SetTexture(0, "g_tOutlineMask", outlineMask);

        using var _ = rendererContext.RenderState.Scope(blend: true);

        GL.BindVertexArray(rendererContext.MeshBufferCache.EmptyVAO);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
    }
}
