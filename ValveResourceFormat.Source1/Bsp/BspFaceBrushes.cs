namespace ValveResourceFormat.Source1.Bsp;

/// <summary>
/// Finds the brush of the world that a face of the map was made from. The map keeps no link from a face to its brush, so
/// a face goes with the brush that has a side on its plane with its texture info, and the face inside it.
/// </summary>
internal sealed class BspFaceBrushes
{
    // How far a side may be from the plane of a face and still be on it
    private const float PlaneTolerance = 0.1f;

    // How far outside a brush the center of a face may be, since the brush corners are rebuilt from float planes
    private const float InsideTolerance = 1f;

    private const float NormalDot = 0.999f;

    private readonly record struct BrushSide(BspBrushSolid Brush, BspBrushFace Face);

    private readonly BspFace[] faces;
    private readonly BspPlane[] planes;
    private readonly Dictionary<int, List<BrushSide>> sidesByTexInfo = [];
    private readonly Dictionary<int, int> brushesByDisplacement = [];

    /// <summary>Prepares the brushes of the world, which the faces of the world are matched against.</summary>
    /// <param name="bsp">The map.</param>
    /// <param name="brushes">The brushes of the map.</param>
    /// <param name="faces">The faces of the map, with the lightmaps the converted world uses.</param>
    public BspFaceBrushes(BspFile bsp, BspBrushSet brushes, BspFace[] faces)
    {
        this.faces = faces;
        planes = bsp.Planes;

        foreach (var brush in brushes.GetModelBrushes(0))
        {
            foreach (var face in brush.Faces)
            {
                if (face.DispInfo >= 0)
                {
                    brushesByDisplacement.TryAdd(face.DispInfo, brush.Index);
                    continue;
                }

                if (!sidesByTexInfo.TryGetValue(face.TexInfo, out var sides))
                {
                    sides = [];
                    sidesByTexInfo.Add(face.TexInfo, sides);
                }

                sides.Add(new BrushSide(brush, face));
            }
        }
    }

    /// <summary>Finds the brush a face was made from.</summary>
    /// <param name="faceIndex">The index of the face in the map.</param>
    /// <param name="center">A point inside the face.</param>
    /// <returns>The index of the brush, or -1 when no brush of the world has the face.</returns>
    public int Find(int faceIndex, Vector3 center)
    {
        var face = faces[faceIndex];

        if (face.DispInfo >= 0)
        {
            return brushesByDisplacement.GetValueOrDefault(face.DispInfo, -1);
        }

        if (face.PlaneNum >= planes.Length || !sidesByTexInfo.TryGetValue(face.TexInfo, out var sides))
        {
            return -1;
        }

        var plane = planes[face.PlaneNum];
        var sign = face.Side == 0 ? 1f : -1f;
        var normal = plane.Normal * sign;
        var distance = plane.Dist * sign;

        foreach (var side in sides)
        {
            var isOnPlane = Vector3.Dot(normal, side.Face.Normal) >= NormalDot
                && MathF.Abs(distance - side.Face.Distance) <= PlaneTolerance;

            if (isOnPlane && IsInside(side.Brush, center))
            {
                return side.Brush.Index;
            }
        }

        return -1;
    }

    private static bool IsInside(BspBrushSolid brush, Vector3 point)
    {
        var margin = new Vector3(InsideTolerance);
        var searchBounds = new AABB(brush.Bounds.Min - margin, brush.Bounds.Max + margin);

        if (!searchBounds.Contains(point))
        {
            return false;
        }

        foreach (var face in brush.Faces)
        {
            if (Vector3.Dot(face.Normal, point) - face.Distance > InsideTolerance)
            {
                return false;
            }
        }

        return true;
    }
}
