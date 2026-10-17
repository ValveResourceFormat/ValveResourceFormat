using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.ToolRender;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The view of a camera or projector, a frustum along +X of the entity whose field of view, near and far
/// planes, color and aspect ratio are keys of it. Its faces are tinted and outlined in white while the entity
/// is selected.
/// </summary>
public sealed class FrustumHelper : EntityHelper
{
    /// <inheritdoc/>
    public override bool IsVisual => false;

    /// <inheritdoc/>
    public override bool NeedsShowHelpers => true;

    /// <inheritdoc/>
    public override bool RequiresSelection => true;

    private const float DefaultFov = 90f;
    private const float DefaultNearPlane = 10f;
    private const float DefaultFarPlane = 10000f;
    private const float DefaultAspectRatio = 1f;

    private const float FaceAlphaScale = 0.5f;
    private const float EdgeWidth = 2f;

    private static readonly Color32 DefaultColor = new(128, 128, 128, 128);

    /// <summary>Gets the key of the entity that holds the field of view, in degrees.</summary>
    public string FovKey { get; } = "_fov";

    /// <summary>Gets the key of the entity that holds the distance to the near plane.</summary>
    public string NearPlaneKey { get; } = "_nearplane";

    /// <summary>Gets the key of the entity that holds the distance to the far plane.</summary>
    public string FarPlaneKey { get; } = "_farplane";

    /// <summary>Gets the key of the entity that holds the color of the faces.</summary>
    public string ColorKey { get; } = "_color";

    /// <summary>Gets the key of the entity that holds the width of the view over its height.</summary>
    public string AspectRatioKey { get; } = "aspectratio";

    /// <summary>Gets whether the field of view is the vertical one, rather than the horizontal one.</summary>
    public bool IsVerticalFov { get; }

    /// <summary>Initializes the frustum a class declares.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    public FrustumHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
    {
        // The fifth argument is not read
        if (GetArgument(0) is { } fovKey)
        {
            FovKey = fovKey.ToLowerInvariant();
            IsVerticalFov = fovKey.Equals("VerticalFOV", StringComparison.OrdinalIgnoreCase);
        }

        NearPlaneKey = GetArgument(1)?.ToLowerInvariant() ?? NearPlaneKey;
        FarPlaneKey = GetArgument(2)?.ToLowerInvariant() ?? FarPlaneKey;
        ColorKey = GetArgument(3)?.ToLowerInvariant() ?? ColorKey;
        AspectRatioKey = GetArgument(5)?.ToLowerInvariant() ?? AspectRatioKey;
    }

    internal override void Render(ToolRenderContext context, in HelperRenderState state)
    {
        var (fov, nearPlane, farPlane, aspectRatio, color) = GetView();

        // Half the field of view is kept to where its tangent is of use
        var tanHalfFov = MathF.Tan(float.DegreesToRadians(Math.Clamp(fov * 0.5f, 0.01f, 89f)));

        Span<Vector3> near = stackalloc Vector3[4];
        Span<Vector3> far = stackalloc Vector3[4];
        GetPlaneCorners(nearPlane, tanHalfFov, aspectRatio, near);
        GetPlaneCorners(farPlane, tanHalfFov, aspectRatio, far);

        var faceColor = color with { A = (byte)(int)(color.A * FaceAlphaScale + 0.5f) };

        context.PushTransform(Owner.RigidTransform);
        context.LayerOverride = ToolRenderLayer.PostTranslucent;
        context.PushRenderMode(ToolRenderMode.TranslucentFlat);

        DrawFace(context, near[0], near[1], near[2], near[3], faceColor);
        DrawFace(context, far[3], far[2], far[1], far[0], faceColor);

        for (var i = 0; i < 4; i++)
        {
            var next = (i + 1) % 4;
            DrawFace(context, near[i], far[i], far[next], near[next], faceColor);
        }

        context.DrawColor = Color32.White;
        context.DrawLine(Vector3.Zero, new Vector3(farPlane, 0f, 0f));

        context.PopRenderMode();
        context.LayerOverride = null;
        context.PopTransform();
    }

    private (float Fov, float NearPlane, float FarPlane, float AspectRatio, Color32 Color) GetView()
    {
        var aspectRatio = GetFloatKey(AspectRatioKey, DefaultAspectRatio);
        var fov = GetFloatKey(FovKey, DefaultFov);

        if (IsVerticalFov)
        {
            fov = float.RadiansToDegrees(MathF.Atan(MathF.Tan(float.DegreesToRadians(fov * 0.5f)) * aspectRatio)) * 2f;
        }

        var color = GetStringKey(ColorKey) is { Length: > 0 } colorText ? CameraFrustum.ParseColor(colorText) : DefaultColor;

        return (fov, GetFloatKey(NearPlaneKey, DefaultNearPlane), GetFloatKey(FarPlaneKey, DefaultFarPlane), aspectRatio, color);
    }

    // A face is tinted and outlined in white
    private static void DrawFace(ToolRenderContext context, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 color)
    {
        context.DrawQuad(a, b, c, d, color);

        context.PushRenderMode(ToolRenderMode.Wireframe);
        context.DrawColor = Color32.White;
        context.LineWidth = EdgeWidth;

        context.DrawLine(a, b);
        context.DrawLine(b, c);
        context.DrawLine(c, d);
        context.DrawLine(d, a);

        context.LineWidth = 0f;
        context.PopRenderMode();
    }

    // The corners of the frustum at a distance along +X, around from its -Y -Z corner
    private static void GetPlaneCorners(float distance, float tanHalfFov, float aspectRatio, Span<Vector3> corners)
    {
        var halfWidth = tanHalfFov * distance;
        var halfHeight = halfWidth / aspectRatio;

        corners[0] = new Vector3(distance, -halfWidth, -halfHeight);
        corners[1] = new Vector3(distance, -halfWidth, halfHeight);
        corners[2] = new Vector3(distance, halfWidth, halfHeight);
        corners[3] = new Vector3(distance, halfWidth, -halfHeight);
    }
}
