using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer.Editor.Selection;

namespace ValveResourceFormat.Renderer.Editor.ToolRender;

/// <summary>
/// Draws the selection outline, the reference grid and the tool shapes over the final image, as Hammer draws its
/// tool layers: hidden by the scene and by each other unless their mode draws them over it, blended in linear
/// space, and multisampled with the scene.
/// </summary>
public sealed class ToolsPass : IDisposable
{
    private const float LabelScale = 13f;

    private static readonly ToolRenderLayer[] Layers = Enum.GetValues<ToolRenderLayer>();

    private readonly ToolShaders shaders;
    private readonly Framebuffer sceneFramebuffer;
    private readonly Framebuffer output;
    private readonly InfiniteGrid grid;
    private readonly List<(ToolSceneNode Node, float Distance)> nodes = [];

    /// <summary>Gets or sets whether the reference grid is drawn in the main scene.</summary>
    public bool ShowGrid { get; set; }

    /// <summary>Initializes the pass.</summary>
    /// <param name="rendererContext">Renderer context for loading the tools and grid shaders.</param>
    /// <param name="sceneFramebuffer">What the scene is rendered into, whose depth hides the shapes.</param>
    public ToolsPass(RendererContext rendererContext, Framebuffer sceneFramebuffer)
    {
        ArgumentNullException.ThrowIfNull(sceneFramebuffer);

        shaders = new ToolShaders(rendererContext);
        grid = new InfiniteGrid(rendererContext);
        this.sceneFramebuffer = sceneFramebuffer;

        output = Framebuffer.Prepare(nameof(ToolsPass), sceneFramebuffer.Width, sceneFramebuffer.Height, sceneFramebuffer.NumSamples,
            ImageFormat.RGBA8888, sceneFramebuffer.DepthFormat);
        output.IsColorSrgb = true;
        output.Initialize();
    }

    /// <summary>
    /// Shrinks the output framebuffer to free its memory. The next <see cref="Render"/> restores its size.
    /// </summary>
    public void ReleaseOutput() => output.Resize(1, 1);

    /// <summary>
    /// Post processes the scene, outlines the selected nodes, draws the grid and the occlusion debug of the main
    /// scene, then its tool shapes, those of the selection and the extra shapes over it, and puts the result in the
    /// target.
    /// </summary>
    /// <param name="renderer">The renderer of the frame.</param>
    /// <param name="target">Where the final image goes.</param>
    /// <param name="selection">What is selected, with its shapes in world space.</param>
    /// <param name="extraShapes">Shapes in world space of the main scene that are not tied to a node or the selection.</param>
    /// <param name="textRenderer">What the text among the shapes of the main scene is written with.</param>
    public void Render(Renderer renderer, Framebuffer target, SelectionVisuals selection, ToolGeometry extraShapes, TextRenderer textRenderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(extraShapes);
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
                var state = (Pass: this, Renderer: renderer, Selection: selection, ExtraShapes: extraShapes, TextRenderer: textRenderer);

                renderer.DrawOverScenes(output, ref state, static (context, ref state) =>
                {
                    state.Pass.RenderScene(context.View!, state.Renderer, state.Selection, state.ExtraShapes, state.TextRenderer);
                });
            }

            GL.Disable(EnableCap.FramebufferSrgb);
        }

        GL.BlitNamedFramebuffer(output.FboHandle, target.FboHandle, 0, 0, width, height, 0, 0, width, height,
            ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);

        // What is drawn over the final image after this, such as text, goes to the target
        target.Bind(FramebufferTarget.Framebuffer);
    }

    private void RenderScene(SceneViewState view, Renderer renderer, SelectionVisuals selection, ToolGeometry extraShapes, TextRenderer textRenderer)
    {
        CollectNodes(view);

        // The shapes of the selection and the extra shapes are in the space of the main scene
        var isMainView = view == renderer.MainViewState;
        var selectionShapes = isMainView && !selection.Shapes.IsEmpty ? selection.Shapes : null;
        var mainExtraShapes = isMainView && !extraShapes.IsEmpty ? extraShapes : null;

        AddLabels(textRenderer, renderer.Camera);
        AddLabels(textRenderer, renderer.Camera, selectionShapes);
        AddLabels(textRenderer, renderer.Camera, mainExtraShapes);

        if (isMainView)
        {
            DrawMainSceneOverlays(renderer);
        }

        foreach (var layer in Layers)
        {
            foreach (var (node, _) in nodes)
            {
                node.RenderTools(layer, shaders);
            }

            selectionShapes?.DrawLayer(layer, shaders, objectId: 0);
            mainExtraShapes?.DrawLayer(layer, shaders, objectId: 0);
        }
    }

    private void DrawMainSceneOverlays(Renderer renderer)
    {
        using var _ = new GLDebugGroup("Overlays");

        if (renderer.Settings.OcclusionDebugEnabled)
        {
            renderer.Scene.OcclusionDebug?.Render();
        }

        if (ShowGrid)
        {
            grid.Render();
        }
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

        var requests = view.ToolsRenderList;

        for (var i = 0; i < requests.Count; i++)
        {
            if (requests[i].Node is ToolSceneNode { IsHiddenWhileSelected: false } node)
            {
                nodes.Add((node, requests[i].DistanceFromCamera));
            }
        }

        nodes.Sort(static (a, b) => b.Distance.CompareTo(a.Distance));
    }

    /// <summary>Deletes the GL objects.</summary>
    public void Dispose() => output.Delete();
}
