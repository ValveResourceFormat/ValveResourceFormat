using System.Globalization;
using ValveResourceFormat.Renderer.Editor.Entities;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor;

/// <summary>
/// Builds the lines of wireframe boxes drawn by editor overlays, solid where the scene does not hide them and
/// faint over it.
/// </summary>
internal static class BoxLines
{
    // Opacity of the lines over the scene, out of 255
    private const byte HiddenLineAlpha = 32;

    private static readonly Color32 AxisX = new(255, 140, 140);
    private static readonly Color32 AxisY = new(140, 255, 140);
    private static readonly Color32 AxisZ = new(140, 140, 255);

    /// <summary>Adds the twelve edges of a transformed box.</summary>
    public static void Add(HelperVertices vertices, in Matrix4x4 transform, in AABB box, Color32 color)
        => Add(vertices, transform, box, color, null, null);

    /// <summary>
    /// Adds the twelve edges of a transformed box, colouring the three edges at the corner in view nearest
    /// the camera by axis and labelling them with their lengths, as Hammer shows the size of a selection.
    /// </summary>
    public static void AddWithSize(HelperVertices vertices, in Matrix4x4 transform, in AABB box, Color32 color, Camera camera, TextRenderer textRenderer)
        => Add(vertices, transform, box, color, camera, textRenderer);

    private static void Add(HelperVertices vertices, in Matrix4x4 transform, in AABB box, Color32 color, Camera? camera, TextRenderer? textRenderer)
    {
        Span<Vector3> c = stackalloc Vector3[8];
        GetCorners(box, c);

        foreach (ref var corner in c)
        {
            corner = Vector3.Transform(corner, transform);
        }

        ReadOnlySpan<(int Start, int End)> Lines =
        [
            (0, 1), (1, 3), (3, 2), (2, 0), // Bottom face
            (4, 5), (5, 7), (7, 6), (6, 4), // Top face
            (0, 4), (1, 5), (3, 7), (2, 6), // Vertical edges
        ];

        var closestIndex = camera != null ? ClosestVertexInView(camera, c) : -1;

        for (var i = 0; i < Lines.Length; i++)
        {
            var line = Lines[i];

            if (closestIndex == line.Start || closestIndex == line.End)
            {
                var axis = i >= 8 ? 2 : i % 2;

                var axisColor = axis switch
                {
                    0 => AxisX,
                    1 => AxisY,
                    _ => AxisZ,
                };

                var (v0, v1) = (c[line.Start], c[line.End]);
                var middle = Vector3.Lerp(v0, v1, 0.5f);

                if (FormatLength(Vector3.Distance(v0, v1)) is { } text && camera!.ViewFrustum.Intersects(middle))
                {
                    textRenderer!.AddTextBillboard(middle, new TextRenderer.TextRenderRequest
                    {
                        Scale = 13f,
                        Color = axisColor,
                        Text = text,
                        CenterVertical = true,
                        CenterHorizontal = true,
                    }, camera);
                }

                AddLine(vertices, c[line.Start], c[line.End], axisColor);
                continue;
            }

            AddLine(vertices, c[line.Start], c[line.End], color);
        }
    }

    /// <summary>Gets the corners of a box. Corner i takes the max along X, Y and Z where bits 0, 1 and 2 of i are set.</summary>
    public static void GetCorners(in AABB box, Span<Vector3> corners)
    {
        for (var i = 0; i < corners.Length; i++)
        {
            corners[i] = new Vector3(
                (i & 1) != 0 ? box.Max.X : box.Min.X,
                (i & 2) != 0 ? box.Max.Y : box.Min.Y,
                (i & 4) != 0 ? box.Max.Z : box.Min.Z);
        }
    }

    /// <summary>Adds a line, solid where seen and faint over the scene.</summary>
    public static void AddLine(HelperVertices vertices, Vector3 start, Vector3 end, Color32 color)
        => vertices.AddLine(start, end, color with { A = HiddenLineAlpha }, HelperPasses.Both);

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
    private static int ClosestVertexInView(Camera camera, ReadOnlySpan<Vector3> vertices)
    {
        var minDistance = float.MaxValue;
        var closestIndex = 0;

        for (var i = 0; i < vertices.Length; i++)
        {
            if (camera.ViewFrustum.Intersects(vertices[i]))
            {
                var distance = Vector3.DistanceSquared(vertices[i], camera.Location);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    closestIndex = i;
                }
            }
        }

        return closestIndex;
    }
}
