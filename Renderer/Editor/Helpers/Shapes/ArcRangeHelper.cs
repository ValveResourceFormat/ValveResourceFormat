using ValveResourceFormat.Renderer.Editor.ToolRender;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The range of angles an entity turns or sees through around an axis, as a slice of a disc in the space of
/// the entity: from a minimum to a maximum angle, or a field of view centered on zero. While the entity is
/// selected the slice is drawn full size over everything, the part past zero apart from the part before it;
/// while it is not, it is a faint slice half the size.
/// </summary>
internal sealed class ArcRangeHelper : EntityHelper
{
    /// <inheritdoc/>
    public override bool IsToolShape => true;

    /// <inheritdoc/>
    public override bool NeedsShowHelpers => true;

    /// <inheritdoc/>
    public override bool HiddenInGameObjectsOnly => true;

    private const float DefaultRadius = 20f;
    private const int Segments = 64;

    private static readonly Color32 PositiveColor = new(255, 255, 128, 32);
    private static readonly Color32 NegativeColor = new(255, 200, 128, 32);
    private static readonly Color32 UnselectedColor = new(255, 255, 255, 16);
    private static readonly Color32 TriangleColor = new(64, 200, 200, 64);

    /// <summary>Gets the key of the entity that holds whether the range is drawn.</summary>
    private string IsEnabledKey { get; }

    /// <summary>Gets the key of the entity that holds the axis.</summary>
    private string AxisKey { get; }

    /// <summary>Gets the key of the entity that holds the smallest angle.</summary>
    private string MinAngleKey { get; }

    /// <summary>Gets the key of the entity that holds the largest angle.</summary>
    private string MaxAngleKey { get; }

    /// <summary>Gets the key of the entity that holds the field of view, which takes the place of both angles when set.</summary>
    private string FovKey { get; }

    /// <summary>
    /// Gets whether the axis key holds two points in the space of the entity, rather than a point in world
    /// space the axis points to from the entity.
    /// </summary>
    private bool IsLocalAxis { get; }

    /// <summary>Gets whether zero degrees is the forward axis of the entity, as near as it can be across the axis.</summary>
    private bool AlignPieToLocalForward { get; }

    /// <summary>Gets whether zero degrees is the up axis of the entity, the slice turning about its forward axis.</summary>
    private bool AlignPieToLocalUp { get; }

    /// <summary>Gets the radius of the slice.</summary>
    private float Radius { get; }

    /// <summary>Gets whether a triangle points to zero degrees while the entity is selected.</summary>
    private bool DrawTriangle { get; }

    public ArcRangeHelper(IHelperOwner owner, HelperDeclaration declaration) : base(owner, declaration)
    {
        IsEnabledKey = (GetMetadata("is_enabled_key") ?? "is_enabled").ToLowerInvariant();
        AxisKey = (GetMetadata("axis_key") ?? "axis").ToLowerInvariant();
        MinAngleKey = (GetMetadata("min_angle_key") ?? "min_angle").ToLowerInvariant();
        MaxAngleKey = (GetMetadata("max_angle_key") ?? "max_angle").ToLowerInvariant();
        FovKey = (GetMetadata("fov_key") ?? "fov").ToLowerInvariant();
        IsLocalAxis = GetMetadataFlag("is_local_axis", true);
        AlignPieToLocalForward = GetMetadataFlag("align_pie_to_local_forward", false);
        AlignPieToLocalUp = GetMetadataFlag("align_pie_to_local_up", false);
        Radius = declaration.Metadata?.GetFloatProperty("radius", DefaultRadius) ?? DefaultRadius;
        DrawTriangle = GetMetadataFlag("draw_triangle", true);
    }

    /// <inheritdoc/>
    public override void AddToScene() => AddShapes();

    internal override void Render(ToolRenderContext context, in HelperRenderState state)
    {
        var fov = GetFloatKey(FovKey);
        var (minAngle, maxAngle) = GetAngles(fov);

        // Drawn until the key says otherwise, as it is for a class without the key
        if (!GetBooleanKey(IsEnabledKey, true) || fov == 360f || minAngle > maxAngle)
        {
            return;
        }

        var (center, direction) = GetAxis();
        var (axisX, axisY, triangleAxis) = GetPieAxes(direction);

        context.PushTransform(Owner.RigidTransform);

        if (!state.IsSelected)
        {
            context.PushRenderMode(ToolRenderMode.TranslucentFlat);
            context.DrawArcEx(center, axisX, axisY, Radius * 0.5f, Segments, minAngle, maxAngle, UnselectedColor, wireframe: false, ToolFaceSides.Both);
            context.PopRenderMode();
            context.PopTransform();
            return;
        }

        // The angles past zero are told apart from the ones before it, and zero itself is marked
        var (positiveStart, positiveEnd) = (MathF.Max(0f, minAngle), MathF.Max(0f, maxAngle));
        var (negativeStart, negativeEnd) = (MathF.Min(0f, minAngle), MathF.Min(0f, maxAngle));

        context.PushRenderMode(ToolRenderMode.WireframeNoZ);
        context.LineWidth = 2f;
        context.DrawArcEx(center, axisX, axisY, Radius, Segments, positiveStart, positiveEnd, PositiveColor, wireframe: true, ToolFaceSides.Both);
        context.DrawLine(center, center + axisX * 1.2f * Radius, NegativeColor);
        context.DrawArcEx(center, axisX, axisY, Radius, Segments, negativeStart, negativeEnd, NegativeColor, wireframe: true, ToolFaceSides.Both);
        context.LineWidth = 0f;
        context.PopRenderMode();

        context.PushRenderMode(ToolRenderMode.TranslucentFlatNoZ);
        context.DrawArcEx(center, axisX, axisY, Radius, Segments, positiveStart, positiveEnd, PositiveColor, wireframe: false, ToolFaceSides.Both);
        context.DrawArcEx(center, axisX, axisY, Radius, Segments, negativeStart, negativeEnd, NegativeColor, wireframe: false, ToolFaceSides.Both);

        if (DrawTriangle)
        {
            var a = center + triangleAxis * 0.5f * Radius;
            var b = center + axisX * Radius;
            var c = center - triangleAxis * 0.5f * Radius;

            context.DrawTriangle(a, b, c, TriangleColor);
            context.DrawTriangle(c, b, a, TriangleColor);
        }

        context.PopRenderMode();
        context.PopTransform();
    }

    // A field of view takes the place of both angles, centered on zero, or on the back for a negative one
    private (float Min, float Max) GetAngles(float fov)
    {
        if (fov == 0f)
        {
            return (GetFloatKey(MinAngleKey), GetFloatKey(MaxAngleKey));
        }

        var half = fov * 0.5f;

        return fov >= 0f ? (-half, half) : (half + 180f, 180f - half);
    }

    // The center of the slice and the axis it turns about, in the space of the entity. The numbers the key
    // holds replace the default axis one by one, as far as they can be read.
    private (Vector3 Center, Vector3 Direction) GetAxis()
    {
        var start = Vector3.Zero;
        var end = Vector3.UnitZ;
        var value = GetStringKey(AxisKey);

        if (IsLocalAxis)
        {
            KeyValueScanner.ReadAxis(value, ref start, ref end);

            return (start, MathUtils.SafeNormalize(end - start));
        }

        // A point in the world the axis points to from the entity
        Span<float> point = stackalloc float[3];
        KeyValueScanner.Scan(value, point);
        start = new Vector3(point);

        Matrix4x4.Invert(Owner.RigidTransform, out var worldToLocal);

        return (Vector3.Zero, MathUtils.SafeNormalize(Vector3.TransformNormal(start - Owner.RigidTransform.Translation, worldToLocal)));
    }

    /// <summary>
    /// The axes at zero and ninety degrees of the slice, and the one the base of the triangle lies along.
    /// </summary>
    private (Vector3 AxisX, Vector3 AxisY, Vector3 TriangleAxis) GetPieAxes(Vector3 direction)
    {
        if (AlignPieToLocalForward)
        {
            var reference = Vector3.Dot(direction, Vector3.UnitX) > 0.99f ? Vector3.UnitZ : Vector3.UnitX;
            var axisX = MathUtils.SafeNormalize(reference - direction * Vector3.Dot(direction, reference));

            return (axisX, Vector3.Cross(direction, axisX), direction);
        }

        if (AlignPieToLocalUp)
        {
            return (Vector3.UnitZ, Vector3.Cross(Vector3.UnitX, Vector3.UnitZ), Vector3.UnitX);
        }

        // Some axis across the direction, turned out of it
        var across = MathUtils.SafeNormalize(new Vector3((1f - direction.Z) * direction.Y * direction.Y + direction.Z, 0f, -direction.X));
        across = MathUtils.SafeNormalize(across - direction * Vector3.Dot(across, direction));

        return (Vector3.Cross(across, direction), across, direction);
    }
}
