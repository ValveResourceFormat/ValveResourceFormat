using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.ToolRender;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The two points of an axis one key of a selected entity holds, such as what a door turns about, with a
/// handle at each over everything and a line between them that is dashed where the scene hides it. The points
/// are in world space, or in the space of the entity for a local axis.
/// </summary>
internal sealed class AxisHelper : EntityHelper
{
    /// <inheritdoc/>
    public override bool IsVisual => false;

    /// <inheritdoc/>
    public override bool NeedsShowHelpers => true;

    private const string DefaultAxisKey = "axis";
    private const float HandleSize = 10f;

    /// <summary>Gets the key of the entity that holds the axis.</summary>
    private string AxisKey { get; }

    /// <summary>Gets whether the points are in the space of the entity, rather than in world space.</summary>
    private bool IsLocal { get; }

    public AxisHelper(IHelperOwner owner, HelperDeclaration declaration) : base(owner, declaration)
    {
        AxisKey = (GetArgument(0) ?? DefaultAxisKey).ToLowerInvariant();
        IsLocal = declaration.Name.Equals("localaxis", StringComparison.OrdinalIgnoreCase);
    }

    internal override void Render(ToolRenderContext context, in HelperRenderState state)
    {
        if (!state.IsSelected)
        {
            return;
        }

        var (start, end) = GetPoints();

        context.LayerOverride = ToolRenderLayer.Overlay0;
        context.DrawHandle(HandleSize, ToolHandleType.Circle, Color32.White, start, noZ: true, border: false, translucent: true);
        context.DrawHandle(HandleSize, ToolHandleType.Circle, Color32.White, end, noZ: true, border: false, translucent: true);
        context.LayerOverride = null;

        DrawLineOverHidden(context, start, end, Color32.White);
    }

    // Both points are at the entity until the key holds all six numbers, comma and all
    private (Vector3 Start, Vector3 End) GetPoints()
    {
        var start = Vector3.Zero;
        var end = Vector3.Zero;

        if (KeyValueScanner.ReadAxis(GetStringKey(AxisKey), ref start, ref end) < 6)
        {
            var origin = Owner.RigidTransform.Translation;
            return (origin, origin);
        }

        return IsLocal
            ? (Vector3.Transform(start, Owner.RigidTransform), Vector3.Transform(end, Owner.RigidTransform))
            : (start, end);
    }
}
