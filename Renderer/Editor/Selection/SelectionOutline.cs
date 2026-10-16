using System.Diagnostics;
using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer.Editor.Selection;

/// <summary>
/// Outlines the selected nodes over the final image. Where they are is drawn into a mask, and its edges are then
/// drawn over the image, bright where the selection is seen and faint where the scene hides it.
/// </summary>
public sealed class SelectionOutline : IDisposable
{
    // The image is already display encoded when the edges are drawn over it
    private static readonly Vector4 VisibleColor = new(255f / 255f, 183f / 255f, 45f / 255f, 1f);
    private static readonly Vector4 OccludedColor = new(228f / 255f, 210f / 255f, 176f / 255f, 0.5f);

    // Red: the selection wherever it is. Green: where the scene does not hide it.
    private const ImageFormat MaskFormat = ImageFormat.RG1616;

    private readonly RendererContext rendererContext;
    private readonly Shader maskShader;
    private readonly Shader edgeShader;
    private readonly HashSet<SceneNode> outlinedNodes = [];
    private Framebuffer? mask;

    /// <summary>Initializes the outline.</summary>
    /// <param name="rendererContext">Renderer context for loading the outline shaders.</param>
    public SelectionOutline(RendererContext rendererContext)
    {
        ArgumentNullException.ThrowIfNull(rendererContext);

        this.rendererContext = rendererContext;
        maskShader = rendererContext.ShaderLoader.LoadShader("outline");
        edgeShader = rendererContext.ShaderLoader.LoadShader("outline_post");
    }

    /// <summary>Outlines nodes as the last frame drew them.</summary>
    /// <param name="renderer">The renderer of the frame, which resolved the scene depth.</param>
    /// <param name="sceneFramebuffer">What the scene was rendered into.</param>
    /// <param name="target">What the outline is drawn over, the size of the scene.</param>
    /// <param name="nodes">The nodes to outline.</param>
    public void Render(Renderer renderer, Framebuffer sceneFramebuffer, Framebuffer target, IReadOnlyList<SceneNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(sceneFramebuffer);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(nodes);

        if (nodes.Count == 0)
        {
            return;
        }

        outlinedNodes.Clear();
        outlinedNodes.UnionWith(nodes);

        var maskBuffer = EnsureMask(sceneFramebuffer);

        using (new GLDebugGroup("Selection Outline Mask"))
        using (GraphicsContext.RenderState.Scope(cullMode: RsCullMode.None, multisampleEnable: maskBuffer.NumSamples > 1,
            depthTest: false, depthWrite: false, blend: false))
        {
            maskBuffer.BindAndClear();
            renderer.RenderNodes(maskBuffer, maskShader, outlinedNodes);
        }

        using (new GLDebugGroup("Selection Outline Edge"))
        using (GraphicsContext.RenderState.Scope(blend: true))
        {
            target.Bind(FramebufferTarget.Framebuffer);
            GL.Viewport(0, 0, target.Width, target.Height);

            edgeShader.Use();
            edgeShader.SetUniform("g_nNumSamplesMSAA", maskBuffer.NumSamples);
            edgeShader.SetUniform("g_vOutlineColor", VisibleColor);
            edgeShader.SetUniform("g_vOccludedColor", OccludedColor);
            edgeShader.SetTexture(0, "g_tOutlineMask", maskBuffer.Color!);

            GL.BindVertexArray(rendererContext.MeshBufferCache.EmptyVAO);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        }
    }

    // The edges are found per sample, so the mask is multisampled as the scene is
    private Framebuffer EnsureMask(Framebuffer sceneFramebuffer)
    {
        var (width, height, samples) = (sceneFramebuffer.Width, sceneFramebuffer.Height, sceneFramebuffer.NumSamples);

        Debug.Assert(samples > 0);

        if (mask == null)
        {
            mask = Framebuffer.Prepare(nameof(SelectionOutline), width, height, samples, MaskFormat, null);
            mask.ClearMask = ClearBufferMask.ColorBufferBit;
            mask.Initialize();
        }
        else
        {
            mask.Resize(width, height, samples);
        }

        return mask;
    }

    /// <summary>Deletes the GL objects.</summary>
    public void Dispose()
    {
        mask?.Delete();
        mask = null;
    }
}
