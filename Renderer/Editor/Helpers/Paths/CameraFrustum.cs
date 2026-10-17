using ValveResourceFormat.Renderer.Editor.ToolRender;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The view of a camera, or of one that moves along a path, drawn as a frustum along +X: tinted faces outlined
/// in white, and a white line along the middle out to the far plane.
/// </summary>
internal static class CameraFrustum
{
    /// <summary>
    /// The field of view of the views of the editor, which a camera path takes where its nodes give none. It is
    /// a setting of the editor; this is its default.
    /// </summary>
    public const float ViewFov = 90f;

    /// <summary>Key of a camera path that makes the field of view of each node add to the one of the view.</summary>
    public const string RelativeFovKey = "relative_fov";

    /// <summary>Key of a camera path node that holds its field of view.</summary>
    public const string FovKey = "fov";

    /// <summary>The field of view of a camera entity that does not set one, in degrees.</summary>
    public const float DefaultFov = 90f;

    /// <summary>The distance to the near plane of a camera entity that does not set one.</summary>
    public const float DefaultNearPlane = 10f;

    /// <summary>The distance to the far plane of a camera entity that does not set one.</summary>
    public const float DefaultFarPlane = 10000f;

    /// <summary>The width over the height of the view of a camera entity that does not set it.</summary>
    public const float DefaultAspectRatio = 1f;

    /// <summary>The color of a camera entity that does not set one.</summary>
    public static readonly Color32 DefaultColor = new(128, 128, 128, 128);

    private const float EdgeWidth = 2f;

    // The faces of the view of a camera entity are half as opaque as its color
    private const float FaceAlphaScale = 0.5f;

    /// <summary>Draws the frustum, in the space of the camera.</summary>
    /// <param name="context">What the frustum is drawn with.</param>
    /// <param name="fov">The horizontal field of view, in degrees.</param>
    /// <param name="nearPlane">The distance to the near plane.</param>
    /// <param name="farPlane">The distance to the far plane.</param>
    /// <param name="aspectRatio">The width of the view over its height.</param>
    /// <param name="faceColor">The tint of the faces.</param>
    public static void Draw(ToolRenderContext context, float fov, float nearPlane, float farPlane, float aspectRatio, Color32 faceColor)
    {
        var tanHalfFov = MathF.Tan(float.DegreesToRadians(Math.Clamp(fov * 0.5f, 0.01f, 89f)));

        Span<Vector3> near = stackalloc Vector3[4];
        Span<Vector3> far = stackalloc Vector3[4];
        GetPlaneCorners(nearPlane, tanHalfFov, aspectRatio, near);
        GetPlaneCorners(farPlane, tanHalfFov, aspectRatio, far);

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
    }

    /// <summary>
    /// Draws the frustum of a camera entity at a transform, after what is translucent and with its faces half as
    /// opaque as its color.
    /// </summary>
    /// <param name="context">What the frustum is drawn with.</param>
    /// <param name="transform">The space of the camera.</param>
    /// <param name="fov">The horizontal field of view, in degrees.</param>
    /// <param name="nearPlane">The distance to the near plane.</param>
    /// <param name="farPlane">The distance to the far plane.</param>
    /// <param name="aspectRatio">The width of the view over its height.</param>
    /// <param name="color">The color of the camera.</param>
    public static void DrawEntityView(ToolRenderContext context, Matrix4x4 transform, float fov, float nearPlane, float farPlane, float aspectRatio, Color32 color)
    {
        context.PushTransform(transform);
        context.LayerOverride = ToolRenderLayer.PostTranslucent;

        Draw(context, fov, nearPlane, farPlane, aspectRatio, color with { A = (byte)(int)(color.A * FaceAlphaScale + 0.5f) });

        context.LayerOverride = null;
        context.PopTransform();
    }

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
