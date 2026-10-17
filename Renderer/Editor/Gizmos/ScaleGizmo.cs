using ValveResourceFormat.Renderer.Editor.ToolRender;

namespace ValveResourceFormat.Renderer.Editor.Gizmos;

/// <summary>
/// Scales what is selected, the way Hammer's scale gizmo does: along an axis by the cube at its end, within a
/// plane by the square between two axes, or along every axis by its center. An axis or plane scales by how far
/// the cursor is from the pivot against how far the grabbed handle was. While snapping, the edges of the bounds of
/// the selection land on the grid, and it shrinks no smaller than a step of the grid.
/// </summary>
public sealed class ScaleGizmo : Gizmo
{
    private const float MinimumScale = 0.001f;
    private const float MaximumScale = 10000f;

    // The center scales by a hundredth for every pixel the cursor moves right
    private const float UniformScalePerPixel = 0.01f;

    private static readonly Color32 CenterColor = new(200, 60, 200, 255);

    private static readonly (GizmoPart Part, int AxisA, int AxisB, int ColorAxis)[] Planes =
    [
        (GizmoPart.PlaneXY, 0, 1, 2),
        (GizmoPart.PlaneYZ, 1, 2, 0),
        (GizmoPart.PlaneXZ, 0, 2, 1),
    ];

    private Vector2 mouseStart;
    private Vector3 dragStart;
    private Vector3 handlePosition;
    private AABB dragBounds;

    /// <summary>Initializes a scale gizmo.</summary>
    /// <param name="snapping">How its drags snap, or <see langword="null"/> for not at all.</param>
    public ScaleGizmo(IGizmoSnapping? snapping) : base(snapping)
    {
    }

    /// <inheritdoc/>
    protected override GizmoResultFlags ResultFlags
        => GizmoResultFlags.Scale
        | (ActivePart == GizmoPart.UniformScale ? GizmoResultFlags.UniformScale : GizmoResultFlags.None)
        | (IsLocalSpace && Kind == GizmoSelectionKind.Multiple ? GizmoResultFlags.Local : GizmoResultFlags.None);

    /// <inheritdoc/>
    protected override void Draw(ToolRenderContext context, Camera camera, ToolPickTargets picks)
    {
        var position = Pivot;
        var part = ActivePart != GizmoPart.None ? ActivePart : HoveredPart;
        var dragging = ActivePart != GizmoPart.None;
        var length = GetScreenSize(camera, position, AxisLengthPixels);
        var halfCube = length * 0.1f;
        var scale = Result.Scale;

        bool IsVisible(GizmoPart visiblePart)
        {
            if (dragging)
            {
                return visiblePart == ActivePart || visiblePart == GizmoPart.UniformScale;
            }

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

        for (var axis = 0; axis < 3; axis++)
        {
            var axisPart = GizmoPart.AxisX + axis;

            if (!IsVisible(axisPart))
            {
                continue;
            }

            var direction = GetAxis(axis);
            var color = GetPartColor(axisPart, GetAxisColor(axis), part);
            var end = position + direction * (scale[axis] * length);

            context.BeginPickObject(picks, (int)axisPart);
            context.DrawLine(position, end, color);

            context.PushRenderMode(ToolRenderMode.Flat);
            context.PushTransform(GetFrameAt(end));
            context.DrawBoxFaces(new Vector3(-halfCube), new Vector3(halfCube), color);
            context.PopTransform();
            context.PopRenderMode();

            if (!dragging)
            {
                context.PushRenderMode(ToolRenderMode.PickProxy);
                context.DrawScreenAlignedLine(position + direction * halfCube, position + direction * (halfCube + length), 2f * AxisPickScale * halfCube, color, doubleSided: true);
                context.PopRenderMode();
            }

            context.EndPickObject();
        }

        if (IsVisible(GizmoPart.UniformScale))
        {
            var color = GetPartColor(GizmoPart.UniformScale, CenterColor, part);

            if (dragging)
            {
                context.DrawHandle(CenterHandleSize, ToolHandleType.Circle, color, position, noZ: false, border: false, translucent: false);
            }
            else
            {
                context.BeginPickObject(picks, (int)GizmoPart.UniformScale);
                context.PushRenderMode(ToolRenderMode.Flat);
                context.PushTransform(GetFrameAt(position));
                context.DrawBoxFaces(new Vector3(-halfCube), new Vector3(halfCube), color);
                context.PopTransform();
                context.PopRenderMode();
                context.EndPickObject();
            }
        }

        foreach (var (planePart, axisA, axisB, colorAxis) in Planes)
        {
            if (!IsVisible(planePart))
            {
                continue;
            }

            var a = GetAxis(axisA);
            var b = GetAxis(axisB);
            var color = GetPartColor(planePart, GetAxisColor(colorAxis), part);

            // The square moves out with the scale of its two axes
            var minA = length * 0.5f * scale[axisA];
            var minB = length * 0.5f * scale[axisB];
            var (maxA, maxB) = (minA + length * 0.25f, minB + length * 0.25f);

            if (dragging)
            {
                context.PushRenderMode(ToolRenderMode.Wireframe);
                context.DrawOrientedRect(position, a, b, minA, maxA, minB, maxB, color, outline: true);
                context.PopRenderMode();
            }
            else
            {
                context.PushRenderMode(ToolRenderMode.Flat);
                context.DrawOrientedRect(position, a, b, minA, maxA, minB, maxB, color, outline: false);
                context.PopRenderMode();
            }

            context.BeginPickObject(picks, (int)planePart);
            context.PushRenderMode(ToolRenderMode.PickProxy);
            context.DrawOrientedRect(position, a, b, minA, maxA, minB, maxB, color, outline: false);
            context.PopRenderMode();
            context.EndPickObject();
        }

        context.LayerOverride = null;
    }

    /// <inheritdoc/>
    protected override void OnBeginDrag(Camera camera, Vector2 mouse)
    {
        mouseStart = mouse;
        dragBounds = IsLocalSpace ? LocalBounds : WorldBounds;

        if (ActivePart == GizmoPart.UniformScale)
        {
            return;
        }

        var length = GetScreenSize(camera, Pivot, AxisLengthPixels);
        var viewDirection = camera.ScreenPointToDirection(mouse);

        // Where each part is grabbed from, which a drag scales by its distance from the pivot
        handlePosition = ActivePart switch
        {
            GizmoPart.PlaneXY => Pivot + (GetAxis(0) + GetAxis(1)) * (length * 0.55f),
            GizmoPart.PlaneYZ => Pivot + (GetAxis(1) + GetAxis(2)) * (length * 0.55f),
            GizmoPart.PlaneXZ => Pivot + (GetAxis(0) + GetAxis(2)) * (length * 0.55f),
            _ => Pivot + GetAxis(ActivePart - GizmoPart.AxisX) * length,
        };

        var normal = ActivePart switch
        {
            GizmoPart.PlaneXY => GetAxis(2),
            GizmoPart.PlaneYZ => GetAxis(0),
            GizmoPart.PlaneXZ => GetAxis(1),
            _ => GetPlaneNormalAlongView(ActivePart - GizmoPart.AxisX, viewDirection),
        };

        SetDragPlane(normal, Pivot);
        dragStart = IntersectMouseWithDragPlane(camera, mouse);
    }

    /// <inheritdoc/>
    protected override void OnUpdateDrag(Camera camera, Vector2 mouse)
    {
        if (ActivePart == GizmoPart.UniformScale)
        {
            var uniform = Math.Clamp(1f + UniformScalePerPixel * (mouse.X - mouseStart.X), MinimumScale, MaximumScale);

            Result.Transform = Matrix4x4.CreateScale(uniform);
            Result.Scale = new Vector3(uniform);
            return;
        }

        var point = IntersectMouseWithDragPlane(camera, mouse);

        if (ActivePart is GizmoPart.AxisX or GizmoPart.AxisY or GizmoPart.AxisZ)
        {
            var axis = Vector3.Normalize(GetAxis(ActivePart - GizmoPart.AxisX));
            point = dragStart + axis * (Vector3.Dot(point, axis) - Vector3.Dot(dragStart, axis));
        }

        ComputeScale(point);
    }

    // How far the cursor is from the pivot against how far the handle was, measured in the frame of the gizmo
    private void ComputeScale(Vector3 point)
    {
        if (!Matrix4x4.Invert(Frame, out var toFrame))
        {
            return;
        }

        var origin = Vector3.Transform(Pivot, toFrame);
        var handle = Vector3.Transform(handlePosition, toFrame) - origin;
        var cursor = Vector3.Transform(point, toFrame) - origin;
        var handleDistance = handle.Length();

        if (handleDistance <= 0f)
        {
            return;
        }

        var direction = handle / handleDistance;
        var factor = MathF.Max(0f, Vector3.Dot(direction, cursor)) / handleDistance;

        if (Snapping?.IsSnappingEnabled == true)
        {
            factor = SnapScaleToGrid(Snapping, factor, direction, origin, dragBounds);

            // Shrinking stops at the smallest size the snapping allows
            if (factor < 1f)
            {
                var smallest = float.MaxValue;

                for (var i = 0; i < 3; i++)
                {
                    var extent = dragBounds.Max[i] - dragBounds.Min[i];

                    if (direction[i] > 1.1920929e-7f && extent > 1.1920929e-7f)
                    {
                        smallest = MathF.Min(smallest, MathF.Min(1f, Snapping.ClampMinimumSize(extent * factor) / extent));
                    }
                }

                if (smallest <= 1f)
                {
                    factor = MathF.Max(factor, smallest);
                }
            }
        }

        var scale = new Vector3(
            MathF.Abs(direction.X) > 0.001f ? factor : 1f,
            MathF.Abs(direction.Y) > 0.001f ? factor : 1f,
            MathF.Abs(direction.Z) > 0.001f ? factor : 1f);

        Result.ScaleStart = handle;
        Result.ScaleCurrent = handle * scale;
        Result.Scale = scale;
    }

    /// <summary>
    /// Snaps a scale so an edge of the bounds lands on the grid: of the grid lines either side of where each edge
    /// would go, the one giving the scale nearest the unsnapped one wins.
    /// </summary>
    /// <param name="snapping">The snapping, which gives the size of the grid.</param>
    /// <param name="factor">The unsnapped scale.</param>
    /// <param name="direction">The direction scaled along, in the frame of the gizmo.</param>
    /// <param name="origin">The pivot, in the frame of the gizmo.</param>
    /// <param name="bounds">The bounds of the selection at the start of the drag.</param>
    /// <returns>The snapped scale, or the unsnapped one when no edge can be snapped.</returns>
    public static float SnapScaleToGrid(IGizmoSnapping snapping, float factor, Vector3 direction, Vector3 origin, AABB bounds)
    {
        ArgumentNullException.ThrowIfNull(snapping);

        var best = 1f;
        var snapped = false;
        var grid = snapping.GridSize;

        for (var i = 0; i < 3; i++)
        {
            if (direction[i] < 0.001f)
            {
                continue;
            }

            foreach (var edge in (ReadOnlySpan<float>)[bounds.Min[i], bounds.Max[i]])
            {
                var distance = edge - origin[i];

                if (MathF.Abs(distance) < 0.001f)
                {
                    continue;
                }

                // The grid line the scaled edge is past, and the one after it
                var scaled = distance * factor + origin[i];
                var line = (int)(scaled / grid) * grid;
                var nextLine = scaled <= 0f ? line - grid : line + grid;

                foreach (var candidate in (ReadOnlySpan<float>)[MathF.Max(0f, (line - origin[i]) / distance), MathF.Max(0f, (nextLine - origin[i]) / distance)])
                {
                    if (MathF.Abs(candidate - factor) < MathF.Abs(best - factor))
                    {
                        best = candidate;
                    }
                }

                snapped = true;
            }
        }

        return snapped ? best : factor;
    }
}
