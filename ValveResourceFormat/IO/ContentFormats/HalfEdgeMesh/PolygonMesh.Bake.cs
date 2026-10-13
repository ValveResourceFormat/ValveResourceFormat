using System.Linq;
using System.Runtime.InteropServices;

namespace ValveResourceFormat.IO.ContentFormats.HalfEdgeMesh;

public sealed partial class PolygonMesh
{
    // Shorter than this, squared, a stored normal or tangent is taken as missing
    private const float MissingLengthSquared = 1e-6f;

    // The streams a mesh that was not read from a file is drawn with, besides its positions, normals and tangents
    private static readonly PolygonMeshStream[] BuiltMeshStreams =
    [
        DescribeBuiltStream("texcoord", 0, 0),
        DescribeBuiltStream("texcoord", 1, 0),
        DescribeBuiltStream("VertexPaintBlendParams", 0, 1),
        DescribeBuiltStream("VertexPaintTintColor", 0, 1),
    ];

    private static PolygonMeshStream DescribeBuiltStream(string semanticName, int semanticIndex, int vertexBufferLocation) => new()
    {
        Component = MeshComponentType.FaceVertex,
        Name = $"{semanticName}:{semanticIndex}",
        StandardAttributeName = semanticName,
        SemanticName = semanticName,
        SemanticIndex = semanticIndex,
        VertexBufferLocation = vertexBufferLocation,
        DataStateFlags = 1,
    };

    /// <summary>
    /// The values a stream of a mesh has at the vertices of a <see cref="BakedSubmesh"/>.
    /// </summary>
    public sealed class BakedStream
    {
        /// <summary>Gets how the mesh describes the stream: its semantic, and the vertex buffer it belongs in.</summary>
        public required PolygonMeshStream Stream { get; init; }

        /// <summary>Gets how many numbers the stream has per vertex, from one to four.</summary>
        public required int ComponentCount { get; init; }

        /// <summary>Gets the values, <see cref="ComponentCount"/> numbers for each vertex in turn.</summary>
        public required float[] Values { get; init; }
    }

    /// <summary>
    /// The triangles of the faces of a mesh that are drawn with one material, with vertices of their own, in the
    /// space of the mesh.
    /// </summary>
    public sealed class BakedSubmesh
    {
        /// <summary>Gets the name of the material, empty when the faces have none.</summary>
        public required string Material { get; init; }

        /// <summary>Gets the position of each vertex.</summary>
        public required Vector3[] Positions { get; init; }

        /// <summary>Gets the normal of each vertex.</summary>
        public required Vector3[] Normals { get; init; }

        /// <summary>Gets the tangent of each vertex, with the handedness of the bitangent in W.</summary>
        public required Vector4[] Tangents { get; init; }

        /// <summary>Gets every other stream the mesh has for its vertices or face corners, in the order the mesh holds them.</summary>
        public required BakedStream[] Streams { get; init; }

        /// <summary>Gets the triangles, three vertex indices each.</summary>
        public required int[] Indices { get; init; }
    }

    // A stream that is carried onto the vertices as it is, and where its numbers go among those of a vertex
    private sealed class StreamSource
    {
        public required PolygonMeshStream Stream { get; init; }

        public required int ComponentCount { get; init; }

        public required int Offset { get; init; }

        // Whether the stream holds a value per vertex rather than per face corner
        public required bool IsPerVertex { get; init; }

        public required Func<int, Vector4> Read { get; init; }

        // The value at a point of the subdivision stream the stream is bound to, when it is bound to one
        public Func<int, Vector4>? ReadPatch { get; init; }

        // Whether a subdivided face spreads the values of its corners over its patches, for want of a bound stream
        public bool IsSpread { get; init; }
    }

    private struct BakedVertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector4 Tangent;

        // Where the values of the other streams start in the numbers gathered while triangulating
        public int Extras;
    }

    private StreamSource[] streamSources = [];
    private int extrasStride;
    private List<float> bakedExtras = [];

    /// <summary>
    /// Triangulates the mesh into standalone geometry, the way it is drawn: every face that is not hidden
    /// has its corners turned into vertices carrying every stream of the mesh, and the faces of each material
    /// get vertices and triangles of their own. A subdivided face is drawn as the displaced grids of points it
    /// stores around its corners.
    /// </summary>
    public BakedSubmesh[] Triangulate()
    {
        var vertices = new List<BakedVertex>();
        var indices = new List<int>();
        var submeshes = new List<BakedSubmesh>();

        // The faces of a material stay in the order the mesh holds them
        var faces = Topology.FaceHandles
            .Where(hFace => !IsFaceHidden(hFace))
            .OrderBy(hFace => GetFaceMaterialIndex(hFace))
            .ThenBy(static hFace => hFace.Index)
            .ToList();

        var faceNormals = new Vector3[Topology.FaceCount];
        var facePatchSizes = new int[Topology.FaceCount];

        foreach (var hFace in Topology.FaceHandles)
        {
            PlaneEquation(hFace, out faceNormals[hFace.Index], out _);
            facePatchSizes[hFace.Index] = GetFacePatchSize(hFace);
        }

        faceNormalCache = faceNormals;
        facePatchSizeCache = facePatchSizes;
        subdivisionDisplacements = FindSubdivisionValues<Vector3>("displacement");
        streamSources = FindStreamSources();
        extrasStride = streamSources.Sum(static source => source.ComponentCount);
        bakedExtras = [];

        try
        {
            var corners = new List<BakedVertex>();
            var outline = new List<Vector3>();

            for (var i = 0; i < faces.Count; i++)
            {
                var hFace = faces[i];
                var patchSize = facePatchSizes[hFace.Index];

                if (patchSize > 0)
                {
                    TriangulateSubdividedFaceInto(hFace, patchSize, vertices, indices);
                }
                else
                {
                    TriangulateFaceInto(hFace, vertices, indices, corners, outline);
                }

                var materialIndex = GetFaceMaterialIndex(hFace);

                if (i + 1 == faces.Count || GetFaceMaterialIndex(faces[i + 1]) != materialIndex)
                {
                    if (indices.Count > 0)
                    {
                        submeshes.Add(CreateSubmesh(materialIndex >= 0 ? materials[materialIndex] : string.Empty, vertices, indices));
                    }

                    vertices.Clear();
                    indices.Clear();
                    bakedExtras.Clear();
                }
            }
        }
        finally
        {
            faceNormalCache = null;
            facePatchSizeCache = null;
            subdivisionPatches = null;
            subdivisionDisplacements = null;
            streamSources = [];
            bakedExtras = [];
        }

        return [.. submeshes];
    }

    private BakedSubmesh CreateSubmesh(string material, List<BakedVertex> vertices, List<int> indices)
    {
        var submesh = new BakedSubmesh
        {
            Material = material,
            Positions = new Vector3[vertices.Count],
            Normals = new Vector3[vertices.Count],
            Tangents = new Vector4[vertices.Count],
            Streams = new BakedStream[streamSources.Length],
            Indices = [.. indices],
        };

        for (var i = 0; i < streamSources.Length; i++)
        {
            submesh.Streams[i] = new BakedStream
            {
                Stream = streamSources[i].Stream,
                ComponentCount = streamSources[i].ComponentCount,
                Values = new float[vertices.Count * streamSources[i].ComponentCount],
            };
        }

        var extras = CollectionsMarshal.AsSpan(bakedExtras);

        for (var i = 0; i < vertices.Count; i++)
        {
            var vertex = vertices[i];

            submesh.Positions[i] = vertex.Position;
            submesh.Normals[i] = vertex.Normal;
            submesh.Tangents[i] = vertex.Tangent;

            for (var s = 0; s < streamSources.Length; s++)
            {
                var source = streamSources[s];

                extras.Slice(vertex.Extras + source.Offset, source.ComponentCount)
                    .CopyTo(submesh.Streams[s].Values.AsSpan(i * source.ComponentCount, source.ComponentCount));
            }
        }

        return submesh;
    }

    // The streams of the vertices and face corners, other than the position, normal and tangent that are worked out
    // for each vertex
    private StreamSource[] FindStreamSources()
    {
        var sources = new List<StreamSource>();
        var offset = 0;

        foreach (var stream in StoredStreams.Count > 0 ? StoredStreams : (IReadOnlyList<PolygonMeshStream>)BuiltMeshStreams)
        {
            if (stream.Component is not (MeshComponentType.Vertex or MeshComponentType.FaceVertex) || IsComputedStream(stream))
            {
                continue;
            }

            var componentCount = GetStreamReader(stream, out var read);

            if (componentCount == 0 || read == null)
            {
                continue;
            }

            Func<int, Vector4>? readPatch = null;

            if (stream.SubdivisionBinding is (var targetDataType, var targetStreamIndex, SubdivisionStreamSource.Stream))
            {
                GetSubdivisionStreamReader(targetDataType, targetStreamIndex, out readPatch);
            }

            sources.Add(new StreamSource
            {
                Stream = stream,
                ComponentCount = componentCount,
                Offset = offset,
                IsPerVertex = stream.Component == MeshComponentType.Vertex,
                Read = read,
                ReadPatch = readPatch,
                IsSpread = readPatch == null && stream.Component == MeshComponentType.FaceVertex && stream.SemanticIndex == 0
                    && (stream.SemanticName.Equals("texcoord", StringComparison.OrdinalIgnoreCase) || PaintStreams.Contains(stream.SemanticName, StringComparer.OrdinalIgnoreCase)),
            });

            offset += componentCount;
        }

        return [.. sources];
    }

    private static bool IsComputedStream(PolygonMeshStream stream)
        => stream.SemanticIndex == 0 && (stream.Component, stream.SemanticName.ToLowerInvariant()) is
            (MeshComponentType.Vertex, "position") or (MeshComponentType.FaceVertex, "normal") or (MeshComponentType.FaceVertex, "tangent");

    // How to read a stream by the index of a vertex or half edge, and how many numbers its values have
    private int GetStreamReader(PolygonMeshStream stream, out Func<int, Vector4>? read)
    {
        switch (stream.Component, stream.SemanticName.ToLowerInvariant(), stream.SemanticIndex)
        {
            case (MeshComponentType.FaceVertex, "texcoord", 0):
                read = index => TextureCoords[index].AsVector4();
                return 2;
            case (MeshComponentType.FaceVertex, "texcoord", 1):
                read = index => TextureCoords1[index].AsVector4();
                return 2;
            case (MeshComponentType.FaceVertex, "vertexpaintblendparams", 0):
                read = index => VertexPaintBlendParams[index];
                return 4;
            case (MeshComponentType.FaceVertex, "vertexpainttintcolor", 0):
                read = index => VertexPaintTintColor[index];
                return 4;
            default:
                return GetArrayReader(stream.Values, out read);
        }
    }

    private static int GetArrayReader(Array? values, out Func<int, Vector4>? read)
    {
        switch (values)
        {
            case float[] numbers:
                read = index => new Vector4(Get(numbers, index), 0, 0, 0);
                return 1;
            case Vector2[] vectors:
                read = index => Get(vectors, index).AsVector4();
                return 2;
            case Vector3[] vectors:
                read = index => Get(vectors, index).AsVector4();
                return 3;
            case Vector4[] vectors:
                read = index => Get(vectors, index);
                return 4;
            default:
                read = null;
                return 0;
        }
    }

    // A subdivision stream is known by the type of its values and where it comes among the streams of that type
    private void GetSubdivisionStreamReader(PolygonMeshDataType dataType, int streamIndex, out Func<int, Vector4>? read)
    {
        read = null;

        foreach (var stream in StoredStreams)
        {
            if (stream.Component != MeshComponentType.Subdivision)
            {
                continue;
            }

            if (stream.DataType == dataType && streamIndex-- == 0)
            {
                GetArrayReader(stream.Values, out read);
                return;
            }
        }
    }

    // Gathers the values of the other streams at a face corner, and returns where they start
    private int AddExtras(HalfEdgeHandle hFaceVertex)
    {
        var start = bakedExtras.Count;

        foreach (var source in streamSources)
        {
            AddExtra(source, source.Read(source.IsPerVertex ? hFaceVertex.Vertex.Index : hFaceVertex.Index));
        }

        return start;
    }

    private void AddExtra(StreamSource source, Vector4 value)
    {
        for (var i = 0; i < source.ComponentCount; i++)
        {
            bakedExtras.Add(value[i]);
        }
    }

    // The vertex part of the way from one to another
    private BakedVertex LerpVertex(in BakedVertex from, in BakedVertex to, float amount)
    {
        var start = bakedExtras.Count;

        for (var i = 0; i < extrasStride; i++)
        {
            bakedExtras.Add(float.Lerp(bakedExtras[from.Extras + i], bakedExtras[to.Extras + i], amount));
        }

        return new BakedVertex
        {
            Position = Vector3.Lerp(from.Position, to.Position, amount),
            Normal = MathUtils.SafeNormalize(Vector3.Lerp(from.Normal, to.Normal, amount), from.Normal),
            Tangent = new Vector4(MathUtils.SafeNormalize(Vector3.Lerp(from.Tangent.AsVector3(), to.Tangent.AsVector3(), amount), from.Tangent.AsVector3()), from.Tangent.W),
            Extras = start,
        };
    }

    private int GetFaceMaterialIndex(FaceHandle hFace)
    {
        var material = MaterialIndex[hFace];

        return (uint)material < (uint)materials.Count ? material : -1;
    }

    // The normal a face corner stores, or failing that the one its faces give it
    private Vector3 GetFaceVertexNormal(HalfEdgeHandle hFaceVertex)
    {
        var normal = Normals[hFaceVertex];

        return normal.LengthSquared() < MissingLengthSquared ? ComputeFaceVertexNormal(hFaceVertex) : normal;
    }

    private void TriangulateFaceInto(FaceHandle hFace, List<BakedVertex> vertices, List<int> indices, List<BakedVertex> corners, List<Vector3> outline)
    {
        corners.Clear();
        outline.Clear();

        var hFaceVertex = hFace.Edge;

        do
        {
            var normal = GetFaceVertexNormal(hFaceVertex);
            var tangent = Tangents[hFaceVertex];

            if (tangent.AsVector3().LengthSquared() < MissingLengthSquared)
            {
                ComputeFaceVertexTangent(hFaceVertex, normal, out tangent);
            }

            corners.Add(new BakedVertex
            {
                Position = Positions[hFaceVertex.Vertex],
                Normal = normal,
                Tangent = tangent,
                Extras = AddExtras(hFaceVertex),
            });

            hFaceVertex = hFaceVertex.NextEdge;
        }
        while (hFaceVertex != hFace.Edge);

        var firstVertex = vertices.Count;

        for (var i = 0; i < corners.Count; i++)
        {
            var corner = corners[i];
            var nextCorner = corners[(i + 1) % corners.Count];

            // An edge shared with a subdivided face gets a point for each one that face has along it
            hFaceVertex = hFaceVertex.NextEdge;
            var pieces = GetEdgeSplit(hFaceVertex, 1);

            for (var piece = 0; piece < pieces; piece++)
            {
                var vertex = piece == 0 ? corner : LerpVertex(corner, nextCorner, piece / (float)pieces);

                vertices.Add(vertex);
                outline.Add(vertex.Position);
            }
        }

        ComputeFaceNormal(hFace, out var faceNormal);
        MeshUtils.TriangulatePolygon(CollectionsMarshal.AsSpan(outline), faceNormal, firstVertex, indices);
    }
}
