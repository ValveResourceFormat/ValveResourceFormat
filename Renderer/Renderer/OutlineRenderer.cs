using System.Diagnostics;
using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// Fullscreen pass that draws an outline using using stencil edge detection.
/// </summary>
public class OutlineRenderer(RendererContext rendererContext)
{
    private Shader? outlineEdge;

    public void Load()
    {
        outlineEdge = rendererContext.ShaderLoader.LoadShader("vrf.outline_post");
    }

    /// <summary>
    /// Execute the outline post-pass. Caller must ensure the destination framebuffer is bound.
    /// </summary>
    public void Render(RenderTexture stencil, int numSamples, bool flipY)
    {
        Debug.Assert(outlineEdge != null);

        outlineEdge.Use();

        outlineEdge.SetUniform1("g_bFlipY", flipY);
        outlineEdge.SetUniform1("g_nNumSamplesMSAA", numSamples);

        outlineEdge.SetTexture(0, "g_tStencilBuffer", stencil);

        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        GL.BindVertexArray(rendererContext.MeshBufferCache.EmptyVAO);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

        GL.Disable(EnableCap.Blend);
    }
}
