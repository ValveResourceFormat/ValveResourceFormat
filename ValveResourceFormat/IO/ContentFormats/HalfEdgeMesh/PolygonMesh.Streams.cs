using System.Linq;

namespace ValveResourceFormat.IO.ContentFormats.HalfEdgeMesh;

/// <summary>
/// A data stream of a mesh as it was read from a file: how the file describes the stream, and its values when
/// <see cref="PolygonMesh"/> has no property of its own for them. Kept so that a mesh is written back with
/// everything it was read with.
/// </summary>
public sealed class PolygonMeshStream
{
    /// <summary>Gets what the stream holds a value for.</summary>
    public required MeshComponentType Component { get; init; }

    /// <summary>Gets the name of the stream, its semantic and index joined by a colon.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the name Hammer knows the stream by.</summary>
    public required string StandardAttributeName { get; init; }

    /// <summary>Gets the name the stream binds to in the shader.</summary>
    public required string SemanticName { get; init; }

    /// <summary>Gets the channel of the semantic the stream fills.</summary>
    public int SemanticIndex { get; init; }

    /// <summary>Gets or sets the slot the stream occupies in the vertex buffer.</summary>
    public int VertexBufferLocation { get; set; }

    /// <summary>Gets or sets the flags describing how the stream is stored.</summary>
    public int DataStateFlags { get; set; }

    /// <summary>
    /// Gets how a subdivided face fills the stream for its points: the data type and index of the subdivision stream
    /// it reads, and where the values come from. Null when the stream says nothing about it.
    /// </summary>
    public (PolygonMeshDataType TargetDataType, int TargetStreamIndex, SubdivisionStreamSource StreamSourceType)? SubdivisionBinding { get; init; }

    /// <summary>
    /// Gets the values of the stream, an array of int, float, Vector2, Vector3 or Vector4. One per vertex, half edge
    /// or face by index, an edge having the same value on both of its half edges. A subdivision stream holds its
    /// patches one after the other, each starting where <see cref="PolygonMesh.SubdivisionPatchStarts"/> says.
    /// Null when the mesh holds the values in a property of its own.
    /// </summary>
    public Array? Values { get; set; }

    /// <summary>Gets the type of <see cref="Values"/>, null when the stream has none or they are of no known type.</summary>
    public PolygonMeshDataType? DataType => Values switch
    {
        float[] => PolygonMeshDataType.Float,
        Vector2[] => PolygonMeshDataType.Vector2,
        Vector3[] => PolygonMeshDataType.Vector3,
        Vector4[] => PolygonMeshDataType.Vector4,
        int[] => PolygonMeshDataType.Int,
        _ => null,
    };
}

public sealed partial class PolygonMesh
{
    // Streams painted onto the face corners, which are always stored the same way
    private static readonly string[] PaintStreams =
    [
        "VertexPaintTintColor",
        "VertexPaintBlendParams",
        "VertexPaintBlendParams1",
        "PerVertexLighting",
        "VertexPaintDetailWeight",
    ];

    /// <summary>
    /// Gets the streams the mesh was read with, in the order of the file. Empty for a mesh that was not read from one.
    /// </summary>
    public List<PolygonMeshStream> StoredStreams { get; } = [];

    /// <summary>
    /// Gets where a file kept the data of each vertex. A mesh is stored with its data in this order again for as
    /// long as every vertex has a place of its own in it.
    /// </summary>
    public VertexData<int> VertexDataIndices { get; }

    /// <summary>Gets where a file kept the data of each face corner, see <see cref="VertexDataIndices"/>.</summary>
    public HalfEdgeData<int> FaceVertexDataIndices { get; }

    /// <summary>Gets where a file kept the data of the edge each half edge is one side of, see <see cref="VertexDataIndices"/>.</summary>
    public HalfEdgeData<int> EdgeDataIndices { get; }

    /// <summary>Gets where a file kept the data of each face, see <see cref="VertexDataIndices"/>.</summary>
    public FaceData<int> FaceDataIndices { get; }

    /// <summary>
    /// Adds the streams every mesh is stored with to <see cref="StoredStreams"/> when the mesh was read without
    /// them, and brings the way the material and paint streams are stored in line.
    /// </summary>
    public void EnsureStandardStreams()
    {
        EnsureStream(MeshComponentType.Vertex, "position", 3);
        EnsureStream(MeshComponentType.FaceVertex, "normal", 1);
        EnsureStream(MeshComponentType.FaceVertex, "tangent", 1);
        EnsureStream(MeshComponentType.FaceVertex, "texcoord", 1);
        EnsureStream(MeshComponentType.Edge, "flags", 3);
        EnsureStream(MeshComponentType.Face, "materialindex", 8).DataStateFlags = 8;
        EnsureStream(MeshComponentType.Face, "textureAxisU", 0);
        EnsureStream(MeshComponentType.Face, "textureAxisV", 0);
        EnsureStream(MeshComponentType.Face, "textureScale", 0);
        EnsureStream(MeshComponentType.Face, "flags", 3);

        foreach (var stream in StoredStreams)
        {
            if (stream.Component == MeshComponentType.FaceVertex && stream.SemanticIndex == 0
                && PaintStreams.Contains(stream.SemanticName, StringComparer.OrdinalIgnoreCase))
            {
                stream.DataStateFlags = 1;
                stream.VertexBufferLocation = 1;
            }
        }

        EnsureStream(MeshComponentType.Face, "lightmapScaleBias", 1, Array.Empty<int>());
    }

    private PolygonMeshStream EnsureStream(MeshComponentType component, string semanticName, int dataStateFlags, Array? values = null)
    {
        foreach (var stream in StoredStreams)
        {
            if (stream.Component == component && stream.SemanticIndex == 0 && stream.SemanticName.Equals(semanticName, StringComparison.OrdinalIgnoreCase))
            {
                return stream;
            }
        }

        var added = new PolygonMeshStream
        {
            Component = component,
            Name = $"{semanticName}:0",
            StandardAttributeName = semanticName,
            SemanticName = semanticName,
            DataStateFlags = dataStateFlags,
            Values = values,
        };

        StoredStreams.Add(added);

        return added;
    }

    /// <summary>
    /// Gets whether the mesh was read with a stream.
    /// </summary>
    /// <param name="component">What the stream holds a value for.</param>
    /// <param name="semanticName">The semantic of the stream.</param>
    public bool HasStoredStream(MeshComponentType component, string semanticName)
    {
        foreach (var stream in StoredStreams)
        {
            if (stream.Component == component && stream.SemanticName.Equals(semanticName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
