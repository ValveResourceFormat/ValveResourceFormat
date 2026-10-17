using System.Linq;
using System.Runtime.InteropServices;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO.ContentFormats.HalfEdgeMesh;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Editor.Map;

/// <summary>
/// What a map mesh is drawn from: the triangles of its <see cref="PolygonMesh"/>, grouped by material, and the
/// vertex and index buffers made of each group. The placements of a mesh share one of these, except a placement
/// with lighting baked for it alone.
/// </summary>
public sealed class PolygonMeshRenderData
{
    // The vertex buffers a group is split over, by the location its streams name
    private const int VertexBufferCount = 2;

    private readonly record struct Field(string SemanticName, int SemanticIndex, int ComponentCount, float[] Values);

    // The buffers of each group, built the first time a placement of the mesh draws it
    private readonly (VBIB Buffers, AABB Bounds)?[] buffers;

    /// <summary>Gets the triangles of the mesh by material, in its own space.</summary>
    public PolygonMesh.BakedSubmesh[] MaterialGroups { get; }

    /// <summary>Initializes a new instance of the <see cref="PolygonMeshRenderData"/> class.</summary>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="replacementStreams">Streams drawn in place of the streams of the mesh, see <see cref="PolygonMesh.Triangulate"/>.</param>
    public PolygonMeshRenderData(PolygonMesh mesh, IReadOnlyList<PolygonMeshStream>? replacementStreams = null)
    {
        MaterialGroups = mesh.Triangulate(replacementStreams);
        buffers = new (VBIB, AABB)?[MaterialGroups.Length];
    }

    /// <summary>
    /// Gets the vertex and index buffers of the faces of one material, see <see cref="BuildVertexAndIndexBuffers"/>.
    /// They are built once and shared by every placement of the mesh, which all draw the group with one material.
    /// </summary>
    /// <param name="group">Index of the group in <see cref="MaterialGroups"/>.</param>
    /// <param name="inputSignature">The vertex inputs of the material of the group.</param>
    /// <returns>The buffers, and the bounds of the vertices.</returns>
    public (VBIB Buffers, AABB Bounds) GetBuffers(int group, Material.VsInputSignature inputSignature)
        => buffers[group] ??= BuildVertexAndIndexBuffers(MaterialGroups[group], inputSignature);

    /// <summary>
    /// Builds the vertex and index buffers of the faces of one material. The streams of the mesh the material has an
    /// input for are packed into one vertex buffer per location the streams name, the rest are left out.
    /// </summary>
    /// <param name="group">The faces of the material.</param>
    /// <param name="inputSignature">The vertex inputs of the material.</param>
    /// <returns>The buffers, and the bounds of the vertices.</returns>
    public static (VBIB Buffers, AABB Bounds) BuildVertexAndIndexBuffers(PolygonMesh.BakedSubmesh group, Material.VsInputSignature inputSignature)
    {
        ArgumentNullException.ThrowIfNull(group);

        var vertexCount = group.Positions.Length;
        var bounds = default(AABB);

        for (var i = 0; i < vertexCount; i++)
        {
            bounds = i == 0 ? new AABB(group.Positions[i], group.Positions[i]) : bounds.Encapsulate(group.Positions[i]);
        }

        var fields = new List<Field>[VertexBufferCount];

        fields[0] =
        [
            new Field("POSITION", 0, 3, MemoryMarshal.Cast<Vector3, float>(group.Positions).ToArray()),
            new Field("NORMAL", 0, 3, MemoryMarshal.Cast<Vector3, float>(group.Normals).ToArray()),
            new Field("TANGENT", 0, 4, MemoryMarshal.Cast<Vector4, float>(group.Tangents).ToArray()),
        ];
        fields[1] = [];

        foreach (var stream in group.Streams)
        {
            var location = stream.Stream.VertexBufferLocation;

            if ((uint)location < VertexBufferCount && TryResolveSemantic(stream.Stream, inputSignature, out var semanticName, out var semanticIndex))
            {
                fields[location].Add(new Field(semanticName, semanticIndex, stream.ComponentCount, GetValues(stream)));
            }
        }

        var vbib = new VBIB() { Resource = null! };

        foreach (var bufferFields in fields)
        {
            if (bufferFields.Count > 0)
            {
                vbib.VertexBuffers.Add(BuildVertexBuffer(bufferFields, vertexCount));
            }
        }

        vbib.IndexBuffers.Add(new VBIB.OnDiskBufferData
        {
            ElementCount = (uint)group.Indices.Length,
            ElementSizeInBytes = sizeof(uint),
            Data = MemoryMarshal.AsBytes(group.Indices.AsSpan()).ToArray(),
            InputLayoutFields = [],
        });

        return (vbib, bounds);
    }

    private static VBIB.OnDiskBufferData BuildVertexBuffer(List<Field> fields, int vertexCount)
    {
        var stride = fields.Sum(static field => field.ComponentCount);
        var data = new float[vertexCount * stride];
        var layout = new VBIB.RenderInputLayoutField[fields.Count];
        var offset = 0;

        for (var f = 0; f < fields.Count; f++)
        {
            var field = fields[f];

            layout[f] = new VBIB.RenderInputLayoutField
            {
                SemanticName = field.SemanticName,
                SemanticIndex = field.SemanticIndex,
                Offset = (uint)(offset * sizeof(float)),
                Format = field.ComponentCount switch
                {
                    1 => DXGI_FORMAT.R32_FLOAT,
                    2 => DXGI_FORMAT.R32G32_FLOAT,
                    3 => DXGI_FORMAT.R32G32B32_FLOAT,
                    _ => DXGI_FORMAT.R32G32B32A32_FLOAT,
                },
            };

            for (var i = 0; i < vertexCount; i++)
            {
                field.Values.AsSpan(i * field.ComponentCount, field.ComponentCount).CopyTo(data.AsSpan((i * stride) + offset, field.ComponentCount));
            }

            offset += field.ComponentCount;
        }

        return new VBIB.OnDiskBufferData
        {
            ElementCount = (uint)vertexCount,
            ElementSizeInBytes = (uint)(stride * sizeof(float)),
            Data = MemoryMarshal.AsBytes(data.AsSpan()).ToArray(),
            InputLayoutFields = layout,
        };
    }

    // The semantic a stream is bound to a shader by. Texture coordinates and colors are bound by their index, any
    // other stream by the input of the material that is named after it. A stream the material has no input for is
    // not drawn with.
    private static bool TryResolveSemantic(PolygonMeshStream stream, Material.VsInputSignature inputSignature, out string semanticName, out int semanticIndex)
    {
        var name = stream.SemanticName.ToUpperInvariant();

        if (name is "TEXCOORD" or "COLOR")
        {
            semanticName = name;
            semanticIndex = stream.SemanticIndex;

            foreach (var element in inputSignature.Elements)
            {
                if (element.D3DSemanticName == semanticName && element.D3DSemanticIndex == semanticIndex)
                {
                    return true;
                }
            }

            // A material that says nothing of its inputs is given the plain ones
            return inputSignature.Elements.Length == 0;
        }

        foreach (var element in inputSignature.Elements)
        {
            if (stream.SemanticIndex == 0 && stream.SemanticName.Equals(element.Semantic, StringComparison.OrdinalIgnoreCase))
            {
                semanticName = element.D3DSemanticName;
                semanticIndex = element.D3DSemanticIndex;
                return true;
            }
        }

        semanticName = string.Empty;
        semanticIndex = 0;
        return false;
    }

    private static float[] GetValues(PolygonMesh.BakedStream stream)
    {
        if (stream.ComponentCount != 4 || !stream.Stream.SemanticName.Equals("VertexPaintTintColor", StringComparison.OrdinalIgnoreCase))
        {
            return stream.Values;
        }

        // Vertices that were never painted hold zero, which a painted material must read as no tint
        var values = stream.Values.ToArray();

        for (var i = 0; i < values.Length; i += 4)
        {
            if (values.AsSpan(i, 4).IndexOfAnyExcept(0f) < 0)
            {
                values.AsSpan(i, 4).Fill(1f);
            }
        }

        return values;
    }
}
