using System.Buffers;
using System.Linq;
using ValveResourceFormat.IO.ContentFormats.DmxModel;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.IO;

/// <summary>
/// How a render mesh skinned to the bones a cloth proxy generated is written back: those bones leave the joint list, the
/// influences on them move to the nearest kept bone, and the weight they held becomes <c>cloth_enable</c> paint the
/// compiler re-binds the mesh to its proxy by.
/// </summary>
internal sealed class ClothRenderBinding
{
    /// <summary>The <c>cloth_enable</c> paint from which the compiler binds a render vertex to the proxy.</summary>
    private const float ClothEnableThreshold = 0.05f;

    /// <summary>The paint of a grown vertex, the smallest share the render mesh's binding gate allows.</summary>
    private const float ClothEnableGrowthPaint = 0.006f;

    private readonly string[]? boneNames;
    private readonly ClothProxySurface? surface;
    private readonly Dictionary<(int, int), List<int>> triangles = [];

    private ClothRenderBinding(int[] compaction, string[]? boneNames, ClothProxySurface? surface)
    {
        Compaction = compaction;
        this.boneNames = boneNames;
        this.surface = surface;
    }

    /// <summary>Gets where each skeleton bone lands in the emitted joint list, see <see cref="ClothBones.Compaction"/>.</summary>
    internal int[] Compaction { get; }

    /// <summary>Gets whether painted vertices are bound to a proxy surface, which needs their positions.</summary>
    internal bool BindsPositions => surface is not null;

    private bool IsProxyBone(int bone) => bone >= 0 && bone < Compaction.Length && Compaction[bone] < 0;

    /// <summary>
    /// The binding of a mesh skinned to <paramref name="skeleton"/>, its painted vertices bound to
    /// <paramref name="surface"/>, or null where no bone of the skeleton was generated from a cloth proxy.
    /// </summary>
    internal static ClothRenderBinding? Create(Skeleton? skeleton, ClothProxySurface? surface)
    {
        if (skeleton is null)
        {
            return null;
        }

        if (!Array.Exists(skeleton.Bones, ClothBones.IsGeneratedProxyBone))
        {
            return null;
        }

        var boneNames = surface is not null ? skeleton.Bones.Select(static bone => bone.Name).ToArray() : null;
        return new ClothRenderBinding(ClothBones.Compaction(skeleton), boneNames, surface);
    }

    /// <summary>Records a draw call's triangles on the vertex data element <paramref name="vertexBuffer"/>.</summary>
    internal void AddTriangles((int, int) vertexBuffer, int baseVertex, ReadOnlySpan<int> indices)
    {
        // Only growing paint over a proxy surface reads the triangles
        if (surface is null)
        {
            return;
        }

        var list = ClothReconstruction.GetOrAdd(triangles, vertexBuffer);

        foreach (var index in indices)
        {
            list.Add(baseVertex + index);
        }
    }

    /// <summary>The triangles recorded on <paramref name="vertexBuffer"/>, or null where none were.</summary>
    internal List<int>? TrianglesOf((int, int) vertexBuffer) => triangles.GetValueOrDefault(vertexBuffer);

    /// <summary>
    /// Writes the <c>cloth_enable</c> paint of a vertex data element, then drops the influences on the generated bones.
    /// </summary>
    internal void Apply(DmeVertexData vertexData, int[] indices, int boneWeightCount, List<int>? elementTriangles,
        int[]? blendIndices, float[]? blendWeights, Vector3[]? positions)
    {
        AddClothEnablePaint(vertexData, indices, boneWeightCount, elementTriangles, blendIndices, blendWeights, positions);
        DropProxyInfluences(boneWeightCount, blendIndices, blendWeights);
    }

    /// <summary>
    /// Zeroes the influences a vertex holds on the cloth proxy bones the compiler regenerates and renormalises the rest, in
    /// place in the blend weight stream.
    /// </summary>
    private void DropProxyInfluences(int boneWeightCount, int[]? blendIndices, float[]? blendWeights)
    {
        if (blendIndices is null || blendWeights is null || boneWeightCount <= 0)
        {
            return;
        }

        var vertexCount = Math.Min(blendIndices.Length, blendWeights.Length) / boneWeightCount;

        for (var vertex = 0; vertex < vertexCount; vertex++)
        {
            var first = vertex * boneWeightCount;
            var kept = 0f;

            for (var slot = first; slot < first + boneWeightCount; slot++)
            {
                var bone = blendIndices[slot];

                if (bone >= 0 && bone < Compaction.Length && Compaction[bone] < 0)
                {
                    blendWeights[slot] = 0f;
                }
                else
                {
                    kept += blendWeights[slot];
                }
            }

            if (kept <= 0f)
            {
                blendWeights[first] = 1f;
                continue;
            }

            for (var slot = first; slot < first + boneWeightCount; slot++)
            {
                blendWeights[slot] /= kept;
            }
        }
    }

    /// <summary>
    /// Writes the <c>cloth_enable</c> paint the compiler re-binds a render mesh to its proxy bones by, reconstructed as the
    /// weight each vertex holds on the generated cloth bones.
    /// </summary>
    private void AddClothEnablePaint(DmeVertexData vertexData, int[] indices, int boneWeightCount, List<int>? elementTriangles,
        int[]? blendIndices, float[]? blendWeights, Vector3[]? positions)
    {
        if (blendIndices is null || blendWeights is null || boneWeightCount <= 0
            || vertexData.VertexFormat.Contains("cloth_enable$0"))
        {
            return;
        }

        var vertexCount = Math.Min(indices.Length, blendIndices.Length / boneWeightCount);
        var paint = new float[indices.Length];

        for (var vertex = 0; vertex < vertexCount; vertex++)
        {
            var total = 0f;

            for (var slot = vertex * boneWeightCount; slot < (vertex + 1) * boneWeightCount; slot++)
            {
                var bone = blendIndices[slot];

                if (IsProxyBone(bone) && slot < blendWeights.Length)
                {
                    total += blendWeights[slot];
                }
            }

            paint[vertex] = MathUtils.Saturate(total);
        }

        if (!Array.Exists(paint, value => value > 0f))
        {
            return;
        }

        if (surface is not null && positions is not null && boneNames is not null)
        {
            var unbound = new HashSet<string>(surface.BoneOwningNodes, StringComparer.OrdinalIgnoreCase);
            var boundNodes = new List<string>();
            var certainNodes = new List<string>();
            vertexCount = Math.Min(vertexCount, positions.Length);

            for (var vertex = 0; vertex < vertexCount; vertex++)
            {
                if (paint[vertex] < ClothEnableGrowthPaint)
                {
                    continue;
                }

                surface.TryBind(positions[vertex], boundNodes, certainNodes);

                for (var slot = vertex * boneWeightCount; slot < (vertex + 1) * boneWeightCount; slot++)
                {
                    var bone = blendIndices[slot];

                    if (IsProxyBone(bone)
                        && slot < blendWeights.Length && blendWeights[slot] > 0f
                        && boundNodes.Contains(boneNames[bone], StringComparer.OrdinalIgnoreCase))
                    {
                        unbound.Remove(boneNames[bone]);
                    }
                }
            }

            GrowClothEnablePaint(paint, vertexCount, elementTriangles, positions, surface, unbound);
        }

        vertexData.AddIndexedStream("cloth_enable$0", paint, indices);
    }

    /// <summary>
    /// Grows the paint ring by ring across the mesh's triangles, in triangle order, until every one of the
    /// <paramref name="unbound"/> bone-owning proxy nodes is bound or nothing new is reached. A reached vertex is painted
    /// only when it binds one of them, and a vertex that may bind a node the skeleton has no bone for is neither painted
    /// nor grown through.
    /// </summary>
    private static void GrowClothEnablePaint(float[] paint, int vertexCount, List<int>? elementTriangles, Vector3[] positions,
        ClothProxySurface surface, HashSet<string> unbound)
    {
        if (elementTriangles is null)
        {
            return;
        }

        var triangleCount = elementTriangles.Count / 3;
        var trianglesOfVertex = new List<int>?[vertexCount];

        for (var triangle = 0; triangle < triangleCount; triangle++)
        {
            var a = elementTriangles[triangle * 3];
            var b = elementTriangles[triangle * 3 + 1];
            var c = elementTriangles[triangle * 3 + 2];

            if ((uint)a >= (uint)vertexCount || (uint)b >= (uint)vertexCount || (uint)c >= (uint)vertexCount)
            {
                continue;
            }

            (trianglesOfVertex[a] ??= []).Add(triangle);
            (trianglesOfVertex[b] ??= []).Add(triangle);
            (trianglesOfVertex[c] ??= []).Add(triangle);
        }

        var reached = new bool[vertexCount];
        var blocked = new bool[vertexCount];
        var visited = new bool[triangleCount];
        var newlyReached = new List<int>();
        var ring = new List<int>();
        var boundNodes = new List<string>();
        var certainNodes = new List<string>();

        for (var vertex = 0; vertex < vertexCount; vertex++)
        {
            reached[vertex] = paint[vertex] >= ClothEnableThreshold;

            if (reached[vertex])
            {
                newlyReached.Add(vertex);
            }
        }

        while (unbound.Count > 0)
        {
            ring.Clear();

            foreach (var vertex in newlyReached)
            {
                foreach (var triangle in trianglesOfVertex[vertex] ?? [])
                {
                    if (!visited[triangle])
                    {
                        visited[triangle] = true;
                        ring.Add(triangle);
                    }
                }
            }

            ring.Sort();
            newlyReached.Clear();

            foreach (var triangle in ring)
            {
                for (var corner = triangle * 3; corner < triangle * 3 + 3; corner++)
                {
                    var vertex = elementTriangles[corner];

                    if (reached[vertex] || blocked[vertex] || paint[vertex] > 0f)
                    {
                        continue;
                    }

                    if (!surface.TryBind(positions[vertex], boundNodes, certainNodes))
                    {
                        blocked[vertex] = true;
                        continue;
                    }

                    reached[vertex] = true;
                    newlyReached.Add(vertex);

                    if (boundNodes.Exists(unbound.Contains))
                    {
                        paint[vertex] = ClothEnableGrowthPaint;
                        unbound.ExceptWith(certainNodes);
                    }
                }
            }

            if (newlyReached.Count == 0)
            {
                return;
            }
        }
    }
}

/// <summary>
/// The faces of a model's cloth proxies, for predicting how the compiler binds a painted render vertex: the nearest face
/// binds all its corners from its interior, or the two ends of its nearest edge from outside it. Every rotation-free
/// generated node a binding reaches is given a <c>$cloth</c> bone.
/// </summary>
internal sealed class ClothProxySurface
{
    /// <summary>
    /// A proxy face: its corners, their node names, which of them own a generated skeleton bone and which would be given
    /// one the skeleton does not have.
    /// </summary>
    internal sealed record Face(Vector3[] Corners, string[] Names, bool[] OwnsBone, bool[] Boneless);

    /// <summary>
    /// How much farther than the nearest face another face may be and still count as bound, since the compiler's choice
    /// between near ties is not reproducible. A vertex this close to the surface counts every corner of such a face.
    /// </summary>
    private const float TieTolerance = 0.01f;

    /// <summary>The projection weight above which a bound corner is certain to be given its bone.</summary>
    private const float CertainWeight = 0.001f;

    /// <summary>
    /// Relative slack on the culling lower bound, well above float error, so a face is skipped only when its projection
    /// cannot come within <see cref="TieTolerance"/> of the nearest.
    /// </summary>
    private const float CullSlack = 1e-3f;

    private readonly Face[] faces;
    private readonly FaceGeometry[] geometry;

    internal ClothProxySurface(List<Face> faces)
    {
        this.faces = [.. faces];
        geometry = [.. this.faces.Select(static face => new FaceGeometry(face.Corners))];
        BoneOwningNodes = faces.SelectMany(static face => face.Names.Where((_, i) => face.OwnsBone[i]))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Gets the names of the proxy nodes that own a generated skeleton bone.</summary>
    public IReadOnlySet<string> BoneOwningNodes { get; }

    /// <summary>
    /// Projects a render vertex at <paramref name="position"/>. Fills <paramref name="boundNodes"/> with the bone-owning
    /// nodes it binds and <paramref name="certainNodes"/> with those it binds by a weight above
    /// <see cref="CertainWeight"/>. Returns false when it may bind a node the skeleton has no bone for.
    /// </summary>
    public bool TryBind(Vector3 position, List<string> boundNodes, List<string> certainNodes)
    {
        boundNodes.Clear();
        certainNodes.Clear();

        var projections = ArrayPool<(float Distance, int Bound, int Certain)>.Shared.Rent(faces.Length);
        var lowerBounds = ArrayPool<float>.Shared.Rent(faces.Length);

        try
        {
            return TryBind(position, boundNodes, certainNodes, projections, lowerBounds);
        }
        finally
        {
            ArrayPool<float>.Shared.Return(lowerBounds);
            ArrayPool<(float Distance, int Bound, int Certain)>.Shared.Return(projections);
        }
    }

    private bool TryBind(Vector3 position, List<string> boundNodes, List<string> certainNodes,
        (float Distance, int Bound, int Certain)[] projections, float[] lowerBounds)
    {
        var seed = 0;

        for (var f = 0; f < faces.Length; f++)
        {
            lowerBounds[f] = geometry[f].LowerBound(position);

            if (lowerBounds[f] < lowerBounds[seed])
            {
                seed = f;
            }
        }

        var cutoff = faces.Length > 0 ? Project(position, faces[seed].Corners, geometry[seed]).Distance + TieTolerance : 0f;
        var best = float.MaxValue;
        Face? bestFace = null;
        var bestBound = 0;
        var bestCertain = 0;

        for (var f = 0; f < faces.Length; f++)
        {
            if (f != seed && lowerBounds[f] > cutoff)
            {
                projections[f] = (float.MaxValue, 0, 0);
                continue;
            }

            var projection = projections[f] = Project(position, faces[f].Corners, geometry[f]);

            if (projection.Distance < best)
            {
                (best, bestFace, bestBound, bestCertain) = (projection.Distance, faces[f], projection.Bound, projection.Certain);
            }
        }

        if (bestFace is null)
        {
            return true;
        }

        for (var i = 0; i < bestFace.Names.Length; i++)
        {
            if (bestFace.OwnsBone[i] && (bestBound & (1 << i)) != 0)
            {
                boundNodes.Add(bestFace.Names[i]);
            }

            if (bestFace.OwnsBone[i] && (bestCertain & (1 << i)) != 0)
            {
                certainNodes.Add(bestFace.Names[i]);
            }
        }

        for (var f = 0; f < faces.Length; f++)
        {
            var face = faces[f];
            var (distance, bound, _) = projections[f];

            if (distance > best + TieTolerance)
            {
                continue;
            }

            if (best <= TieTolerance)
            {
                bound = (1 << face.Corners.Length) - 1;
            }

            for (var i = 0; i < face.Corners.Length; i++)
            {
                if ((bound & (1 << i)) != 0 && face.Boneless[i])
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// The distance from <paramref name="position"/> to a face, the bit mask of the corners it binds and the mask of those
    /// bound by a weight above <see cref="CertainWeight"/>.
    /// </summary>
    private static (float Distance, int Bound, int Certain) Project(Vector3 position, Vector3[] corners, FaceGeometry geometry)
    {
        if (geometry.IsDegenerate)
        {
            return (float.MaxValue, 0, 0);
        }

        var count = corners.Length;
        Span<float> side = stackalloc float[4];
        var inside = true;

        for (var i = 0; i < count; i++)
        {
            side[i] = Vector3.Dot(position - corners[i], geometry.Inward[i]);
            inside &= side[i] > 0f;
        }

        if (inside)
        {
            Span<float> weights = stackalloc float[4];

            if (count == 4)
            {
                var even = side[0] / (side[0] + side[2]);
                var odd = side[1] / (side[1] + side[3]);
                weights[0] = odd * (1 - even);
                weights[1] = (1 - odd) * (1 - even);
                weights[2] = (1 - odd) * even;
                weights[3] = odd * even;
            }
            else
            {
                for (var i = 0; i < 3; i++)
                {
                    weights[(i + 2) % 3] = MathUtils.Saturate(side[i] / geometry.Opposing[i]);
                }
            }

            var contact = Vector3.Zero;
            var certain = 0;

            for (var i = 0; i < count; i++)
            {
                contact += corners[i] * weights[i];
                certain |= weights[i] > CertainWeight ? 1 << i : 0;
            }

            return (Vector3.Distance(position, contact), (1 << count) - 1, certain);
        }

        var best = float.MaxValue;
        var bestBound = 0;

        for (var i = 0; i < count; i++)
        {
            var length = geometry.EdgeLength[i];

            if (length <= 0f)
            {
                continue;
            }

            var start = corners[i];
            var direction = geometry.EdgeDirection[i];
            var along = Math.Clamp(Vector3.Dot(position - start, direction), 0f, length);
            var distance = Vector3.Distance(position, start + direction * along);

            if (distance >= best)
            {
                continue;
            }

            best = distance;
            var w = along / length;
            bestBound = (w < 0.999f ? 1 << i : 0) | (w > 0.001f ? 1 << ((i + 1) % count) : 0);
        }

        return (best, bestBound, bestBound);
    }

    /// <summary>
    /// The parts of a face's projection that depend only on its corners, and a bounding sphere whose
    /// <see cref="LowerBound"/> no projection onto the face can fall below.
    /// </summary>
    private sealed class FaceGeometry
    {
        public FaceGeometry(Vector3[] corners)
        {
            var count = corners.Length;
            var normal = count == 4
                ? Vector3.Cross(corners[2] - corners[0], corners[3] - corners[1])
                : MathUtils.TriangleCross(corners[0], corners[1], corners[2]);

            IsDegenerate = normal.LengthSquared() < 1e-10f;
            normal = Vector3.Normalize(normal);
            Inward = new Vector3[count];
            Opposing = new float[count];
            EdgeDirection = new Vector3[count];
            EdgeLength = new float[count];

            for (var i = 0; i < count; i++)
            {
                var edge = corners[(i + 1) % count] - corners[i];
                Inward[i] = Vector3.Normalize(Vector3.Cross(normal, edge));
                Opposing[i] = Vector3.Dot(corners[(i + 2) % count] - corners[i], Inward[i]);
                EdgeLength[i] = edge.Length();

                if (EdgeLength[i] > 0f)
                {
                    EdgeDirection[i] = edge / EdgeLength[i];
                }
            }

            var center = Vector3.Zero;

            foreach (var corner in corners)
            {
                center += corner;
            }

            Center = center / count;

            foreach (var corner in corners)
            {
                Radius = MathF.Max(Radius, Vector3.Distance(corner, Center));
            }
        }

        public bool IsDegenerate { get; }

        public Vector3[] Inward { get; }

        public float[] Opposing { get; }

        public Vector3[] EdgeDirection { get; }

        public float[] EdgeLength { get; }

        public Vector3 Center { get; }

        public float Radius { get; }

        /// <summary>
        /// A distance no projection of <paramref name="position"/> onto the face is below: every contact point lies in the
        /// corners' convex hull, inside the bounding sphere. Lowered by <see cref="CullSlack"/>; NaN when the position is.
        /// </summary>
        public float LowerBound(Vector3 position)
        {
            var toCenter = Vector3.Distance(position, Center);
            return toCenter - Radius - (CullSlack * (toCenter + Radius + 1f));
        }
    }
}
