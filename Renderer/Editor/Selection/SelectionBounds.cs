using System.Globalization;
using ValveResourceFormat.Renderer.Editor.ToolRender;

namespace ValveResourceFormat.Renderer.Editor.Selection;

/// <summary>
/// Draws the bounds of what is selected the way Hammer does, over the scene: the edges of the box in the
/// selection color, and a line along each axis from the corner in view nearest the camera, in the color of
/// the axis and labelled with its length.
/// </summary>
internal static class SelectionBounds
{
    private const float DimensionLineWidth = 2f;

    private static readonly Color32 BoxColor = new(255, 255, 0);

    private static readonly Color32 AxisX = new(255, 140, 140);
    private static readonly Color32 AxisY = new(140, 255, 140);
    private static readonly Color32 AxisZ = new(140, 140, 255);

    /// <summary>Draws the bounds of a box placed in the world.</summary>
    /// <param name="context">What the bounds are drawn with.</param>
    /// <param name="transform">Where the box is.</param>
    /// <param name="box">The box, in the space it is placed in.</param>
    /// <param name="camera">The camera, to find the corner nearest it.</param>
    /// <param name="textRenderer">What the lengths are written with.</param>
    public static void Draw(ToolRenderContext context, in Matrix4x4 transform, in AABB box, Camera camera, TextRenderer textRenderer)
    {
        context.PushTransform(transform);
        context.PushRenderMode(ToolRenderMode.WireframeNoZ);

        context.DrawColor = BoxColor;
        context.DrawWireBox(box.Min, box.Max);

        // The dimensions are drawn over the edges they run along
        DrawDimensions(context, box, camera, textRenderer);

        context.PopRenderMode();
        context.PopTransform();
    }

    private static void DrawDimensions(ToolRenderContext context, in AABB box, Camera camera, TextRenderer textRenderer)
    {
        var toWorld = context.Transform;
        var corner = ClosestCornerInView(camera, toWorld, box);

        context.LineWidth = DimensionLineWidth;
        context.LayerOverride = ToolRenderLayer.Overlay1;

        for (var axis = 0; axis < 3; axis++)
        {
            // Along the axis to the other side of the box
            var end = corner;
            end[axis] = corner[axis] == box.Max[axis] ? box.Min[axis] : box.Max[axis];

            var color = axis switch
            {
                0 => AxisX,
                1 => AxisY,
                _ => AxisZ,
            };

            context.DrawLine(corner, end, color);

            var (start, finish) = (Vector3.Transform(corner, toWorld), Vector3.Transform(end, toWorld));
            var middle = Vector3.Lerp(start, finish, 0.5f);

            if (FormatLength(Vector3.Distance(start, finish)) is { } text && camera.ViewFrustum.Intersects(middle))
            {
                textRenderer.AddTextBillboard(middle, new TextRenderer.TextRenderRequest
                {
                    Scale = 13f,
                    Color = color,
                    Text = text,
                    CenterVertical = true,
                    CenterHorizontal = true,
                }, camera);
            }
        }

        context.LineWidth = 0f;
        context.LayerOverride = null;
    }

    // Whole when within a hundredth of it, otherwise to two decimals, and nothing for an edge with no length
    private static string? FormatLength(float length)
    {
        if (length < 0.01f)
        {
            return null;
        }

        var rounded = (int)(length + 0.5f);

        return MathF.Abs(length - rounded) < 0.01f
            ? rounded.ToString(CultureInfo.InvariantCulture)
            : length.ToString("F2", CultureInfo.InvariantCulture);
    }

    // The corner in view nearest the camera, or the box's min corner while none of them are in view
    private static Vector3 ClosestCornerInView(Camera camera, in Matrix4x4 transform, in AABB box)
    {
        var minDistance = float.MaxValue;
        var closest = box.Min;

        for (var i = 0; i < 8; i++)
        {
            var corner = new Vector3((i & 1) != 0 ? box.Max.X : box.Min.X, (i & 2) != 0 ? box.Max.Y : box.Min.Y, (i & 4) != 0 ? box.Max.Z : box.Min.Z);
            var world = Vector3.Transform(corner, transform);

            if (!camera.ViewFrustum.Intersects(world))
            {
                continue;
            }

            var distance = Vector3.DistanceSquared(world, camera.Location);

            if (distance < minDistance)
            {
                minDistance = distance;
                closest = corner;
            }
        }

        return closest;
    }
}
