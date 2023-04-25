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
        }

        public Vector3 Centroid { get; set; }

        /// <summary>
        /// Angular radius for CCD
        /// </summary>
        public float MaxAngularRadius { get; set; }

        /// <summary>
        /// Hull vertices (x1, y1, z1, x2, y2, z2, ...)
        /// </summary>
        public Vector3[] Vertices { get; set; }

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
            Planes = data.GetArray("m_Planes").Select(p => new Plane(p)).ToArray();
            Edges = ParseEdges(data);
            Faces = data.GetArray("m_Faces").Select(f => new Face()).ToArray(); // TODO: Implement
            OrthographicAreas = data.GetSubCollection("m_vOrthographicAreas").ToVector3();
            Volume = data.GetFloatProperty("m_flVolume");

            var bounds = data.GetSubCollection("m_Bounds");
            Min = bounds.GetSubCollection("m_vMinBounds").ToVector3();
            Max = bounds.GetSubCollection("m_vMaxBounds").ToVector3();
        }

        public static Vector3[] ParseVertices(IKeyValueCollection data)
        {
            if (data.IsNotBlobType("m_Vertices"))
            {
                var verticesArr = data.GetArray("m_Vertices");
                return verticesArr.Select(v => v.ToVector3()).ToArray();
            }

            var verticesBlob = data.GetArray<byte>("m_Vertices");
            return Enumerable.Range(0, verticesBlob.Length / 12)
                .Select(i => new Vector3(BitConverter.ToSingle(verticesBlob, i * 12),
                    BitConverter.ToSingle(verticesBlob, (i * 12) + 4),
                    BitConverter.ToSingle(verticesBlob, (i * 12) + 8)))
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
    }
}
