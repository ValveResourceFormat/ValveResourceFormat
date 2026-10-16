using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.ToolRender;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The space a player takes up at either end of a ladder, standing at the entity and at its <c>point1</c> key,
/// which is in the space of the entity, with a line between the two and a handle at the end while the entity
/// is selected.
/// </summary>
public sealed class SweptPlayerHullHelper : EntityHelper
{
    /// <inheritdoc/>
    public override bool HiddenInGameObjectsOnly => true;

    private const string EndKey = "point1";

    // A cylinder around the player box, which is 32 units across and 72 high
    private const float HullHeight = 72f;
    private const float HullRadius = 22.6272f;
    private const int HullSegments = 16;

    // The box a player takes up, which is what the entity covers rather than the round hull drawn
    private static readonly Vector3 HullMins = new(-16f, -16f, 0f);
    private static readonly Vector3 HullMaxs = new(16f, 16f, HullHeight);

    private const float EndHandleSize = 6f;

    private static readonly Color32 HullColor = new(255, 0, 255);
    private static readonly Color32 LineColor = new(200, 180, 0);
    private static readonly Color32 SelectedLineColor = new(255, 0, 0);

    /// <summary>Initializes the hulls a class declares.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    public SweptPlayerHullHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
    {
    }

    /// <inheritdoc/>
    public override void AddToScene()
    {
        AddShapes();
    }

    /// <inheritdoc/>
    public override AABB? ObjectBounds
    {
        get
        {
            var (start, end) = GetEnds();
            return new AABB(start + HullMins, start + HullMaxs).Union(new AABB(end + HullMins, end + HullMaxs));
        }
    }

    // Where the hull stands at the entity and at the other end, in the world
    private (Vector3 Start, Vector3 End) GetEnds()
    {
        // The numbers the key holds are read as far as they go, the rest of the point staying at zero
        Span<float> point = stackalloc float[3];
        KeyValueScanner.Scan(GetStringKey(EndKey), point);

        return (Owner.RigidTransform.Translation, Vector3.Transform(new Vector3(point), Owner.RigidTransform));
    }

    internal override void Render(ToolRenderContext context, in HelperRenderState state)
    {
        var (start, end) = GetEnds();

        foreach (var position in (ReadOnlySpan<Vector3>)[start, end])
        {
            context.DrawSolidCylinderPart(position, position + new Vector3(0f, 0f, HullHeight), HullRadius, HullSegments, true, HullColor, true, ToolFaceSides.Front);
        }

        context.DrawColor = state.IsSelected ? SelectedLineColor : LineColor;
        context.DrawLine(start, end);

        // The end can be moved by its handle while the entity is selected
        if (state.IsSelected)
        {
            context.DrawHandle(EndHandleSize, ToolHandleType.Circle, Color32.White, end, noZ: false, border: true, translucent: false);
        }
    }
}
