using System.Buffers;
using System.Runtime.InteropServices;

namespace ValveResourceFormat.Blocks;

/// <summary>
/// "MSLT" block. Holds packed per-meshlet local index data. Each meshlet vertex is one <see cref="uint"/>
/// entry, so a meshlet spans <c>vertexCount</c> consecutive entries and the meshlets are concatenated.
/// </summary>
/// <remarks>
/// A mesh shader transforms each meshlet vertex once, then emits the local index buffer (0..vertexCount-1)
/// as primitives over those vertices. A vertex's attributes are fetched from MVTX at index
/// (m_nVertexOffset + localIndex). MIDX, the global index buffer, is only used by the classic vertex/fragment
/// draw path; the meshlet path does not need it.
/// </remarks>
public class MeshletBuffer : RawBinary
{
    /// <inheritdoc/>
    public override BlockType Type => BlockType.MSLT;

    /// <summary>
    /// Decodes a single meshlet into its vertex list and its local triangle index buffer.
    /// </summary>
    /// <param name="entryOffset">Uint offset of the meshlet's entries: the summed <c>m_nVertexCount</c> of preceding meshlets (distinct from <c>m_nVertexOffset</c>).</param>
    /// <param name="vertexCount">Number of vertices/entries in the meshlet (its <c>m_nVertexCount</c>).</param>
    /// <param name="triangleCount">Number of triangles in the meshlet (its <c>m_nTriangleCount</c>).</param>
    /// <param name="vertices">Receives <c>vertexCount</c> values: the raw per-entry 14-bit field.</param>
    /// <param name="indices">Receives <c>triangleCount * 3</c> meshlet-local vertex indices.</param>
    /// <remarks>
    /// 6-bit references over a 64-entry sliding window: a reference of <c>(maxIntroduced + 1) &amp; 63</c>
    /// introduces the next vertex, any other value reuses one already in the window. Lets meshlets exceed 64
    /// vertices.
    /// </remarks>
    public void DecodeMeshlet(int entryOffset, int vertexCount, int triangleCount, Span<int> vertices, Span<int> indices)
    {
        if (Resource?.Reader == null)
        {
            throw new InvalidOperationException("Resource reader is required to lazily read meshlet data.");
        }

        var byteCount = vertexCount * sizeof(uint);
        var rented = ArrayPool<byte>.Shared.Rent(byteCount);

        try
        {
            var buffer = rented.AsSpan(0, byteCount);
            Resource.Reader.BaseStream.Position = Offset + (long)entryOffset * sizeof(uint);
            Resource.Reader.Read(buffer);

            var entries = MemoryMarshal.Cast<byte, uint>(buffer);

            for (var i = 0; i < vertexCount; i++)
            {
                vertices[i] = (int)(entries[i] >> 18);
            }

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
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }
}
