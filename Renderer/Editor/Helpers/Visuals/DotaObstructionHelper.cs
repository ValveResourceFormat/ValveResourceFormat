using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.Entities;
using ValveResourceFormat.Renderer.Editor.ToolRender;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// What blocks the grid units move on: a model where the entity snaps to the grid, either the one the class
/// names, which only stands in for it in the editor, or the one its <c>model</c> key does. A magenta box
/// stands there where there is no model.
/// </summary>
public sealed class DotaObstructionHelper : ModelHelper
{
    private const float BoxHalfSize = 16f;

    private static readonly Color32 BoxColor = new(255, 0, 255);

    // The edge of a cell of the grid the game data of the game declares, or null where it declares none
    private readonly float? gridCellSize;

    /// <inheritdoc/>
    protected override bool FollowsEntity => false;

    private DotaObstructionHelper(IHelperOwner owner, FgdHelper declaration, Settings settings, float? gridCellSize)
        : base(owner, declaration, settings, LoadModel(owner, settings.ModelName))
    {
        this.gridCellSize = gridCellSize;
    }

    /// <summary>Creates the obstruction of an entity.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    /// <returns>The obstruction, which is drawn as a box where there is no model.</returns>
    public static new DotaObstructionHelper Create(IHelperOwner owner, FgdHelper declaration)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(declaration);

        var declared = declaration.Arguments.Count > 0 ? declaration.Arguments[0] : null;
        var name = declared ?? GetStringKey(owner, "model");

        var settings = new Settings
        {
            ModelName = string.IsNullOrEmpty(name) ? null : GetModelName(name),
            IsEditorModel = declared != null,
            IsNamedByClass = declared != null,

            // A tree is drawn with the second choice of its first body group
            ForcedFirstBodyGroup = declared == null && owner.Classname.Equals("ent_dota_tree", StringComparison.OrdinalIgnoreCase) ? 1 : null,
        };

        var rendererContext = owner.Scene.RendererContext;
        var gridNav = EditorFgd.Load(rendererContext.FileLoader, rendererContext.Logger).GridNav;

        return new DotaObstructionHelper(owner, declaration, settings, gridNav is { EdgeSize: > 0 } grid ? grid.EdgeSize : null);
    }

    /// <inheritdoc/>
    public override void AddToScene()
    {
        base.AddToScene();

        if (ModelNode == null)
        {
            AddShapes();
        }
    }

    /// <inheritdoc/>
    protected override Matrix4x4 GetModelTransform()
    {
        var transform = Owner.Transform;
        transform.Translation = GetSnappedOrigin();

        return transform;
    }

    internal override void Render(ToolRenderContext context, in HelperRenderState state)
    {
        base.Render(context, state);

        if (ModelNode != null)
        {
            return;
        }

        var center = GetSnappedOrigin();
        var (mins, maxs) = (center - new Vector3(BoxHalfSize), center + new Vector3(BoxHalfSize));

        // Lit faces, tinted while selected and outlined then too
        var color = state.IsSelected ? SelectionOverlayColor : BoxColor;

        context.PushRenderMode(ToolRenderMode.Flat);
        context.DrawBoxFaces(mins, maxs, color);
        context.PopRenderMode();

        if (!state.IsSelected)
        {
            return;
        }

        context.PushRenderMode(ToolRenderMode.Wireframe);
        context.DrawColor = color;
        context.DrawBoxFaceOutlines(mins, maxs);
        context.PopRenderMode();
    }

    // Across the grid the entity moves to the nearest line in each direction, keeping its height
    private Vector3 GetSnappedOrigin()
    {
        var origin = Owner.Transform.Translation;

        if (gridCellSize is not { } size)
        {
            return origin;
        }

        float Snap(float value) => MathF.Floor(value / size - 0.5f) * size + size;

        return new Vector3(Snap(origin.X), Snap(origin.Y), origin.Z);
    }
}
