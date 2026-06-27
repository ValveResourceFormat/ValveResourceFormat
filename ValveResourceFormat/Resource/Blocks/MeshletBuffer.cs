namespace ValveResourceFormat.Blocks;

/// <summary>
/// "MSLT" block. Holds pre-decoded, packed per-meshlet index data.
/// </summary>
/// <remarks>
/// The buffer is a concatenation of per-meshlet segments, one <see cref="uint"/> entry per meshlet vertex.
/// Each entry packs <c>(vertexIndex &lt;&lt; 18) | triangle</c>, where the low 18 bits of the first
/// <c>triangleCount</c> entries store a triangle as three 6-bit local indices, and the high 14 bits of
/// every entry form the meshlet's vertex list.
/// </remarks>
public class MeshletBuffer : RawBinary
{
    /// <inheritdoc/>
    public override BlockType Type => BlockType.MSLT;

    /// <summary>
    /// Decodes a single meshlet into a standard triangle index buffer, remapping the packed 6-bit local
    /// triangle indices through the meshlet's vertex list (the high 14 bits of each entry).
    /// </summary>
    /// <param name="entryOffset">
    /// Entry (uint) offset of the meshlet within the buffer. Segments tile by vertex count, so this is the
    /// summed <c>m_nVertexCount</c> of the preceding meshlets — not the descriptor's <c>m_nVertexOffset</c>.
    /// </param>
    /// <param name="vertexOffset">Base added to each remapped index (the meshlet's <c>m_nVertexOffset</c>).</param>
    /// <param name="vertexCount">Number of vertices/entries in the meshlet (its <c>m_nVertexCount</c>).</param>
    /// <param name="triangleCount">Number of triangles in the meshlet (its <c>m_nTriangleCount</c>).</param>
    /// <returns>An array of <c>triangleCount * 3</c> vertex indices.</returns>
    public int[] DecodeMeshletIndices(int entryOffset, int vertexOffset, int vertexCount, int triangleCount)
    {
        if (Resource?.Reader == null)
        {
            throw new InvalidOperationException("Resource reader is required to decode meshlet indices.");
        }

        var entries = new uint[vertexCount];
        Resource.Reader.BaseStream.Position = Offset + (long)entryOffset * sizeof(uint);

        for (var i = 0; i < vertexCount; i++)
        {
            entries[i] = Resource.Reader.ReadUInt32();
        }

        var indices = new int[triangleCount * 3];

        for (var t = 0; t < triangleCount; t++)
        {
            var triangle = entries[t] & 0x3FFFFu;
            indices[t * 3 + 0] = vertexOffset + (int)(entries[triangle & 0x3F] >> 18);
            indices[t * 3 + 1] = vertexOffset + (int)(entries[(triangle >> 6) & 0x3F] >> 18);
            indices[t * 3 + 2] = vertexOffset + (int)(entries[(triangle >> 12) & 0x3F] >> 18);
        }

        return indices;
    }
}
