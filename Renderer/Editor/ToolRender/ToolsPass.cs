using System.Linq;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer.Editor.Selection;

namespace ValveResourceFormat.Renderer.Editor.ToolRender;

/// <summary>
/// Draws the selection outline and the tool shapes over the final image, as Hammer draws its tool layers: hidden
/// by the scene and by each other unless their mode draws them over it, blended in linear space, and multisampled
/// with the scene.
/// </summary>
public sealed class ToolsPass : IDisposable
{
    private const float LabelScale = 13f;

    private static readonly ToolRenderLayer[] Layers = Enum.GetValues<ToolRenderLayer>();

    private readonly ToolShaders shaders;
    private readonly SelectionOutline selectionOutline;
    private readonly List<(ToolSceneNode Node, float Distance)> nodes = [];
    private Framebuffer? framebuffer;

    /// <summary>Initializes the pass.</summary>
    /// <param name="rendererContext">Renderer context for loading the tools shader.</param>
    public ToolsPass(RendererContext rendererContext)
    {
        shaders = new ToolShaders(rendererContext);
        selectionOutline = new SelectionOutline(rendererContext);
    }

    /// <summary>
    /// Post processes the scene, outlines the selected nodes, draws the tool shapes of the main scene and the given
    /// ones over it, and puts the result in the target.
    /// </summary>
    /// <param name="renderer">The renderer of the frame.</param>
    /// <param name="sceneFramebuffer">What the scene was rendered into.</param>
    /// <param name="target">Where the final image goes.</param>
    /// <param name="selectedNodes">The nodes outlined as selected.</param>
    /// <param name="worldShapes">Shapes given in world space, such as those of what is selected.</param>
    /// <param name="textRenderer">What the text among the shapes of the main scene is written with.</param>
    public void Render(Renderer renderer, Framebuffer sceneFramebuffer, Framebuffer target, IReadOnlyList<SceneNode> selectedNodes,
        IEnumerable<ToolGeometry> worldShapes, TextRenderer textRenderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(sceneFramebuffer);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(selectedNodes);
        ArgumentNullException.ThrowIfNull(worldShapes);
        ArgumentNullException.ThrowIfNull(textRenderer);

        var output = EnsureFramebuffer(sceneFramebuffer);
        var (width, height) = (sceneFramebuffer.Width, sceneFramebuffer.Height);

        renderer.PostprocessRender(sceneFramebuffer, output);
        selectionOutline.Render(renderer, sceneFramebuffer, output, selectedNodes);

        using (new GLDebugGroup("Tools"))
        {
            // The scene's depth, so the shapes are hidden by it
            GL.BlitNamedFramebuffer(sceneFramebuffer.FboHandle, output.FboHandle, 0, 0, width, height, 0, 0, width, height,
                ClearBufferMask.DepthBufferBit, BlitFramebufferFilter.Nearest);

            output.Bind(FramebufferTarget.Framebuffer);
            GL.Viewport(0, 0, width, height);

            var shapes = worldShapes.Where(static geometry => !geometry.IsEmpty).ToList();

            // Blending happens on linear colors, as the shader gives them
            GL.Enable(EnableCap.FramebufferSrgb);

            using (GraphicsContext.RenderState.Scope(multisampleEnable: output.NumSamples > 1))
            {
                renderer.DrawOverScenes(width, height, view =>
                {
                    CollectNodes(view);

                    // Shapes given in world space are in the space of the main scene
                    var isMainView = view == renderer.MainViewState;
                    var viewShapes = isMainView ? shapes : [];

                    if (isMainView)
                    {
                        AddLabels(textRenderer, renderer.Camera, viewShapes);
                    }

                    foreach (var layer in Layers)
                    {
                        foreach (var (node, _) in nodes)
                        {
                            node.RenderTools(layer, shaders);
                        }

                        foreach (var geometry in viewShapes)
                        {
                            geometry.DrawLayer(layer, shaders, objectId: 0);
                        }
                    }
                });
            }

            GL.Disable(EnableCap.FramebufferSrgb);
        }

        GL.BlitNamedFramebuffer(output.FboHandle, target.FboHandle, 0, 0, width, height, 0, 0, width, height,
            ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);

        // What is drawn over the final image after this, such as text, goes to the target
        target.Bind(FramebufferTarget.Framebuffer);
    }

    private void AddLabels(TextRenderer textRenderer, Camera camera, List<ToolGeometry> worldShapes)
    {
        foreach (var (node, _) in nodes)
        {
            foreach (var label in node.Geometry.Labels)
            {
                AddLabel(textRenderer, camera, label, node.Transform);
            }
        }

        foreach (var geometry in worldShapes)
        {
            foreach (var label in geometry.Labels)
            {
                AddLabel(textRenderer, camera, label, Matrix4x4.Identity);
            }
        }
    }

    private static void AddLabel(TextRenderer textRenderer, Camera camera, in ToolLabel label, in Matrix4x4 transform)
    {
        textRenderer.AddTextBillboard(Vector3.Transform(label.Position, transform), new TextRenderer.TextRenderRequest
        {
            Scale = LabelScale,
            Color = label.Color,
            Text = label.Text,
            CenterHorizontal = true,
            CenterVertical = true,
        }, camera);
    }

    // Far to near, so blended shapes cover the ones behind them
    private void CollectNodes(SceneViewState view)
    {
        nodes.Clear();

        foreach (var request in view.ToolsRenderList)
        {
            if (request.Node is ToolSceneNode { IsHiddenWhileSelected: false } node)
            {
                nodes.Add((node, request.DistanceFromCamera));
            }
        }

        nodes.Sort(static (a, b) => b.Distance.CompareTo(a.Distance));
    }

    private Framebuffer EnsureFramebuffer(Framebuffer sceneFramebuffer)
    {
        var (width, height, samples) = (sceneFramebuffer.Width, sceneFramebuffer.Height, sceneFramebuffer.NumSamples);

        if (framebuffer == null)
        {
            framebuffer = Framebuffer.Prepare(nameof(ToolsPass), width, height, samples, ImageFormat.RGBA8888, sceneFramebuffer.DepthFormat);
            framebuffer.IsColorSrgb = true;
            framebuffer.Initialize();
        }
        else
        {
            framebuffer.Resize(width, height, samples);
        }

        return framebuffer;
    }

    /// <summary>Deletes the GL objects.</summary>
    public void Dispose()
    {
        selectionOutline.Dispose();
        framebuffer?.Delete();
        framebuffer = null;
    }
}
