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

            var entryCount = packedIndices.EntryCount;

            if (entryCount == 0)
            {
                return null;
            }

            var descriptors = new MeshletDescriptor[meshlets.Count];

            // Meshlet segments tile the block by vertex count, in the order the meshlets were read. The
            // running sum is what MeshletBuffer.DecodeMeshlet is tested against; m_nTriangleOffset carries
            // the same value in the content seen so far, but nothing depends on that here.
            var entryOffset = 0u;

            for (var i = 0; i < meshlets.Count; i++)
            {
                var meshlet = meshlets[i];

                if (entryOffset + meshlet.VertexCount > (uint)entryCount
                    || meshlet.VertexCount > MeshletLimits.MaxVertices
                    || meshlet.TriangleCount > MeshletLimits.MaxPrimitives)
                {
                    // A meshlet the workgroup cannot hold, or one running past the block, draws nothing
                    // rather than reading somebody else's entries
                    descriptors[i] = default;
                    skippedMeshlets++;
                    entryOffset += meshlet.VertexCount;
                    continue;
                }

                descriptors[i] = new MeshletDescriptor
                {
                    EntryOffset = entryOffset,
                    VertexOffset = (uint)meshlet.VertexOffset,
                    VertexCount = meshlet.VertexCount,
                    TriangleCount = meshlet.TriangleCount,
                };

                entryOffset += meshlet.VertexCount;
            }

            var entries = new uint[entryCount];
            packedIndices.ReadPackedEntries(entries);

            var packedBuffer = new StorageBuffer(ReservedBufferSlots.MeshletPackedIndices, $"{name} MSLT");
            packedBuffer.Create<uint>(entries, BufferUsage.Static);

            var descriptorBuffer = new StorageBuffer(ReservedBufferSlots.MeshletDescriptors, $"{name} meshlets");
            descriptorBuffer.Create<MeshletDescriptor>(descriptors, BufferUsage.Static);

            return new MeshletBuffers(packedBuffer, descriptorBuffer, meshlets.Count);
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
