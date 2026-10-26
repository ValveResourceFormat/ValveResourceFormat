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
    private static readonly Vector4 VisibleColor = new(1f, 1f, 0.2f, 1f);
    private static readonly Vector4 OccludedColor = new(1f, 1f, 0.65f, 0.85f);

    private readonly RendererContext rendererContext;
    private readonly Shader maskShader;
    private readonly Shader edgeShader;
    private readonly HashSet<SceneNode> outlinedNodes = [];

    // Red: the selection wherever it is. Green: where the scene does not hide it. Multisampled as the scene is,
    // so both are tested per sample.
    private readonly Framebuffer mask;

    // The mask resolved to how much of each pixel is covered, which the edges are found in
    private readonly Framebuffer resolvedMask;

    public SelectionOutline(RendererContext rendererContext)
    {
        this.rendererContext = rendererContext;
        maskShader = rendererContext.ShaderLoader.LoadShader("outline");
        edgeShader = rendererContext.ShaderLoader.LoadShader("outline_post");

        mask = Framebuffer.Prepare(nameof(SelectionOutline), 4, 4, 0, ImageFormat.RGBA8888, null);
        mask.ClearMask = ClearBufferMask.ColorBufferBit;
        mask.Initialize();

        resolvedMask = Framebuffer.Prepare($"{nameof(SelectionOutline)}Resolved", 4, 4, 0, ImageFormat.RGBA8888, null);
        resolvedMask.Initialize();
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

        Debug.Assert(sceneFramebuffer.NumSamples > 0 && sceneFramebuffer.Depth != null);

        outlinedNodes.Clear();
        outlinedNodes.UnionWith(nodes);

        var (width, height) = (sceneFramebuffer.Width, sceneFramebuffer.Height);
        mask.Resize(width, height, sceneFramebuffer.NumSamples);
        resolvedMask.Resize(width, height);

        // Tested per sample against the depth the scene left, which a resolved copy cannot do at the silhouette.
        // Only color is cleared, so the scene's depth is left as it is.
        sceneFramebuffer.Depth.AttachToFramebuffer(mask, FramebufferAttachment.DepthAttachment, 0);

        bool drew;

        using (new GLDebugGroup("Selection Outline Mask"))
        {
            mask.BindAndClear();

            using (GraphicsContext.RenderState.Scope(cullMode: RsCullMode.None, multisampleEnable: mask.NumSamples > 1,
                depthTest: false, depthWrite: false, blend: false, colorWriteMask: RsColorWriteEnableBits.R))
            {
                drew = renderer.RenderNodes(mask, maskShader, outlinedNodes);
            }

            if (drew)
            {
                // Nudged closer, since another shader drew the depth and its result may differ in the last bits
                using (GraphicsContext.RenderState.Scope(cullMode: RsCullMode.None, multisampleEnable: mask.NumSamples > 1,
                    depthTest: true, depthFunc: RsComparison.CloserEqual, depthWrite: false, depthBias: 8, blend: false,
                    colorWriteMask: RsColorWriteEnableBits.G))
                {
                    renderer.RenderNodes(mask, maskShader, outlinedNodes);
                }

                GL.BlitNamedFramebuffer(mask.FboHandle, resolvedMask.FboHandle, 0, 0, width, height, 0, 0, width, height,
                    ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
            }
        }

        target.Bind(FramebufferTarget.Framebuffer);
        GL.Viewport(0, 0, target.Width, target.Height);

        if (!drew)
        {
            return;
        }

        using (new GLDebugGroup("Selection Outline Edge"))
        using (GraphicsContext.RenderState.Scope(depthTest: false, depthWrite: false, blend: true))
        {
            edgeShader.Use();
            edgeShader.SetUniform("g_vOutlineColor", VisibleColor);
            edgeShader.SetUniform("g_vOccludedColor", OccludedColor);
            edgeShader.SetTexture(0, "g_tOutlineMask", resolvedMask.Color!);

            GL.BindVertexArray(rendererContext.MeshBufferCache.EmptyVAO);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        }
    }

    public void Delete()
    {
        mask.Delete();
        resolvedMask.Delete();
    }
}
