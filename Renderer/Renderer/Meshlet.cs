using System.Runtime.InteropServices;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Small group of triangles on a mesh used for low level culling
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 16)]
    public readonly struct Meshlet
    {
        /// <summary>
        /// Packed axis-aligned bounding box stored as two 32-bit values.
        /// Each uint packs three 10-bit normalized components (x/y/z in 0..1023).
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct MeshletBounds
        {
            /// <summary>Packed min corner (packed x/y/z, 10 bits per component).</summary>
            public uint Min;
            /// <summary>Packed max corner (packed x/y/z, 10 bits per component).</summary>
            public uint Max;
        }

        /// <summary>
        /// Packed AABB for this meshlet, normalized in parent draw space.
        /// </summary>
        public MeshletBounds PackedAABB { get; init; }

        /// <summary>
        /// Quantized cone used for backface/cone culling.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct MeshletCone
        {
            /// <summary>Quantized X component of cone axis (-127..127).</summary>
            public sbyte ConeAxis0;
            /// <summary>Quantized Y component of cone axis (-127..127).</summary>
            public sbyte ConeAxis1;
            /// <summary>Quantized Z component of cone axis (-127..127).</summary>
            public sbyte ConeAxis2;
            /// <summary>Quantized cone cutoff/angle (-127..127).</summary>
            public sbyte ConeCutoff;
        }

        /// <summary>
        /// Quantized culling info (axis.xyz + cutoff) used for cone/backface culling.
        /// </summary>
        public MeshletCone CullingData { get; init; }

        /// <summary>
        /// Offset (in vertices) into the global vertex/index data for this meshlet.
        /// Used by the renderer to locate the meshlet's vertex data.
        /// </summary>
        public int VertexOffset { get; init; }

        /// <summary>Number of vertices contained in this meshlet.</summary>
        public uint VertexCount { get; init; }

        /// <summary>
        /// Offset (in triangles) into the global index/triangle list for this meshlet.
        /// </summary>
        public int TriangleOffset { get; init; }

        /// <summary>Number of triangles contained in this meshlet. Typically maxes out at 48.</summary>
        public uint TriangleCount { get; init; }

        public Meshlet(KVObject data)
        {
            var packedBounds = data.GetSubCollection("m_PackedAABB");
            var packedMin = packedBounds.GetUInt32Property("m_nMin");
            var packedMax = packedBounds.GetUInt32Property("m_nMax");

            var coneData = data.GetSubCollection("m_CullingData");
            var coneAxis = coneData.GetIntegerArray("m_ConeAxis");

            PackedAABB = new MeshletBounds()
            {
                Min = packedMin,
                Max = packedMax
            };

            CullingData = new MeshletCone()
            {
                ConeAxis0 = (sbyte)coneAxis[0],
                ConeAxis1 = (sbyte)coneAxis[1],
                ConeAxis2 = (sbyte)coneAxis[2],
                ConeCutoff = (sbyte)coneData.GetInt32Property("m_ConeCutoff"),
            };

            // note: these properties are not present in some old resources
            VertexOffset = data.GetInt32Property("m_nVertexOffset");
            VertexCount = data.GetUInt32Property("m_nVertexCount");
            TriangleOffset = data.GetInt32Property("m_nTriangleOffset");
            TriangleCount = data.GetUInt32Property("m_nTriangleCount");
        }
    };
}
