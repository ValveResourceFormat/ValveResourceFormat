namespace ValveResourceFormat.Source1.Bsp;

/// <summary>A side of a brush, with the polygon its plane cuts from the brush.</summary>
/// <param name="Normal">The normal of the plane, facing out of the brush.</param>
/// <param name="Distance">The distance of the plane from the origin along its normal.</param>
/// <param name="TexInfo">The texture info of the side, which has its material and texture axes, or -1 for none.</param>
/// <param name="DispInfo">The displacement made from the side, or -1 for none.</param>
internal sealed record BspBrushFace(Vector3 Normal, float Distance, int TexInfo, int DispInfo);

/// <summary>A brush of a map, rebuilt as the convex solid its sides bound.</summary>
/// <param name="Index">The index of the brush in the map.</param>
/// <param name="Model">The model the brush belongs to, 0 for the world, or -1 when no model has it.</param>
/// <param name="Contents">What the brush is made of.</param>
/// <param name="Faces">The sides the brush was authored with, without the bevel planes added for collision.</param>
/// <param name="Bounds">The bounds of the corners of the sides.</param>
internal sealed record BspBrushSolid(int Index, int Model, BspContents Contents, BspBrushFace[] Faces, AABB Bounds);

/// <summary>
/// The brushes of a map, as the planes of their sides, grouped by the model they belong to.
/// </summary>
internal sealed class BspBrushSet
{
    // Half the size of the polygon a side starts as, before the other sides cut it down
    private const double MaxExtent = 65536;

    // How far outside a plane a corner may be and still count as on it
    private const double ClipEpsilon = 0.01;

    private sealed record BrushData(BspBrushSolid[] Brushes, List<BspBrushSolid>[] ModelBrushes);

    private readonly BspFile bsp;
    private readonly Lazy<BrushData> data;

    // Why the brushes of the map cannot be read, or null when they can. A map that cannot has no brushes.
    private readonly string? unsupportedReason;

    /// <summary>Prepares the brushes of a map, which are built when they are first used.</summary>
    /// <param name="bsp">The map.</param>
    public BspBrushSet(BspFile bsp)
    {
        this.bsp = bsp;

        unsupportedReason = FindUnsupportedLump(bsp);
        data = new Lazy<BrushData>(Build);
    }

    /// <summary>Gets the brushes of a model.</summary>
    /// <param name="model">The index of the model, 0 for the world.</param>
    /// <returns>The brushes, none for a model that is not in the map.</returns>
    public IReadOnlyList<BspBrushSolid> GetModelBrushes(int model)
    {
        var modelBrushes = data.Value.ModelBrushes;

        if ((uint)model >= (uint)modelBrushes.Length)
        {
            return [];
        }

        return modelBrushes[model];
    }

    // Every lump the brushes are read from has the layout read here in the versions the games ship
    private static string? FindUnsupportedLump(BspFile bsp)
    {
        LumpType[] types = [LumpType.Models, LumpType.Planes, LumpType.Nodes, LumpType.LeafBrushes, LumpType.Brushes, LumpType.BrushSides];

        foreach (var type in types)
        {
            var info = bsp.GetLumpInfo(type);
            var isWideLump = type is LumpType.Nodes or LumpType.LeafBrushes or LumpType.BrushSides;
            var expectedVersion = bsp.IsWide && isWideLump ? BspFile.WideLumpVersion : 0;

            if (info.Length > 0 && info.Version != expectedVersion)
            {
                return $"The {type} lump is version {info.Version}, but only version {expectedVersion} is read";
            }
        }

        var minLeafVersion = 0;
        var maxLeafVersion = 1;
        var readVersions = "versions 0 and 1 are";

        if (bsp.IsWide)
        {
            minLeafVersion = BspFile.WideLeafLumpVersion;
            maxLeafVersion = BspFile.WideLeafLumpVersion;
            readVersions = $"version {BspFile.WideLeafLumpVersion} is";
        }

        var leafs = bsp.GetLumpInfo(LumpType.Leafs);

        if (leafs.Length > 0 && (leafs.Version < minLeafVersion || leafs.Version > maxLeafVersion))
        {
            return $"The Leafs lump is version {leafs.Version}, but only {readVersions} read";
        }

        return null;
    }

    private BrushData Build()
    {
        if (unsupportedReason != null)
        {
            return new BrushData([], []);
        }

        var models = FindBrushModels(bsp);
        var source = bsp.Brushes;
        var brushes = new BspBrushSolid[source.Length];
        var modelBrushes = new List<BspBrushSolid>[bsp.Models.Length];

        for (var i = 0; i < modelBrushes.Length; i++)
        {
            modelBrushes[i] = [];
        }

        var buffers = new WindingBuffers();

        for (var i = 0; i < source.Length; i++)
        {
            var brush = BuildBrush(i, models[i], source[i], buffers);
            brushes[i] = brush;

            if (brush.Model >= 0)
            {
                modelBrushes[brush.Model].Add(brush);
            }
        }

        return new BrushData(brushes, modelBrushes);
    }

    // The model whose tree lists each brush in its leaves, or -1 for brushes no tree lists
    private static int[] FindBrushModels(BspFile bsp)
    {
        var models = new int[bsp.Brushes.Length];
        Array.Fill(models, -1);

        var nodes = BspWorldTrace.ReadNodes(bsp);
        var leafs = bsp.Leafs;
        var leafBrushes = bsp.LeafBrushes;
        var pending = new Stack<int>();

        for (var model = 0; model < bsp.Models.Length; model++)
        {
            pending.Push(bsp.Models[model].HeadNode);

            while (pending.TryPop(out var child))
            {
                if (child >= 0)
                {
                    pending.Push(nodes[child].Child0);
                    pending.Push(nodes[child].Child1);
                    continue;
                }

                var leaf = leafs[-1 - child];

                for (var i = 0; i < leaf.NumLeafBrushes; i++)
                {
                    var brush = leafBrushes[leaf.FirstLeafBrush + i];

                    if (brush < models.Length && models[brush] < 0)
                    {
                        models[brush] = model;
                    }
                }
            }
        }

        return models;
    }

    private BspBrushSolid BuildBrush(int index, int model, BspBrush brush, WindingBuffers buffers)
    {
        var planes = bsp.Planes;
        var allSides = bsp.BrushSides;
        var emptyBounds = new AABB(Vector3.Zero, Vector3.Zero);

        if (brush.FirstSide < 0 || brush.NumSides < 0 || brush.FirstSide + brush.NumSides > allSides.Length)
        {
            return new BspBrushSolid(index, model, brush.Contents, [], emptyBounds);
        }

        var sides = allSides.AsSpan(brush.FirstSide, brush.NumSides);
        var faces = new List<BspBrushFace>(sides.Length);
        AABB? bounds = null;

        for (var i = 0; i < sides.Length; i++)
        {
            var side = sides[i];

            if (side.IsBevel || side.Plane >= planes.Length)
            {
                continue;
            }

            var plane = planes[side.Plane];
            var winding = buffers.Start(plane);

            for (var j = 0; j < sides.Length && winding.Count > 0; j++)
            {
                var other = sides[j];

                if (j == i || other.IsBevel || other.Plane == side.Plane || other.Plane >= planes.Length)
                {
                    continue;
                }

                winding = buffers.Clip(planes[other.Plane]);
            }

            if (winding.Count < 3)
            {
                continue;
            }

            foreach (var point in winding)
            {
                var corner = (Vector3)point;
                bounds = bounds?.Encapsulate(corner) ?? new AABB(corner, corner);
            }

            faces.Add(new BspBrushFace(plane.Normal, plane.Dist, side.TexInfo, side.DispInfo));
        }

        return new BspBrushSolid(index, model, brush.Contents, [.. faces], bounds ?? emptyBounds);
    }

    // Two lists that a polygon is clipped back and forth between, so that clipping does not allocate
    private sealed class WindingBuffers
    {
        private List<Point> current = [];
        private List<Point> spare = [];

        // A square on the plane, larger than any map, facing the way the plane does
        public List<Point> Start(BspPlane plane)
        {
            var normal = new Point(plane.Normal.X, plane.Normal.Y, plane.Normal.Z);

            var isMostlyVertical = Math.Abs(normal.Z) >= Math.Abs(normal.X) && Math.Abs(normal.Z) >= Math.Abs(normal.Y);
            var up = isMostlyVertical ? new Point(1, 0, 0) : new Point(0, 0, 1);

            up = (up - normal * Point.Dot(up, normal)).Normalized() * MaxExtent;
            var right = Point.Cross(up, normal);
            var origin = normal * plane.Dist;

            current.Clear();
            current.Add(origin - right + up);
            current.Add(origin + right + up);
            current.Add(origin + right - up);
            current.Add(origin - right - up);

            return current;
        }

        // Keeps the part of the polygon behind the plane, which faces out of the brush
        public List<Point> Clip(BspPlane plane)
        {
            var normal = new Point(plane.Normal.X, plane.Normal.Y, plane.Normal.Z);
            var winding = current;
            var result = spare;
            result.Clear();

            for (var i = 0; i < winding.Count; i++)
            {
                var corner = winding[i];
                var next = winding[(i + 1) % winding.Count];
                var cornerDistance = Point.Dot(corner, normal) - plane.Dist;
                var nextDistance = Point.Dot(next, normal) - plane.Dist;

                var isCornerInside = cornerDistance <= ClipEpsilon;
                var isNextInside = nextDistance <= ClipEpsilon;

                if (isCornerInside)
                {
                    result.Add(corner);
                }

                if (isCornerInside != isNextInside)
                {
                    var fraction = cornerDistance / (cornerDistance - nextDistance);
                    result.Add(corner + (next - corner) * fraction);
                }
            }

            spare = winding;
            current = result;
            return result;
        }
    }

    // Brush corners are found in double precision, since the large starting polygons lose too much in floats
    private readonly record struct Point(double X, double Y, double Z)
    {
        public static Point operator +(Point a, Point b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Point operator -(Point a, Point b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Point operator *(Point a, double scale) => new(a.X * scale, a.Y * scale, a.Z * scale);

        public static double Dot(Point a, Point b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public static Point Cross(Point a, Point b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

        public Point Normalized() => this * (1 / Math.Sqrt(Dot(this, this)));

        public static explicit operator Vector3(Point point) => new((float)point.X, (float)point.Y, (float)point.Z);
    }
}
