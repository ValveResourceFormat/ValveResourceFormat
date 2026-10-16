using System.Globalization;
using ValveResourceFormat.Renderer.Editor.ToolRender;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The view of a camera that moves along a path, drawn as a frustum along +X: tinted faces outlined in white,
/// and a white line along the middle out to the far plane.
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

    private const float EdgeWidth = 2f;

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
    /// Reads a color key as three or four channels, opaque when there are three, and black where it is not a color.
    /// </summary>
    /// <param name="keyValues">The keyvalues of the entity.</param>
    /// <param name="key">Name of the key.</param>
    /// <returns>The color, or <see langword="null"/> when the entity has no such key.</returns>
    public static Color32? ReadColor(Entity keyValues, string key)
    {
        if (!keyValues.TryGetValue(key, out var value))
        {
            return null;
        }

        if (value.IsArray)
        {
            if (value.Count < 3)
            {
                return default(Color32);
            }

            byte GetChannel(int index) => index < value.Count ? value[index].ToByte(CultureInfo.InvariantCulture) : byte.MaxValue;

            return new Color32(GetChannel(0), GetChannel(1), GetChannel(2), GetChannel(3));
        }

        return keyValues.GetStringProperty(key) is { } text ? ParseColor(text) : default(Color32);
    }

    /// <summary>Parses a color as three or four channels, opaque when there are three, and black where it is not a color.</summary>
    /// <param name="text">The color as text.</param>
    /// <returns>The color.</returns>
    public static Color32 ParseColor(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Span<byte> channels = [0, 0, 0, byte.MaxValue];

        if (parts.Length < 3)
        {
            return default;
        }

        for (var i = 0; i < Math.Min(parts.Length, channels.Length); i++)
        {
            if (!byte.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out channels[i]))
            {
                return default;
            }
        }

        return new Color32(channels[0], channels[1], channels[2], channels[3]);
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
