using System.Runtime.InteropServices;
using ValveResourceFormat.Blocks;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Vertex attribute encodings a mesh shader can fetch itself, matching the <c>VERTEX_FORMAT_</c> codes in
    /// <c>common/vertex_fetch.slang</c>. A format missing here reads as <see cref="None"/>, which the shader
    /// substitutes a constant for rather than misreading the buffer.
    /// </summary>
    public enum VertexFetchFormat : uint
    {
        /// <summary>The mesh does not carry this attribute, or it is in an encoding the fetch cannot read.</summary>
        None = 0,
        /// <summary>Three 32 bit floats.</summary>
        R32G32B32Float = 1,
        /// <summary>Two 32 bit floats.</summary>
        R32G32Float = 2,
        /// <summary>One 32 bit word, the packed tangent frame.</summary>
        R32Uint = 3,
        /// <summary>Two 16 bit floats.</summary>
        R16G16Float = 4,
        /// <summary>Two 16 bit signed normalized values.</summary>
        R16G16Snorm = 5,
        /// <summary>Two 16 bit unsigned normalized values.</summary>
        R16G16Unorm = 6,
    }

    /// <summary>
    /// One meshlet as the mesh shader reads it, mirroring <c>MeshletDescriptor</c> in
    /// <c>common/mesh_shader.slang</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MeshletDescriptor
    {
        /// <summary>Index of the meshlet's first MSLT entry.</summary>
        public uint EntryOffset;

        /// <summary>Vertex the meshlet's vertex list is relative to.</summary>
        public uint VertexOffset;

        /// <summary>Vertices, and so MSLT entries, the meshlet spans.</summary>
        public uint VertexCount;

        /// <summary>Triangles the meshlet holds.</summary>
        public uint TriangleCount;
    }

    /// <summary>
    /// The MSLT block and its meshlet table on the GPU, for the mesh shader to decode into triangles. Held per
    /// mesh resource, since every draw call of a mesh indexes the same two buffers.
    /// </summary>
    public sealed class MeshletBuffers
    {
        /// <summary>Gets the packed MSLT entries.</summary>
        public StorageBuffer PackedIndices { get; }

        /// <summary>Gets the per meshlet vertex and triangle ranges.</summary>
        public StorageBuffer Descriptors { get; }

        /// <summary>Gets the number of meshlets described.</summary>
        public int MeshletCount { get; }

        private MeshletBuffers(StorageBuffer packedIndices, StorageBuffer descriptors, int meshletCount)
        {
            PackedIndices = packedIndices;
            Descriptors = descriptors;
            MeshletCount = meshletCount;
        }

        /// <summary>
        /// Uploads a mesh's meshlet data, or returns <see langword="null"/> when it has none to draw with.
        /// </summary>
        /// <param name="name">Mesh name the buffers are labelled with.</param>
        /// <param name="meshlets">The mesh's meshlets, in the order they were read.</param>
        /// <param name="packedIndices">The mesh resource's MSLT block.</param>
        /// <param name="skippedMeshlets">Receives the number of meshlets too large for a workgroup to hold.</param>
        public static MeshletBuffers? Create(string name, List<Meshlet> meshlets, MeshletBuffer? packedIndices, out int skippedMeshlets)
        {
            skippedMeshlets = 0;

            if (packedIndices == null || meshlets.Count == 0)
            {
                return null;
            }

            var descriptors = new MeshletDescriptor[meshlets.Count];
            uint[] entries;

            var encodeVersion = packedIndices.EncodeVersion;

            if (encodeVersion == MeshletBuffer.MeshoptMeshletEncodeVersion)
            {
                // meshoptimizer meshlet codec: decode each meshlet on the CPU and re-pack it into the same
                // packed-entry layout the mesh shader reads, so the shader stays unchanged.
                entries = BuildCompressedEntries(packedIndices, meshlets, descriptors, ref skippedMeshlets);
            }
            else if (encodeVersion < 0)
            {
                entries = BuildPackedEntries(packedIndices, meshlets, descriptors, ref skippedMeshlets);
            }
            else
            {
                // An encoding this build cannot decode into entries; fall back to the classic index path.
                return null;
            }

            if (entries.Length == 0)
            {
                return null;
            }

            var packedBuffer = new StorageBuffer(ReservedBufferSlots.MeshletPackedIndices, $"{name} MSLT");
            packedBuffer.Create<uint>(entries, BufferUsage.Static);

            var descriptorBuffer = new StorageBuffer(ReservedBufferSlots.MeshletDescriptors, $"{name} meshlets");
            descriptorBuffer.Create<MeshletDescriptor>(descriptors, BufferUsage.Static);

            return new MeshletBuffers(packedBuffer, descriptorBuffer, meshlets.Count);
        }

        /// <summary>
        /// Reads the legacy packed MSLT block verbatim and fills in each meshlet's descriptor.
        /// </summary>
        private static uint[] BuildPackedEntries(MeshletBuffer packedIndices, List<Meshlet> meshlets, MeshletDescriptor[] descriptors, ref int skippedMeshlets)
        {
            var entryCount = packedIndices.EntryCount;

            if (entryCount == 0)
            {
                return [];
            }

            var useTriangleOffset = HasUsableTriangleOffsets(meshlets, entryCount);

            // Where the file does not carry the offsets, the segments tile the block by vertex count in the
            // order the meshlets were read, which is what MeshletBuffer.DecodeMeshlet is tested against.
            var tiledOffset = 0u;

            for (var i = 0; i < meshlets.Count; i++)
            {
                var meshlet = meshlets[i];
                var entryOffset = useTriangleOffset ? (uint)meshlet.TriangleOffset : tiledOffset;

                tiledOffset += meshlet.VertexCount;

                // A meshlet's triangles ride in the spare bits of its own vertex list entries, so one with
                // more triangles than vertices cannot be laid out that way, and reading it would take the
                // overflow from whatever meshlet follows. No content seen so far has one.
                if (entryOffset + meshlet.VertexCount > (uint)entryCount
                    || meshlet.VertexCount > MeshletLimits.MaxVertices
                    || meshlet.TriangleCount > MeshletLimits.MaxPrimitives
                    || meshlet.TriangleCount > meshlet.VertexCount)
                {
                    // A meshlet the workgroup cannot hold, or one running past the block, draws nothing
                    // rather than reading somebody else's entries
                    descriptors[i] = default;
                    skippedMeshlets++;
                    continue;
                }

                descriptors[i] = new MeshletDescriptor
                {
                    EntryOffset = entryOffset,
                    VertexOffset = (uint)meshlet.VertexOffset,
                    VertexCount = meshlet.VertexCount,
                    TriangleCount = meshlet.TriangleCount,
                };
            }

            var entries = new uint[entryCount];
            packedIndices.ReadPackedEntries(entries);

            return entries;
        }

        /// <summary>
        /// Decodes a meshoptimizer-encoded MSLT block into the legacy packed-entry layout the mesh shader
        /// reads, with each meshlet's slice tiled by its entry count in read order. The shader resolves the
        /// packed references through the same sliding window as the legacy format, so meshlets over 64 vertices
        /// are handled there rather than here.
        /// </summary>
        private static uint[] BuildCompressedEntries(MeshletBuffer packedIndices, List<Meshlet> meshlets, MeshletDescriptor[] descriptors, ref int skippedMeshlets)
        {
            var totalEntries = 0u;

            foreach (var meshlet in meshlets)
            {
                totalEntries += (uint)MeshletBuffer.CompressedEntryCount((int)meshlet.VertexCount, (int)meshlet.TriangleCount);
            }

            var entries = new uint[totalEntries];
            var entryOffset = 0u;

            // A mesh's meshlets can span several MSLT blocks, but only the first is available here. Meshlets
            // whose chunk lives in a later block are skipped, matching the legacy path's bounds check.
            var chunkCount = packedIndices.CompressedMeshletCount;

            for (var i = 0; i < meshlets.Count; i++)
            {
                var meshlet = meshlets[i];
                var entryCount = (uint)MeshletBuffer.CompressedEntryCount((int)meshlet.VertexCount, (int)meshlet.TriangleCount);
                var offset = entryOffset;

                entryOffset += entryCount;

                if (i >= chunkCount
                    || meshlet.VertexCount == 0
                    || meshlet.VertexCount > MeshletLimits.MaxVertices
                    || meshlet.TriangleCount > MeshletLimits.MaxPrimitives)
                {
                    // A meshlet whose chunk is missing, or that the workgroup cannot hold, draws nothing.
                    descriptors[i] = default;
                    skippedMeshlets++;
                    continue;
                }

                packedIndices.DecodeMeshletCompressedEntries(i, (int)meshlet.VertexCount, (int)meshlet.TriangleCount, entries.AsSpan((int)offset, (int)entryCount));

                descriptors[i] = new MeshletDescriptor
                {
                    EntryOffset = offset,
                    VertexOffset = (uint)meshlet.VertexOffset,
                    VertexCount = meshlet.VertexCount,
                    TriangleCount = meshlet.TriangleCount,
                };
            }

            return entries;
        }

        /// <summary>
        /// Whether <c>m_nTriangleOffset</c> can be taken as each meshlet's offset into the block. It carries
        /// exactly that, but resources predating the field leave it at zero, and taking that literally would
        /// point every meshlet at the first entry. Offsets have to stay inside the block and advance with the
        /// meshlets to be believed.
        /// </summary>
        private static bool HasUsableTriangleOffsets(List<Meshlet> meshlets, int entryCount)
        {
            for (var i = 0; i < meshlets.Count; i++)
            {
                var meshlet = meshlets[i];

                if (meshlet.TriangleOffset < 0 || (uint)meshlet.TriangleOffset + meshlet.VertexCount > (uint)entryCount)
                {
                    return false;
                }

                if (i > 0 && meshlet.TriangleOffset <= meshlets[i - 1].TriangleOffset)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Binds both buffers to their reserved slots.</summary>
        public void Bind()
        {
            PackedIndices.BindBufferBase();
            Descriptors.BindBufferBase();
        }

        /// <summary>Frees the GPU buffers.</summary>
        public void Delete()
        {
            PackedIndices.Delete();
            Descriptors.Delete();
        }
    }
}
