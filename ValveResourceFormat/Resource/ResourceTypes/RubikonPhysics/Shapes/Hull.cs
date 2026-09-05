using System.Linq;
using System.Runtime.InteropServices;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes
{
    /// <summary>
    /// Represents a convex hull shape.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/physicslib/RnHull_t">RnHull_t</seealso>
    public readonly struct Hull
    {
        /// <summary>
        /// Represents a plane in the hull.
        /// </summary>
        /// <seealso href="https://s2v.app/SchemaExplorer/cs2/physicslib/RnPlane_t">RnPlane_t</seealso>
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
        /// <seealso href="https://s2v.app/SchemaExplorer/cs2/physicslib/RnHalfEdge_t">RnHalfEdge_t</seealso>
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
        /// <seealso href="https://s2v.app/SchemaExplorer/cs2/physicslib/RnFace_t">RnFace_t</seealso>
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
        /// <seealso href="https://s2v.app/SchemaExplorer/cs2/physicslib/CRegionSVM">CRegionSVM</seealso>
        public class Region
        {
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

            /// <summary>
            /// Raw node words of the region's compact SVM tree. The packing is not schema-enumerated
            /// and has not been decoded, so these are exposed opaquely rather than as typed nodes.
            /// </summary>
            public ReadOnlySpan<uint> GetNodes()
            {
                if (Data.IsNotBlobType("m_Nodes"))
                {
                    return Data.GetArray<object>("m_Nodes").Select(Convert.ToUInt32).ToArray();
                }

                return MemoryMarshal.Cast<byte, uint>(Data.GetArray<byte>("m_Nodes"));
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

        /// <summary>
        /// Gets the surface area of the hull. Absent on hulls compiled before this was tracked.
        /// </summary>
        public float SurfaceArea { get; }

        /// <summary>
        /// Gets the mass properties: a 3x3 inertia tensor in the upper-left block, with the mass-centre
        /// offset in the 4th column.
        /// </summary>
        public Matrix4x4 MassProperties { get; }

        /// <summary>
        /// Gets the hull flags. Not schema-enumerated; VRF has observed the raw values 0x4050, 1 and 3
        /// in shipped content, with no confirmed meaning.
        /// </summary>
        public uint Flags { get; }

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
            SurfaceArea = data.GetFloatProperty("m_flSurfaceArea");
            var massProperties = data.GetSubCollection("m_MassProperties");
            MassProperties = massProperties == null ? default : massProperties.ToMatrix4x4();
            Flags = data.GetUInt32Property("m_nFlags");

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

        /// <summary>
        /// Walks one face's edge loop and returns its vertex indices, in winding order. A hull face is a
        /// polygon of any size, so a consumer that needs triangles wants <see cref="GetFaceTriangles"/>.
        /// </summary>
        /// <param name="edges">The hull's half edges, from <see cref="GetEdges"/>.</param>
        /// <param name="face">The face to walk.</param>
        public static FaceVertexEnumerable GetFaceVertices(ReadOnlySpan<HalfEdge> edges, Face face)
            => new(edges, face.Edge);

        /// <summary>
        /// Triangulates one face as a fan from the first vertex of its edge loop, returning vertex index
        /// triples. A hull face is convex and planar, so a fan is a complete triangulation of it.
        /// </summary>
        /// <param name="edges">The hull's half edges, from <see cref="GetEdges"/>.</param>
        /// <param name="face">The face to triangulate.</param>
        public static FaceTriangleEnumerable GetFaceTriangles(ReadOnlySpan<HalfEdge> edges, Face face)
            => new(edges, face.Edge);

        /// <summary>The vertex indices around one hull face, in winding order.</summary>
        public readonly ref struct FaceVertexEnumerable
        {
            private readonly ReadOnlySpan<HalfEdge> edges;
            private readonly int startEdge;

            internal FaceVertexEnumerable(ReadOnlySpan<HalfEdge> edges, int startEdge)
            {
                this.edges = edges;
                this.startEdge = startEdge;
            }

            /// <summary>Returns an enumerator over the face's vertex indices.</summary>
            public Enumerator GetEnumerator() => new(edges, startEdge);

            /// <summary>Enumerates the vertex indices around one hull face.</summary>
            public ref struct Enumerator
            {
                private readonly ReadOnlySpan<HalfEdge> edges;
                private readonly int startEdge;
                private int edge;

                internal Enumerator(ReadOnlySpan<HalfEdge> edges, int startEdge)
                {
                    this.edges = edges;
                    this.startEdge = startEdge;
                    edge = -1;
                }

                /// <summary>The current vertex index.</summary>
                public readonly int Current => edges[edge].Origin;

                /// <summary>Advances to the next vertex of the loop.</summary>
                public bool MoveNext()
                {
                    if (edge == -1)
                    {
                        edge = startEdge;
                        return true;
                    }

                    edge = edges[edge].Next;

                    return edge != startEdge;
                }
            }
        }

        /// <summary>The triangles of one hull face, as vertex index triples.</summary>
        public readonly ref struct FaceTriangleEnumerable
        {
            private readonly ReadOnlySpan<HalfEdge> edges;
            private readonly int startEdge;

            internal FaceTriangleEnumerable(ReadOnlySpan<HalfEdge> edges, int startEdge)
            {
                this.edges = edges;
                this.startEdge = startEdge;
            }

            /// <summary>Returns an enumerator over the face's triangles.</summary>
            public Enumerator GetEnumerator() => new(edges, startEdge);

            /// <summary>Enumerates a hull face's triangles as a fan from its first vertex.</summary>
            public ref struct Enumerator
            {
                private readonly ReadOnlySpan<HalfEdge> edges;
                private readonly int startEdge;
                private int edge;
                private bool finished;

                internal Enumerator(ReadOnlySpan<HalfEdge> edges, int startEdge)
                {
                    this.edges = edges;
                    this.startEdge = startEdge;
                    edge = edges[startEdge].Next;
                    finished = false;
                    Current = default;
                }

                /// <summary>The current triangle, as vertex indices into the hull's positions.</summary>
                public (int A, int B, int C) Current { get; private set; }

                /// <summary>Advances to the next triangle of the fan.</summary>
                public bool MoveNext()
                {
                    if (finished || edge == startEdge)
                    {
                        finished = true;
                        return false;
                    }

                    var next = edges[edge].Next;

                    if (next == startEdge)
                    {
                        finished = true;
                        return false;
                    }

                    Current = (edges[startEdge].Origin, edges[edge].Origin, edges[next].Origin);
                    edge = next;

                    return true;
                }
            }
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
