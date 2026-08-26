using System.Buffers;
using System.Runtime.InteropServices;
using ValveResourceFormat.Compression;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Blocks;

/// <summary>
/// "MSLT" block. Holds per-meshlet local index data.
/// </summary>
/// <remarks>
/// A mesh shader transforms each meshlet vertex once, then emits the local index buffer (0..vertexCount-1)
/// as primitives over those vertices. A vertex's attributes are fetched from MVTX at index
/// (m_nVertexOffset + localIndex). MIDX, the global index buffer, is only used by the classic vertex/fragment
/// draw path; the meshlet path does not need it.
/// <para>
/// Two on-disk encodings exist, distinguished by the buffer's <c>m_nMeshoptMeshletEncodeVersion</c> in CTRL
/// (see <see cref="EncodeVersion"/>):
/// </para>
/// <list type="bullet">
/// <item>Legacy (version &lt; 0): each meshlet vertex is one <see cref="uint"/> entry, so a meshlet spans
/// <c>vertexCount</c> consecutive entries and the meshlets are concatenated. Decode with <see cref="DecodeMeshlet"/>.</item>
/// <item>meshoptimizer meshlet codec (version 1): a sequence of per-meshlet chunks, each a <see cref="ushort"/>
/// header (body size in bits 0..9, <c>(align2(max(vc,tc)) - 1) &amp; 63</c> in bits 10..15) followed by a
/// meshopt meshlet blob. Decode with <see cref="DecodeMeshletCompressed"/>.</item>
/// </list>
/// </remarks>
public class MeshletBuffer : RawBinary
{
    /// <inheritdoc/>
    public override BlockType Type => BlockType.MSLT;

    /// <summary>
    /// The meshoptimizer meshlet codec encode version this decoder supports (see <see cref="DecodeMeshletCompressed"/>).
    /// </summary>
    public const int MeshoptMeshletEncodeVersion = 1;

    private int? encodeVersion;
    private byte[]? blockData;
    private int[]? chunkOffsets;

    /// <summary>
    /// Gets the meshopt meshlet encode version declared for this buffer in the CTRL block, or -1 for the
    /// legacy packed format (or when it cannot be determined). Version 1 is the meshoptimizer meshlet codec.
    /// </summary>
    public int EncodeVersion => encodeVersion ??= LookupEncodeVersion();

    /// <summary>
    /// Gets the number of packed entries the block holds.
    /// </summary>
    public int EntryCount => (int)(Size / sizeof(uint));

    /// <summary>
    /// Gets the number of meshlet chunks in a meshoptimizer-encoded block (<see cref="EncodeVersion"/> 1),
    /// i.e. how many meshlets <see cref="DecodeMeshletCompressedEntries"/> can address.
    /// </summary>
    public int CompressedMeshletCount
    {
        get
        {
            EnsureChunkOffsets();
            return chunkOffsets!.Length;
        }
    }

    /// <summary>
    /// Reads every packed entry, for handing the block to a mesh shader that decodes it itself.
    /// </summary>
    /// <param name="entries">Receives <see cref="EntryCount"/> entries.</param>
    public void ReadPackedEntries(Span<uint> entries)
    {
        if (Resource?.Reader == null)
        {
            throw new InvalidOperationException("Resource reader is required to lazily read meshlet data.");
        }

        Resource.Reader.BaseStream.Position = Offset;
        Resource.Reader.Read(MemoryMarshal.AsBytes(entries));
    }

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

    /// <summary>
    /// Number of packed <see cref="uint"/> entries a meshoptimizer-encoded meshlet decodes to: the vertex and
    /// triangle counts rounded up to a shared even value (the count both were encoded with).
    /// </summary>
    public static int CompressedEntryCount(int vertexCount, int triangleCount) => (Math.Max(vertexCount, triangleCount) + 1) & ~1;

    /// <summary>
    /// Decodes a single meshlet of the meshoptimizer meshlet codec format (<see cref="EncodeVersion"/> 1) into
    /// the same packed entry layout as the legacy format: <c>(vertexListValue &lt;&lt; 18) | (ref0 | ref1 &lt;&lt; 6 | ref2 &lt;&lt; 12)</c>,
    /// with the three references left as raw 6-bit values for the sliding-window resolution to interpret.
    /// </summary>
    /// <param name="meshletIndex">Zero-based index of the meshlet, in the order the descriptors appear in MDAT. Chunks tile the buffer in this order.</param>
    /// <param name="vertexCount">Number of vertices in the meshlet (its <c>m_nVertexCount</c>).</param>
    /// <param name="triangleCount">Number of triangles in the meshlet (its <c>m_nTriangleCount</c>).</param>
    /// <param name="entries">Receives <see cref="CompressedEntryCount"/> packed entries.</param>
    public void DecodeMeshletCompressedEntries(int meshletIndex, int vertexCount, int triangleCount, Span<uint> entries)
    {
        EnsureChunkOffsets();

        // meshopt was fed both counts as the same even value; padding vertices repeat the last value and
        // padding triangles are degenerate.
        var entryCount = CompressedEntryCount(vertexCount, triangleCount);

        var start = chunkOffsets![meshletIndex];
        var header = (ushort)(blockData![start] | (blockData[start + 1] << 8));
        var bodySize = header & 0x3FF;
        var body = blockData.AsSpan(start + 2, bodySize);

        Span<uint> decodedVertices = stackalloc uint[entryCount];
        Span<uint> decodedTriangles = stackalloc uint[entryCount];

        MeshOptimizerMeshletDecoder.DecodeMeshletRaw(decodedVertices, entryCount, decodedTriangles, entryCount, body);

        for (var i = 0; i < entryCount; i++)
        {
            var triangle = decodedTriangles[i];

            // Pack the three raw references as 6-bit fields, matching the legacy entry layout.
            var references = (triangle & 0x3F) | (((triangle >> 8) & 0x3F) << 6) | (((triangle >> 16) & 0x3F) << 12);
            entries[i] = (decodedVertices[i] << 18) | references;
        }
    }

    /// <summary>
    /// Decodes a single meshlet of the meshoptimizer meshlet codec format (<see cref="EncodeVersion"/> 1) into
    /// its vertex list and its local triangle index buffer, resolving references the same way as
    /// <see cref="DecodeMeshlet"/>.
    /// </summary>
    /// <param name="meshletIndex">Zero-based index of the meshlet, in the order the descriptors appear in MDAT. Chunks tile the buffer in this order.</param>
    /// <param name="vertexCount">Number of vertices in the meshlet (its <c>m_nVertexCount</c>).</param>
    /// <param name="triangleCount">Number of triangles in the meshlet (its <c>m_nTriangleCount</c>).</param>
    /// <param name="vertices">Receives <c>vertexCount</c> values: each the meshlet-local (m_nVertexOffset relative) MVTX index.</param>
    /// <param name="indices">Receives <c>triangleCount * 3</c> meshlet-local vertex indices into <paramref name="vertices"/>.</param>
    public void DecodeMeshletCompressed(int meshletIndex, int vertexCount, int triangleCount, Span<int> vertices, Span<int> indices)
    {
        var entryCount = CompressedEntryCount(vertexCount, triangleCount);
        Span<uint> entries = stackalloc uint[entryCount];
        DecodeMeshletCompressedEntries(meshletIndex, vertexCount, triangleCount, entries);

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
                    indices[t * 3 + k] = ++maxIntroduced;
                }
                else
                {
                    indices[t * 3 + k] = reference + 64 * ((maxIntroduced - reference) / 64);
                }
            }
        }
    }

    private void EnsureChunkOffsets()
    {
        if (chunkOffsets != null)
        {
            return;
        }

        if (Resource?.Reader == null)
        {
            throw new InvalidOperationException("Resource reader is required to lazily read meshlet data.");
        }

        var data = new byte[Size];
        Resource.Reader.BaseStream.Position = Offset;
        Resource.Reader.Read(data);
        blockData = data;

        var offsets = new List<int>();
        var position = 0;

        while (position + 2 <= data.Length)
        {
            offsets.Add(position);
            var header = (ushort)(data[position] | (data[position + 1] << 8));
            var bodySize = header & 0x3FF;
            position += 2 + bodySize;
        }

        chunkOffsets = [.. offsets];
    }

    private int LookupEncodeVersion()
    {
        if (Resource?.GetBlockByType(BlockType.CTRL) is not BinaryKV3 ctrl)
        {
            return -1;
        }

        var blockIndex = Resource.Blocks.IndexOf(this);
        var embeddedMeshes = ctrl.Data.Root.GetArray("embedded_meshes");

        if (embeddedMeshes == null)
        {
            return -1;
        }

        foreach (var embeddedMesh in embeddedMeshes)
        {
            var indexBuffers = embeddedMesh.GetArray("m_indexBuffers");

            if (indexBuffers == null)
            {
                continue;
            }

            foreach (var indexBuffer in indexBuffers)
            {
                if (indexBuffer.GetInt32Property("m_nBlockIndex") == blockIndex)
                {
                    return indexBuffer.GetInt32Property("m_nMeshoptMeshletEncodeVersion", -1);
                }
            }
        }

        return -1;
    }
}
