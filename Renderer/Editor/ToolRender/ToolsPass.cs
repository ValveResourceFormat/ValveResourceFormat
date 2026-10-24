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
    private readonly Framebuffer sceneFramebuffer;
    private readonly Framebuffer output;
    private readonly List<(ToolSceneNode Node, float Distance)> nodes = [];

    /// <summary>Initializes the pass.</summary>
    /// <param name="rendererContext">Renderer context for loading the tools shader.</param>
    /// <param name="sceneFramebuffer">What the scene is rendered into, whose depth hides the shapes.</param>
    public ToolsPass(RendererContext rendererContext, Framebuffer sceneFramebuffer)
    {
        ArgumentNullException.ThrowIfNull(sceneFramebuffer);

        shaders = new ToolShaders(rendererContext);
        this.sceneFramebuffer = sceneFramebuffer;

        output = Framebuffer.Prepare(nameof(ToolsPass), sceneFramebuffer.Width, sceneFramebuffer.Height, sceneFramebuffer.NumSamples,
            ImageFormat.RGBA8888, sceneFramebuffer.DepthFormat);
        output.IsColorSrgb = true;
        output.Initialize();
    }

    /// <summary>
    /// Post processes the scene, outlines the selected nodes, draws the tool shapes of the main scene and those of
    /// the selection over it, and puts the result in the target.
    /// </summary>
    /// <param name="renderer">The renderer of the frame.</param>
    /// <param name="target">Where the final image goes.</param>
    /// <param name="selection">What is selected, with its shapes in world space.</param>
    /// <param name="textRenderer">What the text among the shapes of the main scene is written with.</param>
    public void Render(Renderer renderer, Framebuffer target, SelectionVisuals selection, TextRenderer textRenderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(textRenderer);

        var (width, height) = (sceneFramebuffer.Width, sceneFramebuffer.Height);
        output.Resize(width, height, sceneFramebuffer.NumSamples);

        renderer.PostprocessRender(sceneFramebuffer, output);
        selection.RenderOutline(renderer, sceneFramebuffer, output);

        using (new GLDebugGroup("Tools"))
        {
            // The scene's depth, so the shapes are hidden by it
            GL.BlitNamedFramebuffer(sceneFramebuffer.FboHandle, output.FboHandle, 0, 0, width, height, 0, 0, width, height,
                ClearBufferMask.DepthBufferBit, BlitFramebufferFilter.Nearest);

            output.Bind(FramebufferTarget.Framebuffer);

            // Blending happens on linear colors, as the shader gives them
            GL.Enable(EnableCap.FramebufferSrgb);

            using (GraphicsContext.RenderState.Scope(multisampleEnable: output.NumSamples > 1))
            {
                renderer.DrawOverScenes(output, context =>
                {
                    var view = context.View!;
                    CollectNodes(view);

                    // The shapes of the selection are in the space of the main scene
                    var isMainView = view == renderer.MainViewState;
                    var selectionShapes = isMainView && !selection.Shapes.IsEmpty ? selection.Shapes : null;

                    AddLabels(textRenderer, renderer.Camera);
                    AddLabels(textRenderer, renderer.Camera, selectionShapes);

                    foreach (var layer in Layers)
                    {
                        foreach (var (node, _) in nodes)
                        {
                            node.RenderTools(layer, shaders);
                        }

                        selectionShapes?.DrawLayer(layer, shaders, objectId: 0);
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

    private void AddLabels(TextRenderer textRenderer, Camera camera)
    {
        foreach (var (node, _) in nodes)
        {
            // Written with the main camera, so the labels of a 3D sky go where the sky appears in the world
            var toViewerWorld = node.Transform * node.Scene.ToViewerWorld;

            foreach (var label in node.Geometry.Labels)
            {
                AddLabel(textRenderer, camera, label, toViewerWorld);
            }
        }
    }

    private static void AddLabels(TextRenderer textRenderer, Camera camera, ToolGeometry? worldShapes)
    {
        if (worldShapes == null)
        {
            return;
        }

        foreach (var label in worldShapes.Labels)
        {
            AddLabel(textRenderer, camera, label, Matrix4x4.Identity);
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

    /// <summary>Deletes the GL objects.</summary>
    public void Dispose() => output.Delete();
}
