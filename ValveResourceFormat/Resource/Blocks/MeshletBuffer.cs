namespace ValveResourceFormat.Blocks;

/// <summary>
/// "MSLT" block. Holds packed per-meshlet local index data. There is one <see cref="uint"/> entry per meshlet vertex.
/// </summary>
/// <remarks>
/// How a shader uses this:
///   1. Transform each meshlet vertex once.
///   2. Emit the local index buffer (values 0..vertexCount-1) as primitives over those vertices.
///   3. Fetch a position with vertexBuffer[vertexList[localIndex]], or MVTX[vertexOffset + localIndex]
///      when vertices are stored per-meshlet.
/// The vertexList comes from MIDX. The classic path ignores this block and draws MIDX ranges directly.
/// </remarks>
public class MeshletBuffer : RawBinary
{
    /// <inheritdoc/>
    public override BlockType Type => BlockType.MSLT;

    /// <summary>
    /// Decodes a single meshlet into its vertex list and its local triangle index buffer.
    /// </summary>
    /// <param name="entryOffset">
    /// Entry (uint) offset of the meshlet within the buffer. Segments tile by vertex count, so this is the
    /// summed <c>m_nVertexCount</c> of the preceding meshlets - not the descriptor's <c>m_nVertexOffset</c>.
    /// </param>
    /// <param name="vertexCount">Number of vertices/entries in the meshlet (its <c>m_nVertexCount</c>).</param>
    /// <param name="triangleCount">Number of triangles in the meshlet (its <c>m_nTriangleCount</c>).</param>
    /// <returns>
    /// <c>Indices</c>: <c>triangleCount * 3</c> meshlet-local vertex indices (the triangle topology).
    /// <c>Vertices</c>: the raw per-entry 14-bit field. For the first (identity) meshlet it equals the
    /// vertex list; for other meshlets it can contain duplicates, so treat it as raw data and take the
    /// vertex list from MIDX.
    /// </returns>
    /// <remarks>
    /// Triangle references are 6-bit. Vertices are introduced in increasing order through a 64-entry sliding
    /// window: a reference equal to <c>(maxIntroduced + 1) &amp; 63</c> introduces the next vertex, otherwise
    /// it reuses the vertex with that value within the current window. This lets meshlets with more than 64
    /// vertices address indices 64 and above.
    /// </remarks>
    public (int[] Vertices, int[] Indices) DecodeMeshlet(int entryOffset, int vertexCount, int triangleCount)
    {
        if (Resource?.Reader == null)
        {
            throw new InvalidOperationException("Resource reader is required to lazily read meshlet data.");
        }

        var entries = new uint[vertexCount];
        Resource.Reader.BaseStream.Position = Offset + (long)entryOffset * sizeof(uint);

        for (var i = 0; i < vertexCount; i++)
        {
            entries[i] = Resource.Reader.ReadUInt32();
        }

        var vertices = new int[vertexCount];

        for (var i = 0; i < vertexCount; i++)
        {
            vertices[i] = (int)(entries[i] >> 18);
        }

        var indices = new int[triangleCount * 3];
        var maxIntroduced = -1;

        for (var t = 0; t < triangleCount; t++)
        {
            var triangle = entries[t] & 0x3FFFFu;

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

        return (vertices, indices);
    }
}
