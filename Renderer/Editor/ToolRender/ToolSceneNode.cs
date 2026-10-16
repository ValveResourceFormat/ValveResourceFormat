namespace ValveResourceFormat.Renderer.Editor.ToolRender;

/// <summary>
/// Tool shapes that are part of the scene, such as what an entity's helpers draw while it is not selected. They
/// are culled and shown with the layer they are on, drawn in the tools pass, and picked with the scene.
/// </summary>
public sealed class ToolSceneNode : SceneNode
{
    /// <summary>Gets the shapes, in the space of the node.</summary>
    public ToolGeometry Geometry { get; } = new();

    /// <summary>
    /// Gets or sets whether the shapes are not drawn because what they belong to is selected, and is drawn the
    /// way it is while selected instead.
    /// </summary>
    public bool IsHiddenWhileSelected { get; set; }

    /// <summary>Initializes a node with no shapes yet.</summary>
    /// <param name="scene">The scene the node belongs to.</param>
    public ToolSceneNode(Scene scene) : base(scene)
    {
        RenderPasses = CustomRenderPasses.Opaque | CustomRenderPasses.Tools;
    }

    /// <summary>Draws shapes into the node, and fits its bounds to everything it holds.</summary>
    /// <param name="draw">Draws the shapes, in the space of the node.</param>
    public void Draw(Action<ToolRenderContext> draw)
    {
        ArgumentNullException.ThrowIfNull(draw);

        draw(new ToolRenderContext(Geometry));
        LocalBoundingBox = Geometry.Bounds;
    }

    /// <inheritdoc/>
    public override void Render(Scene.RenderContext context)
    {
        // The scene passes only pick it, the tools pass draws it
        if (context.RenderPass == RenderPass.Opaque && context.ReplacementShader is { } shader)
        {
            Geometry.DrawWith(shader, Id);
        }
    }

    /// <summary>Draws the shapes of one layer in the tools pass.</summary>
    internal void RenderTools(ToolRenderLayer layer, ToolShaders shaders) => Geometry.DrawLayer(layer, shaders, Id);

    /// <inheritdoc/>
    public override void Delete()
    {
        Geometry.Delete();
    }
}
