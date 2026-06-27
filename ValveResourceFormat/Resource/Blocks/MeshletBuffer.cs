namespace ValveResourceFormat.Blocks;

/// <summary>
/// "MSLT" block. Holds pre-decoded, packed per-meshlet local index data.
/// </summary>
/// <remarks>
/// The buffer is a concatenation of per-meshlet segments, one <see cref="uint"/> entry per meshlet vertex.
/// The low 18 bits of the first <c>triangleCount</c> entries store a triangle as three 6-bit meshlet-local
/// vertex indices; the high 14 bits of every entry are a separate per-vertex field (the meshlet's vertex
/// list / local-to-global remap). The triangles index the meshlet's own vertex buffer, not the global one -
/// the global index buffer lives in the MIDX block.
/// </remarks>
public class MeshletBuffer : RawBinary
{
    /// <inheritdoc/>
    public override BlockType Type => BlockType.MSLT;

    /// <summary>
    /// Decodes a single meshlet into its local triangle index buffer.
    /// </summary>
    /// <param name="entryOffset">
    /// Entry (uint) offset of the meshlet within the buffer. Segments tile by vertex count, so this is the
    /// summed <c>m_nVertexCount</c> of the preceding meshlets - not the descriptor's <c>m_nVertexOffset</c>.
    /// </param>
    /// <param name="triangleCount">Number of triangles in the meshlet (its <c>m_nTriangleCount</c>).</param>
    /// <returns>An array of <c>triangleCount * 3</c> meshlet-local vertex indices.</returns>
    /// <remarks>
    /// Each triangle stores three 6-bit references. Vertices are introduced in increasing order through a
    /// 64-entry sliding window: a reference equal to <c>(maxIntroduced + 1) &amp; 63</c> introduces the next
    /// vertex, otherwise it reuses the vertex with that value within the current window. This lets meshlets
    /// with more than 64 vertices address indices 64 and above.
    /// </remarks>
    public int[] DecodeMeshletIndices(int entryOffset, int triangleCount)
    {
        if (Resource?.Reader == null)
        {
            throw new InvalidOperationException("Resource reader is required to decode meshlet indices.");
        }

        var indices = new int[triangleCount * 3];
        Resource.Reader.BaseStream.Position = Offset + (long)entryOffset * sizeof(uint);

        var maxIntroduced = -1;

        for (var t = 0; t < triangleCount; t++)
        {
            var triangle = Resource.Reader.ReadUInt32() & 0x3FFFFu;

            for (var k = 0; k < 3; k++)
            {
                var reference = (int)((triangle >> (6 * k)) & 0x3F);

                if (reference == ((maxIntroduced + 1) & 0x3F))
                {
                    // Next vertex in introduction order.
                    indices[t * 3 + k] = ++maxIntroduced;
                }
                else
                {
                    // Reuse: the vertex congruent to the reference within the current 64-entry window.
                    indices[t * 3 + k] = reference + 64 * ((maxIntroduced - reference) / 64);
                }
            }
        }

        return indices;
    }
}
