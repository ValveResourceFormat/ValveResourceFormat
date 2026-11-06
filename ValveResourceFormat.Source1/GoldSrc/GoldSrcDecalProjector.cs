namespace ValveResourceFormat.GoldSrc;

/// <summary>
/// The part of a face of a brush model that a decal covers.
/// </summary>
/// <param name="Face">The index of the face.</param>
/// <param name="Positions">The corners of the covered polygon, wound as the face is drawn.</param>
/// <param name="TextureCoordinates">The coordinates of the corners in the texel space of the face.</param>
/// <param name="DecalCoordinates">The coordinates of the corners in the decal texture, from 0 to 1.</param>
internal sealed record GoldSrcDecalPiece(int Face, Vector3[] Positions, Vector2[] TextureCoordinates, Vector2[] DecalCoordinates);

/// <summary>
/// Projects a decal onto the faces of a brush model.
/// </summary>
internal static class GoldSrcDecalProjector
{
    // Only the faces of a plane that passes this close to the center of the decal take it
    private const float PlaneTolerance = 4f;

    private readonly record struct ClipVertex(Vector3 Position, Vector2 TextureCoordinates);

    /// <summary>
    /// Finds the faces of a model that a decal covers. The decal is as large in world units as its texture is in texels,
    /// and its sides follow the texture axes of each face.
    /// </summary>
    /// <param name="bsp">The map.</param>
    /// <param name="modelIndex">The model to project onto.</param>
    /// <param name="center">The center of the decal in the space of the model.</param>
    /// <param name="width">The width of the decal texture.</param>
    /// <param name="height">The height of the decal texture.</param>
    /// <param name="canReceive">Whether a face takes decals at all.</param>
    /// <returns>The covered part of each face the decal lands on.</returns>
    public static List<GoldSrcDecalPiece> Project(GoldSrcBspFile bsp, int modelIndex, Vector3 center, int width, int height, Func<int, bool> canReceive)
    {
        var pieces = new List<GoldSrcDecalPiece>();
        var radius = Math.Max(width, height) >> 1;

        VisitNode(bsp, bsp.Models[modelIndex].HeadNode0, center, radius, width, height, canReceive, pieces);

        return pieces;
    }

    private static void VisitNode(GoldSrcBspFile bsp, int nodeIndex, Vector3 center, int radius, int width, int height, Func<int, bool> canReceive, List<GoldSrcDecalPiece> pieces)
    {
        while (nodeIndex >= 0 && nodeIndex < bsp.Nodes.Length)
        {
            var node = bsp.Nodes[nodeIndex];
            var plane = bsp.Planes[node.PlaneNum];
            var distance = Vector3.Dot(center, plane.Normal) - plane.Dist;

            if (distance > radius)
            {
                nodeIndex = node.Front;
                continue;
            }

            if (distance < -radius)
            {
                nodeIndex = node.Back;
                continue;
            }

            if (MathF.Abs(distance) < PlaneTolerance)
            {
                AddNodeFaces(bsp, node, center, width, height, canReceive, pieces);
            }

            VisitNode(bsp, node.Front, center, radius, width, height, canReceive, pieces);
            nodeIndex = node.Back;
        }
    }

    private static void AddNodeFaces(GoldSrcBspFile bsp, GoldSrcNode node, Vector3 center, int width, int height, Func<int, bool> canReceive, List<GoldSrcDecalPiece> pieces)
    {
        for (var i = 0; i < node.NumFaces; i++)
        {
            var faceIndex = node.FirstFace + i;

            if (faceIndex >= bsp.Faces.Length || !canReceive(faceIndex))
            {
                continue;
            }

            var piece = ClipFace(bsp, faceIndex, center, width, height);

            if (piece != null)
            {
                pieces.Add(piece);
            }
        }
    }

    private static GoldSrcDecalPiece? ClipFace(GoldSrcBspFile bsp, int faceIndex, Vector3 center, int width, int height)
    {
        var face = bsp.Faces[faceIndex];
        var info = bsp.TextureInfos[face.TexInfo];

        // The decal is sized by the length of the first texture axis for both of its sides
        var scale = info.S.Length();
        var decalSize = new Vector2((int)(width * scale), (int)(height * scale));

        if (decalSize.X < 1f || decalSize.Y < 1f)
        {
            return null;
        }

        var decalCenter = info.GetTextureCoordinates(center);
        var min = decalCenter - (decalSize * 0.5f);
        var max = decalCenter + (decalSize * 0.5f);

        var normal = bsp.Planes[face.PlaneNum].Normal;

        if (face.Side != 0)
        {
            normal = -normal;
        }

        var positions = bsp.GetFaceVertices(face);
        GoldSrcMapConversion.ReverseIfBackFacing(positions, normal);

        var polygon = new List<ClipVertex>(positions.Length);

        foreach (var position in positions)
        {
            polygon.Add(new ClipVertex(position, info.GetTextureCoordinates(position)));
        }

        polygon = Clip(polygon, 0, min.X, keepAbove: true);
        polygon = Clip(polygon, 0, max.X, keepAbove: false);
        polygon = Clip(polygon, 1, min.Y, keepAbove: true);
        polygon = Clip(polygon, 1, max.Y, keepAbove: false);

        if (polygon.Count < 3)
        {
            return null;
        }

        var piece = new GoldSrcDecalPiece(faceIndex, new Vector3[polygon.Count], new Vector2[polygon.Count], new Vector2[polygon.Count]);

        for (var i = 0; i < polygon.Count; i++)
        {
            piece.Positions[i] = polygon[i].Position;
            piece.TextureCoordinates[i] = polygon[i].TextureCoordinates;
            piece.DecalCoordinates[i] = (polygon[i].TextureCoordinates - min) / decalSize;
        }

        return piece;
    }

    // Keeps the part of a convex polygon on one side of an axis aligned line in texel space
    private static List<ClipVertex> Clip(List<ClipVertex> polygon, int axis, float bound, bool keepAbove)
    {
        var result = new List<ClipVertex>(polygon.Count + 1);

        for (var i = 0; i < polygon.Count; i++)
        {
            var current = polygon[i];
            var next = polygon[(i + 1) % polygon.Count];
            var currentOffset = GetOffset(current, axis, bound, keepAbove);
            var nextOffset = GetOffset(next, axis, bound, keepAbove);

            if (currentOffset >= 0f)
            {
                result.Add(current);
            }

            if ((currentOffset >= 0f) != (nextOffset >= 0f))
            {
                var fraction = currentOffset / (currentOffset - nextOffset);

                result.Add(new ClipVertex(
                    Vector3.Lerp(current.Position, next.Position, fraction),
                    Vector2.Lerp(current.TextureCoordinates, next.TextureCoordinates, fraction)));
            }
        }

        return result;
    }

    // The distance of a vertex inside the kept side of the line, negative outside of it
    private static float GetOffset(ClipVertex vertex, int axis, float bound, bool keepAbove)
    {
        var value = axis == 0 ? vertex.TextureCoordinates.X : vertex.TextureCoordinates.Y;

        return keepAbove ? value - bound : bound - value;
    }
}
