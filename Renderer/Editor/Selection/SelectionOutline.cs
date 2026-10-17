using System.Diagnostics;
using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer.Editor.Selection;

/// <summary>
/// Outlines the selected nodes over the final image. Where they are is drawn into a mask, and its edges are then
/// drawn over the image, bright where the selection is seen and faint where the scene hides it.
/// </summary>
internal sealed class SelectionOutline
{
    // The image is already display encoded when the edges are drawn over it
    private static readonly Vector4 VisibleColor = new(255f / 255f, 183f / 255f, 45f / 255f, 1f);
    private static readonly Vector4 OccludedColor = new(228f / 255f, 210f / 255f, 176f / 255f, 0.5f);

    private readonly RendererContext rendererContext;
    private readonly Shader maskShader;
    private readonly Shader edgeShader;
    private readonly HashSet<SceneNode> outlinedNodes = [];

    // Red: the selection wherever it is. Green: where the scene does not hide it. Multisampled as the scene is,
    // since the edges are found per sample.
    private readonly Framebuffer mask;

    public SelectionOutline(RendererContext rendererContext)
    {
        this.rendererContext = rendererContext;
        maskShader = rendererContext.ShaderLoader.LoadShader("outline");
        edgeShader = rendererContext.ShaderLoader.LoadShader("outline_post");

        mask = Framebuffer.Prepare(nameof(SelectionOutline), 4, 4, 0, ImageFormat.RG1616, null);
        mask.ClearMask = ClearBufferMask.ColorBufferBit;
        mask.Initialize();
    }

    /// <summary>Outlines nodes as the last frame drew them.</summary>
    /// <param name="renderer">The renderer of the frame, which resolved the scene depth.</param>
    /// <param name="sceneFramebuffer">What the scene was rendered into.</param>
    /// <param name="target">What the outline is drawn over, the size of the scene.</param>
    /// <param name="nodes">The nodes to outline.</param>
    public void Render(Renderer renderer, Framebuffer sceneFramebuffer, Framebuffer target, IReadOnlyList<SceneNode> nodes)
    {
        if (nodes.Count == 0)
        {
            return;
        }

        Debug.Assert(sceneFramebuffer.NumSamples > 0);

        outlinedNodes.Clear();
        outlinedNodes.UnionWith(nodes);

        mask.Resize(sceneFramebuffer.Width, sceneFramebuffer.Height, sceneFramebuffer.NumSamples);

        using (new GLDebugGroup("Selection Outline Mask"))
        using (GraphicsContext.RenderState.Scope(cullMode: RsCullMode.None, multisampleEnable: mask.NumSamples > 1,
            depthTest: false, depthWrite: false, blend: false))
        {
            mask.BindAndClear();

            if (!renderer.RenderNodes(mask, maskShader, outlinedNodes))
            {
                return;
            }
        }

        using (new GLDebugGroup("Selection Outline Edge"))
        using (GraphicsContext.RenderState.Scope(depthTest: false, depthWrite: false, blend: true))
        {
            target.Bind(FramebufferTarget.Framebuffer);
            GL.Viewport(0, 0, target.Width, target.Height);

            edgeShader.Use();
            edgeShader.SetUniform("g_nNumSamplesMSAA", mask.NumSamples);
            edgeShader.SetUniform("g_vOutlineColor", VisibleColor);
            edgeShader.SetUniform("g_vOccludedColor", OccludedColor);
            edgeShader.SetTexture(0, "g_tOutlineMask", mask.Color!);

            GL.BindVertexArray(rendererContext.MeshBufferCache.EmptyVAO);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        }
    }

    public void Delete() => mask.Delete();
}
