using System.Globalization;
using System.Linq;
using Datamodel;
using ValveResourceFormat.IO.ContentFormats.HalfEdgeMesh;

namespace ValveResourceFormat.IO.ContentFormats.ValveMap;

public partial class CDmePolygonMesh
{
    // The streams a mesh that was not read from a file is written with
    private static readonly PolygonMeshStream[] StandardStreams =
    [
        DescribeStandardStream(MeshComponentType.Face, "textureScale", 0, 0),
        DescribeStandardStream(MeshComponentType.Face, "textureAxisU", 0, 0),
        DescribeStandardStream(MeshComponentType.Face, "textureAxisV", 0, 0),
        DescribeStandardStream(MeshComponentType.Face, "materialindex", 0, 8),
        DescribeStandardStream(MeshComponentType.Face, "flags", 0, 3),
        DescribeStandardStream(MeshComponentType.Face, "lightmapScaleBias", 0, 1, values: Array.Empty<int>()),
        DescribeStandardStream(MeshComponentType.FaceVertex, "texcoord", 0, 1),
        DescribeStandardStream(MeshComponentType.FaceVertex, "texcoord", 1, 1, "texcoord1"),
        DescribeStandardStream(MeshComponentType.FaceVertex, "VertexPaintBlendParams", 0, 1, vertexBufferLocation: 1),
        DescribeStandardStream(MeshComponentType.FaceVertex, "VertexPaintTintColor", 0, 1, vertexBufferLocation: 1),
        DescribeStandardStream(MeshComponentType.FaceVertex, "normal", 0, 1),
        DescribeStandardStream(MeshComponentType.FaceVertex, "tangent", 0, 1),
        DescribeStandardStream(MeshComponentType.Vertex, "position", 0, 3),
        DescribeStandardStream(MeshComponentType.Edge, "flags", 0, 3),
    ];

    private static PolygonMeshStream DescribeStandardStream(MeshComponentType component, string semanticName, int semanticIndex, int dataStateFlags, string? standardAttributeName = null, Array? values = null, int vertexBufferLocation = 0) => new()
    {
        Component = component,
        Name = string.Create(CultureInfo.InvariantCulture, $"{semanticName}:{semanticIndex}"),
        StandardAttributeName = standardAttributeName ?? semanticName,
        SemanticName = semanticName,
        SemanticIndex = semanticIndex,
        VertexBufferLocation = vertexBufferLocation,
        DataStateFlags = dataStateFlags,
        Values = values,
    };

    /// <summary>
    /// Copies the stored mesh into a mesh to work with. Vertices, half edges and faces keep the indices they are
    /// stored under, and every stream is carried over along with where it keeps the data of each of them, so that
    /// <see cref="CopyFromPolyMesh"/> stores the mesh again with nothing lost. A stored mesh whose arrays disagree
    /// on how many vertices, edges or faces it has is not read, and leaves the mesh empty.
    /// </summary>
    /// <param name="mesh">The mesh to fill, which has to be empty.</param>
    public void CopyToPolyMesh(PolygonMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        if (IsConsistent() && mesh.Topology.Deserialize(
            VertexEdgeIndices.AsSpan(),
            EdgeVertexIndices.AsSpan(),
            EdgeOppositeIndices.AsSpan(),
            EdgeNextIndices.AsSpan(),
            EdgeFaceIndices.AsSpan(),
            FaceEdgeIndices.AsSpan()))
        {
            ReadComponentData(mesh);
        }

        var materialIndices = new int[Materials.Count];

        for (var i = 0; i < materialIndices.Length; i++)
        {
            materialIndices[i] = mesh.AddMaterial(Materials[i]);
        }

        for (var i = 0; i < mesh.Topology.FaceCount; i++)
        {
            var material = mesh.MaterialIndex[i];
            mesh.MaterialIndex[i] = (uint)material < (uint)materialIndices.Length ? materialIndices[material] : -1;
        }
    }

    // Every vertex, half edge and face has an index of its data, and every edge and each of the others an entry of data
    private bool IsConsistent()
    {
        var vertexCount = VertexEdgeIndices.Count;
        var halfEdgeCount = EdgeVertexIndices.Count;
        var faceCount = FaceEdgeIndices.Count;

        return VertexDataIndices.Count == vertexCount
            && EdgeDataIndices.Count == halfEdgeCount
            && EdgeVertexDataIndices.Count == halfEdgeCount
            && FaceDataIndices.Count == faceCount
            && VertexData.Size == vertexCount
            && FaceVertexData.Size == halfEdgeCount
            && EdgeData.Size == halfEdgeCount / 2
            && FaceData.Size == faceCount;
    }

    private void ReadComponentData(PolygonMesh mesh)
    {
        var topology = mesh.Topology;

        // A face has no material until a stream gives it one
        for (var i = 0; i < topology.FaceCount; i++)
        {
            mesh.MaterialIndex[i] = -1;
            mesh.FaceDataIndices[i] = FaceDataIndices[i];
        }

        for (var i = 0; i < topology.VertexCount; i++)
        {
            mesh.VertexDataIndices[i] = VertexDataIndices[i];
        }

        for (var i = 0; i < topology.HalfEdgeCount; i++)
        {
            mesh.FaceVertexDataIndices[i] = EdgeVertexDataIndices[i];
            mesh.EdgeDataIndices[i] = EdgeDataIndices[i];
        }

        // Each stream is reached through the data index of the vertex, half edge or face
        ReadStreams(mesh, VertexData, MeshComponentType.Vertex, VertexDataIndices.AsSpan(), topology.VertexCount);
        ReadStreams(mesh, FaceVertexData, MeshComponentType.FaceVertex, EdgeVertexDataIndices.AsSpan(), topology.HalfEdgeCount);
        ReadStreams(mesh, EdgeData, MeshComponentType.Edge, EdgeDataIndices.AsSpan(), topology.HalfEdgeCount);
        ReadStreams(mesh, FaceData, MeshComponentType.Face, FaceDataIndices.AsSpan(), topology.FaceCount);

        // The subdivision streams hold one patch per subdivided face corner, in the order of the data of the corners
        var levels = SubdivisionData.SubdivisionLevels.AsSpan();
        var patchStarts = new int[levels.Length + 1];

        for (var i = 0; i < levels.Length; i++)
        {
            var size = PolygonMesh.GetPatchSize(levels[i]);
            patchStarts[i + 1] = patchStarts[i] + (size * size);
        }

        var cornerData = EdgeVertexDataIndices.AsSpan();

        for (var i = 0; i < topology.HalfEdgeCount; i++)
        {
            var corner = cornerData[i];

            if ((uint)corner < (uint)levels.Length)
            {
                mesh.SubdivisionLevels[i] = levels[corner];
                mesh.SubdivisionPatchStarts[i] = patchStarts[corner];
            }
        }

        foreach (var stream in SubdivisionData.Streams.OfType<CDmePolygonMeshDataStream>())
        {
            mesh.StoredStreams.Add(DescribeStream(stream, MeshComponentType.Subdivision, CopyValues(stream.Data)));
        }
    }

    private static void ReadStreams(PolygonMesh mesh, CDmePolygonMeshDataArray array, MeshComponentType component, ReadOnlySpan<int> dataIndices, int count)
    {
        foreach (var stream in array.Streams.OfType<CDmePolygonMeshDataStream>())
        {
            var values = ReadIntoMesh(mesh, component, stream, dataIndices, count) ? (Array?)null : stream.Data switch
            {
                IList<int> data => Spread(data, dataIndices, count),
                IList<float> data => Spread(data, dataIndices, count),
                IList<Vector2> data => Spread(data, dataIndices, count),
                IList<Vector3> data => Spread(data, dataIndices, count),
                IList<Vector4> data => Spread(data, dataIndices, count),
                _ => null,
            };

            mesh.StoredStreams.Add(DescribeStream(stream, component, values));
        }
    }

    // Reads a stream the mesh has a property for into that property
    private static bool ReadIntoMesh(PolygonMesh mesh, MeshComponentType component, CDmePolygonMeshDataStream stream, ReadOnlySpan<int> dataIndices, int count)
    {
        switch (component, stream.SemanticName.ToLowerInvariant(), stream.SemanticIndex, stream.Data)
        {
            case (MeshComponentType.Vertex, "position", 0, IList<Vector3> data):
                Fill(mesh.Positions, data, dataIndices, count);
                return true;
            case (MeshComponentType.FaceVertex, "texcoord", 0, IList<Vector2> data):
                Fill(mesh.TextureCoords, data, dataIndices, count);
                return true;
            case (MeshComponentType.FaceVertex, "texcoord", 1, IList<Vector2> data):
                Fill(mesh.TextureCoords1, data, dataIndices, count);
                return true;
            case (MeshComponentType.FaceVertex, "normal", 0, IList<Vector3> data):
                Fill(mesh.Normals, data, dataIndices, count);
                return true;
            case (MeshComponentType.FaceVertex, "tangent", 0, IList<Vector4> data):
                Fill(mesh.Tangents, data, dataIndices, count);
                return true;
            case (MeshComponentType.FaceVertex, "vertexpaintblendparams", 0, IList<Vector4> data):
                Fill(mesh.VertexPaintBlendParams, data, dataIndices, count);
                return true;
            case (MeshComponentType.FaceVertex, "vertexpainttintcolor", 0, IList<Vector4> data):
                Fill(mesh.VertexPaintTintColor, data, dataIndices, count);
                return true;
            case (MeshComponentType.Edge, "flags", 0, IList<int> data):
                Fill(mesh.EdgeFlags, data, dataIndices, count);
                return true;
            case (MeshComponentType.Face, "materialindex", 0, IList<int> data):
                Fill(mesh.MaterialIndex, data, dataIndices, count);
                return true;
            case (MeshComponentType.Face, "flags", 0, IList<int> data):
                Fill(mesh.FaceFlags, data, dataIndices, count);
                return true;
            case (MeshComponentType.Face, "texturescale", 0, IList<Vector2> data):
                Fill(mesh.TextureScale, data, dataIndices, count);
                return true;

            // The axes carry the texel offset in W
            case (MeshComponentType.Face, "textureaxisu", 0, IList<Vector4> data):
            {
                var axes = Spread(data, dataIndices, count);

                for (var i = 0; i < count; i++)
                {
                    mesh.TextureUAxis[i] = axes[i].AsVector3();
                    mesh.TextureOffset[i] = mesh.TextureOffset[i] with { X = axes[i].W };
                }

                return true;
            }

            case (MeshComponentType.Face, "textureaxisv", 0, IList<Vector4> data):
            {
                var axes = Spread(data, dataIndices, count);

                for (var i = 0; i < count; i++)
                {
                    mesh.TextureVAxis[i] = axes[i].AsVector3();
                    mesh.TextureOffset[i] = mesh.TextureOffset[i] with { Y = axes[i].W };
                }

                return true;
            }

            default:
                return false;
        }
    }

    private static void Fill<T>(ComponentData<T> target, IList<T> data, ReadOnlySpan<int> dataIndices, int count) where T : struct
    {
        for (var i = 0; i < count; i++)
        {
            var index = i < dataIndices.Length ? dataIndices[i] : -1;

            if ((uint)index < (uint)data.Count)
            {
                target[i] = data[index];
            }
        }
    }

    // The value each vertex, half edge or face has in a stream
    private static T[] Spread<T>(IList<T> data, ReadOnlySpan<int> dataIndices, int count) where T : struct
    {
        var values = new T[count];

        for (var i = 0; i < count; i++)
        {
            var index = i < dataIndices.Length ? dataIndices[i] : -1;

            if ((uint)index < (uint)data.Count)
            {
                values[i] = data[index];
            }
        }

        return values;
    }

    private static Array? CopyValues(System.Collections.IList? data) => data switch
    {
        IList<int> values => values.ToArray(),
        IList<float> values => values.ToArray(),
        IList<Vector2> values => values.ToArray(),
        IList<Vector3> values => values.ToArray(),
        IList<Vector4> values => values.ToArray(),
        _ => null,
    };

    private static PolygonMeshStream DescribeStream(CDmePolygonMeshDataStream stream, MeshComponentType component, Array? values) => new()
    {
        Component = component,
        Name = stream.Name,
        StandardAttributeName = stream.StandardAttributeName,
        SemanticName = stream.SemanticName,
        SemanticIndex = stream.SemanticIndex,
        VertexBufferLocation = stream.VertexBufferLocation,
        DataStateFlags = stream.DataStateFlags,
        SubdivisionBinding = stream.SubdivisionBinding is CDmePolygonMeshSubdivisiondataBinding binding
            ? ((PolygonMeshDataType)binding.TargetDataType, binding.TargetStreamIndex, (SubdivisionStreamSource)binding.StreamSourceType)
            : null,
        Values = values,
    };

    /// <summary>
    /// Stores a mesh in this element, replacing what it holds. Vertices, half edges and faces are stored without the
    /// gaps that editing leaves between them, and their data in the order it was read in when the mesh still has
    /// that. A mesh that was read from a file is stored with the streams it was read with, any other mesh with the
    /// standard ones.
    /// </summary>
    /// <param name="mesh">The mesh to store.</param>
    public void CopyFromPolyMesh(PolygonMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        var topology = mesh.Topology;

        var vertices = FindAllocated(topology.VertexCount, topology.IsVertexAllocated, out var vertexRemap);
        var halfEdges = FindAllocated(topology.HalfEdgeCount, topology.IsHalfEdgeAllocated, out var halfEdgeRemap);
        var faces = FindAllocated(topology.FaceCount, topology.IsFaceAllocated, out var faceRemap);

        // An edge has one entry of data for both of its half edges, and is known here by the first of them
        var edges = new List<int>(halfEdges.Count / 2);
        var edgeOfHalfEdge = new int[topology.HalfEdgeCount];

        Array.Fill(edgeOfHalfEdge, -1);

        foreach (var halfEdge in halfEdges)
        {
            if (edgeOfHalfEdge[halfEdge] >= 0)
            {
                continue;
            }

            var opposite = new HalfEdgeHandle(halfEdge, topology).OppositeEdge.Index;

            edgeOfHalfEdge[halfEdge] = halfEdge;

            if (opposite >= 0)
            {
                edgeOfHalfEdge[opposite] = halfEdge;
            }

            edges.Add(halfEdge);
        }

        var vertexData = OrderData(vertices, mesh.VertexDataIndices, topology.VertexCount, out var vertexDataIndices);
        var faceVertexData = OrderData(halfEdges, mesh.FaceVertexDataIndices, topology.HalfEdgeCount, out var faceVertexDataIndices);
        var edgeData = OrderData(edges, mesh.EdgeDataIndices, topology.HalfEdgeCount, out var edgeDataIndices);
        var faceData = OrderData(faces, mesh.FaceDataIndices, topology.FaceCount, out var faceDataIndices);

        ClearMesh();

        foreach (var vertex in vertices)
        {
            var edge = new VertexHandle(vertex, topology).Edge.Index;

            VertexEdgeIndices.Add(edge >= 0 ? halfEdgeRemap[edge] : -1);
            VertexDataIndices.Add(vertexDataIndices[vertex]);
        }

        foreach (var halfEdge in halfEdges)
        {
            var hEdge = new HalfEdgeHandle(halfEdge, topology);
            var face = hEdge.Face.Index;

            EdgeVertexIndices.Add(vertexRemap[hEdge.Vertex.Index]);
            EdgeOppositeIndices.Add(halfEdgeRemap[hEdge.OppositeEdge.Index]);
            EdgeNextIndices.Add(halfEdgeRemap[hEdge.NextEdge.Index]);
            EdgeFaceIndices.Add(face >= 0 ? faceRemap[face] : -1);
            EdgeDataIndices.Add(edgeDataIndices[edgeOfHalfEdge[halfEdge]]);
            EdgeVertexDataIndices.Add(faceVertexDataIndices[halfEdge]);
        }

        foreach (var face in faces)
        {
            FaceEdgeIndices.Add(halfEdgeRemap[new FaceHandle(face, topology).Edge.Index]);
            FaceDataIndices.Add(faceDataIndices[face]);
        }

        VertexData.Size = vertexData.Count;
        FaceVertexData.Size = faceVertexData.Count;
        EdgeData.Size = edgeData.Count;
        FaceData.Size = faceData.Count;

        Materials.AddRange(mesh.Materials);

        var wasRead = mesh.StoredStreams.Count > 0;
        var streams = wasRead ? mesh.StoredStreams : (IReadOnlyList<PolygonMeshStream>)StandardStreams;

        WriteStreams(mesh, streams, MeshComponentType.Vertex, VertexData, vertexData);
        WriteStreams(mesh, streams, MeshComponentType.FaceVertex, FaceVertexData, faceVertexData);
        WriteStreams(mesh, streams, MeshComponentType.Edge, EdgeData, edgeData);
        WriteStreams(mesh, streams, MeshComponentType.Face, FaceData, faceData);

        WriteSubdivisionData(mesh, faceVertexData, wasRead);
    }

    // The vertices, half edges or faces in the order their data is stored in, and where the data of each one is in
    // that order. This is the order the mesh was read with for as long as every one of them still has a place of its
    // own in it, and their own order otherwise.
    private static List<int> OrderData(List<int> components, ComponentData<int> storedIndices, int count, out int[] dataIndices)
    {
        var ordered = new int[components.Count];
        var isStoredOrder = true;

        Array.Fill(ordered, -1);

        foreach (var component in components)
        {
            var dataIndex = storedIndices[component];

            if ((uint)dataIndex >= (uint)ordered.Length || ordered[dataIndex] >= 0)
            {
                isStoredOrder = false;
                break;
            }

            ordered[dataIndex] = component;
        }

        var order = isStoredOrder ? ordered.ToList() : components;

        dataIndices = new int[count];

        for (var i = 0; i < order.Count; i++)
        {
            dataIndices[order[i]] = i;
        }

        return order;
    }

    // The streams of an array are stored by the type of their values
    private static void WriteStreams(PolygonMesh mesh, IReadOnlyList<PolygonMeshStream> streams, MeshComponentType component, CDmePolygonMeshDataArray array, List<int> order)
    {
        var written = new List<CDmePolygonMeshDataStream>();

        foreach (var stream in streams)
        {
            if (stream.Component == component && (WriteFromMesh(mesh, stream, order) ?? Gather(stream.Values, order)) is { } data)
            {
                written.Add(CreateStream(stream, data));
            }
        }

        array.Streams.AddRange(written.OrderBy(static stream => stream.Data switch
        {
            FloatArray => PolygonMeshDataType.Float,
            Vector2Array => PolygonMeshDataType.Vector2,
            Vector3Array => PolygonMeshDataType.Vector3,
            Vector4Array => PolygonMeshDataType.Vector4,
            _ => PolygonMeshDataType.Int,
        }));
    }

    private void ClearMesh()
    {
        VertexEdgeIndices.Clear();
        VertexDataIndices.Clear();
        EdgeVertexIndices.Clear();
        EdgeOppositeIndices.Clear();
        EdgeNextIndices.Clear();
        EdgeFaceIndices.Clear();
        EdgeDataIndices.Clear();
        EdgeVertexDataIndices.Clear();
        FaceEdgeIndices.Clear();
        FaceDataIndices.Clear();
        Materials.Clear();
        VertexData.Streams.Clear();
        FaceVertexData.Streams.Clear();
        EdgeData.Streams.Clear();
        FaceData.Streams.Clear();
        SubdivisionData.SubdivisionLevels.Clear();
        SubdivisionData.Streams.Clear();
    }

    // The indices in use, in order, and where each index is in that order
    private static List<int> FindAllocated(int count, Func<int, bool> isAllocated, out int[] remap)
    {
        var allocated = new List<int>(count);
        remap = new int[count];

        for (var i = 0; i < count; i++)
        {
            remap[i] = isAllocated(i) ? allocated.Count : -1;

            if (remap[i] >= 0)
            {
                allocated.Add(i);
            }
        }

        return allocated;
    }

    // Stores a stream the mesh has a property for from that property
    private static System.Collections.IList? WriteFromMesh(PolygonMesh mesh, PolygonMeshStream stream, List<int> order)
    {
        switch (stream.Component, stream.SemanticName.ToLowerInvariant(), stream.SemanticIndex)
        {
            case (MeshComponentType.Vertex, "position", 0):
                return Gather<Vector3, Vector3Array>(mesh.Positions, order);
            case (MeshComponentType.FaceVertex, "texcoord", 0):
                return Gather<Vector2, Vector2Array>(mesh.TextureCoords, order);
            case (MeshComponentType.FaceVertex, "texcoord", 1):
                return Gather<Vector2, Vector2Array>(mesh.TextureCoords1, order);
            case (MeshComponentType.FaceVertex, "normal", 0):
                return Gather<Vector3, Vector3Array>(mesh.Normals, order);
            case (MeshComponentType.FaceVertex, "tangent", 0):
                return Gather<Vector4, Vector4Array>(mesh.Tangents, order);
            case (MeshComponentType.FaceVertex, "vertexpaintblendparams", 0):
                return Gather<Vector4, Vector4Array>(mesh.VertexPaintBlendParams, order);
            case (MeshComponentType.FaceVertex, "vertexpainttintcolor", 0):
                return Gather<Vector4, Vector4Array>(mesh.VertexPaintTintColor, order);
            case (MeshComponentType.Edge, "flags", 0):
                return Gather<int, IntArray>(mesh.EdgeFlags, order);
            case (MeshComponentType.Face, "materialindex", 0):
                return Gather<int, IntArray>(mesh.MaterialIndex, order);
            case (MeshComponentType.Face, "flags", 0):
                return Gather<int, IntArray>(mesh.FaceFlags, order);
            case (MeshComponentType.Face, "texturescale", 0):
                return Gather<Vector2, Vector2Array>(mesh.TextureScale, order);

            // The axes carry the texel offset in W
            case (MeshComponentType.Face, "textureaxisu", 0):
            {
                var axes = new Vector4Array(order.Count);

                foreach (var face in order)
                {
                    axes.Add(new Vector4(mesh.TextureUAxis[face], mesh.TextureOffset[face].X));
                }

                return axes;
            }

            case (MeshComponentType.Face, "textureaxisv", 0):
            {
                var axes = new Vector4Array(order.Count);

                foreach (var face in order)
                {
                    axes.Add(new Vector4(mesh.TextureVAxis[face], mesh.TextureOffset[face].Y));
                }

                return axes;
            }

            default:
                return null;
        }
    }

    private static TArray Gather<T, TArray>(ComponentData<T> source, List<int> order)
        where T : struct
        where TArray : Array<T>, new()
    {
        var data = new TArray();

        foreach (var index in order)
        {
            data.Add(Sanitize(source[index]));
        }

        return data;
    }

    // A value with a component that is not a number is stored as zero
    private static T Sanitize<T>(T value) where T : struct => value switch
    {
        float number when !float.IsFinite(number) => default,
        Vector2 vector when !float.IsFinite(vector.X + vector.Y) => default,
        Vector3 vector when !float.IsFinite(vector.X + vector.Y + vector.Z) => default,
        Vector4 vector when !float.IsFinite(vector.X + vector.Y + vector.Z + vector.W) => default,
        _ => value,
    };

    private static System.Collections.IList? Gather(Array? values, List<int> order) => values switch
    {
        int[] typed => Gather<int, IntArray>(typed, order),
        float[] typed => Gather<float, FloatArray>(typed, order),
        Vector2[] typed => Gather<Vector2, Vector2Array>(typed, order),
        Vector3[] typed => Gather<Vector3, Vector3Array>(typed, order),
        Vector4[] typed => Gather<Vector4, Vector4Array>(typed, order),
        _ => null,
    };

    // A value the stream does not reach is stored as zero
    private static TArray Gather<T, TArray>(T[] values, List<int> order)
        where T : struct
        where TArray : Array<T>, new()
    {
        var data = new TArray();

        foreach (var index in order)
        {
            data.Add((uint)index < (uint)values.Length ? Sanitize(values[index]) : default);
        }

        return data;
    }

    private static CDmePolygonMeshDataStream CreateStream(PolygonMeshStream stream, System.Collections.IList data) => new()
    {
        Name = stream.Name,
        StandardAttributeName = stream.StandardAttributeName,
        SemanticName = stream.SemanticName,
        SemanticIndex = stream.SemanticIndex,
        VertexBufferLocation = stream.VertexBufferLocation,
        DataStateFlags = stream.DataStateFlags,
        SubdivisionBinding = stream.SubdivisionBinding is var (targetDataType, targetStreamIndex, streamSourceType)
            ? new CDmePolygonMeshSubdivisiondataBinding
            {
                Name = "subdivisionBinding",
                TargetDataType = (int)targetDataType,
                TargetStreamIndex = targetStreamIndex,
                StreamSourceType = (int)streamSourceType,
            }
            : null,
        Data = data,
    };

    // The levels and patches of the face corners follow the order of the data of the corners
    private void WriteSubdivisionData(PolygonMesh mesh, List<int> halfEdges, bool wasRead)
    {
        if (!wasRead)
        {
            // TODO: A mesh that was built rather than read has always been written with these eight levels, whatever its size
            SubdivisionData.SubdivisionLevels.AddRange(Enumerable.Repeat(0, 8));
            return;
        }

        foreach (var halfEdge in halfEdges)
        {
            SubdivisionData.SubdivisionLevels.Add(mesh.SubdivisionLevels[halfEdge]);
        }

        foreach (var stream in mesh.StoredStreams)
        {
            if (stream.Component == MeshComponentType.Subdivision && stream.Values switch
            {
                int[] values => GatherPatches<int, IntArray>(mesh, values, halfEdges),
                float[] values => GatherPatches<float, FloatArray>(mesh, values, halfEdges),
                Vector2[] values => GatherPatches<Vector2, Vector2Array>(mesh, values, halfEdges),
                Vector3[] values => GatherPatches<Vector3, Vector3Array>(mesh, values, halfEdges),
                Vector4[] values => GatherPatches<Vector4, Vector4Array>(mesh, values, halfEdges),
                _ => (System.Collections.IList?)null,
            } is { } data)
            {
                SubdivisionData.Streams.Add(CreateStream(stream, data));
            }
        }
    }

    // The patches of the subdivided face corners one after the other, in the order the corners are stored in
    private static TArray GatherPatches<T, TArray>(PolygonMesh mesh, T[] values, List<int> halfEdges)
        where T : struct
        where TArray : Array<T>, new()
    {
        var data = new TArray();

        foreach (var halfEdge in halfEdges)
        {
            var size = PolygonMesh.GetPatchSize(mesh.SubdivisionLevels[halfEdge]);
            var start = mesh.SubdivisionPatchStarts[halfEdge];

            for (var i = 0; i < size * size; i++)
            {
                data.Add((uint)(start + i) < (uint)values.Length ? values[start + i] : default);
            }
        }

        return data;
    }
}
