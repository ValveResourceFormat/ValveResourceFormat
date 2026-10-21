using System.Globalization;
using ValveResourceFormat.Renderer.Editor.ToolRender;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Gizmos;

/// <summary>
/// Turns what is selected, the way Hammer's rotate gizmo does: around an axis by the ring around it, or freely by
/// the sphere inside the rings. Only the half of a ring facing the camera is drawn and picked, unless the ring
/// faces the camera. A ring turns by where the cursor is around it, in steps of the rotation snap while snapping.
/// </summary>
public sealed class RotateGizmo : Gizmo
{
    private const int RingSegments = 64;
    private const int RingPickSegments = 32;

    // How many times wider than it is drawn a ring is picked
    private const float RingPickInflate = 6f;

    // A ring this close to facing the camera is drawn whole
    private const float FacingRingAlignment = 0.95f;

    // A ring this close to edge on is turned by moving the cursor across it, at half a degree a pixel
    private const float EdgeOnRingAlignment = 0.1f;
    private const float EdgeOnDegreesPerPixel = 0.5f;

    private static readonly Color32 TrackballColor = new(255, 255, 0, 120);
    private static readonly Color32 ViewRingColor = new(100, 100, 100, 255);
    private static readonly Color32 SweepColor = new(120, 120, 120, 128);
    private static readonly Color32 CursorLineColor = new(180, 180, 180, 255);
    private static readonly Color32 CenterCrossColor = new(255, 255, 0, 255);
    private static readonly Color32 TrackballPointColor = new(255, 255, 255, 255);

    private Vector2 mouseStart;
    private Vector2 edgeOnTangent;
    private bool isEdgeOn;
    private float startAngle;
    private float unsnappedAngle;
    private Vector3 axis;
    private Vector3 ringPoint;
    private Vector3 trackballStart;
    private Vector3 trackballCurrent;

    /// <summary>Initializes a rotate gizmo.</summary>
    /// <param name="snapping">How its drags snap, or <see langword="null"/> for not at all.</param>
    public RotateGizmo(IGizmoSnapping? snapping) : base(snapping)
    {
    }

    /// <inheritdoc/>
    protected override GizmoResultFlags ResultFlags
        => GizmoResultFlags.Rotate | (IsLocalSpace ? GizmoResultFlags.Local : GizmoResultFlags.None);

    /// <inheritdoc/>
    protected override void Draw(ToolRenderContext context, Camera camera, ToolPickTargets picks)
    {
        var center = Pivot;
        var radius = GetScreenSize(camera, center, AxisLengthPixels);
        var width = radius * LineThicknessPixels / AxisLengthPixels;
        var highlighted = HoveredPart;

        context.LayerOverride = ToolRenderLayer.Overlay1;
        context.PushRenderMode(ToolRenderMode.Flat);

        context.DrawSolidRing(center, MathUtils.SafeNormalize(camera.Location - center), radius, width, RingSegments, ViewRingColor);

        // The sphere inside the rings turns freely, and shows only while it is under the cursor
        context.BeginPickObject(picks, (int)GizmoPart.Trackball);
        context.PushRenderMode(highlighted == GizmoPart.Trackball ? ToolRenderMode.TranslucentFlat : ToolRenderMode.PickProxy);
        context.DrawSolidSphere(center, radius - 0.5f * width * RingPickInflate, 20, 20, TrackballColor);
        context.PopRenderMode();
        context.EndPickObject();

        // The rings turn with the drag
        var rotation = Result.Transform with { Translation = Vector3.Zero };
        var toCenter = MathUtils.SafeNormalize(center - camera.Location);

        for (var i = 0; i < 3; i++)
        {
            var part = GizmoPart.AxisX + i;
            var normal = Vector3.TransformNormal(GetAxis(i), rotation);
            var color = GetPartColor(part, GetAxisColor(i), highlighted);

            if (MathF.Abs(Vector3.Dot(normal, toCenter)) > FacingRingAlignment)
            {
                context.DrawSolidRing(center, normal, radius, width, RingSegments, color);

                context.BeginPickObject(picks, (int)part);
                context.PushRenderMode(ToolRenderMode.PickProxy);
                context.DrawSolidRing(center, normal, radius, width * RingPickInflate, RingPickSegments, color);
                context.PopRenderMode();
                context.EndPickObject();
                continue;
            }

            // The half facing the camera, around the direction to it in the plane of the ring
            var towardsCamera = MathUtils.SafeNormalize(camera.Location - Vector3.Dot(normal, camera.Location - center) * normal - center);
            var across = MathUtils.SafeNormalize(Vector3.Cross(normal, towardsCamera));

            context.DrawSolidRingArc(center, towardsCamera, across, radius, width, RingSegments, -90f, 90f, color);

            context.BeginPickObject(picks, (int)part);
            context.PushRenderMode(ToolRenderMode.PickProxy);
            context.DrawSolidRingArc(center, towardsCamera, across, radius, width * RingPickInflate, RingPickSegments, -90f, 90f, color);
            context.PopRenderMode();
            context.EndPickObject();
        }

        if (IsDragging)
        {
            DrawDragFeedback(context, camera, center, radius, width);
        }

        context.PopRenderMode();

        for (var i = 0; i < 3; i++)
        {
            var worldAxis = i switch
            {
                0 => Vector3.UnitX,
                1 => Vector3.UnitY,
                _ => Vector3.UnitZ,
            };

            context.DrawLine(center - worldAxis * (radius * 0.1f), center + worldAxis * (radius * 0.1f), CenterCrossColor);
        }

        context.LayerOverride = null;
    }

    private void DrawDragFeedback(ToolRenderContext context, Camera camera, Vector3 center, float radius, float width)
    {
        if (ActivePart == GizmoPart.Trackball)
        {
            context.PushRenderMode(ToolRenderMode.TranslucentFlat);
            context.DrawAngleArc(center, trackballStart, trackballCurrent, 10, SweepColor, ToolFaceSides.Both);
            context.PopRenderMode();

            context.DrawScreenAlignedRect(trackballStart, width * 2f, width * 2f, TrackballPointColor);
            context.DrawScreenAlignedRect(trackballCurrent, width * 2f, width * 2f, TrackballPointColor);
            return;
        }

        var ring = ActivePart - GizmoPart.AxisX;
        var angle = Result.Angle;

        context.PushRenderMode(ToolRenderMode.TranslucentFlat);
        context.DrawArcEx(center, GetAxis((ring + 1) % 3), GetAxis((ring + 2) % 3), radius, Math.Abs((int)(angle * RingSegments / 360f)),
            startAngle, startAngle + angle, SweepColor, wireframe: false, ToolFaceSides.Both);
        context.PopRenderMode();

        context.PushRenderMode(ToolRenderMode.Wireframe);
        context.DrawLine(center, ringPoint, CursorLineColor);
        context.PopRenderMode();

        context.DrawText(angle.ToString("0.00", CultureInfo.InvariantCulture), center + camera.Up * (radius * 1.2f), Color32.White);
    }

    /// <inheritdoc/>
    protected override void OnBeginDrag(Camera camera, Vector2 mouse)
    {
        var center = Pivot;

        if (ActivePart == GizmoPart.Trackball)
        {
            SetDragPlane(MathUtils.SafeNormalize(camera.Location - center), center);
            trackballStart = GetTrackballPoint(camera, mouse);
            trackballCurrent = trackballStart;
            return;
        }

        var ring = ActivePart - GizmoPart.AxisX;

        mouseStart = mouse;
        unsnappedAngle = 0f;
        axis = GetAxis(ring);

        if (MathF.Abs(Vector3.Dot(axis, MathUtils.SafeNormalize(center - camera.Location))) <= EdgeOnRingAlignment)
        {
            isEdgeOn = true;
            startAngle = 0f;

            var direction = camera.WorldToScreenPoint(center) is { } start && camera.WorldToScreenPoint(center + axis) is { } end
                ? end - start
                : Vector2.Zero;

            direction = direction == Vector2.Zero ? Vector2.Zero : Vector2.Normalize(direction);
            edgeOnTangent = new Vector2(-direction.Y, direction.X);
        }
        else
        {
            isEdgeOn = false;
            SetDragPlane(axis, center);
            ringPoint = IntersectMouseWithDragPlane(camera, mouse);
            startAngle = GetAngleOnRing(center, ringPoint, ring);
        }
    }

    /// <inheritdoc/>
    protected override void OnUpdateDrag(Camera camera, Vector2 mouse)
    {
        var center = Pivot;

        if (ActivePart == GizmoPart.Trackball)
        {
            trackballCurrent = GetTrackballPoint(camera, mouse);

            var from = MathUtils.SafeNormalize(trackballStart - center);
            var to = MathUtils.SafeNormalize(trackballCurrent - center);

            axis = MathUtils.SafeNormalize(Vector3.Cross(from, to));
            Result.Angle = float.RadiansToDegrees(MathF.Acos(Math.Clamp(Vector3.Dot(from, to), -1f, 1f)));
        }
        else
        {
            var ring = ActivePart - GizmoPart.AxisX;
            axis = GetAxis(ring);

            float angle;

            if (isEdgeOn)
            {
                angle = Vector2.Dot(mouse - mouseStart, edgeOnTangent) * EdgeOnDegreesPerPixel;
            }
            else
            {
                ringPoint = IntersectMouseWithDragPlane(camera, mouse);
                angle = GetAngleOnRing(center, ringPoint, ring);
            }

            unsnappedAngle = UnwrapAngleDelta(angle, unsnappedAngle, startAngle);
            Result.Angle = Snapping?.IsRotationSnappingEnabled == true ? Snapping.SnapAngle(unsnappedAngle) : unsnappedAngle;
        }

        // Turned about the pivot, or in local space about where the selection is
        var pivot = IsLocalSpace ? OriginalPivot : center;

        Result.Axis = ToFrame(axis);
        Result.Transform = Matrix4x4.CreateTranslation(-pivot)
            * Matrix4x4.CreateFromAxisAngle(axis, float.DegreesToRadians(Result.Angle))
            * Matrix4x4.CreateTranslation(pivot);
    }

    // Where the cursor is on the sphere the rings lie on, or towards it on the plane facing the camera when it misses
    private Vector3 GetTrackballPoint(Camera camera, Vector2 mouse)
    {
        var center = Pivot;
        var radius = GetScreenSize(camera, center, AxisLengthPixels);
        var direction = camera.ScreenPointToDirection(mouse);

        if (IntersectRaySphere(camera.Location, direction, center, radius) is { } hit)
        {
            return hit;
        }

        return center + MathUtils.SafeNormalize(IntersectMouseWithDragPlane(camera, mouse) - center) * radius;
    }

    // The nearest point a ray enters a sphere at, or where it starts when that is inside
    private static Vector3? IntersectRaySphere(Vector3 origin, Vector3 direction, Vector3 center, float radius)
    {
        var toCenter = center - origin;

        if (toCenter.LengthSquared() <= radius * radius)
        {
            return origin;
        }

        var along = Vector3.Dot(toCenter, direction);

        if (along < 0f)
        {
            return null;
        }

        var closestSquared = (origin + direction * along - center).LengthSquared();

        if (closestSquared > radius * radius)
        {
            return null;
        }

        return origin + direction * (along - MathF.Sqrt(radius * radius - closestSquared));
    }

    /// <summary>
    /// Gets the angle of a point around a ring, from the axis after the ring's towards the one after that, so it
    /// grows turning right handed about the ring's axis.
    /// </summary>
    private float GetAngleOnRing(Vector3 center, Vector3 point, int ring)
    {
        var offset = point - center;
        var degrees = float.RadiansToDegrees(MathF.Atan2(Vector3.Dot(offset, GetAxis((ring + 2) % 3)), Vector3.Dot(offset, GetAxis((ring + 1) % 3))));

        return degrees < 0f ? degrees + 360f : degrees;
    }

    /// <summary>
    /// Gets how far a ring has turned since the drag started, following the cursor past a whole turn by the turn it
    /// had before, so it runs on between minus and plus a whole turn.
    /// </summary>
    /// <param name="angle">The angle of the cursor around the ring, in degrees.</param>
    /// <param name="previous">How far the ring had turned before.</param>
    /// <param name="start">The angle of the cursor when the drag started.</param>
    /// <returns>How far the ring has turned, in degrees.</returns>
    public static float UnwrapAngleDelta(float angle, float previous, float start)
    {
        if (MathF.Abs(angle - start - previous) > 180f)
        {
            return (previous < 0f ? angle - 360f - start : angle + 360f - start) % 360f;
        }

        return (angle - start) % 360f;
    }
}
