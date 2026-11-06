using System.Runtime.InteropServices;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Source1;

/// <summary>Helpers for the vertex and index buffers of converted meshes.</summary>
internal static class Source1VertexBuffers
{
    /// <summary>Creates the buffers of a mesh with one vertex buffer and one index buffer.</summary>
    /// <param name="vertices">The vertices.</param>
    /// <param name="fields">The fields of the vertex struct that the shaders read.</param>
    /// <param name="indices">The indices, of a type whose size is the size of one index.</param>
    public static VBIB Create<TVertex, TIndex>(TVertex[] vertices, VertexField[] fields, TIndex[] indices)
        where TVertex : unmanaged
        where TIndex : unmanaged
        => Create([vertices], fields, indices);

    /// <summary>Creates the buffers of a mesh with a vertex buffer for each array of vertices and one index buffer.</summary>
    /// <param name="vertexBuffers">The vertices of each vertex buffer.</param>
    /// <param name="fields">The fields of the vertex struct that the shaders read.</param>
    /// <param name="indices">The indices, of a type whose size is the size of one index.</param>
    public static VBIB Create<TVertex, TIndex>(IReadOnlyList<TVertex[]> vertexBuffers, VertexField[] fields, TIndex[] indices)
        where TVertex : unmanaged
        where TIndex : unmanaged
    {
        var inputFields = new VBIB.RenderInputLayoutField[fields.Length];

        for (var i = 0; i < fields.Length; i++)
        {
            var field = fields[i];
            var offset = (uint)Marshal.OffsetOf<TVertex>(field.Name);

            inputFields[i] = new VBIB.RenderInputLayoutField(field.Semantic, field.Format, offset)
            {
                SemanticIndex = field.SemanticIndex,
            };

            if (field.ShaderSemantic != null)
            {
                inputFields[i].ShaderSemantic = field.ShaderSemantic;
            }
        }

        var vbib = new VBIB { Resource = null! };

        foreach (var vertices in vertexBuffers)
        {
            vbib.VertexBuffers.Add(new VBIB.OnDiskBufferData
            {
                ElementCount = (uint)vertices.Length,
                ElementSizeInBytes = (uint)Marshal.SizeOf<TVertex>(),
                Data = MemoryMarshal.AsBytes(vertices.AsSpan()).ToArray(),
                InputLayoutFields = inputFields,
            });
        }

        vbib.IndexBuffers.Add(new VBIB.OnDiskBufferData
        {
            ElementCount = (uint)indices.Length,
            ElementSizeInBytes = (uint)Marshal.SizeOf<TIndex>(),
            Data = MemoryMarshal.AsBytes(indices.AsSpan()).ToArray(),
            InputLayoutFields = [],
        });

        return vbib;
    }

    /// <summary>Gets the corners of the box that contains the positions of the vertices of several buffers.</summary>
    /// <param name="vertexBuffers">The vertices of each buffer.</param>
    /// <param name="getPosition">Gets the position of a vertex.</param>
    /// <returns>The corners, or zero for both when there are no vertices.</returns>
    public static (Vector3 Min, Vector3 Max) GetBounds<TVertex>(IReadOnlyList<TVertex[]> vertexBuffers, Func<TVertex, Vector3> getPosition)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        var hasVertices = false;

        foreach (var vertices in vertexBuffers)
        {
            foreach (var vertex in vertices)
            {
                var position = getPosition(vertex);
                min = Vector3.Min(min, position);
                max = Vector3.Max(max, position);
                hasVertices = true;
            }
        }

        return hasVertices ? (min, max) : (Vector3.Zero, Vector3.Zero);
    }

    /// <summary>Gets the corners of the box that contains the positions of the vertices.</summary>
    /// <param name="vertices">The vertices.</param>
    /// <param name="getPosition">Gets the position of a vertex.</param>
    /// <returns>The corners, or zero for both when there are no vertices.</returns>
    public static (Vector3 Min, Vector3 Max) GetBounds<TVertex>(TVertex[] vertices, Func<TVertex, Vector3> getPosition)
    {
        if (vertices.Length == 0)
        {
            return (Vector3.Zero, Vector3.Zero);
        }

        var min = getPosition(vertices[0]);
        var max = min;

        foreach (var vertex in vertices)
        {
            var position = getPosition(vertex);
            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
        }

        return (min, max);
    }

    /// <summary>Converts a value in [0, 1] to the nearest 8-bit unsigned normalized value. Values outside the range are clamped.</summary>
    public static byte ToUnorm8(float x) => (byte)MathF.Round(MathUtils.Saturate(x) * 255f);
}

/// <summary>A field of a vertex struct that a model reads as a vertex attribute.</summary>
/// <param name="Name">The name of the field.</param>
/// <param name="Semantic">The semantic of the attribute, such as <c>TEXCOORD</c>.</param>
/// <param name="SemanticIndex">The index of the attribute among those of its semantic.</param>
/// <param name="Format">The format of the field.</param>
/// <param name="ShaderSemantic">The name of the shader input when no semantic names it, or null.</param>
internal readonly record struct VertexField(string Name, string Semantic, int SemanticIndex, DXGI_FORMAT Format, string? ShaderSemantic = null);
