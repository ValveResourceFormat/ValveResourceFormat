using System;
using System.Linq;
using System.Numerics;
using ValveResourceFormat.Serialization;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes
{
    public struct Hull
    {
        public struct Plane
        {
            public Vector3 Normal { get; set; }
            /// <summary>
            /// The plane offset such that P: n*x - d = 0
            /// </summary>
            public float Offset { get; set; }

            public Plane(IKeyValueCollection data)
            {
                Normal = data.GetSubCollection("m_vNormal").ToVector3();
                Offset = data.GetFloatProperty("m_flOffset");
            }

            public Plane(ReadOnlySpan<byte> data)
            {
                Normal = new Vector3(BitConverter.ToSingle(data), BitConverter.ToSingle(data[4..8]), BitConverter.ToSingle(data[8..12]));
                Offset = BitConverter.ToSingle(data[12..16]);
            }
        }

        public struct HalfEdge
        {
            /// <summary>
            /// Next edge index in CCW circular list around face
            /// </summary>
            public byte Next { get; set; }
            public byte Twin { get; set; }
            public byte Origin { get; set; }
            public byte Face { get; set; }

            public HalfEdge(IKeyValueCollection data)
            {
                Next = data.GetByteProperty("m_nNext");
                Twin = data.GetByteProperty("m_nTwin");
                Origin = data.GetByteProperty("m_nOrigin");
                Face = data.GetByteProperty("m_nFace");
            }

            public HalfEdge(ReadOnlySpan<byte> data)
            {
                Next = data[0];
                Twin = data[1];
                Origin = data[2];
                Face = data[3];
            }
        }

        public struct Face
        {
            /// <summary>
            /// Index of first edge in CCW circular list around face
            /// </summary>
            public byte Edge { get; set; }

            public Face(IKeyValueCollection data)
            {
                Edge = data.GetByteProperty("m_nEdge");
            }

            public Face(ReadOnlySpan<byte> data)
            {
                Edge = data[0];
            }
        }

        public class Region
        {
            public Plane[] Planes { get; set; }
            public object[] Nodes { get; set; }

            public Region(IKeyValueCollection data)
            {
                Planes = ParsePlanes(data);
                Nodes = null; // TODO
            }
        }

        public Vector3 Centroid { get; set; }

        /// <summary>
        /// Angular radius for CCD
        /// </summary>
        public float MaxAngularRadius { get; set; }

        public Region RegionSVM { get; set; }

        /// <summary>
        /// Hull vertex indices. Hulls can have up to 255 vertices.
        /// </summary>
        /// </remarks> Empty for resources compiled before 2023-11-4. </remarks>
        public byte[] Vertices { get; set; }

        /// <summary>
        /// Hull vertex positions.
        /// /// </summary>
        public Vector3[] VertexPositions { get; set; }

        /// <summary>
        /// Hull face planes with outward pointing normals (n1, -d1, n2, -d2, ...)
        /// </summary>
        public Plane[] Planes { get; set; }

        /// <summary>
        /// Hull half edges order such that each edge e is followed by its twin e' (e1, e1', e2, e2', ...)
        /// </summary>
        public HalfEdge[] Edges { get; set; }

        public Face[] Faces { get; set; }

        /// <summary>
        /// Fraction 0..1 of coverage along YZ,ZX,XY sides of AABB
        /// </summary>
        public Vector3 OrthographicAreas { get; set; }

        public float Volume { get; set; }

        //public AABB Bounds { get; set; }
        public Vector3 Min { get; set; }
        public Vector3 Max { get; set; }

        public Hull(IKeyValueCollection data)
        {
            Centroid = data.GetSubCollection("m_vCentroid").ToVector3();
            MaxAngularRadius = data.GetFloatProperty("m_flMaxAngularRadius");
            Vertices = ParseVertices(data);
            VertexPositions = ParseVertexPositions(data);
            Edges = ParseEdges(data);
            Faces = ParseFaces(data);
            Planes = ParsePlanes(data);
            OrthographicAreas = data.GetSubCollection("m_vOrthographicAreas").ToVector3();
            Volume = data.GetFloatProperty("m_flVolume");

            var bounds = data.GetSubCollection("m_Bounds");
            Min = bounds.GetSubCollection("m_vMinBounds").ToVector3();
            Max = bounds.GetSubCollection("m_vMaxBounds").ToVector3();

            var regionSVM = data.GetSubCollection("m_pRegionSVM");
            RegionSVM = regionSVM == null ? null : new Region(regionSVM);
        }

        // 2023-11-4: Explicit vertex indices 
        public static bool HasExplicitVertexIndices(IKeyValueCollection data)
            => data.ContainsKey("m_VertexPositions");

        public static byte[] ParseVertices(IKeyValueCollection data)
        {
            if (!HasExplicitVertexIndices(data))
            {
                return Array.Empty<byte>();
            }

            var verticesBlob = data.GetArray<byte>("m_Vertices");
            return verticesBlob.ToArray();
        }

        public static Vector3[] ParseVertexPositions(IKeyValueCollection data)
        {
            if (data.IsNotBlobType("m_Vertices"))
            {
                var verticesArr = data.GetArray("m_Vertices");
                return verticesArr.Select(v => v.ToVector3()).ToArray();
            }

            var vertexPositionsBlob = HasExplicitVertexIndices(data)
                ? data.GetArray<byte>("m_VertexPositions")
                : data.GetArray<byte>("m_Vertices");

            return Enumerable.Range(0, vertexPositionsBlob.Length / 12)
                .Select(i => new Vector3(BitConverter.ToSingle(vertexPositionsBlob, i * 12),
                    BitConverter.ToSingle(vertexPositionsBlob, (i * 12) + 4),
                    BitConverter.ToSingle(vertexPositionsBlob, (i * 12) + 8)))
                .ToArray();
        }

        private static HalfEdge[] ParseEdges(IKeyValueCollection data)
        {
            if (data.IsNotBlobType("m_Edges"))
            {
                var edgesArr = data.GetArray("m_Edges");
                return edgesArr.Select(e => new HalfEdge(e)).ToArray();
            }

            var edgesBlob = data.GetArray<byte>("m_Edges");
            return Enumerable.Range(0, edgesBlob.Length / 4)
                .Select(i => new HalfEdge(edgesBlob.AsSpan(i * 4, 4)))
                .ToArray();
        }

        private static Face[] ParseFaces(IKeyValueCollection data)
        {
            if (data.IsNotBlobType("m_Faces"))
            {
                var edgesArr = data.GetArray("m_Faces");
                return edgesArr.Select(e => new Face(e)).ToArray();
            }

            var edgesBlob = data.GetArray<byte>("m_Faces");
            return Enumerable.Range(0, edgesBlob.Length / 1)
                .Select(i => new Face(edgesBlob.AsSpan(i * 1, 1)))
                .ToArray();
        }

        private static Plane[] ParsePlanes(IKeyValueCollection data)
        {
            if (data.IsNotBlobType("m_Planes"))
            {
                var planesArr = data.GetArray("m_Planes");
                return planesArr.Select(p => new Plane(p)).ToArray();
            }

            var planesBlob = data.GetArray<byte>("m_Planes");
            return Enumerable.Range(0, planesBlob.Length / 16)
                .Select(i => new Plane(planesBlob.AsSpan(i * 16, 16)))
                .ToArray();
        }
    }
}
