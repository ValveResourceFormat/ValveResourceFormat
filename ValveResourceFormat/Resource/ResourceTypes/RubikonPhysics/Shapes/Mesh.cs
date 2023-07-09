using System;
using System.IO;
using System.Linq;
using System.Numerics;
using ValveResourceFormat.Serialization;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes
{
    public struct Mesh
    {
        public enum NodeType
        {
            SplitX = 0,
            SplitY = 1,
            SplitZ = 2,
            Leaf = 3,
        }

        public struct Node
        {
            public Vector3 Min { get; set; }
            public Vector3 Max { get; set; }

            /// <summary>
            /// The 2nd child offset and the node type/split axis.
            /// Type is stored in the first 2 MSBs.
            /// </summary>
            public uint Children { get; set; }

            /// <summary>
            /// If leaf node this is the offset into the associated triangle array
            /// </summary>
            public uint TriangleOffset { get; set; }

            public Node(IKeyValueCollection data)
            {
                Min = data.GetSubCollection("m_vMin").ToVector3();
                Max = data.GetSubCollection("m_vMax").ToVector3();
                Children = data.GetUInt32Property("m_nChildren");
                TriangleOffset = data.GetUInt32Property("m_nTriangleOffset");
            }

            public Node(ReadOnlySpan<byte> data)
            {
                Min = new Vector3(
                    BitConverter.ToSingle(data[..4]),
                    BitConverter.ToSingle(data.Slice(4, 4)),
                    BitConverter.ToSingle(data.Slice(8, 4))
                );

                Children = BitConverter.ToUInt32(data.Slice(12, 4));

                Max = new Vector3(
                    BitConverter.ToSingle(data.Slice(16, 4)),
                    BitConverter.ToSingle(data.Slice(20, 4)),
                    BitConverter.ToSingle(data.Slice(24, 4))
                );

                TriangleOffset = BitConverter.ToUInt32(data.Slice(28, 4));
            }
        }

        public struct Triangle
        {
            public int[] Indices { get; set; }

            public Triangle(IKeyValueCollection data)
            {
                Indices = data.GetArray<object>("m_nIndex").Select(Convert.ToInt32).ToArray();
                if (Indices.Length != 3)
                {
                    throw new InvalidDataException("Triangle must have 3 indices");
                }
            }

            public Triangle(ReadOnlySpan<byte> data)
            {
                Indices = new int[3];
                Indices[0] = BitConverter.ToInt32(data[..4]);
                Indices[1] = BitConverter.ToInt32(data.Slice(4, 4));
                Indices[2] = BitConverter.ToInt32(data.Slice(8, 4));
            }
        }

        public Vector3 Min { get; set; }
        public Vector3 Max { get; set; }


        /// <summary>
        /// Per triangle index to surface properties. Can be empty if the whole mesh has the same material.
        /// </summary>
        public int[] Materials { get; set; }

        /// <summary>
        /// The nodes of the loose kd-tree to accelerate ray casts and volume queries against this mesh.
        /// </summary>
        public Node[] Nodes { get; set; }

        /// <summary>
        /// The mesh vertices in the space of the parent shape.
        /// </summary>
        public Vector3[] Vertices { get; set; }

        /// <summary>
        /// The mesh triangles with additional topology information similar to the half-edge data structure.
        /// </summary>
        public Triangle[] Triangles { get; set; }

        /// <summary>
        /// Fraction 0..1 of coverage along YZ,ZX,XY sides of AABB
        /// </summary>
        public Vector3 OrthographicAreas { get; set; }

        public Mesh(IKeyValueCollection data)
        {
            Min = data.GetSubCollection("m_vMin").ToVector3();
            Max = data.GetSubCollection("m_vMax").ToVector3();
            Materials = data.GetArray<object>("m_Materials").Select(Convert.ToInt32).ToArray();
            Nodes = ParseNodes(data);
            Vertices = Hull.ParseVertices(data);
            Triangles = ParseTriangles(data);
            OrthographicAreas = data.GetSubCollection("m_vOrthographicAreas").ToVector3();
        }

        private static Node[] ParseNodes(IKeyValueCollection data)
        {
            if (data.IsNotBlobType("m_Nodes"))
            {
                var nodesArr = data.GetArray("m_Nodes");
                return nodesArr.Select(n => new Node(n)).ToArray();
            }

            var nodesBlob = data.GetArray<byte>("m_Nodes");
            return Enumerable.Range(0, nodesBlob.Length / 32)
                .Select(i => new Node(nodesBlob.AsSpan(i * 32, 32)))
                .ToArray();
        }

        private static Triangle[] ParseTriangles(IKeyValueCollection data)
        {
            if (data.IsNotBlobType("m_Triangles"))
            {
                var trianglesArr = data.GetArray("m_Triangles");
                return trianglesArr.Select(t => new Triangle(t)).ToArray();
            }

            var trianglesBlob = data.GetArray<byte>("m_Triangles");
            return Enumerable.Range(0, trianglesBlob.Length / 12)
                .Select(i => new Triangle(trianglesBlob.AsSpan(i * 12, 12)))
                .ToArray();
        }
    }
}
