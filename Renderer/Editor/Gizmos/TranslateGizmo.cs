using System.Globalization;
using ValveResourceFormat.Renderer.Editor.ToolRender;

namespace ValveResourceFormat.Renderer.Editor.Gizmos;

/// <summary>
/// Moves what is selected, the way Hammer's translate gizmo does: along an axis by its arrow, within a plane by
/// the square between two axes, or freely by its center. A drag snaps where the selection ends up, not how far it
/// moved, and shows where it would be without snapping.
/// </summary>
public sealed class TranslateGizmo : Gizmo
{
    private static readonly Color32 CenterColor = new(200, 60, 200, 255);
    private static readonly Color32 DragLineColor = new(0, 110, 200, 255);
    private static readonly Color32 DistanceTextColor = new(224, 224, 224, 224);

    // Each plane by the part it is, its two axes, and the axis whose color it has
    private static readonly (GizmoPart Part, int AxisA, int AxisB, int ColorAxis)[] Planes =
    [
        (GizmoPart.PlaneXY, 0, 1, 2),
        (GizmoPart.PlaneYZ, 1, 2, 0),
        (GizmoPart.PlaneXZ, 0, 2, 1),
    ];

    private Vector3 dragStart;
    private Vector3 ghostOffset;

    /// <summary>Initializes a translate gizmo.</summary>
    /// <param name="snapping">How its drags snap, or <see langword="null"/> for not at all.</param>
    public TranslateGizmo(IGizmoSnapping? snapping) : base(snapping)
    {
    }

    /// <summary>Gets or sets whether a drag is drawn with a line back to where it started, labelled with its length.</summary>
    public bool ShowDistance { get; set; } = true;

    /// <inheritdoc/>
    protected override GizmoResultFlags ResultFlags => IsLocalSpace ? GizmoResultFlags.Local : GizmoResultFlags.None;

    /// <inheritdoc/>
    protected override void Draw(ToolRenderContext context, Camera camera, ToolPickTargets picks)
    {
        var position = Vector3.Transform(Pivot, Result.Transform);
        var part = ActivePart != GizmoPart.None ? ActivePart : HoveredPart;

        DrawGizmo(context, camera, picks, position, part, enabled: true);

        // Where the drag would be without snapping
        if (ActivePart != GizmoPart.None && Snapping?.IsSnappingEnabled == true && ghostOffset != Vector3.Zero)
        {
            DrawGizmo(context, camera, picks, position + ghostOffset, part, enabled: false);
        }
    }

    private void DrawGizmo(ToolRenderContext context, Camera camera, ToolPickTargets picks, Vector3 position, GizmoPart part, bool enabled)
    {
        var pixels = AxisLengthPixels;
        var length = GetScreenSize(camera, position, pixels);
        var dragging = ActivePart != GizmoPart.None;

        bool IsVisible(GizmoPart visiblePart)
        {
            if (dragging)
            {
                return visiblePart == ActivePart || visiblePart == GizmoPart.Center;
            }

            // An axis along the view hides with the planes it is in
            return visiblePart switch
            {
                GizmoPart.AxisX or GizmoPart.AxisY or GizmoPart.AxisZ => !IsAlongView(camera, position, GetAxis(visiblePart - GizmoPart.AxisX)),
                GizmoPart.PlaneXY => !IsAlongView(camera, position, GetAxis(0)) && !IsAlongView(camera, position, GetAxis(1)),
                GizmoPart.PlaneYZ => !IsAlongView(camera, position, GetAxis(1)) && !IsAlongView(camera, position, GetAxis(2)),
                GizmoPart.PlaneXZ => !IsAlongView(camera, position, GetAxis(0)) && !IsAlongView(camera, position, GetAxis(2)),
                _ => true,
            };
        }

        context.LayerOverride = ToolRenderLayer.Overlay1;
        context.PushRenderMode(enabled ? ToolRenderMode.Flat : ToolRenderMode.TranslucentFlat);

        var coneRadius = length * 0.1f;
        var centerSize = GetWorldUnitsPerPixel(camera, position, pixels) * CenterHandleSize;

        for (var axis = 0; axis < 3; axis++)
        {
            var axisPart = GizmoPart.AxisX + axis;

            if (!IsVisible(axisPart))
            {
                continue;
            }

            var direction = GetAxis(axis);
            var color = GetPartColor(axisPart, GetAxisColor(axis), part, enabled);

            context.BeginPickObject(picks, (int)axisPart);
            context.DrawLine(position, position + direction * length, color);
            context.DrawSolidCone(position + direction * length, position + direction * (length * 1.25f), coneRadius, 8, color, color);

            // The arrow is picked by a wider band along it
            if (!dragging)
            {
                context.PushRenderMode(ToolRenderMode.PickProxy);
                context.DrawScreenAlignedLine(position + direction * centerSize, position + direction * (length * 1.25f), 2f * AxisPickScale * coneRadius, color, doubleSided: true);
                context.PopRenderMode();
            }

            context.EndPickObject();
        }

        if (IsVisible(GizmoPart.Center))
        {
            context.BeginPickObject(picks, (int)GizmoPart.Center);
            context.DrawHandle(CenterHandleSize, ToolHandleType.Circle, GetPartColor(GizmoPart.Center, CenterColor, part, enabled), position, noZ: false, border: false, translucent: !enabled);
            context.EndPickObject();
        }

        foreach (var (planePart, axisA, axisB, colorAxis) in Planes)
        {
            if (!IsVisible(planePart))
            {
                continue;
            }

            var a = GetAxis(axisA);
            var b = GetAxis(axisB);
            var color = GetPartColor(planePart, GetAxisColor(colorAxis), part, enabled);

            context.BeginPickObject(picks, (int)planePart);

            // A plane being dragged is drawn as its outline, so what it moves shows through it
            if (dragging)
            {
                context.PushRenderMode(ToolRenderMode.Wireframe);
                context.DrawOrientedRect(position, a, b, length * 0.5f, length * 0.75f, length * 0.5f, length * 0.75f, color, outline: true);
                context.PopRenderMode();
            }
            else
            {
                context.DrawOrientedRect(position, a, b, length * 0.5f, length * 0.75f, length * 0.5f, length * 0.75f, color, outline: false);
            }

            context.PushRenderMode(ToolRenderMode.PickProxy);
            context.DrawOrientedRect(position, a, b, length * 0.5f, length * 0.75f, length * 0.5f, length * 0.75f, color, outline: false);
            context.PopRenderMode();
            context.EndPickObject();
        }

        if (enabled && dragging && IsDragging && ShowDistance)
        {
            context.LayerOverride = ToolRenderLayer.Overlay2;
            context.DrawLine(position, Pivot, DragLineColor);

            var distance = Vector3.Distance(position, Pivot);
            context.DrawText(distance.ToString("0.00", CultureInfo.InvariantCulture), (position + Pivot) * 0.5f, DistanceTextColor);
        }

        context.PopRenderMode();
        context.LayerOverride = null;
    }

    /// <inheritdoc/>
    protected override void OnBeginDrag(Camera camera, Vector2 mouse)
    {
        var viewDirection = camera.ScreenPointToDirection(mouse);
        var normal = ActivePart switch
        {
            GizmoPart.AxisX or GizmoPart.AxisY or GizmoPart.AxisZ => GetPlaneNormalAlongView(ActivePart - GizmoPart.AxisX, viewDirection),
            GizmoPart.PlaneXY => GetAxis(2),
            GizmoPart.PlaneYZ => GetAxis(0),
            GizmoPart.PlaneXZ => GetAxis(1),

            // The center moves along the ground of the world
            _ => new Vector3(WorkPlane.M31, WorkPlane.M32, WorkPlane.M33),
        };

        SetDragPlane(normal, Pivot);
        dragStart = IntersectMouseWithDragPlane(camera, mouse);
        ghostOffset = Vector3.Zero;
    }

    /// <inheritdoc/>
    protected override void OnUpdateDrag(Camera camera, Vector2 mouse)
    {
        var delta = ActivePart == GizmoPart.Center ? GetFreeDelta(camera, mouse) : GetConstrainedDelta(camera, mouse);

        Result.Transform = Matrix4x4.CreateTranslation(delta);
    }

    /// <inheritdoc/>
    protected override void OnEndDrag()
    {
        ghostOffset = Vector3.Zero;
    }

    // The center goes to the point under the cursor, on the plane through where the drag started
    private Vector3 GetFreeDelta(Camera camera, Vector2 mouse)
    {
        var point = IntersectMouseWithDragPlane(camera, mouse);

        if (Snapping?.IsSnappingEnabled == true)
        {
            Snapping.SnapPosition(ref point, GizmoSnapConstraint.Plane, DragPlaneNormal);
        }

        return point - Pivot;
    }

    // How far the cursor moved along the axis or in the plane dragged, keeping where on the part it was grabbed
    private Vector3 GetConstrainedDelta(Camera camera, Vector2 mouse)
    {
        var isAxis = ActivePart is GizmoPart.AxisX or GizmoPart.AxisY or GizmoPart.AxisZ;
        var axis = isAxis ? GetAxis(ActivePart - GizmoPart.AxisX) : Vector3.Zero;

        Vector3 Constrain(Vector3 delta) => isAxis
            ? axis * Vector3.Dot(delta, axis)
            : delta - DragPlaneNormal * Vector3.Dot(delta, DragPlaneNormal);

        var delta = Constrain(IntersectMouseWithDragPlane(camera, mouse) - dragStart);
        ghostOffset = Vector3.Zero;

        if (Snapping?.IsSnappingEnabled != true)
        {
            return delta;
        }

        // Where the selection ends up is snapped, not how far it moved
        var unsnapped = delta;
        var position = Pivot + delta;
        var snappedToGrid = isAxis
            ? Snapping.SnapPosition(ref position, GizmoSnapConstraint.Axis, axis)
            : Snapping.SnapPosition(ref position, GizmoSnapConstraint.Plane, DragPlaneNormal);

        delta = Constrain(position - Pivot);

        if (snappedToGrid)
        {
            ghostOffset = unsnapped - delta;
        }

        return delta;
    }
}
