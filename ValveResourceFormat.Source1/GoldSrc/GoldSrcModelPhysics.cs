using ValveResourceFormat.Source1.Physics;

namespace ValveResourceFormat.GoldSrc;

internal sealed class GoldSrcModelPhysics
{
    private const float PlaneEpsilon = 0.01f;
    private const float VertexEpsilon = 0.02f;
    private const float PlaneNormalTolerance = 0.9999f;
    private const float PlaneDistanceTolerance = 0.1f;
    private const float MinVolume = 0.01f;
    private const float NeighbourOffset = 0.5f;
    private const float ShellThickness = 32f;
    private const int MaxTreeDepth = 512;

    // The BSP does not store the sizes of its player hulls, they are the engine defaults
    private static readonly Vector3 StandingHullHalfExtents = new(16f, 16f, 36f);
    private static readonly Vector3 LargeHullHalfExtents = new(32f, 32f, 32f);
    private static readonly Vector3 CrouchingHullHalfExtents = new(16f, 16f, 18f);

    private readonly record struct HalfSpace(Vector3 Normal, float Distance);

    private readonly record struct PlaneKey(int X, int Y, int Z, int Distance);

    private sealed record TexturedFace(Vector3 Normal, float Distance, Vector3 Center, float Area, string Surface);

    private readonly record struct Face(List<Vector3> Points, int Space);

    private sealed record Cell(List<Face> Faces, Vector3 Centroid);

    private sealed class ClipLeaf(List<HalfSpace> spaces)
    {
        public List<HalfSpace> Spaces { get; } = spaces;
        public bool IsClip { get; set; }
    }

    private sealed class ClipHull(int headNode, Vector3 halfExtents, List<HalfSpace> bounds, int attributes)
    {
        public int HeadNode { get; } = headNode;
        public Vector3 HalfExtents { get; } = halfExtents;
        public List<HalfSpace> Bounds { get; } = bounds;
        public int Attributes { get; } = attributes;
        public Dictionary<int, ClipLeaf> Leaves { get; } = [];
    }

    private readonly GoldSrcBspFile bsp;
    private readonly GoldSrcModel model;
    private readonly PhysicsAggregateBuilder builder = new();
    private readonly List<HalfSpace> solidBounds;
    private readonly List<ClipHull> clipHulls;
    private readonly float quadSize;
    private readonly Dictionary<PlaneKey, List<TexturedFace>> facesByPlane = [];

    private GoldSrcModelPhysics(GoldSrcBspFile bsp, int modelIndex, GoldSrcTextureTypes textureTypes)
    {
        this.bsp = bsp;
        model = bsp.Models[modelIndex];
        AddTexturedFaces(textureTypes);

        solidBounds = CreateBounds(model.Min - Vector3.One, model.Max + Vector3.One);
        quadSize = Vector3.Distance(model.Min, model.Max) + (4f * LargeHullHalfExtents.Length()) + 16f;

        var playerAndMonsters = builder.GetCollisionAttributes("conditionallysolid", ["playerclip", "npcclip"]);
        var playersOnly = builder.GetCollisionAttributes("conditionallysolid", ["playerclip"]);
        var monstersOnly = builder.GetCollisionAttributes("conditionallysolid", ["npcclip"]);

        clipHulls =
        [
            CreateClipHull(model.HeadNode1, StandingHullHalfExtents, playerAndMonsters),
            CreateClipHull(model.HeadNode3, CrouchingHullHalfExtents, playersOnly),
            CreateClipHull(model.HeadNode2, LargeHullHalfExtents, monstersOnly),
        ];
    }

    // Coplanar faces can lie on different node planes, so the faces are grouped by the geometry of their plane
    private void AddTexturedFaces(GoldSrcTextureTypes textureTypes)
    {
        var textureNames = bsp.TextureNames;

        for (var i = model.FirstFace; i < model.FirstFace + model.NumFaces; i++)
        {
            var face = bsp.Faces[i];
            var mipTexture = bsp.TextureInfos[face.TexInfo].MipTexture;

            if ((uint)mipTexture >= (uint)textureNames.Count)
            {
                continue;
            }

            var plane = bsp.Planes[face.PlaneNum];
            var normal = face.Side == 0 ? plane.Normal : -plane.Normal;
            var distance = face.Side == 0 ? plane.Dist : -plane.Dist;

            var points = bsp.GetFaceVertices(face);
            var center = Vector3.Zero;
            var area = 0f;

            foreach (var point in points)
            {
                center += point;
            }

            for (var corner = 1; corner < points.Length - 1; corner++)
            {
                area += Vector3.Cross(points[corner] - points[0], points[corner + 1] - points[0]).Length() * 0.5f;
            }

            var key = GetPlaneKey(normal, distance, 0);

            if (!facesByPlane.TryGetValue(key, out var faces))
            {
                faces = [];
                facesByPlane.Add(key, faces);
            }

            var surface = textureTypes.FindSurface(textureNames[mipTexture]);
            faces.Add(new TexturedFace(normal, distance, center / points.Length, area, surface));
        }
    }

    private static PlaneKey GetPlaneKey(Vector3 normal, float distance, int distanceOffset)
    {
        var x = (int)MathF.Round(normal.X * 100f);
        var y = (int)MathF.Round(normal.Y * 100f);
        var z = (int)MathF.Round(normal.Z * 100f);

        return new PlaneKey(x, y, z, (int)MathF.Round(distance) + distanceOffset);
    }

    private ClipHull CreateClipHull(int headNode, Vector3 halfExtents, int attributes)
    {
        var bounds = CreateBounds(model.Min - halfExtents - Vector3.One, model.Max + halfExtents + Vector3.One);
        return new ClipHull(headNode, halfExtents, bounds, attributes);
    }

    /// <summary>
    /// Converts the collision hulls of a model to physics: the solid and liquid leaves of the point hull, and the volumes
    /// that block a player hull but not the point hull, as clips. A clip brush is in every player hull, so the volumes of
    /// the crouching and large hulls are only added where an earlier hull has none.
    /// </summary>
    /// <param name="bsp">The map.</param>
    /// <param name="modelIndex">The index of the model, where 0 is the world.</param>
    /// <returns>The physics, or null when the model has no collision.</returns>
    /// <param name="textureTypes">The texture types that give the surface of each face.</param>
    public static Resource? Convert(GoldSrcBspFile bsp, int modelIndex, GoldSrcTextureTypes textureTypes)
    {
        var physics = new GoldSrcModelPhysics(bsp, modelIndex, textureTypes);

        physics.AddPointHullCells(physics.model.HeadNode0, []);
        physics.AddClipCells();

        return physics.builder.IsEmpty ? null : physics.builder.BuildResource();
    }

    private static List<HalfSpace> CreateBounds(Vector3 min, Vector3 max)
    {
        return
        [
            new(Vector3.UnitX, max.X),
            new(-Vector3.UnitX, -min.X),
            new(Vector3.UnitY, max.Y),
            new(-Vector3.UnitY, -min.Y),
            new(Vector3.UnitZ, max.Z),
            new(-Vector3.UnitZ, -min.Z),
        ];
    }

    private HalfSpace GetFrontSpace(int planeNumber)
    {
        var plane = bsp.Planes[planeNumber];
        return new HalfSpace(-plane.Normal, -plane.Dist);
    }

    private HalfSpace GetBackSpace(int planeNumber)
    {
        var plane = bsp.Planes[planeNumber];
        return new HalfSpace(plane.Normal, plane.Dist);
    }

    private void AddPointHullCells(int child, List<HalfSpace> path)
    {
        if (child < 0)
        {
            var leafIndex = ~child;

            if (leafIndex < bsp.Leafs.Length)
            {
                AddPointHullCell(bsp.Leafs[leafIndex].Contents, path);
            }

            return;
        }

        if (child >= bsp.Nodes.Length || path.Count >= MaxTreeDepth)
        {
            return;
        }

        var node = bsp.Nodes[child];

        if (node.PlaneNum < 0 || node.PlaneNum >= bsp.Planes.Length)
        {
            return;
        }

        path.Add(GetFrontSpace(node.PlaneNum));
        AddPointHullCells(node.Front, path);

        path[^1] = GetBackSpace(node.PlaneNum);
        AddPointHullCells(node.Back, path);

        path.RemoveAt(path.Count - 1);
    }

    private void AddPointHullCell(int contents, List<HalfSpace> path)
    {
        var isSolid = contents is GoldSrcContents.Solid or GoldSrcContents.Sky;
        var isCurrent = contents <= GoldSrcContents.CurrentFirst && contents >= GoldSrcContents.CurrentLast;
        var isLiquid = contents is GoldSrcContents.Water or GoldSrcContents.Slime or GoldSrcContents.Lava || isCurrent;

        if (!isSolid && !isLiquid)
        {
            return;
        }

        var cell = BuildCell([.. path, .. solidBounds]);

        if (cell == null)
        {
            return;
        }

        if (isSolid)
        {
            var attributes = builder.GetCollisionAttributes("default", []);

            if (TouchesBounds(cell, path.Count))
            {
                AddSurfaceShell(cell, path, attributes);
                return;
            }

            AddSolidHull(cell, path, attributes);
            return;
        }

        var surface = contents == GoldSrcContents.Water ? GoldSrcSurfaceProperties.WaterSurface : GoldSrcSurfaceProperties.DefaultSurface;
        AddHull(cell, builder.GetCollisionAttributes("conditionallysolid", ["water"]), surface);
    }

    private static bool TouchesBounds(Cell cell, int pathCount)
    {
        foreach (var face in cell.Faces)
        {
            if (face.Space >= pathCount)
            {
                return true;
            }
        }

        return false;
    }

    // The solid of the point hull is infinite outside of the map, so a cell that reaches the bounds of the model is the walls
    // together with the void behind them. Only a layer under each face that borders open space is kept.
    private void AddSurfaceShell(Cell cell, List<HalfSpace> path, int attributes)
    {
        foreach (var face in cell.Faces)
        {
            if (face.Space >= path.Count || !BordersOpenSpace(face, path[face.Space]))
            {
                continue;
            }

            var space = path[face.Space];
            var layer = new HalfSpace(-space.Normal, -(space.Distance - ShellThickness));
            var shell = BuildCell([.. path, .. solidBounds, layer]);

            if (shell != null)
            {
                AddSolidHull(shell, path, attributes);
            }
        }
    }

    // Samples the point hull next to the centre of the face and next to each corner pulled towards the centre
    private bool BordersOpenSpace(Face face, HalfSpace space)
    {
        var center = Vector3.Zero;

        foreach (var point in face.Points)
        {
            center += point;
        }

        center /= face.Points.Count;

        if (IsOpenBeside(center, space))
        {
            return true;
        }

        foreach (var point in face.Points)
        {
            if (IsOpenBeside(Vector3.Lerp(point, center, 0.1f), space))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsOpenBeside(Vector3 point, HalfSpace space)
    {
        var contents = GetPointContents(point + (space.Normal * NeighbourOffset));
        return contents is not (GoldSrcContents.Solid or GoldSrcContents.Sky);
    }

    private void AddClipCells()
    {
        foreach (var hull in clipHulls)
        {
            CollectClipLeaves(hull, hull.HeadNode, -1, []);
        }

        for (var hullIndex = 0; hullIndex < clipHulls.Count; hullIndex++)
        {
            AddClipCells(hullIndex);
        }
    }

    private void AddClipCells(int hullIndex)
    {
        var hull = clipHulls[hullIndex];

        foreach (var leaf in hull.Leaves.Values)
        {
            if (!leaf.IsClip)
            {
                continue;
            }

            var expanded = BuildCell([.. leaf.Spaces, .. hull.Bounds]);

            if (expanded == null)
            {
                continue;
            }

            var spaces = new List<HalfSpace>(leaf.Spaces);

            foreach (var face in expanded.Faces)
            {
                if (face.Space < leaf.Spaces.Count && !IsInsideClipVolume(hull, face, spaces[face.Space]))
                {
                    spaces[face.Space] = Shrink(spaces[face.Space], hull.HalfExtents);
                }
            }

            var cell = BuildCell([.. spaces, .. hull.Bounds]);

            if (cell != null && !IsClipInEarlierHull(hullIndex, cell.Centroid))
            {
                AddHull(cell, hull.Attributes, GoldSrcSurfaceProperties.DefaultSurface);
            }
        }
    }

    private bool IsClipInEarlierHull(int hullIndex, Vector3 point)
    {
        for (var i = 0; i < hullIndex; i++)
        {
            var key = FindClipLeafKey(clipHulls[i], point);

            if (clipHulls[i].Leaves.TryGetValue(key, out var leaf) && leaf.IsClip)
            {
                return true;
            }
        }

        return false;
    }

    private void CollectClipLeaves(ClipHull hull, int node, int key, List<HalfSpace> path)
    {
        var clipNodes = bsp.ClipNodes;

        if (node < 0)
        {
            if (node is GoldSrcContents.Solid or GoldSrcContents.Sky)
            {
                AddClipLeaf(hull, key, path);
            }

            return;
        }

        if (node >= clipNodes.Length || path.Count >= MaxTreeDepth)
        {
            return;
        }

        var clipNode = clipNodes[node];

        if (clipNode.PlaneNum < 0 || clipNode.PlaneNum >= bsp.Planes.Length)
        {
            return;
        }

        path.Add(GetFrontSpace(clipNode.PlaneNum));
        CollectClipLeaves(hull, clipNode.Front, node * 2, path);

        path[^1] = GetBackSpace(clipNode.PlaneNum);
        CollectClipLeaves(hull, clipNode.Back, (node * 2) + 1, path);

        path.RemoveAt(path.Count - 1);
    }

    // A leaf of the player hull is a clip volume when what remains of it after shrinking by the player is not in solid
    // of the point hull
    private void AddClipLeaf(ClipHull hull, int key, List<HalfSpace> path)
    {
        var leaf = new ClipLeaf([.. path]);
        var shrunk = new List<HalfSpace>(path.Count + hull.Bounds.Count);

        foreach (var space in path)
        {
            shrunk.Add(Shrink(space, hull.HalfExtents));
        }

        shrunk.AddRange(hull.Bounds);

        var cell = BuildCell(shrunk);

        if (cell != null)
        {
            var contents = GetPointContents(cell.Centroid);
            leaf.IsClip = contents is not (GoldSrcContents.Solid or GoldSrcContents.Sky);
        }

        hull.Leaves[key] = leaf;
    }

    // A face between two parts of one clip volume is inside it, and stays where the player hull put it
    private bool IsInsideClipVolume(ClipHull hull, Face face, HalfSpace space)
    {
        var center = Vector3.Zero;

        foreach (var point in face.Points)
        {
            center += point;
        }

        center /= face.Points.Count;

        var key = FindClipLeafKey(hull, center + (space.Normal * NeighbourOffset));
        return hull.Leaves.TryGetValue(key, out var neighbour) && neighbour.IsClip;
    }

    private int FindClipLeafKey(ClipHull hull, Vector3 point)
    {
        var clipNodes = bsp.ClipNodes;
        var node = hull.HeadNode;
        var key = -1;

        for (var depth = 0; depth < MaxTreeDepth && node >= 0 && node < clipNodes.Length; depth++)
        {
            var clipNode = clipNodes[node];

            if ((uint)clipNode.PlaneNum >= (uint)bsp.Planes.Length)
            {
                break;
            }

            var plane = bsp.Planes[clipNode.PlaneNum];
            var isFront = Vector3.Dot(point, plane.Normal) - plane.Dist >= 0f;

            key = (node * 2) + (isFront ? 0 : 1);
            node = isFront ? clipNode.Front : clipNode.Back;
        }

        return key;
    }

    private int GetPointContents(Vector3 point)
    {
        var node = model.HeadNode0;

        for (var depth = 0; depth < MaxTreeDepth && node >= 0 && node < bsp.Nodes.Length; depth++)
        {
            var treeNode = bsp.Nodes[node];

            if ((uint)treeNode.PlaneNum >= (uint)bsp.Planes.Length)
            {
                break;
            }

            var plane = bsp.Planes[treeNode.PlaneNum];
            var isFront = Vector3.Dot(point, plane.Normal) - plane.Dist >= 0f;

            node = isFront ? treeNode.Front : treeNode.Back;
        }

        if (node >= 0 || ~node >= bsp.Leafs.Length)
        {
            return GoldSrcContents.Solid;
        }

        return bsp.Leafs[~node].Contents;
    }

    private static HalfSpace Shrink(HalfSpace space, Vector3 halfExtents)
    {
        var reach = Vector3.Dot(Vector3.Abs(space.Normal), halfExtents);
        return space with { Distance = space.Distance - reach };
    }

    // A solid cell has the surface of the faces that border open space. A cell whose faces have different surfaces is
    // cut into a pyramid for each face, so that every face keeps its own.
    private void AddSolidHull(Cell cell, List<HalfSpace> spaces, int attributes)
    {
        var faceSurfaces = new string?[cell.Faces.Count];
        string? single = null;
        var isMixed = false;

        for (var i = 0; i < faceSurfaces.Length; i++)
        {
            var surface = FindFaceSurface(cell.Faces[i], spaces);
            faceSurfaces[i] = surface;

            if (surface == null)
            {
                continue;
            }

            single ??= surface;
            isMixed |= surface != single;
        }

        if (!isMixed)
        {
            AddHull(cell, attributes, single ?? GoldSrcSurfaceProperties.DefaultSurface);
            return;
        }

        for (var i = 0; i < faceSurfaces.Length; i++)
        {
            var pyramid = CreatePyramid(cell.Faces[i], cell.Centroid);
            AddHull(pyramid, attributes, faceSurfaces[i] ?? GoldSrcSurfaceProperties.DefaultSurface);
        }
    }

    // The face is the base, the sides are the triangles from its edges to the apex
    private static Cell CreatePyramid(Face face, Vector3 apex)
    {
        var points = face.Points;
        var faces = new List<Face>(points.Count + 1) { face };

        for (var i = 0; i < points.Count; i++)
        {
            var next = points[(i + 1) % points.Count];
            faces.Add(new Face([next, points[i], apex], face.Space));
        }

        return new Cell(faces, apex);
    }

    // The surface under most of the area of a face: the textured faces on its plane whose centers are inside it vote with
    // their area. Null where no textured face lies on the face.
    private string? FindFaceSurface(Face face, List<HalfSpace> spaces)
    {
        if (face.Space >= spaces.Count)
        {
            return null;
        }

        var space = spaces[face.Space];
        var areas = new Dictionary<string, float>();

        for (var offset = -1; offset <= 1; offset++)
        {
            var key = GetPlaneKey(space.Normal, space.Distance, offset);

            if (!facesByPlane.TryGetValue(key, out var candidates))
            {
                continue;
            }

            foreach (var candidate in candidates)
            {
                var isOnPlane = Vector3.Dot(candidate.Normal, space.Normal) > PlaneNormalTolerance
                    && MathF.Abs(candidate.Distance - space.Distance) <= PlaneDistanceTolerance;

                if (isOnPlane && ContainsPoint(face, space.Normal, candidate.Center))
                {
                    areas[candidate.Surface] = areas.GetValueOrDefault(candidate.Surface) + candidate.Area;
                }
            }
        }

        string? best = null;
        var bestArea = 0f;

        foreach (var (surface, area) in areas)
        {
            if (area > bestArea)
            {
                best = surface;
                bestArea = area;
            }
        }

        return best;
    }

    // The face is convex, so the point is inside when it is on the same side of every edge
    private static bool ContainsPoint(Face face, Vector3 normal, Vector3 point)
    {
        var points = face.Points;
        var hasPositive = false;
        var hasNegative = false;

        for (var i = 0; i < points.Count; i++)
        {
            var start = points[i];
            var end = points[(i + 1) % points.Count];
            var side = Vector3.Dot(Vector3.Cross(end - start, point - start), normal);

            hasPositive |= side > PlaneEpsilon;
            hasNegative |= side < -PlaneEpsilon;
        }

        return !(hasPositive && hasNegative);
    }

    private void AddHull(Cell cell, int attributes, string surface)
    {
        var points = new List<Vector3>();
        var triangles = new List<int>();

        foreach (var face in cell.Faces)
        {
            var corners = new List<int>();

            foreach (var point in face.Points)
            {
                var index = FindOrAddPoint(points, point);

                if (corners.Count == 0 || corners[^1] != index)
                {
                    corners.Add(index);
                }
            }

            if (corners.Count > 1 && corners[0] == corners[^1])
            {
                corners.RemoveAt(corners.Count - 1);
            }

            for (var i = 1; i < corners.Count - 1; i++)
            {
                triangles.Add(corners[0]);
                triangles.Add(corners[i]);
                triangles.Add(corners[i + 1]);
            }
        }

        builder.AddHull(points, triangles, attributes, builder.GetSurfaceProperty(surface));
    }

    private static int FindOrAddPoint(List<Vector3> points, Vector3 point)
    {
        for (var i = 0; i < points.Count; i++)
        {
            if (Vector3.DistanceSquared(points[i], point) <= VertexEpsilon * VertexEpsilon)
            {
                return i;
            }
        }

        points.Add(point);
        return points.Count - 1;
    }

    // The polyhedron inside all of the half spaces, or null when it has no volume
    private Cell? BuildCell(List<HalfSpace> spaces)
    {
        var faces = new List<Face>();

        for (var i = 0; i < spaces.Count; i++)
        {
            var polygon = BuildFace(spaces, i);

            if (polygon.Count >= 3)
            {
                faces.Add(new Face(polygon, i));
            }
        }

        if (faces.Count < 4)
        {
            return null;
        }

        var origin = faces[0].Points[0];
        var volume = 0f;
        var center = Vector3.Zero;
        var pointCount = 0;

        foreach (var face in faces)
        {
            var points = face.Points;

            for (var corner = 1; corner < points.Count - 1; corner++)
            {
                volume += Vector3.Dot(points[0] - origin, Vector3.Cross(points[corner] - origin, points[corner + 1] - origin)) / 6f;
            }

            foreach (var point in points)
            {
                center += point;
            }

            pointCount += points.Count;
        }

        if (volume < MinVolume)
        {
            return null;
        }

        return new Cell(faces, center / pointCount);
    }

    // The part of the plane of a half space that is inside all of the others
    private List<Vector3> BuildFace(List<HalfSpace> spaces, int index)
    {
        var space = spaces[index];

        for (var i = 0; i < index; i++)
        {
            if (IsSamePlane(space, spaces[i]))
            {
                return [];
            }
        }

        var polygon = CreateQuad(space);

        for (var i = 0; i < spaces.Count && polygon.Count >= 3; i++)
        {
            if (i != index && !IsSamePlane(space, spaces[i]))
            {
                polygon = Clip(polygon, spaces[i]);
            }
        }

        return polygon;
    }

    private static bool IsSamePlane(HalfSpace a, HalfSpace b)
    {
        return Vector3.Dot(a.Normal, b.Normal) > 0.99999f && MathF.Abs(a.Distance - b.Distance) <= PlaneEpsilon;
    }

    private List<Vector3> CreateQuad(HalfSpace space)
    {
        var center = (model.Min + model.Max) * 0.5f;
        var onPlane = center - (space.Normal * (Vector3.Dot(space.Normal, center) - space.Distance));
        var helper = MathF.Abs(space.Normal.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX;
        var u = Vector3.Normalize(Vector3.Cross(space.Normal, helper)) * quadSize;
        var v = Vector3.Normalize(Vector3.Cross(space.Normal, u)) * quadSize;

        return [onPlane - u - v, onPlane + u - v, onPlane + u + v, onPlane - u + v];
    }

    private static List<Vector3> Clip(List<Vector3> polygon, HalfSpace space)
    {
        var result = new List<Vector3>(polygon.Count + 1);

        for (var i = 0; i < polygon.Count; i++)
        {
            var current = polygon[i];
            var next = polygon[(i + 1) % polygon.Count];
            var currentDistance = Vector3.Dot(current, space.Normal) - space.Distance;
            var nextDistance = Vector3.Dot(next, space.Normal) - space.Distance;

            if (currentDistance <= PlaneEpsilon)
            {
                result.Add(current);
            }

            var crosses = (currentDistance < -PlaneEpsilon && nextDistance > PlaneEpsilon)
                || (currentDistance > PlaneEpsilon && nextDistance < -PlaneEpsilon);

            if (crosses)
            {
                var fraction = currentDistance / (currentDistance - nextDistance);
                result.Add(current + ((next - current) * fraction));
            }
        }

        return result;
    }
}
