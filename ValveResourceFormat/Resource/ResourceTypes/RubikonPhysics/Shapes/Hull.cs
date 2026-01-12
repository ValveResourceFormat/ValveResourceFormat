using System.Linq;
using System.Runtime.InteropServices;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes
{
    /// <summary>
    /// Represents a convex hull shape.
    /// </summary>
    public readonly struct Hull
    {
        /// <summary>
        /// Represents a plane in the hull.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public readonly struct Plane
        {
            /// <summary>
            /// The plane normal.
            /// </summary>
            public readonly Vector3 Normal;
            /// <summary>
            /// The plane offset such that P: n*x - d = 0
            /// </summary>
            public readonly float Offset;

            /// <summary>
            /// Initializes a new instance of the <see cref="Plane"/> struct.
            /// </summary>
            public Plane(KVObject data)
            {
                Normal = data.GetSubCollection("m_vNormal").ToVector3();
                Offset = data.GetFloatProperty("m_flOffset");
            }
        }

        /// <summary>
        /// Represents a half-edge in the hull mesh.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public readonly struct HalfEdge
        {
            /// <summary>
            /// Next edge index in CCW circular list around face
            /// </summary>
            public readonly byte Next;
            /// <summary>
            /// The twin edge index.
            /// </summary>
            public readonly byte Twin;
            /// <summary>
            /// The origin vertex index.
            /// </summary>
            public readonly byte Origin;
            /// <summary>
            /// The face index.
            /// </summary>
            public readonly byte Face;

            /// <summary>
            /// Initializes a new instance of the <see cref="HalfEdge"/> struct.
            /// </summary>
            public HalfEdge(KVObject data)
            {
                Next = data.GetByteProperty("m_nNext");
                Twin = data.GetByteProperty("m_nTwin");
                Origin = data.GetByteProperty("m_nOrigin");
                Face = data.GetByteProperty("m_nFace");
            }
        }

        /// <summary>
        /// Represents a face in the hull mesh.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public readonly struct Face
        {
            /// <summary>
            /// Index of first edge in CCW circular list around face
            /// </summary>
            public readonly byte Edge;

            /// <summary>
            /// Initializes a new instance of the <see cref="Face"/> struct.
            /// </summary>
            public Face(KVObject data)
            {
                Edge = data.GetByteProperty("m_nEdge");
            }
        }

        /// <summary>
        /// Represents a region in the hull.
        /// </summary>
        public class Region
        {
            /// <summary>
            /// Gets the region nodes.
            /// </summary>
            public object[]? Nodes { get; }
            /// <summary>
            /// Gets the region data.
            /// </summary>
            public KVObject Data { get; }

            /// <summary>
            /// Initializes a new instance of the <see cref="Region"/> class.
            /// </summary>
            public Region(KVObject data)
            {
                Data = data;
                Nodes = null; // TODO
            }

            /// <summary>
            /// Hull face planes with outward pointing normals (n1, -d1, n2, -d2, ...)
            /// </summary>
            public ReadOnlySpan<Plane> GetPlanes()
            {
                if (Data.IsNotBlobType("m_Planes"))
                {
                    var planesArr = Data.GetArray("m_Planes");
                    return planesArr.Select(p => new Plane(p)).ToArray().AsSpan();
                }

                return MemoryMarshal.Cast<byte, Plane>(Data.GetArray<byte>("m_Planes"));
            }
        }

        /// <summary>
        /// Gets the centroid of the hull.
        /// </summary>
        public Vector3 Centroid { get; }

        /// <summary>
        /// Angular radius for CCD
        /// </summary>
        public float MaxAngularRadius { get; }

        /// <summary>
        /// Gets the region SVM data.
        /// </summary>
        public Region? RegionSVM { get; }

        /// <summary>
        /// Fraction 0..1 of coverage along YZ,ZX,XY sides of AABB
        /// </summary>
        public Vector3 OrthographicAreas { get; }

        /// <summary>
        /// Gets the volume of the hull.
        /// </summary>
        public float Volume { get; }

        //public AABB Bounds { get; set; }
        /// <summary>
        /// Gets the minimum bounds.
        /// </summary>
        public Vector3 Min { get; }
        /// <summary>
        /// Gets the maximum bounds.
        /// </summary>
        public Vector3 Max { get; }
        /// <summary>
        /// Gets the raw data.
        /// </summary>
        public KVObject Data { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Hull"/> struct.
        /// </summary>
        public Hull(KVObject data)
        {
            Centroid = data.GetSubCollection("m_vCentroid").ToVector3();
            MaxAngularRadius = data.GetFloatProperty("m_flMaxAngularRadius");
            OrthographicAreas = data.GetSubCollection("m_vOrthographicAreas").ToVector3();
            Volume = data.GetFloatProperty("m_flVolume");

            var bounds = data.GetSubCollection("m_Bounds");
            Min = bounds.GetSubCollection("m_vMinBounds").ToVector3();
            Max = bounds.GetSubCollection("m_vMaxBounds").ToVector3();

            var regionSVM = data.GetSubCollection("m_pRegionSVM");
            RegionSVM = regionSVM == null ? null : new Region(regionSVM);
            Data = data;
        }

        // 2023-11-4: Explicit vertex indices
        private static bool HasExplicitVertexIndices(KVObject data)
            => data.ContainsKey("m_VertexPositions");

        /// <summary>
        /// Hull vertex indices. Hulls can have up to 255 vertices.
        /// </summary>
        /// <remarks>Empty for resources compiled before 2023-11-04.</remarks>
        public Span<byte> GetVertices()
        {
            if (!HasExplicitVertexIndices(Data))
            {
                return [];
            }

            return Data.GetArray<byte>("m_Vertices");
        }

        /// <summary>
        /// Hull vertex positions.
        /// </summary>
        public ReadOnlySpan<Vector3> GetVertexPositions() => ParseVertices(Data);

        /// <summary>
        /// Hull half edges order such that each edge e is followed by its twin e' (e1, e1', e2, e2', ...)
        /// </summary>
        public ReadOnlySpan<HalfEdge> GetEdges()
        {
            if (Data.IsNotBlobType("m_Edges"))
            {
                var edgesArr = Data.GetArray("m_Edges");
                return edgesArr.Select(e => new HalfEdge(e)).ToArray();
            }

            return MemoryMarshal.Cast<byte, HalfEdge>(Data.GetArray<byte>("m_Edges"));
        }

        /// <summary>
        /// Hull faces.
        /// </summary>
        public ReadOnlySpan<Face> GetFaces()
        {
            if (Data.IsNotBlobType("m_Faces"))
            {
                var edgesArr = Data.GetArray("m_Faces");
                return edgesArr.Select(e => new Face(e)).ToArray();
            }

            return MemoryMarshal.Cast<byte, Face>(Data.GetArray<byte>("m_Faces"));
        }

        /// <summary>
        /// Hull face planes with outward pointing normals (n1, -d1, n2, -d2, ...)
        /// </summary>
        public ReadOnlySpan<Plane> GetPlanes()
        {
            if (Data.IsNotBlobType("m_Planes"))
            {
                var planesArr = Data.GetArray("m_Planes");
                return planesArr.Select(p => new Plane(p)).ToArray();
            }

            return MemoryMarshal.Cast<byte, Plane>(Data.GetArray<byte>("m_Planes"));
        }

        internal static ReadOnlySpan<Vector3> ParseVertices(KVObject data)
        {
            if (data.IsNotBlobType("m_Vertices"))
            {
                var verticesArr = data.GetArray("m_Vertices");
                return verticesArr.Select(v => v.ToVector3()).ToArray();
            }

            var verticesName = HasExplicitVertexIndices(data) ? "m_VertexPositions" : "m_Vertices";

            return MemoryMarshal.Cast<byte, Vector3>(data.GetArray<byte>(verticesName));
        }
    }
}
