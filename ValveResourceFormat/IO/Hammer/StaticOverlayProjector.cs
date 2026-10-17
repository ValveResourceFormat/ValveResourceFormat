using System.Linq;
using ValveResourceFormat.IO.ContentFormats.HalfEdgeMesh;
using ValveResourceFormat.IO.ContentFormats.ValveMap;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.IO;

/// <summary>What a static overlay lands on, see <see cref="CMapStaticOverlay.ProjectionMode"/>.</summary>
public enum OverlayReceiverKind
{
    /// <summary>The faces of a map mesh.</summary>
    Mesh,

    /// <summary>The render meshes of a model.</summary>
    Model,
}

/// <summary>
/// Geometry a static overlay can be projected onto, in world space. Its triangles are only built the first time an
/// overlay reaches its bounds.
/// </summary>
/// <param name="kind">What the geometry is.</param>
/// <param name="bounds">The bounds of the geometry.</param>
/// <param name="buildTriangles">Builds the triangles, three positions each, and the normal at each of their corners.</param>
public sealed class OverlayReceiver(OverlayReceiverKind kind, AABB bounds, Func<(Vector3[] Positions, Vector3[] Normals)> buildTriangles)
{
    private readonly Lazy<(Vector3[] Positions, Vector3[] Normals)> triangles = new(buildTriangles);

    /// <summary>Gets what the geometry is.</summary>
    public OverlayReceiverKind Kind { get; } = kind;

    /// <summary>Gets the bounds of the geometry.</summary>
    public AABB Bounds { get; } = bounds;

    /// <summary>Gets the corners of the triangles, three for each.</summary>
    public Vector3[] Positions => triangles.Value.Positions;

    /// <summary>Gets the normal at each of <see cref="Positions"/>.</summary>
    public Vector3[] Normals => triangles.Value.Normals;
}

/// <summary>
/// Projects a static overlay onto the geometry under it the way Hammer previews it: the faces of each material of the
/// overlay are pushed along the way they face on average, and cut out of every surface triangle they reach.
/// </summary>
public static class StaticOverlayProjector
{
    // Overlay corners are moved onto a grid this fine once they are in the world
    private const float SnapStep = 0.125f;

    // Faces flagged with any of these do not project
    private const PolygonMeshFaceFlags NotProjectedFaceFlags = PolygonMeshFaceFlags.Selected | PolygonMeshFaceFlags.Hidden | PolygonMeshFaceFlags.CoveredByTile;

    // Corner weights of a point summing to this close to one put it inside a triangle
    private const float WeightSumTolerance = 0.001f;

    private readonly record struct Barycentric(int Triangle, Vector3 Weights);

    private sealed class SourcePolygon
    {
        private bool? isConvex;

        public required Vector3[] Positions { get; init; }
        public required Vector2[] TexCoords { get; init; }
        public Vector4[]? Tints { get; init; }
        public Vector4[]? Blends { get; init; }
        public required Vector3 Normal { get; init; }
        public required int[] Triangles { get; init; }

        public bool IsConvex => isConvex ??= ComputeIsConvex(Positions, Normal);
    }

    private sealed class Surfaces
    {
        public List<Vector3> Positions { get; } = [];
        public List<Vector3> Normals { get; } = [];
    }

    /// <summary>Projects an overlay onto the geometry under it.</summary>
    /// <param name="overlay">The overlay.</param>
    /// <param name="transform">Where the overlay is placed, without its scale, which projection leaves out.</param>
    /// <param name="receivers">
    /// What the overlay can land on. For an overlay that projects onto its <see cref="CMapStaticOverlay.ProjectionTargets"/>,
    /// only the geometry of those targets.
    /// </param>
    /// <param name="instanceData">What the editor baked for the overlay where it is placed, to light it with.</param>
    /// <returns>The projected triangles of each material of the overlay, in world space.</returns>
    public static PolygonMesh.BakedSubmesh[] Project(CMapStaticOverlay overlay, Matrix4x4 transform, IEnumerable<OverlayReceiver> receivers, CDmeNodeInstanceData? instanceData = null)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(receivers);

        var depth = MathF.Max(overlay.ProjectionFar, 0f);
        var groups = BuildGroups(overlay.Mesh, transform);

        if (groups.Count == 0)
        {
            return [];
        }

        // Targets of an overlay that projects no distance are taken whole, anything else only where it is in reach
        var projectionMode = (MapOverlayProjectionMode)overlay.ProjectionMode;
        var testBounds = projectionMode != MapOverlayProjectionMode.Targets || depth > 0f;
        var surfaces = GatherSurfaces(receivers, GetProjectionBounds(groups, depth), projectionMode, testBounds);

        if (surfaces.Positions.Count == 0)
        {
            return [];
        }

        var submeshes = new List<PolygonMesh.BakedSubmesh>(groups.Count);

        foreach (var (material, polygons) in groups)
        {
            if (ProjectGroup(material, polygons, surfaces, overlay, depth, instanceData) is { } submesh)
            {
                submeshes.Add(submesh);
            }
        }

        return [.. submeshes];
    }

    // The faces of the overlay that are drawn, in the world, grouped by material in the order of the materials
    private static List<(string Material, List<SourcePolygon> Polygons)> BuildGroups(PolygonMesh mesh, Matrix4x4 transform)
    {
        var hasTint = mesh.HasStoredStream(MeshComponentType.FaceVertex, "VertexPaintTintColor");
        var hasBlend = mesh.HasStoredStream(MeshComponentType.FaceVertex, "VertexPaintBlendParams");
        var groups = new List<(string, List<SourcePolygon>)>();

        for (var material = 0; material < mesh.Materials.Count; material++)
        {
            var polygons = new List<SourcePolygon>();

            foreach (var hFace in mesh.FaceHandles)
            {
                if (mesh.MaterialIndex[hFace] != material || ((PolygonMeshFaceFlags)mesh.FaceFlags[hFace] & NotProjectedFaceFlags) != 0)
                {
                    continue;
                }

                var positions = new List<Vector3>();
                var texCoords = new List<Vector2>();
                var tints = new List<Vector4>();
                var blends = new List<Vector4>();
                var hFaceVertex = hFace.Edge;

                do
                {
                    positions.Add(Vector3.Transform(mesh.Positions[hFaceVertex.Vertex], transform));
                    texCoords.Add(mesh.TextureCoords[hFaceVertex]);
                    tints.Add(mesh.VertexPaintTintColor[hFaceVertex]);
                    blends.Add(mesh.VertexPaintBlendParams[hFaceVertex]);
                    hFaceVertex = hFaceVertex.NextEdge;
                }
                while (hFaceVertex != hFace.Edge);

                var polygon = CreatePolygon([.. positions], [.. texCoords], hasTint ? [.. tints] : null, hasBlend ? [.. blends] : null);

                // The plane and triangles stay those of the corners from before they are snapped
                for (var i = 0; i < polygon.Positions.Length; i++)
                {
                    var position = polygon.Positions[i];

                    polygon.Positions[i] = new Vector3(Snap(position.X), Snap(position.Y), Snap(position.Z));
                }

                RecenterTexCoords(polygon.TexCoords);
                polygons.Add(polygon);
            }

            if (polygons.Count > 0)
            {
                groups.Add((mesh.Materials[material], polygons));
            }
        }

        return groups;

        static float Snap(float value) => MathF.Floor((value / SnapStep) + 0.5f) * SnapStep;
    }

    private static SourcePolygon CreatePolygon(Vector3[] positions, Vector2[] texCoords, Vector4[]? tints, Vector4[]? blends)
    {
        var normal = ComputePolygonNormal(positions);
        var triangles = new List<int>();

        MeshUtils.TriangulatePolygon(positions, normal, 0, triangles);

        return new SourcePolygon
        {
            Positions = positions,
            TexCoords = texCoords,
            Tints = tints,
            Blends = blends,
            Normal = normal,
            Triangles = [.. triangles],
        };
    }

    // Newell's method, the way the polygon winds counter clockwise around
    private static Vector3 ComputePolygonNormal(ReadOnlySpan<Vector3> positions)
    {
        var normal = Vector3.Zero;

        for (var i = 0; i < positions.Length; i++)
        {
            var a = positions[i];
            var b = positions[(i + 1) % positions.Length];

            normal += new Vector3((a.Y - b.Y) * (a.Z + b.Z), (a.Z - b.Z) * (a.X + b.X), (a.X - b.X) * (a.Y + b.Y));
        }

        return normal / (normal.Length() + 1.1920929e-7f);
    }

    // Texture coordinates are moved by whole tiles to be as near zero as they can be
    private static void RecenterTexCoords(Vector2[] texCoords)
    {
        if (texCoords.Length == 0)
        {
            return;
        }

        var min = texCoords.Aggregate(Vector2.Min);
        var max = texCoords.Aggregate(Vector2.Max);
        var centre = new Vector2((int)((min.X + max.X) * 0.5f), (int)((min.Y + max.Y) * 0.5f));

        for (var i = 0; i < texCoords.Length; i++)
        {
            texCoords[i] -= centre;
        }
    }

    private static bool ComputeIsConvex(ReadOnlySpan<Vector3> positions, Vector3 normal)
    {
        const float Tolerance = 1e-7f;

        var count = positions.Length;
        bool? turnsPositive = null;

        for (var i = 0; i < count; i++)
        {
            var a = positions[i];
            var b = positions[(i + 1) % count];
            var c = positions[(i + 2) % count];
            var turn = Vector3.Dot(Vector3.Cross(b - a, c - b), normal);

            if (turnsPositive is not { } positive)
            {
                turnsPositive = turn > 0f;
            }
            else if (positive ? turn + Tolerance < 0f : turn - Tolerance > 0f)
            {
                return false;
            }
        }

        return true;
    }

    // Everything the faces reach: their corners and the corners pushed as far as they project, a unit further out
    private static AABB GetProjectionBounds(List<(string Material, List<SourcePolygon> Polygons)> groups, float depth)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        foreach (var (_, polygons) in groups)
        {
            foreach (var polygon in polygons)
            {
                foreach (var position in polygon.Positions)
                {
                    var far = position - (polygon.Normal * depth);

                    min = Vector3.Min(min, Vector3.Min(position, far));
                    max = Vector3.Max(max, Vector3.Max(position, far));
                }
            }
        }

        return new AABB(min - Vector3.One, max + Vector3.One);
    }

    private static Surfaces GatherSurfaces(IEnumerable<OverlayReceiver> receivers, AABB bounds, MapOverlayProjectionMode projectionMode, bool testBounds)
    {
        var surfaces = new Surfaces();
        var centre = bounds.Center;
        var halfExtents = bounds.Size * 0.5f;

        foreach (var receiver in receivers)
        {
            if ((receiver.Kind == OverlayReceiverKind.Mesh && projectionMode == MapOverlayProjectionMode.Models)
                || (receiver.Kind == OverlayReceiverKind.Model && projectionMode == MapOverlayProjectionMode.World))
            {
                continue;
            }

            if (testBounds && !receiver.Bounds.Intersects(bounds))
            {
                continue;
            }

            var positions = receiver.Positions;
            var normals = receiver.Normals;

            for (var i = 0; i + 2 < positions.Length; i += 3)
            {
                if (testBounds && !MathUtils.TriangleIntersectsBox(positions[i], positions[i + 1], positions[i + 2], centre, halfExtents))
                {
                    continue;
                }

                surfaces.Positions.AddRange(positions.AsSpan(i, 3));
                surfaces.Normals.AddRange(normals.AsSpan(i, 3));
            }
        }

        return surfaces;
    }

    private static PolygonMesh.BakedSubmesh? ProjectGroup(string material, List<SourcePolygon> polygons, Surfaces surfaces, CMapStaticOverlay overlay, float depth, CDmeNodeInstanceData? instanceData)
    {
        // Every face of the material goes the way the faces face on average. Points are brought back onto the plane
        // of that way through the middle of the first face to take their texture coordinates and paint from a face.
        var normal = MathUtils.SafeNormalize(polygons.Aggregate(Vector3.Zero, static (sum, polygon) => sum + polygon.Normal));
        var firstFace = polygons[0].Positions;
        var planeDistance = Vector3.Dot(normal, firstFace.Aggregate(Vector3.Add) / firstFace.Length);
        var direction = -normal;

        var hasTint = polygons.Any(static polygon => polygon.Tints is { Length: > 0 });
        var hasBlend = polygons.Any(static polygon => polygon.Blends is { Length: > 0 });
        var cullBackFaces = !overlay.ProjectOnBackFaces;
        var angle = Math.Clamp(overlay.BackFacingAngle, 0f, 180f);

        var positions = new List<Vector3>();
        var barycentrics = new List<Barycentric>();
        var texCoords = new List<Vector2>();
        var tints = new List<Vector4>();
        var blends = new List<Vector4>();
        var sourceFaces = new List<int>();

        for (var i = 0; i < polygons.Count; i++)
        {
            var polygon = polygons[i];
            var start = positions.Count;

            if (polygon.IsConvex)
            {
                ClipTriangles(polygon.Positions, direction, depth, cullBackFaces, angle, surfaces.Positions, positions, barycentrics);
            }
            else
            {
                // A face that is not convex is cut out one of its triangles at a time
                for (var t = 0; t + 2 < polygon.Triangles.Length; t += 3)
                {
                    Vector3[] triangle = [polygon.Positions[polygon.Triangles[t]], polygon.Positions[polygon.Triangles[t + 1]], polygon.Positions[polygon.Triangles[t + 2]]];
                    ClipTriangles(triangle, direction, depth, cullBackFaces, angle, surfaces.Positions, positions, barycentrics);
                }
            }

            for (var v = start; v < positions.Count; v++)
            {
                var onPlane = positions[v] - ((Vector3.Dot(positions[v], normal) - planeDistance) * normal);

                texCoords.Add(GetTexCoord(polygon, onPlane));

                if (hasTint)
                {
                    tints.Add(GetStreamValue(polygon, onPlane, polygon.Tints, Vector4.One));
                }

                if (hasBlend)
                {
                    blends.Add(GetStreamValue(polygon, onPlane, polygon.Blends, Vector4.Zero));
                }
            }

            for (var t = start; t < positions.Count; t += 3)
            {
                sourceFaces.Add(i);
            }
        }

        if (positions.Count == 0)
        {
            return null;
        }

        // Either flat with the face of the overlay, or the normals of the surfaces under it
        var normals = new Vector3[positions.Count];

        for (var v = 0; v < positions.Count; v++)
        {
            if (overlay.UseBaseNormals)
            {
                normals[v] = polygons[sourceFaces[v / 3]].Normal;
                continue;
            }

            var (triangle, weights) = barycentrics[v];
            var corner = triangle * 3;

            normals[v] = MathUtils.SafeNormalize((weights.X * surfaces.Normals[corner]) + (weights.Y * surfaces.Normals[corner + 1]) + (weights.Z * surfaces.Normals[corner + 2]));
        }

        var streams = new List<PolygonMesh.BakedStream>
        {
            CreateStream("texcoord", 0, 2, [.. texCoords.SelectMany(static uv => (float[])[uv.X, uv.Y])]),
        };

        if (hasTint)
        {
            streams.Add(CreateStream("VertexPaintTintColor", 1, 4, Flatten(tints)));
        }

        if (hasBlend)
        {
            streams.Add(CreateStream("VertexPaintBlendParams", 1, 4, Flatten(blends)));
        }

        if (instanceData?.SampleVertexLighting(positions) is { } lighting)
        {
            streams.Add(CreateStream("PerVertexLighting", 1, 4, Flatten(lighting)));
        }

        return new PolygonMesh.BakedSubmesh
        {
            Material = material,
            Positions = [.. positions],
            Normals = normals,
            Tangents = ComputeTangents(positions, normals, texCoords),
            Streams = [.. streams],
            Indices = [.. Enumerable.Range(0, positions.Count)],
        };

        static float[] Flatten(IEnumerable<Vector4> values) => [.. values.SelectMany(static value => (float[])[value.X, value.Y, value.Z, value.W])];
    }

    private static PolygonMesh.BakedStream CreateStream(string semanticName, int vertexBufferLocation, int componentCount, float[] values) => new()
    {
        Stream = new PolygonMeshStream
        {
            Component = MeshComponentType.FaceVertex,
            Name = $"{semanticName}:0",
            StandardAttributeName = semanticName,
            SemanticName = semanticName,
            VertexBufferLocation = vertexBufferLocation,
        },
        ComponentCount = componentCount,
        Values = values,
    };

    // Cuts a face of the overlay out of every surface triangle it reaches and adds what is left as triangles. Each
    // corner keeps the surface triangle it lies on and its weights there.
    private static void ClipTriangles(Vector3[] face, Vector3 direction, float depth, bool cullBackFaces, float angle, List<Vector3> surfaces,
        List<Vector3> outPositions, List<Barycentric> outBarycentrics)
    {
        var counts = new List<int>();
        var corners = new List<Vector3>();
        var weights = new List<Barycentric>();

        // A surface turned further from the overlay than the angle is left alone
        var threshold = Math.Clamp(MathF.Cos(angle * (MathF.PI / 180f)) + 1e-5f, -1f, 0.99999f);

        // Nothing is left of a triangle outside the reach of the face, which with a depth is a closed prism. A unit of
        // room keeps the corners the planes count as on them.
        var reach = default(AABB?);

        if (depth > 0f)
        {
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);

            foreach (var corner in face)
            {
                var far = corner + (direction * depth);

                min = Vector3.Min(min, Vector3.Min(corner, far));
                max = Vector3.Max(max, Vector3.Max(corner, far));
            }

            reach = new AABB(min - Vector3.One, max + Vector3.One);
        }

        for (var t = 0; t + 2 < surfaces.Count; t += 3)
        {
            var a = surfaces[t];
            var b = surfaces[t + 1];
            var c = surfaces[t + 2];

            if (reach is { } bounds && !bounds.Intersects(new AABB(Vector3.Min(a, Vector3.Min(b, c)), Vector3.Max(a, Vector3.Max(b, c)))))
            {
                continue;
            }

            if (cullBackFaces && Vector3.Dot(-direction, MathUtils.SafeNormalize(Vector3.Cross(b - a, c - a))) <= threshold)
            {
                continue;
            }

            var triangle = t / 3;

            counts.Add(3);
            corners.AddRange(a, b, c);
            weights.AddRange(new Barycentric(triangle, Vector3.UnitX), new Barycentric(triangle, Vector3.UnitY), new Barycentric(triangle, Vector3.UnitZ));
        }

        if (counts.Count == 0)
        {
            return;
        }

        // Between the face and as far as it projects, then inside each of its edges
        if (depth > 0f)
        {
            var near = MathUtils.SafeNormalize(-direction);
            ClipAll(near, Vector3.Dot(near, face[0]));

            var far = MathUtils.SafeNormalize(direction);
            ClipAll(far, Vector3.Dot(far, face[0] + (direction * depth)));
        }

        for (var k = 0; k < face.Length; k++)
        {
            var previous = face[(k + face.Length - 1) % face.Length];
            var side = MathUtils.SafeNormalize(Vector3.Cross(direction, MathUtils.SafeNormalize(face[k] - previous)));

            ClipAll(side, Vector3.Dot(side, previous));
        }

        var start = 0;
        var triangles = new List<int>();

        foreach (var count in counts)
        {
            if (count >= 3)
            {
                var polygon = corners.GetRange(start, count).ToArray();

                triangles.Clear();
                MeshUtils.TriangulatePolygon(polygon, ComputePolygonNormal(polygon), 0, triangles);

                foreach (var index in triangles)
                {
                    outPositions.Add(polygon[index]);
                    outBarycentrics.Add(weights[start + index]);
                }
            }

            start += count;
        }

        void ClipAll(Vector3 planeNormal, float planeDistance)
        {
            var clippedCorners = new List<Vector3>(corners.Count);
            var clippedWeights = new List<Barycentric>(weights.Count);
            var first = 0;

            for (var i = 0; i < counts.Count; i++)
            {
                var count = counts[i];

                counts[i] = ClipPolygon(corners.GetRange(first, count), weights.GetRange(first, count), planeNormal, planeDistance, clippedCorners, clippedWeights);
                first += count;
            }

            corners = clippedCorners;
            weights = clippedWeights;
        }
    }

    // Keeps the part of a convex polygon behind a plane
    private static int ClipPolygon(List<Vector3> corners, List<Barycentric> weights, Vector3 planeNormal, float planeDistance,
        List<Vector3> outCorners, List<Barycentric> outWeights)
    {
        var count = corners.Count;

        if (count == 0)
        {
            return 0;
        }

        // Corners this near the plane, for the size of the polygon, count as on it
        var longestEdgeSquared = 0f;

        for (var i = 0; i < count; i++)
        {
            longestEdgeSquared = MathF.Max(longestEdgeSquared, Vector3.DistanceSquared(corners[i], corners[(i + count - 1) % count]));
        }

        var epsilon = Math.Clamp(longestEdgeSquared * 9.999999e-9f, 1.4210855e-14f, 1f);
        var added = 0;

        for (var i = 0; i < count; i++)
        {
            var previous = (i + count - 1) % count;
            var from = corners[previous];
            var to = corners[i];
            var fromDistance = Vector3.Dot(planeNormal, from) - planeDistance;
            var toDistance = Vector3.Dot(planeNormal, to) - planeDistance;

            if (fromDistance * fromDistance < epsilon)
            {
                fromDistance = 0f;
            }

            if (toDistance * toDistance < epsilon)
            {
                toDistance = 0f;
            }

            if (fromDistance <= 0f)
            {
                outCorners.Add(from);
                outWeights.Add(weights[previous]);
                added++;
            }

            if (fromDistance * toDistance < 0f)
            {
                var amount = -fromDistance / (toDistance - fromDistance);

                outCorners.Add(Vector3.Lerp(from, to, amount));
                outWeights.Add(weights[previous] with { Weights = Vector3.Lerp(weights[previous].Weights, weights[i].Weights, amount) });
                added++;
            }
        }

        return added;
    }

    // The texture coordinates of a face at a point on its plane
    private static Vector2 GetTexCoord(SourcePolygon polygon, Vector3 point)
    {
        var positions = polygon.Positions;
        var texCoords = polygon.TexCoords;

        if (texCoords.Length < positions.Length)
        {
            return Vector2.Zero;
        }

        if (positions.Length == 4 && polygon.IsConvex && InverseBilinear(positions, polygon.Normal, point) is var (s, t))
        {
            return Vector2.Lerp(Vector2.Lerp(texCoords[0], texCoords[3], t), Vector2.Lerp(texCoords[1], texCoords[2], t), s);
        }

        if (FindTriangle(polygon, point) is var (a, b, c, weights))
        {
            return (weights.X * texCoords[a]) + (weights.Y * texCoords[b]) + (weights.Z * texCoords[c]);
        }

        // Outside the face the texture carries on the way it runs at the corner that is the most open and has the
        // longest sides
        var best = 0;
        var bestScore = -1f;

        for (var i = 0; i < positions.Length; i++)
        {
            var toPrevious = positions[(i + positions.Length - 1) % positions.Length] - positions[i];
            var toNext = positions[(i + 1) % positions.Length] - positions[i];
            var score = (1f - MathF.Abs(Vector3.Dot(MathUtils.SafeNormalize(toPrevious), MathUtils.SafeNormalize(toNext)))) * toPrevious.LengthSquared() * toNext.LengthSquared();

            if (score > bestScore)
            {
                best = i;
                bestScore = score;
            }
        }

        var previous = (best + positions.Length - 1) % positions.Length;
        var next = (best + 1) % positions.Length;
        var (gradientU, gradientV) = GetTexCoordGradients(positions[best], positions[previous], positions[next], texCoords[best], texCoords[previous], texCoords[next]);
        var offset = point - positions[best];

        return texCoords[best] + new Vector2(Vector3.Dot(offset, gradientU), Vector3.Dot(offset, gradientV));
    }

    // The paint of a face at a point on its plane: from the triangle the point is in, or else along the nearest edge
    private static Vector4 GetStreamValue(SourcePolygon polygon, Vector3 point, Vector4[]? values, Vector4 fallback)
    {
        var positions = polygon.Positions;

        if (values == null || values.Length < positions.Length || positions.Length < 3)
        {
            return fallback;
        }

        if (FindTriangle(polygon, point) is var (a, b, c, weights))
        {
            return (weights.X * values[a]) + (weights.Y * values[b]) + (weights.Z * values[c]);
        }

        var bestDistance = float.MaxValue;
        var bestAmount = 0f;
        var bestFrom = 0;
        var bestTo = 1;

        for (var i = 0; i < positions.Length; i++)
        {
            var from = (i + positions.Length - 1) % positions.Length;
            var edge = positions[i] - positions[from];
            var lengthSquared = edge.LengthSquared();
            var amount = lengthSquared > 0f ? Math.Clamp(Vector3.Dot(point - positions[from], edge) / lengthSquared, 0f, 1f) : 0f;
            var distance = Vector3.DistanceSquared(point, positions[from] + (edge * amount));

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestAmount = amount;
                bestFrom = from;
                bestTo = i;
            }
        }

        return Vector4.Lerp(values[bestFrom], values[bestTo], bestAmount);
    }

    // The first triangle of a face whose corner weights for the point add up to one
    private static (int A, int B, int C, Vector3 Weights)? FindTriangle(SourcePolygon polygon, Vector3 point)
    {
        var positions = polygon.Positions;
        var triangles = polygon.Triangles;

        for (var t = 0; t + 2 < triangles.Length; t += 3)
        {
            var a = positions[triangles[t]];
            var b = positions[triangles[t + 1]];
            var c = positions[triangles[t + 2]];
            var area = Vector3.Cross(b - a, c - a).Length() * 0.5f;
            var inverseArea = area == 0f ? 0f : 1f / area;
            var weights = new Vector3(
                Vector3.Cross(b - point, c - point).Length(),
                Vector3.Cross(c - point, a - point).Length(),
                Vector3.Cross(b - point, a - point).Length()) * (0.5f * inverseArea);

            if (MathF.Abs(1f - (weights.X + weights.Y + weights.Z)) < WeightSumTolerance)
            {
                return (triangles[t], triangles[t + 1], triangles[t + 2], weights);
            }
        }

        return null;
    }

    // How far a point on the plane of a quad is along its first side and along its last side
    private static (float S, float T)? InverseBilinear(Vector3[] quad, Vector3 normal, Vector3 point)
    {
        const float Tolerance = 1e-5f;

        // Worked out in the two axes the quad faces least along
        var absolute = Vector3.Abs(normal);
        var dropped = absolute.X > absolute.Y ? (absolute.X > absolute.Z ? 0 : 2) : (absolute.Y > absolute.Z ? 1 : 2);

        var origin = Flatten(quad[0]);
        var alongS = Flatten(quad[1]) - origin;
        var alongT = Flatten(quad[3]) - origin;
        var twist = origin - Flatten(quad[1]) + Flatten(quad[2]) - Flatten(quad[3]);
        var offset = Flatten(point) - origin;

        // offset = s alongS + t alongT + s t twist, solved for t
        var a = Cross(alongT, twist);
        var b = Cross(alongT, alongS) - Cross(offset, twist);
        var c = -Cross(offset, alongS);
        Span<float> roots = stackalloc float[2];
        var rootCount = 0;

        if (MathF.Abs(a) < 1e-12f)
        {
            if (MathF.Abs(b) > 1e-12f)
            {
                roots[rootCount++] = -c / b;
            }
        }
        else
        {
            var discriminant = (b * b) - (4f * a * c);

            if (discriminant < 0f)
            {
                return null;
            }

            var root = MathF.Sqrt(discriminant);
            roots[rootCount++] = (-b - root) / (2f * a);
            roots[rootCount++] = (-b + root) / (2f * a);
        }

        foreach (var t in roots[..rootCount])
        {
            var denominator = alongS + (twist * t);
            var s = MathF.Abs(denominator.X) > MathF.Abs(denominator.Y)
                ? (offset.X - (alongT.X * t)) / denominator.X
                : (offset.Y - (alongT.Y * t)) / denominator.Y;

            if (s is >= -Tolerance and <= 1f + Tolerance && t is >= -Tolerance and <= 1f + Tolerance)
            {
                return (s, t);
            }
        }

        return null;

        Vector2 Flatten(Vector3 value) => dropped switch
        {
            0 => new Vector2(value.Y, value.Z),
            1 => new Vector2(value.X, value.Z),
            _ => new Vector2(value.X, value.Y),
        };

        static float Cross(Vector2 left, Vector2 right) => (left.X * right.Y) - (left.Y * right.X);
    }

    // How the texture coordinates change across a triangle, as a direction in the world for each of them
    private static (Vector3 U, Vector3 V) GetTexCoordGradients(Vector3 p0, Vector3 p1, Vector3 p2, Vector2 uv0, Vector2 uv1, Vector2 uv2)
    {
        var e1 = p1 - p0;
        var e2 = p2 - p0;
        var d1 = uv1 - uv0;
        var d2 = uv2 - uv0;

        if (d1.LengthSquared() < 1e-6f && d2.LengthSquared() < 1e-6f)
        {
            return (Vector3.UnitX, Vector3.UnitY);
        }

        // The sign of a determinant this small is not kept
        var determinant = (d2.Y * d1.X) - (d2.X * d1.Y);

        if (MathF.Abs(determinant) < 1e-6f)
        {
            determinant = 1e-6f;
        }

        var s = ((e1 * d2.Y) - (e2 * d1.Y)) / determinant;
        var t = ((e2 * d1.X) - (e1 * d2.X)) / determinant;
        var n = Vector3.Cross(s, t);

        var basis = new Matrix4x4(
            s.X, t.X, n.X, 0f,
            s.Y, t.Y, n.Y, 0f,
            s.Z, t.Z, n.Z, 0f,
            0f, 0f, 0f, 1f);

        if (!Matrix4x4.Invert(basis, out var inverse))
        {
            return (Vector3.UnitX, Vector3.UnitY);
        }

        return (new Vector3(inverse.M11, inverse.M12, inverse.M13), new Vector3(inverse.M21, inverse.M22, inverse.M23));
    }

    // The way the texture runs across each triangle, kept at a right angle to the normal of each of its corners
    private static Vector4[] ComputeTangents(List<Vector3> positions, Vector3[] normals, List<Vector2> texCoords)
    {
        var tangents = new Vector4[positions.Count];

        for (var t = 0; t + 2 < positions.Count; t += 3)
        {
            var (gradientU, gradientV) = GetTexCoordGradients(positions[t], positions[t + 1], positions[t + 2], texCoords[t], texCoords[t + 1], texCoords[t + 2]);

            for (var v = t; v < t + 3; v++)
            {
                var normal = normals[v];
                var u = gradientU;
                var w = gradientV;

                if (u.Length() < 1e-12f || w.Length() < 1e-12f)
                {
                    u = MathUtils.SafeNormalize(Vector3.Cross(MathF.Abs(normal.Z) < 0.999f ? Vector3.UnitZ : Vector3.UnitY, normal));
                    w = Vector3.Cross(normal, u);
                }

                var sign = Vector3.Dot(normal, Vector3.Cross(u, w)) < 0f ? -1f : 1f;
                var alongU = MathUtils.SafeNormalize(Vector3.Cross(Vector3.Cross(normal, u), normal));
                var alongV = sign > 0f ? MathUtils.SafeNormalize(Vector3.Cross(w, normal)) : MathUtils.SafeNormalize(Vector3.Cross(normal, w));

                tangents[v] = new Vector4(MathUtils.SafeNormalize(alongU + alongV), sign);
            }
        }

        return tangents;
    }
}
