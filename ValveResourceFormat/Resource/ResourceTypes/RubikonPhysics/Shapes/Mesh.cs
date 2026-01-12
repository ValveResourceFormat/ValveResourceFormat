using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes
{
    /// <summary>
    /// Represents a mesh shape.
    /// </summary>
    public readonly struct Mesh
    {
        /// <summary>
        /// The node type for mesh BVH nodes.
        /// </summary>
        public enum NodeType
        {
#pragma warning disable CS1591
            SplitX = 0,
            SplitY = 1,
            SplitZ = 2,
            Leaf = 3,
#pragma warning restore CS1591
        }

        /// <summary>
        /// Represents a node in the mesh BVH.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public readonly struct Node
        {
            /// <summary>
            /// The minimum bounds.
            /// </summary>
            public readonly Vector3 Min;
            private readonly uint PackedTypeChildOffset;

            /// <summary>
            /// The node type/split axis.
            /// </summary>
            public NodeType Type => (NodeType)(PackedTypeChildOffset >> 30);

            /// <summary>
            /// The 2nd child offset, otherwise when <see cref="Type" /> is <see cref="NodeType.Leaf" />, this is the triangle count.
            /// </summary>
            public uint ChildOffset => PackedTypeChildOffset & 0x3FFFFFFF;

            /// <summary>
            /// The maximum bounds.
            /// </summary>
            public readonly Vector3 Max;

            /// <summary>
            /// If leaf node this is the offset into the associated triangle array
            /// </summary>
            public readonly uint TriangleOffset;

            /// <summary>
            /// Initializes a new instance of the <see cref="Node"/> struct.
            /// </summary>
            public Node(KVObject data)
            {
                Min = data.GetSubCollection("m_vMin").ToVector3();
                Max = data.GetSubCollection("m_vMax").ToVector3();
                PackedTypeChildOffset = data.GetUInt32Property("m_nChildren");
                TriangleOffset = data.GetUInt32Property("m_nTriangleOffset");
            }
        }

        /// <summary>
        /// Represents a triangle in the mesh.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public readonly struct Triangle
        {
            /// <summary>The X component of the triangle.</summary>
            public readonly int X;
            /// <summary>The Y component of the triangle.</summary>
            public readonly int Y;
            /// <summary>The Z component of the triangle.</summary>
            public readonly int Z;

            /// <summary>
            /// Initializes a new instance of the <see cref="Triangle"/> struct.
            /// </summary>
            public Triangle(KVObject data)
            {
                var indices = data.GetArray<object>("m_nIndex")!.Select(Convert.ToInt32).ToArray();

                if (indices.Length != 3)
                {
                    throw new InvalidDataException("Triangle must have 3 indices");
                }

                X = indices[0];
                Y = indices[1];
                Z = indices[2];
            }
        }

        /// <summary>
        /// Gets the minimum bounds.
        /// </summary>
        public Vector3 Min { get; }
        /// <summary>
        /// Gets the maximum bounds.
        /// </summary>
        public Vector3 Max { get; }


        /// <summary>
        /// Per triangle index to surface properties. Can be empty if the whole mesh has the same material.
        /// </summary>
        public int[] Materials { get; }

        /// <summary>
        /// Fraction 0..1 of coverage along YZ,ZX,XY sides of AABB
        /// </summary>
        public Vector3 OrthographicAreas { get; }
        /// <summary>
        /// Gets the raw data.
        /// </summary>
        public KVObject Data { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Mesh"/> struct.
        /// </summary>
        public Mesh(KVObject data)
        {
            Data = data;
            Min = data.GetSubCollection("m_vMin").ToVector3();
            Max = data.GetSubCollection("m_vMax").ToVector3();
            Materials = data.GetArray<object>("m_Materials")!.Select(Convert.ToInt32).ToArray();
            OrthographicAreas = data.GetSubCollection("m_vOrthographicAreas").ToVector3();
        }

        /// <summary>
        /// The mesh vertices in the space of the parent shape.
        /// </summary>
        public ReadOnlySpan<Vector3> GetVertices() => Hull.ParseVertices(Data);

        /// <summary>
        /// The nodes of the loose kd-tree to accelerate ray casts and volume queries against this mesh.
        /// </summary>
        public ReadOnlySpan<Node> ParseNodes()
        {
            if (Data.IsNotBlobType("m_Nodes"))
            {
                var nodesArr = Data.GetArray("m_Nodes");
                return nodesArr.Select(n => new Node(n)).ToArray();
            }

            return MemoryMarshal.Cast<byte, Node>(Data.GetArray<byte>("m_Nodes"));
        }

        /// <summary>
        /// The mesh triangles with additional topology information similar to the half-edge data structure.
        /// </summary>
        public ReadOnlySpan<Triangle> GetTriangles()
        {
            if (Data.IsNotBlobType("m_Triangles"))
            {
                var trianglesArr = Data.GetArray("m_Triangles");
                return trianglesArr.Select(t => new Triangle(t)).ToArray();
            }

            return MemoryMarshal.Cast<byte, Triangle>(Data.GetArray<byte>("m_Triangles"));
        }
    }
}
