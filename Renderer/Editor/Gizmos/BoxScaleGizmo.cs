using ValveResourceFormat.Renderer.Editor.ToolRender;

namespace ValveResourceFormat.Renderer.Editor.Gizmos;

/// <summary>
/// Scales what is selected by the faces of its bounds, the way Hammer's select mode does: a handle on each face
/// drags that face while the opposite one stays where it is, or about the center of the bounds instead. The bounds
/// never get smaller than a unit, or a step of the grid while snapping, so they cannot turn inside out.
/// </summary>
/// <remarks>
/// Hammer has handles on the edges and corners of the bounds too, but never shows them, so only the faces are here.
/// </remarks>
public sealed class BoxScaleGizmo : Gizmo
{
    private const float FaceHandleSize = 6f;
    private const float HighlightedHandleScale = 1.5f;

    // How big the arrow inside each face handle is, in pixels
    private const float FaceArrowPixels = 3f;

    private static readonly GizmoPart[] Faces =
    [
        GizmoPart.AxisX, GizmoPart.AxisY, GizmoPart.AxisZ,
        GizmoPart.FaceNegativeX, GizmoPart.FaceNegativeY, GizmoPart.FaceNegativeZ,
    ];

    private Vector3 dragStart;
    private Vector3 handleStart;
    private AABB dragBounds;

    /// <summary>Initializes a box scale gizmo.</summary>
    /// <param name="snapping">How its drags snap, or <see langword="null"/> for not at all.</param>
    public BoxScaleGizmo(IGizmoSnapping? snapping) : base(snapping)
    {
    }

    /// <summary>Gets or sets whether a face scales about the center of the bounds rather than the opposite face.</summary>
    public bool ScalesAboutCenter
    {
        get;
        set
        {
            field = value;
            UpdatePivot();
        }
    }

    /// <summary>
    /// Gets or sets whether a drag scales every axis it drags by the same amount, and does not snap. A face only
    /// drags its own axis, so for the handles shown this only stops the snapping.
    /// </summary>
    public bool ScalesUniformly { get; set; }

    /// <inheritdoc/>
    protected override GizmoResultFlags ResultFlags
        => GizmoResultFlags.Scale | (IsLocalSpace && Kind == GizmoSelectionKind.Multiple ? GizmoResultFlags.Local : GizmoResultFlags.None);

    private bool IsSnappingEnabled => Snapping?.IsSnappingEnabled == true && !ScalesUniformly;

    private static int GetAxisOf(GizmoPart face) => (int)face % 3;

    private static GizmoPart GetOpposite(GizmoPart face) => face switch
    {
        GizmoPart.AxisX => GizmoPart.FaceNegativeX,
        GizmoPart.AxisY => GizmoPart.FaceNegativeY,
        GizmoPart.AxisZ => GizmoPart.FaceNegativeZ,
        GizmoPart.FaceNegativeX => GizmoPart.AxisX,
        GizmoPart.FaceNegativeY => GizmoPart.AxisY,
        _ => GizmoPart.AxisZ,
    };

    private AABB Bounds => IsLocalSpace ? LocalBounds : WorldBounds;

    // The middle of a face of the bounds, in the world
    private Vector3 GetFacePosition(GizmoPart face)
    {
        var bounds = Bounds;
        var position = bounds.Center;
        var axis = GetAxisOf(face);

        position[axis] = face >= GizmoPart.FaceNegativeX ? bounds.Min[axis] : bounds.Max[axis];

        return Vector3.Transform(position, Frame);
    }

    // A face scales about the face opposite it, or about the center of the bounds
    private void UpdatePivot()
    {
        var face = ActivePart != GizmoPart.None ? ActivePart : HoveredPart;

        if (face is < GizmoPart.AxisX or > GizmoPart.FaceNegativeZ)
        {
            return;
        }

        var opposite = GetFacePosition(GetOpposite(face));
        Pivot = ScalesAboutCenter ? (GetFacePosition(face) + opposite) * 0.5f : opposite;
    }

    /// <inheritdoc/>
    protected override void OnHoveredPartChanged()
    {
        UpdatePivot();
    }

    /// <inheritdoc/>
    protected override void OnSetup()
    {
        UpdatePivot();
    }

    /// <inheritdoc/>
    protected override void Draw(ToolRenderContext context, Camera camera, ToolPickTargets picks)
    {
        var highlighted = HoveredPart >= GizmoPart.AxisX ? HoveredPart : GizmoPart.None;
        var bounds = Bounds;

        bool IsVisible(GizmoPart face)
        {
            if (ActivePart != GizmoPart.None)
            {
                return face == ActivePart;
            }

            // An axis of a flat box, or one the view looks along at both its faces, has no handles
            var axis = GetAxisOf(face);
            var direction = GetAxis(axis);

            return bounds.Max[axis] - bounds.Min[axis] > 1.1920929e-7f
                && !(IsAlongView(camera, GetFacePosition(GizmoPart.AxisX + axis), direction) && IsAlongView(camera, GetFacePosition(GizmoPart.FaceNegativeX + axis), direction));
        }

        foreach (var face in Faces)
        {
            if (!IsVisible(face))
            {
                continue;
            }

            var axis = GetAxisOf(face);
            var position = GetFacePosition(face);
            var opposite = GetFacePosition(GetOpposite(face));
            var color = GetPartColor(face, GetAxisColor(axis), highlighted);
            var size = face == highlighted ? FaceHandleSize * HighlightedHandleScale : FaceHandleSize;

            context.LayerOverride = ToolRenderLayer.Overlay1;

            context.BeginPickObject(picks, (int)face);
            context.PushRenderMode(ToolRenderMode.Flat);
            context.DrawHandle(size, ToolHandleType.Circle, color, position, noZ: false, border: false, translucent: false);
            context.EndPickObject();

            // An arrow inside the box, just behind the handle, pointing out of the face
            var arrowRadius = 2f * FaceArrowPixels * GetWorldUnitsPerPixel(camera, position, FaceArrowPixels);
            var outwards = Vector3.Normalize(position - opposite);
            var darkened = new Color32((byte)(color.R * 0.7f), (byte)(color.G * 0.7f), (byte)(color.B * 0.7f), color.A);

            context.DrawSolidCone(position - outwards * (arrowRadius * 3f), position - outwards * arrowRadius, arrowRadius, 8, darkened, darkened);
            context.PopRenderMode();

            // Each axis is drawn across the box between its faces, unless one of them is highlighted
            if (face <= GizmoPart.AxisZ && face != highlighted && GetOpposite(face) != highlighted)
            {
                context.PushRenderMode(ToolRenderMode.DottedNoZ);
                context.DrawLine(opposite, position, color);
                context.PopRenderMode();
            }

            context.LayerOverride = null;
        }

        if (highlighted != GizmoPart.None)
        {
            context.LayerOverride = ToolRenderLayer.Overlay1;
            context.PushRenderMode(ToolRenderMode.Flat);
            context.DrawLine(Pivot, GetFacePosition(highlighted), HighlightColor);
            context.PopRenderMode();
            context.LayerOverride = null;
        }
    }

    /// <inheritdoc/>
    protected override void OnBeginDrag(Camera camera, Vector2 mouse)
    {
        UpdatePivot();
        dragBounds = Bounds;

        // The drag starts on the handle itself, so the face does not jump to the cursor
        handleStart = GetFacePosition(ActivePart);
        var start = camera.WorldToScreenPoint(handleStart) ?? mouse;
        var axis = GetAxisOf(ActivePart);

        SetDragPlane(GetPlaneNormalAlongView(axis, camera.ScreenPointToDirection(start)), Pivot);
        dragStart = IntersectMouseWithDragPlane(camera, start);
    }

    /// <inheritdoc/>
    protected override void OnUpdateDrag(Camera camera, Vector2 mouse)
    {
        var point = IntersectMouseWithDragPlane(camera, mouse);

        if (IsSnappingEnabled)
        {
            Snapping!.SnapPosition(ref point, GizmoSnapConstraint.None, Vector3.Zero);
        }

        var axisDirection = Vector3.Normalize(GetAxis(GetAxisOf(ActivePart)));
        point = dragStart + axisDirection * Vector3.Dot(point - dragStart, axisDirection);

        ComputeScale(point);
    }

    // The face goes where the cursor is, measured from the pivot in the frame of the gizmo
    private void ComputeScale(Vector3 point)
    {
        if (!Matrix4x4.Invert(Frame, out var toFrame))
        {
            return;
        }

        var origin = Vector3.Transform(Pivot, toFrame);
        var handle = Vector3.Transform(handleStart, toFrame);
        var cursor = Vector3.Transform(point, toFrame);
        var fromPivot = handle - origin;
        var moved = cursor - handle;
        var axis = GetAxisOf(ActivePart);
        var scale = Vector3.One;

        if (MathF.Abs(fromPivot[axis]) > 0.001f && MathF.Abs(moved[axis]) > 0.001f)
        {
            var ratio = moved[axis] / fromPivot[axis] + 1f;
            var extent = dragBounds.Max[axis] - dragBounds.Min[axis];

            // The box keeps at least a unit, or a step of the grid while snapping in the world
            if (extent > 0f)
            {
                ratio = IsLocalSpace || !IsSnappingEnabled
                    ? MathF.Max(1f, extent * ratio) / extent
                    : Snapping!.ClampMinimumSize(extent * ratio) / extent;
            }

            scale[axis] = ratio;
        }

        Result.ScaleStart = fromPivot;
        Result.ScaleCurrent = fromPivot * scale;
        Result.Scale = scale;
    }
}
