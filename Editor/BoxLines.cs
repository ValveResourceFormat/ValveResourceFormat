using System.Globalization;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Editor;

/// <summary>
/// Builds the lines of wireframe boxes drawn by editor overlays, solid where the scene does not hide them and
/// faint over it.
/// </summary>
internal static class BoxLines
{
    // Opacity of the lines over the scene, out of 255
    private const byte HiddenLineAlpha = 32;

    private static readonly Color32 AxisX = new(1.0f, 0.2f, 0.2f, 1);
    private static readonly Color32 AxisY = new(0.2f, 0.8f, 0.2f, 1);
    private static readonly Color32 AxisZ = new(0.2f, 0.2f, 1.0f, 1);

    /// <summary>Adds the twelve edges of a transformed box.</summary>
    public static void Add(HelperVertices vertices, in Matrix4x4 transform, in AABB box, Color32 color)
        => Add(vertices, transform, box, color, null, null);

    /// <summary>
    /// Adds the twelve edges of a transformed box, colouring the three edges at the corner nearest the
    /// camera by axis and labelling them with their lengths.
    /// </summary>
    public static void AddWithSize(HelperVertices vertices, in Matrix4x4 transform, in AABB box, Color32 color, Camera camera, TextRenderer textRenderer)
        => Add(vertices, transform, box, color, camera, textRenderer);

    private static void Add(HelperVertices vertices, in Matrix4x4 transform, in AABB box, Color32 color, Camera? camera, TextRenderer? textRenderer)
    {
        ReadOnlySpan<Vector3> c =
        [
            Vector3.Transform(new Vector3(box.Min.X, box.Min.Y, box.Min.Z), transform),
            Vector3.Transform(new Vector3(box.Max.X, box.Min.Y, box.Min.Z), transform),
            Vector3.Transform(new Vector3(box.Max.X, box.Max.Y, box.Min.Z), transform),
            Vector3.Transform(new Vector3(box.Min.X, box.Max.Y, box.Min.Z), transform),
            Vector3.Transform(new Vector3(box.Min.X, box.Min.Y, box.Max.Z), transform),
            Vector3.Transform(new Vector3(box.Max.X, box.Min.Y, box.Max.Z), transform),
            Vector3.Transform(new Vector3(box.Max.X, box.Max.Y, box.Max.Z), transform),
            Vector3.Transform(new Vector3(box.Min.X, box.Max.Y, box.Max.Z), transform),
        ];

        ReadOnlySpan<(int Start, int End)> Lines =
        [
            (0, 1), (1, 2), (2, 3), (3, 0), // Bottom face
            (4, 5), (5, 6), (6, 7), (7, 4), // Top face
            (0, 4), (1, 5), (2, 6), (3, 7), // Vertical edges
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
                var length = Vector3.Distance(v0, v1);

                textRenderer!.AddTextBillboard(Vector3.Lerp(v0, v1, 0.5f), new TextRenderer.TextRenderRequest
                {
                    Scale = 13f,
                    Color = axisColor,
                    Text = length.ToString("0.##", CultureInfo.InvariantCulture),
                    CenterVertical = true,
                    CenterHorizontal = true,
                }, camera!);

                AddLine(vertices, c[line.Start], c[line.End], axisColor);
                continue;
            }

            AddLine(vertices, c[line.Start], c[line.End], color);
        }
    }

    /// <summary>Adds a line, solid where seen and faint over the scene.</summary>
    public static void AddLine(HelperVertices vertices, Vector3 start, Vector3 end, Color32 color)
        => vertices.AddLine(start, end, color with { A = HiddenLineAlpha }, HelperPasses.Both);

    private static int ClosestVertexInView(Camera camera, ReadOnlySpan<Vector3> vertices)
    {
        var minDistance = float.MaxValue;
        var closestIndex = -1;

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
