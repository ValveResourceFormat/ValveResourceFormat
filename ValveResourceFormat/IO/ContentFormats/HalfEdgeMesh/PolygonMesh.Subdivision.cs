namespace ValveResourceFormat.IO.ContentFormats.HalfEdgeMesh;

public sealed partial class PolygonMesh
{
    // The finest a face can be subdivided, its patches then being 17 points wide
    private const int MaxSubdivisionLevel = 5;

    /// <summary>
    /// Gets the level each face corner is subdivided to, 0 for the corners of a face that is not subdivided.
    /// </summary>
    public HalfEdgeData<int> SubdivisionLevels { get; }

    /// <summary>
    /// Gets where the patch of each face corner starts in the values of the <see cref="MeshComponentType.Subdivision"/> streams.
    /// </summary>
    public HalfEdgeData<int> SubdivisionPatchStarts { get; }

    // The displacements of the patch points, patch sizes by face and patches by the half edge of their corner,
    // all kept while the mesh is triangulated
    private Vector3[]? subdivisionDisplacements;
    private int[]? facePatchSizeCache;
    private Patch?[]? subdivisionPatches;

    // The grid of points a subdivided face has around one of its corners, stored row by row. It starts at
    // the corner and ends at the centre of the face, with its columns running towards the next corner of
    // the face and its rows towards the previous one.
    private sealed class Patch(int size)
    {
        public int Size { get; } = size;

        public Vector3[] Positions { get; } = new Vector3[size * size];

        // The way the patch faces at each point going by its own points alone
        public Vector3[] Normals { get; } = new Vector3[size * size];
    }

    /// <summary>
    /// Gets the number of points along a side of the patch of a face corner that is subdivided to a level, 0 for a
    /// level that is no subdivision.
    /// </summary>
    public static int GetPatchSize(int level)
        => level is > 0 and <= MaxSubdivisionLevel ? (1 << (level - 1)) + 1 : 0;

    // The values of a subdivision stream: one patch per subdivided face corner, an empty stream being the same as none
    private T[]? FindSubdivisionValues<T>(string semanticName)
    {
        foreach (var stream in StoredStreams)
        {
            if (stream.Component == MeshComponentType.Subdivision && stream.SemanticIndex == 0 && stream.Values is T[] { Length: > 0 } values
                && stream.SemanticName.Equals(semanticName, StringComparison.OrdinalIgnoreCase))
            {
                return values;
            }
        }

        return null;
    }

    private static T Get<T>(T[]? stream, int index) where T : struct
        => stream != null && (uint)index < (uint)stream.Length ? stream[index] : default;

    // Points along a side of the patches of a face, 0 for a face that is not subdivided. A face is
    // subdivided to the level of its first corner, which InitFaceSubdivisionLevels brings the others to.
    private int GetFacePatchSize(FaceHandle hFace)
    {
        if (!hFace.IsValid)
        {
            return 0;
        }

        if (facePatchSizeCache is { } cache)
        {
            return cache[hFace.Index];
        }

        return GetPatchSize(SubdivisionLevels[hFace.Edge]);
    }

    /// <summary>
    /// Brings every corner of a face to the level its first corner is subdivided to. The patch of a corner that
    /// was at another level is resampled: thinned out to a coarser level, interpolated to a finer one.
    /// </summary>
    public void InitFaceSubdivisionLevels()
    {
        var levels = new int[Topology.HalfEdgeCount];
        var changed = false;

        foreach (var hFaceVertex in Topology.HalfEdgeHandles)
        {
            levels[hFaceVertex.Index] = SubdivisionLevels[hFaceVertex];
        }

        foreach (var hFace in Topology.FaceHandles)
        {
            var hFirstFaceVertex = hFace.Edge;
            var level = levels[hFirstFaceVertex.Index];

            if (level is < 0 or > MaxSubdivisionLevel)
            {
                continue;
            }

            for (var hFaceVertex = hFirstFaceVertex.NextEdge; hFaceVertex != hFirstFaceVertex; hFaceVertex = hFaceVertex.NextEdge)
            {
                changed |= levels[hFaceVertex.Index] != level;
                levels[hFaceVertex.Index] = level;
            }
        }

        if (!changed)
        {
            return;
        }

        var starts = new int[levels.Length];
        var total = 0;

        foreach (var hFaceVertex in Topology.HalfEdgeHandles)
        {
            var size = GetPatchSize(levels[hFaceVertex.Index]);

            starts[hFaceVertex.Index] = total;
            total += size * size;
        }

        foreach (var stream in StoredStreams)
        {
            if (stream.Component != MeshComponentType.Subdivision)
            {
                continue;
            }

            stream.Values = stream.Values switch
            {
                float[] values => ResamplePatches(values, levels, starts, total, float.Lerp),
                Vector2[] values => ResamplePatches(values, levels, starts, total, Vector2.Lerp),
                Vector3[] values => ResamplePatches(values, levels, starts, total, Vector3.Lerp),
                Vector4[] values => ResamplePatches(values, levels, starts, total, Vector4.Lerp),
                var values => values,
            };
        }

        foreach (var hFaceVertex in Topology.HalfEdgeHandles)
        {
            SubdivisionLevels[hFaceVertex] = levels[hFaceVertex.Index];
            SubdivisionPatchStarts[hFaceVertex] = starts[hFaceVertex.Index];
        }
    }

    private T[] ResamplePatches<T>(T[] values, int[] levels, int[] starts, int total, Func<T, T, float, T> lerp) where T : struct
    {
        var resampled = new T[total];

        foreach (var hFaceVertex in Topology.HalfEdgeHandles)
        {
            var oldSize = GetPatchSize(SubdivisionLevels[hFaceVertex]);
            var newSize = GetPatchSize(levels[hFaceVertex.Index]);
            var oldStart = SubdivisionPatchStarts[hFaceVertex];

            // TODO: A corner that had no patch gets an empty one, where it should be filled in from the face
            if (oldSize == 0 || newSize == 0 || oldStart < 0 || oldStart + (oldSize * oldSize) > values.Length)
            {
                continue;
            }

            var source = values.AsSpan(oldStart, oldSize * oldSize);
            var target = resampled.AsSpan(starts[hFaceVertex.Index], newSize * newSize);

            if (oldSize == newSize)
            {
                source.CopyTo(target);
            }
            else if (oldSize > newSize)
            {
                var step = (oldSize - 1) / (newSize - 1);

                for (var row = 0; row < newSize; row++)
                {
                    for (var column = 0; column < newSize; column++)
                    {
                        target[(row * newSize) + column] = source[(row * step * oldSize) + (column * step)];
                    }
                }
            }
            else
            {
                var ratio = (newSize - 1) / (oldSize - 1);

                for (var row = 0; row < oldSize - 1; row++)
                {
                    for (var column = 0; column < oldSize - 1; column++)
                    {
                        var near = (row * oldSize) + column;
                        var far = near + oldSize;

                        for (var i = 0; i <= ratio; i++)
                        {
                            var left = lerp(source[near], source[far], (float)i / ratio);
                            var right = lerp(source[near + 1], source[far + 1], (float)i / ratio);

                            for (var j = 0; j <= ratio; j++)
                            {
                                target[(((row * ratio) + i) * newSize) + (column * ratio) + j] = lerp(left, right, (float)j / ratio);
                            }
                        }
                    }
                }
            }
        }

        return resampled;
    }

    // How many pieces a face draws each of its stretches of an edge in, of which the edge has the given
    // number. More than one when the face on the other side divides the edge finer, so that the two meet
    // point for point.
    private int GetEdgeSplit(HalfEdgeHandle hEdge, int stretches)
    {
        var neighbourSize = GetFacePatchSize(hEdge.OppositeEdge.Face);

        return neighbourSize == 0 ? 1 : Math.Max(1, 2 * (neighbourSize - 1) / stretches);
    }

    private Patch? GetPatch(HalfEdgeHandle hFaceVertex)
    {
        var size = GetFacePatchSize(hFaceVertex.Face);

        if (size == 0)
        {
            return null;
        }

        subdivisionPatches ??= new Patch?[Topology.HalfEdgeCount];

        return subdivisionPatches[hFaceVertex.Index] ??= BuildPatch(hFaceVertex, size);
    }

    private Patch BuildPatch(HalfEdgeHandle hFaceVertex, int size)
    {
        var hFace = hFaceVertex.Face;
        var patch = new Patch(size);
        var cells = size - 1;

        var centre = Vector3.Zero;
        var count = 0;
        var hCurrent = hFace.Edge;

        do
        {
            centre += Positions[hCurrent.Vertex];
            count++;
            hCurrent = hCurrent.NextEdge;
        }
        while (hCurrent != hFace.Edge);

        centre /= count;

        // Before it is displaced the patch is the flat quad between the corner, the middles of the two
        // edges that meet at it, and the centre of the face
        var origin = Positions[hFaceVertex.Vertex];
        var towardsNext = (origin + Positions[hFaceVertex.NextEdge.Vertex]) * 0.5f;
        var towardsPrevious = (origin + Positions[HalfEdgeMesh.FindPreviousEdgeInFaceLoop(hFaceVertex).Vertex]) * 0.5f;

        // Displacement is stored in the frame of the face: along the line from the middle of its first
        // edge to its centre, across that line, and out along its normal
        ComputeFaceNormal(hFace, out var normal);

        var firstEdgeMiddle = (Positions[hFace.Edge.Vertex] + Positions[hFace.Edge.NextEdge.Vertex]) * 0.5f;
        var across = MathUtils.SafeNormalize(Vector3.Cross(normal, centre - firstEdgeMiddle));
        var along = MathUtils.SafeNormalize(Vector3.Cross(across, normal));

        var firstPoint = SubdivisionPatchStarts[hFaceVertex];

        for (var index = 0; index < patch.Positions.Length; index++)
        {
            var u = (index % size) / (float)cells;
            var v = (index / size) / (float)cells;
            var displacement = Get(subdivisionDisplacements, firstPoint + index);

            patch.Positions[index] = Vector3.Lerp(Vector3.Lerp(origin, towardsPrevious, v), Vector3.Lerp(towardsNext, centre, v), u)
                + (along * displacement.X) + (across * displacement.Y) + (normal * displacement.Z);
        }

        ComputePatchNormals(patch);

        return patch;
    }

    private static void ComputePatchNormals(Patch patch)
    {
        var size = patch.Size;
        var last = size - 1;
        var points = patch.Positions;

        for (var row = 0; row < size; row++)
        {
            for (var column = 0; column < size; column++)
            {
                var index = (row * size) + column;
                var point = points[index];
                var normal = Vector3.Zero;

                if ((row == 0 || row == last) && (column == 0 || column == last))
                {
                    // A corner of the patch faces the way the triangle it makes with the next point
                    // around the outline and the point diagonally inside it does
                    var rowStep = row == 0 ? size : -size;
                    var columnStep = column == 0 ? 1 : -1;
                    var outline = (row == 0) == (column == 0) ? columnStep : rowStep;

                    normal = Vector3.Cross(points[index + outline] - point, points[index + rowStep + columnStep] - point);
                }
                else
                {
                    // Any other point faces the average way of the cells around it, each going by the
                    // two sides that meet at the point
                    if (column < last)
                    {
                        normal += GetCellFacings(points[index + 1] - point);
                    }

                    if (column > 0)
                    {
                        normal += GetCellFacings(point - points[index - 1]);
                    }
                }

                patch.Normals[index] = MathUtils.SafeNormalize(normal);

                Vector3 GetCellFacings(Vector3 alongRow)
                {
                    var facings = Vector3.Zero;

                    if (row < last)
                    {
                        facings += MathUtils.SafeNormalize(Vector3.Cross(alongRow, points[index + size] - point));
                    }

                    if (row > 0)
                    {
                        facings += MathUtils.SafeNormalize(Vector3.Cross(alongRow, point - points[index - size]));
                    }

                    return facings;
                }
            }
        }
    }

    // The way the face of a corner faces at it, which for a subdivided face is the way the displaced patch
    // of the corner starts out
    private Vector3 GetSubdividedFacing(HalfEdgeHandle hFaceVertex)
    {
        if (GetPatch(hFaceVertex) is { } patch)
        {
            return patch.Normals[0];
        }

        ComputeFaceNormal(hFaceVertex.Face, out var normal);

        return normal;
    }

    // Whether the edge between a face vertex and the next one around their vertex shades smooth, going by
    // the displaced surfaces of subdivided faces
    private bool IsSubdividedEdgeSmooth(HalfEdgeHandle hFaceVertex, HalfEdgeHandle hNextFaceVertex)
        => GetEdgeSmoothingOverride(hNextFaceVertex)
            ?? Vector3.Dot(GetSubdividedFacing(hFaceVertex), GetSubdividedFacing(hNextFaceVertex)) > smoothingThreshold + SmoothingEpsilon;

    // The normal at the corner a patch starts at: the average of the way the patches around the vertex
    // start out, as far as they are reached without crossing a hard edge
    private Vector3 ComputeSubdividedFaceVertexNormal(HalfEdgeHandle hTargetFaceVertex)
    {
        var facing = GetSubdividedFacing(hTargetFaceVertex);

        if (!FindSmoothingFan(hTargetFaceVertex, subdivided: true, out var hFirstFaceVertex, out var hLastFaceVertex))
        {
            return facing;
        }

        var normal = Vector3.Zero;
        var hCurrentFaceVertex = hFirstFaceVertex;

        do
        {
            // Next to a face that is not subdivided the corner shades the way it would without subdivision
            if (GetPatch(hCurrentFaceVertex) == null)
            {
                return GetFaceVertexNormal(hTargetFaceVertex);
            }

            normal += GetSubdividedFacing(hCurrentFaceVertex);
            hCurrentFaceVertex = GetNextFaceVertexConnectedToVertex(hCurrentFaceVertex);
        }
        while (hCurrentFaceVertex != hLastFaceVertex && hCurrentFaceVertex.IsValid);

        return MathUtils.SafeNormalize(normal, facing);
    }

    // The normals a patch is drawn with. Where it meets other patches its own facing is averaged with
    // theirs, across the edges of the face only as far as those edges shade smooth.
    private Vector3[] ComputeSubdivisionNormals(HalfEdgeHandle hFaceVertex, Patch patch)
    {
        var size = patch.Size;
        var last = size - 1;
        Vector3[] blended = [.. patch.Normals];

        var nextPatch = GetPatch(hFaceVertex.NextEdge)!;
        var previousPatch = GetPatch(HalfEdgeMesh.FindPreviousEdgeInFaceLoop(hFaceVertex))!;

        // Its last row is the last column of the patch of the previous corner, and its last column the
        // last row of the patch of the next one
        for (var i = 0; i < last; i++)
        {
            var onLastRow = (last * size) + i;
            var onLastColumn = (i * size) + last;

            blended[onLastRow] = MathUtils.SafeNormalize(blended[onLastRow] + previousPatch.Normals[onLastColumn]);
            blended[onLastColumn] = MathUtils.SafeNormalize(blended[onLastColumn] + nextPatch.Normals[onLastRow]);
        }

        var centre = Vector3.Zero;
        var hCurrent = hFaceVertex;

        do
        {
            centre += GetPatch(hCurrent)!.Normals[^1];
            hCurrent = hCurrent.NextEdge;
        }
        while (hCurrent != hFaceVertex);

        blended[^1] = MathUtils.SafeNormalize(centre);
        blended[0] = ComputeSubdividedFaceVertexNormal(hFaceVertex);

        BlendAcrossEdge(blended, patch, hFaceVertex, towardsNext: true);
        BlendAcrossEdge(blended, patch, hFaceVertex, towardsNext: false);

        return blended;
    }

    // Blends the side of a patch that runs along an edge of its face, from the point after the corner up
    // to the middle of the edge, with the face on the other side of that edge
    private void BlendAcrossEdge(Vector3[] blended, Patch patch, HalfEdgeHandle hFaceVertex, bool towardsNext)
    {
        var hEdge = towardsNext ? hFaceVertex.NextEdge : hFaceVertex;
        var hOpposite = hEdge.OppositeEdge;

        if (!hOpposite.Face.IsValid || GetEdgeSmoothing(hEdge) == EdgeSmoothMode.Hard)
        {
            return;
        }

        // The corners the other face has at the same vertex and at the far end of the edge
        var hBefore = HalfEdgeMesh.FindPreviousEdgeInFaceLoop(hOpposite);

        if (!hBefore.IsValid)
        {
            return;
        }

        var hNeighbour = towardsNext ? hOpposite : hBefore;
        var hFar = towardsNext ? hBefore : hOpposite;

        var isSoft = GetEdgeSmoothing(hEdge) == EdgeSmoothMode.Soft;
        var step = towardsNext ? 1 : patch.Size;
        var cells = patch.Size - 1;

        if (GetPatch(hNeighbour) is not { } neighbourPatch || GetPatch(hFar) is not { } farPatch)
        {
            // A face that is not subdivided has the side take the normals it shades with along the edge
            var from = GetFaceVertexNormal(hNeighbour);
            var to = GetFaceVertexNormal(hFar);

            for (var i = 1; i <= cells; i++)
            {
                var normal = MathUtils.SafeNormalize(Vector3.Lerp(from, to, i * 0.5f / cells));

                if (isSoft || Vector3.Dot(blended[i * step], normal) > smoothingThreshold + SmoothingEpsilon)
                {
                    blended[i * step] = normal;
                }
            }

            return;
        }

        // The patch of the other face at the same vertex runs along the edge the other way around
        var neighbourStep = towardsNext ? neighbourPatch.Size : 1;
        var neighbourCells = neighbourPatch.Size - 1;
        var farMiddle = towardsNext ? neighbourCells : neighbourCells * farPatch.Size;

        Blend(cells, MathUtils.SafeNormalize(farPatch.Normals[farMiddle] + neighbourPatch.Normals[neighbourCells * neighbourStep]));

        for (var i = 1; i < cells; i++)
        {
            // A coarser patch has no point for every one of this side
            var position = i * neighbourCells / (float)cells;
            var index = (int)position;
            var normal = neighbourPatch.Normals[index * neighbourStep];

            if (index < position)
            {
                normal = MathUtils.SafeNormalize(Vector3.Lerp(normal, neighbourPatch.Normals[(index + 1) * neighbourStep], position - index));
            }

            Blend(i, normal);
        }

        void Blend(int point, Vector3 normal)
        {
            if (isSoft || Vector3.Dot(blended[point * step], normal) > smoothingThreshold + SmoothingEpsilon)
            {
                blended[point * step] = MathUtils.SafeNormalize(blended[point * step] + normal);
            }
        }
    }

    // The value at a point of a patch of something only the corners of the face store: the corner's own at
    // the start of the patch, halfway to those of the corners after and before it along the two sides, and
    // the average of the face at the end
    private static Vector4 Spread(Vector4 corner, Vector4 next, Vector4 previous, Vector4 centre, float u, float v)
    {
        var towardsNext = Vector4.Lerp(corner, next, 0.5f);
        var towardsPrevious = Vector4.Lerp(corner, previous, 0.5f);

        return Vector4.Lerp(Vector4.Lerp(corner, towardsPrevious, v), Vector4.Lerp(towardsNext, centre, v), u);
    }

    private void TriangulateSubdividedFaceInto(FaceHandle hFace, int size, List<BakedVertex> vertices, List<int> indices)
    {
        var cells = size - 1;

        // What a patch does not store per point is spread over it from the corners of the face, reaching
        // their average at its centre
        var centreTangent = Vector3.Zero;
        Span<Vector4> centres = stackalloc Vector4[streamSources.Length];
        var count = 0;
        var hFaceVertex = hFace.Edge;

        do
        {
            centreTangent += Tangents[hFaceVertex].AsVector3();

            for (var i = 0; i < streamSources.Length; i++)
            {
                if (streamSources[i].IsSpread)
                {
                    centres[i] += streamSources[i].Read(hFaceVertex.Index);
                }
            }

            count++;
            hFaceVertex = hFaceVertex.NextEdge;
        }
        while (hFaceVertex != hFace.Edge);

        centreTangent = MathUtils.SafeNormalize(centreTangent);

        for (var i = 0; i < centres.Length; i++)
        {
            centres[i] /= count;
        }

        do
        {
            var hNext = hFaceVertex.NextEdge;
            var hPrevious = HalfEdgeMesh.FindPreviousEdgeInFaceLoop(hFaceVertex);
            var firstPoint = SubdivisionPatchStarts[hFaceVertex];
            var baseVertex = vertices.Count;

            var patch = GetPatch(hFaceVertex)!;
            var patchNormals = ComputeSubdivisionNormals(hFaceVertex, patch);

            var tangent = Tangents[hFaceVertex];
            var nextTangent = MathUtils.SafeNormalize(tangent.AsVector3() + Tangents[hNext].AsVector3());
            var previousTangent = MathUtils.SafeNormalize(tangent.AsVector3() + Tangents[hPrevious].AsVector3());

            for (var index = 0; index < patchNormals.Length; index++)
            {
                var u = (index % size) / (float)cells;
                var v = (index / size) / (float)cells;
                var normal = patchNormals[index];

                var spreadTangent = Vector3.Lerp(
                    MathUtils.SafeNormalize(Vector3.Lerp(tangent.AsVector3(), previousTangent, v)),
                    MathUtils.SafeNormalize(Vector3.Lerp(nextTangent, centreTangent, v)),
                    u);

                // Kept at a right angle to the normal of the point
                var pointTangent = Vector3.Cross(Vector3.Cross(normal, spreadTangent), normal);

                if (pointTangent.LengthSquared() < MissingLengthSquared)
                {
                    BuildBasis(normal, out pointTangent, out _);
                }

                var extras = bakedExtras.Count;

                // A stream has its own values for the points when it is bound to a subdivision stream. Texture
                // coordinates and paint that are not bound are spread from the corners, anything else is left empty.
                for (var i = 0; i < streamSources.Length; i++)
                {
                    var source = streamSources[i];

                    var value = source.ReadPatch != null
                        ? source.ReadPatch(firstPoint + index)
                        : source.IsSpread
                            ? Spread(source.Read(hFaceVertex.Index), source.Read(hNext.Index), source.Read(hPrevious.Index), centres[i], u, v)
                            : Vector4.Zero;

                    AddExtra(source, value);
                }

                vertices.Add(new BakedVertex
                {
                    Position = patch.Positions[index],
                    Normal = normal,
                    Tangent = new Vector4(Vector3.Normalize(pointTangent), tangent.W == 0f ? 1f : tangent.W),
                    Extras = extras,
                });
            }

            // The first row runs along the edge towards the next corner of the face, the first column
            // along the one towards the previous corner
            var rowSplit = GetEdgeSplit(hNext, 2 * cells);
            var columnSplit = GetEdgeSplit(hFaceVertex, 2 * cells);
            var rowExtras = AddSplitPoints(vertices, baseVertex, 1, cells, rowSplit);
            var columnExtras = AddSplitPoints(vertices, baseVertex, size, cells, columnSplit);

            AddPatchTriangles(indices, baseVertex, size, rowSplit, rowExtras, columnSplit, columnExtras);

            hFaceVertex = hNext;
        }
        while (hFaceVertex != hFace.Edge);
    }

    // Adds the points that split every cell along a side of a patch into pieces, when the side has to meet
    // a finer one. Returns the first of them.
    private int AddSplitPoints(List<BakedVertex> vertices, int baseVertex, int step, int cells, int split)
    {
        var first = vertices.Count;

        for (var cell = 0; cell < cells; cell++)
        {
            var from = baseVertex + (cell * step);

            for (var i = 1; i < split; i++)
            {
                vertices.Add(LerpVertex(vertices[from], vertices[from + step], i / (float)split));
            }
        }

        return first;
    }

    // Cells inside a patch are two triangles, split from their point nearest the corner. Along the two
    // outer sides, where a cell may have been split into pieces, the pieces are instead fanned out from the
    // points one step inside: all of them from the same point in the cell at the corner, and half from
    // either end of the inner side in the other cells.
    private static void AddPatchTriangles(List<int> indices, int baseVertex, int size, int rowSplit, int rowExtras, int columnSplit, int columnExtras)
    {
        var cells = size - 1;
        var inner = baseVertex + size + 1;

        for (var i = 0; i < rowSplit; i++)
        {
            AddTriangle(OnRow(i), OnRow(i + 1), inner);
        }

        for (var i = 0; i < columnSplit; i++)
        {
            AddTriangle(OnColumn(i), inner, OnColumn(i + 1));
        }

        for (var cell = 1; cell < cells; cell++)
        {
            var nearInRow = inner + cell - 1;
            var nearInColumn = inner + ((cell - 1) * size);

            for (var i = 0; i < rowSplit; i++)
            {
                AddTriangle(OnRow((cell * rowSplit) + i), OnRow((cell * rowSplit) + i + 1), i < rowSplit / 2 ? nearInRow : nearInRow + 1);
            }

            AddTriangle(OnRow((cell * rowSplit) + (rowSplit / 2)), nearInRow + 1, nearInRow);

            for (var i = 0; i < columnSplit; i++)
            {
                AddTriangle(OnColumn((cell * columnSplit) + i), i < columnSplit / 2 ? nearInColumn : nearInColumn + size, OnColumn((cell * columnSplit) + i + 1));
            }

            AddTriangle(OnColumn((cell * columnSplit) + (columnSplit / 2)), nearInColumn, nearInColumn + size);
        }

        for (var row = 1; row < cells; row++)
        {
            for (var column = 1; column < cells; column++)
            {
                var point = baseVertex + (row * size) + column;

                AddTriangle(point, point + 1, point + size + 1);
                AddTriangle(point, point + size + 1, point + size);
            }
        }

        // Points along the first row and the first column, counting the ones that split their cells
        int OnRow(int piece)
            => piece % rowSplit == 0
                ? baseVertex + (piece / rowSplit)
                : rowExtras + (piece / rowSplit * (rowSplit - 1)) + (piece % rowSplit) - 1;

        int OnColumn(int piece)
            => piece % columnSplit == 0
                ? baseVertex + (piece / columnSplit * size)
                : columnExtras + (piece / columnSplit * (columnSplit - 1)) + (piece % columnSplit) - 1;

        void AddTriangle(int a, int b, int c)
        {
            indices.Add(a);
            indices.Add(b);
            indices.Add(c);
        }
    }
}
