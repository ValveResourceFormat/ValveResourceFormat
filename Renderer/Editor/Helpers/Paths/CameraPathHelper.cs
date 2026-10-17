using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.ToolRender;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The camera a path moves, where it starts: at the first node, turned as that node is and seeing as far as its
/// field of view, drawn as a frustum from the <c>ZNear</c>, <c>ZFar</c>, <c>rendercolor</c> and
/// <c>aspectRatio</c> keys of the path, with an arrow along the path that keeps its size on the screen. It is
/// drawn while the path or any of its nodes is selected, and only for a path the camera takes some time to run.
/// </summary>
internal sealed class CameraPathHelper(IHelperOwner owner, FgdHelper declaration) : EntityHelper(owner, declaration)
{
    /// <inheritdoc/>
    public override bool NeedsShowHelpers => true;

    /// <inheritdoc/>
    public override bool HiddenInGameObjectsOnly => true;

    /// <inheritdoc/>
    internal override bool IsSelectedWithPathNodes => true;

    private const string NearPlaneKey = "znear";
    private const string FarPlaneKey = "zfar";
    private const string ColorKey = "rendercolor";
    private const string AspectRatioKey = "aspectratio";
    private const string ConstantSpeedKey = "constant_speed";
    private const string DurationKey = "duration";

    private const float DefaultNearPlane = 4f;
    private const float DefaultFarPlane = 100f;
    private const float DefaultAspectRatio = 1f;

    // How long the arrow is on the screen, in pixels: the length of the axes of gizmos, which is a setting of the
    // editor; this is its default
    private const float ArrowLength = 80f;

    private const float ArrowHeadLength = 0.25f;
    private const float ArrowHeadRadius = 0.1f;
    private const int ArrowHeadSides = 8;

    private static readonly Color32 ArrowColor = new(255, 230, 0);

    internal override void Render(ToolRenderContext context, in HelperRenderState state)
    {
        if (!state.IsSelected)
        {
            return;
        }

        var nodes = PathSpline.ReadNodes(Owner, includeClosingNode: false);

        if (nodes.Length < 2 || !TakesTime())
        {
            return;
        }

        var position = nodes[0].Position;
        var firstNode = Owner.FindPathNode(0);

        // The camera is turned as the first node is
        var transform = firstNode?.RigidTransform ?? Matrix4x4.Identity;
        transform.Translation = position;

        var color = CameraFrustum.ReadColor(Owner.KeyValues, ColorKey)
            ?? (GetStringKey(ColorKey) is { Length: > 0 } colorText ? CameraFrustum.ParseColor(colorText) : Color32.White);

        context.PushTransform(transform);
        CameraFrustum.Draw(context, GetFov(firstNode), GetFloatKey(NearPlaneKey, DefaultNearPlane), GetFloatKey(FarPlaneKey, DefaultFarPlane),
            GetFloatKey(AspectRatioKey, DefaultAspectRatio), color);
        context.PopTransform();

        // The arrow points the way the path leaves the node
        var forward = MathUtils.SafeNormalize(nodes[0].OutTangent);

        if (state.Camera is not { } camera || forward == Vector3.Zero)
        {
            return;
        }

        var scale = context.Transform.MaxAxisScale();
        var length = GetPixelLength(camera, Vector3.Transform(position, context.Transform)) * ArrowLength / scale;
        var headBase = position + forward * length;

        context.PushRenderMode(ToolRenderMode.TranslucentFlat);
        context.DrawLine(position, headBase, ArrowColor);
        context.DrawSolidCone(headBase, headBase + forward * (length * ArrowHeadLength), length * ArrowHeadRadius, ArrowHeadSides, ArrowColor, ArrowColor);
        context.PopRenderMode();
    }

    // The camera is only placed on a path it takes some time to run: as long as its duration at a constant speed,
    // or as long as its length takes at the speeds of its nodes
    private bool TakesTime()
        => GetBooleanKey(ConstantSpeedKey, defaultValue: false)
            ? GetFloatKey(DurationKey) > 0f
            : PathSpline.Create(Owner) is { Length: > 0f };

    // A node with no field of view of its own has the one of the view, unless the path adds them to it
    private float GetFov(IHelperOwner? firstNode)
    {
        var fov = firstNode?.KeyValues.GetFloatProperty(CameraFrustum.FovKey) ?? 0f;

        if (GetIntegerKey(CameraFrustum.RelativeFovKey) != 0)
        {
            return CameraFrustum.ViewFov + fov;
        }

        return fov > 0f ? fov : CameraFrustum.ViewFov;
    }

    // How far one pixel of the view reaches at a point, across the screen at the distance of the point
    private static float GetPixelLength(Camera camera, Vector3 point)
    {
        var toPoint = point - camera.Location;
        var depth = Vector3.Dot(toPoint, camera.Forward);

        // A point behind the camera is measured where it would be in front of it
        if (depth < 0f)
        {
            toPoint -= 2f * depth * camera.Forward;
            depth = -depth;
        }

        if (depth <= 0f || camera.WindowSize.X <= 0f)
        {
            return 0f;
        }

        var tanHalfHeight = MathF.Tan(camera.GetFOV() * 0.5f);
        var pixelWidth = 2f * tanHalfHeight * camera.AspectRatio / camera.WindowSize.X;
        var across = Vector3.Dot(toPoint, camera.Right) / depth;
        var up = Vector3.Dot(toPoint, camera.Up) / depth;

        var direction = Vector3.Normalize(camera.Forward + camera.Right * across + camera.Up * up);
        var nextDirection = Vector3.Normalize(camera.Forward + camera.Right * (across + pixelWidth) + camera.Up * up);

        return Vector3.Distance(direction, nextDirection) * toPoint.Length();
    }
}
