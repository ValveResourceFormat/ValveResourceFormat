using System.Globalization;
using System.IO;
using System.Linq;
using Datamodel;
using ValveResourceFormat.IO.ContentFormats.DmxModel;
using ValveResourceFormat.IO.ContentFormats.HalfEdgeMesh;
using ValveResourceFormat.IO.ContentFormats.ValveMap;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.RubikonPhysics;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes.Mesh;

namespace ValveResourceFormat.IO
{
    /// <summary>
    /// Matches vertices between render meshes and physics meshes.
    /// </summary>
    public class PhysicsVertexMatcher
    {
        /// <summary>
        /// Contains physics mesh data and tracks deleted vertices.
        /// </summary>
        public class PhysMeshData
        {
            /// <summary>Gets the mesh descriptor.</summary>
            public MeshDescriptor Mesh { get; }
            /// <summary>Gets the array of vertex positions.</summary>
            public Vector3[] VertexPositions { get; }
            /// <summary>Gets the array of triangles.</summary>
            public Triangle[] Triangles { get; }
            /// <summary>Gets the physics tree nodes.</summary>
            public Node[] PhysicsTree { get; }
            /// <summary>Gets the set of deleted vertex indices.</summary>
            public HashSet<int> DeletedVertexIndices { get; }

            /// <summary>
            /// Initializes a new instance of the <see cref="PhysMeshData"/> class.
            /// </summary>
            public PhysMeshData(MeshDescriptor mesh)
            {
                Mesh = mesh;

                VertexPositions = mesh.Shape.GetVertices().ToArray();
                Triangles = mesh.Shape.GetTriangles().ToArray();
                PhysicsTree = mesh.Shape.ParseNodes().ToArray();

                DeletedVertexIndices = [];
                DeletedVertexIndices.EnsureCapacity(VertexPositions.Length / 4);
            }
        }

        /// <summary>Gets the list of physics meshes.</summary>
        public List<PhysMeshData> PhysicsMeshes { get; } = [];

        /// <summary>
        /// Initializes a new instance of the <see cref="PhysicsVertexMatcher"/> class.
        /// </summary>
        public PhysicsVertexMatcher(MeshDescriptor[] meshes)
        {
            for (var i = 0; i < meshes.Length; i++)
            {
                PhysicsMeshes.Add(new PhysMeshData(meshes[i]));
            }
        }

        /*
          Deleting vertices might inadvertently eat up good triangles, but I couldn't
          get the triangle delete method to work as good as vertex delete.

        public void TryMatchRenderTriangleToPhysics(ReadOnlySpan<int> renderMeshTriangle)
        {
            if (RenderToPhys.TryGetValue(renderMeshTriangle[0], out var i0)
             && RenderToPhys.TryGetValue(renderMeshTriangle[1], out var i1)
             && RenderToPhys.TryGetValue(renderMeshTriangle[2], out var i2))
            {
                DeletedTriangles.Add((i0, i1, i2));
            }
        }
        */

        record struct RnMeshNodeWithIndex(int Index, Node Node);
        /// <summary>Gets or sets the last set of positions scanned.</summary>
        public object? LastPositions { get; set; }
        /// <summary>
        /// Scans physics meshes to find matching vertices from render mesh positions.
        /// </summary>
        public void ScanPhysicsPointCloudForMatches(ReadOnlySpan<Vector3> renderMeshPositions, IProgress<string>? progressReporter)
        {
            Span<int> triangleIndices = [0, 0, 0];

            var localMatches = new HashSet<int>(capacity: renderMeshPositions.Length);
            var stack = new Stack<RnMeshNodeWithIndex>(64);

            for (var i = 0; i < PhysicsMeshes.Count; i++)
            {
                var meshData = PhysicsMeshes[i];

                localMatches.Clear();
                stack.Clear();

                for (var j = 0; j < renderMeshPositions.Length; ++j)
                {
                    var renderPosition = renderMeshPositions[j];
                    const float epsilon = 0.016f;

                    stack.Push(new(0, meshData.PhysicsTree[0])); // root

                    while (stack.TryPop(out var nodeWithIndex))
                    {
                        var node = nodeWithIndex.Node;
                        var nodeContains =
                            renderPosition.X >= node.Min.X && renderPosition.X <= node.Max.X &&
                            renderPosition.Y >= node.Min.Y && renderPosition.Y <= node.Max.Y &&
                            renderPosition.Z >= node.Min.Z && renderPosition.Z <= node.Max.Z;

                        if (!nodeContains)
                        {
                            continue;
                        }

                        if (node.Type != NodeType.Leaf)
                        {
                            var id = nodeWithIndex.Index + 1; // GetLeftChild
                            stack.Push(new(id, meshData.PhysicsTree[id]));

                            id = nodeWithIndex.Index + (int)node.ChildOffset; // GetRightChild
                            stack.Push(new(id, meshData.PhysicsTree[id]));

                            continue;
                        }

                        var triangleOffset = node.TriangleOffset;
                        var triangleCount = node.ChildOffset; // Same packing

                        for (var k = 0; k < triangleCount; k++)
                        {
                            var triangle = meshData.Triangles[triangleOffset + k];

                            triangleIndices[0] = triangle.X;
                            triangleIndices[1] = triangle.Y;
                            triangleIndices[2] = triangle.Z;

                            for (var t = 0; t < 3; t++)
                            {
                                var pos = meshData.VertexPositions[triangleIndices[t]];
                                if (Vector3.DistanceSquared(pos, renderPosition) < epsilon)
                                {
                                    localMatches.Add(triangleIndices[t]); // TODO: Add to DeletedVertexIndices
                                }
                            }
                        }
                    }
                }

                meshData.DeletedVertexIndices.UnionWith(localMatches);

#if DEBUG
                var matched = (float)localMatches.Count / renderMeshPositions.Length * 100f;
                progressReporter?.Report($"{nameof(PhysicsVertexMatcher)}: Matched {matched:F2}% ({localMatches.Count} vertices) of rendermesh to physics vertices!");
#endif

            }
        }
    }

    // Most of the work is handled by HalfEdgeMesh.cs, it handles building and making sure the half edge mesh is valid
    // All attribute data lives in data streams attached to the mesh components (position per vertex, corner data per half edge, material per face)
    // GenerateMesh() loops through the mesh and writes the data to the vmap in the correct format
    internal class HammerMeshBuilder
    {
        [Flags]
        public enum EdgeFlag
        {
            None = 0x0,
            SoftNormals = 0x1,
            HardNormals = 0x2,
        }

        public class VertexStreams
        {
            public List<Vector3> positions = [];
            public List<Vector2> texcoords = [];
            public List<Vector2> texcoords1 = [];
            public List<Vector3> normals = [];
            public List<Vector4> tangents = [];
            public List<Vector4> VertexPaintBlendParams = [];
            public List<Vector4> VertexPaintTintColor = [];
        }

        public int FacesRemoved;
        public int OriginalFaceCount;

        private readonly HalfEdgeMesh HalfEdgeMesh = new();
        private readonly List<VertexHandle> Vertices = [];

        private readonly VertexData<Vector3> Positions;
        private readonly HalfEdgeData<Vector2> TextureCoords;
        private readonly HalfEdgeData<Vector2> TextureCoords1;
        private readonly HalfEdgeData<Vector3> Normals;
        private readonly HalfEdgeData<Vector4> Tangents;
        private readonly HalfEdgeData<Vector4> VertexPaintBlendParams;
        private readonly HalfEdgeData<Vector4> VertexPaintTintColor;
        private readonly FaceData<int> MaterialIndex;

        private readonly List<string> Materials = [];
        private readonly Dictionary<string, int> MaterialIds = [];

        // Source data for the vertices added through AddVertices(), indexed by input vertex index
        // read in order to propagate the vertex data onto the half edges
        private VertexStreams SourceStreams = new();

        public PhysicsVertexMatcher? PhysicsVertexMatcher { get; init; }
        public IProgress<string>? ProgressReporter { get; init; }

        public HammerMeshBuilder()
        {
            Positions = HalfEdgeMesh.CreateVertexData<Vector3>(nameof(Positions));
            TextureCoords = HalfEdgeMesh.CreateHalfEdgeData<Vector2>(nameof(TextureCoords));
            TextureCoords1 = HalfEdgeMesh.CreateHalfEdgeData<Vector2>(nameof(TextureCoords1));
            Normals = HalfEdgeMesh.CreateHalfEdgeData<Vector3>(nameof(Normals));
            Tangents = HalfEdgeMesh.CreateHalfEdgeData<Vector4>(nameof(Tangents));
            VertexPaintBlendParams = HalfEdgeMesh.CreateHalfEdgeData<Vector4>(nameof(VertexPaintBlendParams));
            VertexPaintTintColor = HalfEdgeMesh.CreateHalfEdgeData<Vector4>(nameof(VertexPaintTintColor));
            MaterialIndex = HalfEdgeMesh.CreateFaceData<int>(nameof(MaterialIndex));

            HalfEdgeMesh.OnCopyFaceVertexData = (dst, src) =>
            {
                TextureCoords[dst] = TextureCoords[src];
                TextureCoords1[dst] = TextureCoords1[src];
                Normals[dst] = Normals[src];
                Tangents[dst] = Tangents[src];
                VertexPaintBlendParams[dst] = VertexPaintBlendParams[src];
                VertexPaintTintColor[dst] = VertexPaintTintColor[src];
            };

            HalfEdgeMesh.OnClearFaceVertexData = (hEdge) =>
            {
                TextureCoords[hEdge] = default;
                TextureCoords1[hEdge] = default;
                Normals[hEdge] = default;
                Tangents[hEdge] = default;
                VertexPaintBlendParams[hEdge] = default;
                VertexPaintTintColor[hEdge] = default;
            };
        }

        public CDmePolygonMesh GenerateMesh()
        {
#if DEBUG
            if (FacesRemoved > 0)
            {
                ProgressReporter?.Report($"{nameof(HammerMeshBuilder)}: Removed '{FacesRemoved}' of '{OriginalFaceCount - FacesRemoved}' faces");
            }
#endif

            // merge coplanar triangle pairs into quads before writing to the vmap
            // currently merging faces by material, if materials differ the triangles wont be merget into a quad
            // TODO: there may possibly be smarter heuristics to merge by
            var quadsMerged = HalfEdgeMesh.UntriangulateMesh(Positions, (hFaceA, hFaceB) => MaterialIndex[hFaceA] == MaterialIndex[hFaceB]);

#if DEBUG
            if (quadsMerged > 0)
            {
                ProgressReporter?.Report($"{nameof(HammerMeshBuilder)}: Untriangulated '{quadsMerged}' triangle pairs into quads");
            }
#endif

            // dissolving edges leaves holes in the component lists, build remap tables so the vmap gets dense indices
            // twin half edges are freed in whole pairs, so surviving pairs stay adjacent and both halves map to newIndex / 2
            var halfEdgeRemap = new int[HalfEdgeMesh.HalfEdgeCount];
            var activeHalfEdgeCount = 0;
            for (var i = 0; i < HalfEdgeMesh.HalfEdgeCount; i++)
            {
                halfEdgeRemap[i] = HalfEdgeMesh.IsHalfEdgeAllocated(i) ? activeHalfEdgeCount++ : -1;
            }

            var faceRemap = new int[HalfEdgeMesh.FaceCount];
            var activeFaceCount = 0;
            for (var i = 0; i < HalfEdgeMesh.FaceCount; i++)
            {
                faceRemap[i] = HalfEdgeMesh.IsFaceAllocated(i) ? activeFaceCount++ : -1;
            }

            var mesh = new CDmePolygonMesh();

            var faceMaterialIndices = CreateStream<IntArray, int>(8, "materialindex:0");
            var faceFlags = CreateStream<IntArray, int>(3, "flags:0");
            mesh.FaceData.Streams.Add(faceMaterialIndices);
            mesh.FaceData.Streams.Add(faceFlags);

            var texcoords = CreateStream<Vector2Array, Vector2>(1, "texcoord:0");
            var texcoords1 = CreateStream<Vector2Array, Vector2>(1, "texcoord:1", "texcoord1");
            var vertexpaintblendparams = CreateStream<Vector4Array, Vector4>(1, "VertexPaintBlendParams:0");
            var vertexpainttintcolor = CreateStream<Vector4Array, Vector4>(1, "VertexPaintTintColor:0");
            var normals = CreateStream<Vector3Array, Vector3>(1, "normal:0");
            var tangents = CreateStream<Vector4Array, Vector4>(1, "tangent:0");
            mesh.FaceVertexData.Streams.Add(texcoords);
            mesh.FaceVertexData.Streams.Add(texcoords1);
            mesh.FaceVertexData.Streams.Add(vertexpaintblendparams);
            mesh.FaceVertexData.Streams.Add(vertexpainttintcolor);
            mesh.FaceVertexData.Streams.Add(normals);
            mesh.FaceVertexData.Streams.Add(tangents);

            var vertexPositions = CreateStream<Vector3Array, Vector3>(3, "position:0");
            mesh.VertexData.Streams.Add(vertexPositions);

            var edgeFlags = CreateStream<IntArray, int>(3, "flags:0");
            mesh.EdgeData.Streams.Add(edgeFlags);

            for (var i = 0; i < HalfEdgeMesh.VertexCount; i++)
            {
                var vertexDataIndex = mesh.VertexData.Size;

                var vertexEdge = Vertices[i].Edge.Index;
                mesh.VertexEdgeIndices.Add(vertexEdge == -1 ? -1 : halfEdgeRemap[vertexEdge]);

                mesh.VertexDataIndices.Add(vertexDataIndex);
                mesh.VertexData.Size++;

                vertexPositions.Data.Add(Positions[Vertices[i]]);
            }

            for (var i = 0; i < activeHalfEdgeCount / 2; i++)
            {
                mesh.EdgeData.Size++;
                edgeFlags.Data.Add((int)EdgeFlag.None);
            }

            for (var i = 0; i < HalfEdgeMesh.HalfEdgeCount; i++)
            {
                var newIndex = halfEdgeRemap[i];
                if (newIndex == -1)
                {
                    continue;
                }

                var hEdge = new HalfEdgeHandle(i, HalfEdgeMesh);

                // EdgeData refers to a single edge, so its half of the total of half edges, both halves of the edge should have the same EdgeData Index
                // Twin half edges are always allocated (and freed) as pairs, so both map to edge newIndex / 2
                mesh.EdgeDataIndices.Add(newIndex / 2);

                mesh.EdgeVertexIndices.Add(hEdge.Vertex.Index);
                mesh.EdgeOppositeIndices.Add(halfEdgeRemap[hEdge.OppositeEdge.Index]);
                mesh.EdgeNextIndices.Add(halfEdgeRemap[hEdge.NextEdge.Index]);

                var faceIndex = hEdge.Face.Index;
                mesh.EdgeFaceIndices.Add(faceIndex == -1 ? -1 : faceRemap[faceIndex]);
                mesh.EdgeVertexDataIndices.Add(newIndex);

                mesh.FaceVertexData.Size += 1;

                // corner data was fanned onto the half edge streams in WriteFaceData(),
                // boundary half edges keep the stream defaults (zero)
                normals.Data.Add(Normals[hEdge]);
                tangents.Data.Add(Tangents[hEdge]);
                texcoords.Data.Add(TextureCoords[hEdge]);
                texcoords1.Data.Add(TextureCoords1[hEdge]);
                vertexpaintblendparams.Data.Add(VertexPaintBlendParams[hEdge]);
                vertexpainttintcolor.Data.Add(VertexPaintTintColor[hEdge]);
            }

            foreach (var material in Materials)
            {
                mesh.Materials.Add(material);
            }

            for (var i = 0; i < HalfEdgeMesh.FaceCount; i++)
            {
                if (faceRemap[i] == -1)
                {
                    continue;
                }

                var hFace = new FaceHandle(i, HalfEdgeMesh);

                var faceDataIndex = mesh.FaceData.Size;
                mesh.FaceDataIndices.Add(faceDataIndex);
                mesh.FaceData.Size++;

                faceMaterialIndices.Data.Add(MaterialIndex[hFace]);
                faceFlags.Data.Add(0);

                mesh.FaceEdgeIndices.Add(halfEdgeRemap[hFace.Edge.Index]);
            }

            mesh.SubdivisionData.SubdivisionLevels.AddRange(Enumerable.Repeat(0, 8));

            return mesh;
        }

        public void AddVertices(VertexStreams streams, Vector3 positionOffset = new Vector3())
        {
            SourceStreams = streams;

            var baseVertex = Vertices.Count;
            Vertices.EnsureCapacity(baseVertex + streams.positions.Count);
            Vertices.AddRange(HalfEdgeMesh.AddVertices(streams.positions.Count));

            for (var i = 0; i < streams.positions.Count; i++)
            {
                Positions[Vertices[baseVertex + i]] = streams.positions[i] + positionOffset;
            }
        }

        public void AddFace(ReadOnlySpan<int> indices, string material)
        {
            OriginalFaceCount++;

            if (!VerifyIndicesWithinBounds(indices))
            {
                //ProgressReporter?.Report($"{nameof(HammerMeshBuilder)}: Error! Failed to add face '{HalfEdgeMesh.FaceCount}', face has an index that is out of bounds.");
                FacesRemoved++;
                return;
            }

            // don't allow degenerate faces
            if (indices.Length < 3)
            {
                //ProgressReporter?.Report($"{nameof(HammerMeshBuilder)}: Error! Failed to add face '{HalfEdgeMesh.FaceCount}', face has less than 3 vertices.");
                FacesRemoved++;
                return;
            }

            // some map render meshes have faces with 0 area, check for that
            // only checking triangular faces because doing this for n-gons would be too expensive
            // and I doubt we'll ever get n-gons that are this fucked up
            if (indices.Length == 3)
            {
                if (AreVerticesCollinear(
                    Positions[Vertices[indices[0]]],
                    Positions[Vertices[indices[1]]],
                    Positions[Vertices[indices[2]]]))
                {
                    //ProgressReporter?.Report($"{nameof(HammerMeshBuilder)}: Error! Failed to add face '{HalfEdgeMesh.FaceCount}', face had 0 area");
                    FacesRemoved++;
                    return;
                }
            }

            var vertices = new VertexHandle[indices.Length];
            for (var i = 0; i < indices.Length; i++)
            {
                vertices[i] = Vertices[indices[i]];
            }

            // AddFace will validate the face against all topology rules, if it fails, we dumplicate its vertices, extracting the face
            if (HalfEdgeMesh.AddFace(out var hFace, vertices))
            {
                WriteFaceData(hFace, indices, material);
                return;
            }

            ExtractFace(indices, material);
        }

        // writes the per vertex source data into the half edges
        private void WriteFaceData(FaceHandle hFace, ReadOnlySpan<int> sourceIndices, string material)
        {
            MaterialIndex[hFace] = AddMaterial(material);

            // the face edge points at the half edge ending at the first input vertex,
            // so walking the loop visits the corners in input order
            var hEdge = hFace.Edge;

            for (var i = 0; i < sourceIndices.Length; i++)
            {
                var sourceIndex = sourceIndices[i];

                var normal = SourceStreams.normals.Count > 0
                    ? SourceStreams.normals[sourceIndex]
                    : CalculateNormal(hEdge);

                var tangent = SourceStreams.tangents.Count > 0
                    ? SourceStreams.tangents[sourceIndex]
                    : CalculateTangentFromNormal(normal);

                var position = Positions[hEdge.Vertex];

                Normals[hEdge] = normal;
                Tangents[hEdge] = tangent;

                TextureCoords[hEdge] = SourceStreams.texcoords.Count > 0
                    ? SourceStreams.texcoords[sourceIndex]
                    : CalculateTriplanarUVs(position, normal);

                TextureCoords1[hEdge] = SourceStreams.texcoords1.Count > 0
                    ? SourceStreams.texcoords1[sourceIndex]
                    : CalculateTriplanarUVs(position, normal);

                if (SourceStreams.VertexPaintBlendParams.Count > 0)
                {
                    VertexPaintBlendParams[hEdge] = SourceStreams.VertexPaintBlendParams[sourceIndex];
                }

                if (SourceStreams.VertexPaintTintColor.Count > 0)
                {
                    VertexPaintTintColor[hEdge] = SourceStreams.VertexPaintTintColor[sourceIndex];
                }

                hEdge = hEdge.NextEdge;
            }
        }

        private int AddMaterial(string material)
        {
            if (material is null)
            {
                return -1;
            }

            if (MaterialIds.TryGetValue(material, out var id))
            {
                return id;
            }

            id = Materials.Count;
            Materials.Add(material);
            MaterialIds[material] = id;

            return id;
        }

        // Faces which cant be integrated into the existing topology (they would create a nonmanifold edge or vertex)
        // are added as a disconnected island with duplicated vertices, so no geometry is lost
        private void ExtractFace(ReadOnlySpan<int> indices, string material)
        {
            FacesRemoved++;

#if DEBUG
            ProgressReporter?.Report($"{nameof(HammerMeshBuilder)}: Face '{HalfEdgeMesh.FaceCount}' did not fit into the mesh topology, extracting it with duplicated vertices");
#endif

            var vertices = new VertexHandle[indices.Length];

            for (var i = 0; i < indices.Length; i++)
            {
                var hVertex = HalfEdgeMesh.AddVertex();
                Positions[hVertex] = Positions[Vertices[indices[i]]];
                Vertices.Add(hVertex);
                vertices[i] = hVertex;
            }

            // the duplicated vertices are isolated, so this cant fail
            HalfEdgeMesh.AddFace(out var hFace, vertices);

            // need to write new half edge stream data
            WriteFaceData(hFace, indices, material);
        }

        public void AddPhysHull(HullDescriptor desc, PhysAggregateData phys, Func<string, string> materialNameProvider, Vector3 positionOffset = new Vector3(), string? materialOverride = null)
        {
            var attributes = phys.CollisionAttributes[desc.CollisionAttributeIndex];
            var tags = attributes.GetArray<string>("m_InteractAsStrings") ?? attributes.GetArray<string>("m_PhysicsTagStrings");
            var group = attributes.GetStringProperty("m_CollisionGroupString");
            var material = materialOverride ?? MapExtract.GetToolTextureNameForCollisionTags(new ModelExtract.SurfaceTagCombo(group, tags!));

            if (group == "Default")
            {
                var physicsSurfaceNames = phys.SurfacePropertyHashes.Select(StringToken.GetKnownString).ToArray();

                var surfaceProperty = physicsSurfaceNames[desc.SurfacePropertyIndex];
                material = materialNameProvider.Invoke(surfaceProperty);
            }

            var hull = desc.Shape;
            VertexStreams streams = new()
            {
                positions = hull.GetVertexPositions().ToArray().ToList()
            };
            AddVertices(streams, positionOffset);

            var hullFaces = hull.GetFaces();
            var hullEdges = hull.GetEdges();

            Span<int> inds = stackalloc int[byte.MaxValue];

            foreach (var face in hullFaces)
            {
                var indexCount = 0;

                var startHe = face.Edge;
                var he = startHe;

                do
                {
                    if (indexCount >= byte.MaxValue)
                    {
                        // runaway hull face?
                        break;
                    }

                    inds[indexCount] = hullEdges[he].Origin;
                    he = hullEdges[he].Next;
                    indexCount++;
                }
                while (he != startHe);

                AddFace(inds[..indexCount], material);
            }
        }

        public void AddPhysMesh(MeshDescriptor desc, PhysAggregateData phys, Func<string, string> materialNameProvider, HashSet<int> deletedIndices,
            Vector3 positionOffset = new Vector3(), string? materialOverride = null, int triangleRangeMin = 0, int triangleRangeMax = 0, bool useTriangleRange = false)
        {
            if (useTriangleRange)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(triangleRangeMin);
                ArgumentOutOfRangeException.ThrowIfLessThan(triangleRangeMax, triangleRangeMin);
            }

            var attributes = phys.CollisionAttributes[desc.CollisionAttributeIndex];
            var tags = attributes.GetArray<string>("m_InteractAsStrings") ?? attributes.GetArray<string>("m_PhysicsTagStrings");
            var group = attributes.GetStringProperty("m_CollisionGroupString");
            var material = materialOverride ?? MapExtract.GetToolTextureNameForCollisionTags(new ModelExtract.SurfaceTagCombo(group, tags!));
            var knownKeys = StringToken.InvertedTable;

            var physicsSurfaceNames = phys.SurfacePropertyHashes.Select(StringToken.GetKnownString).ToArray();

            var mesh = desc.Shape;
            var meshTriangles = mesh.GetTriangles();

            var (triangleStart, triangleStop) = useTriangleRange
                ? (triangleRangeMin, triangleRangeMax)
                : (0, meshTriangles.Length);

            var newMesh = ReindexTriangleMesh(mesh.GetVertices(), meshTriangles, triangleStart, triangleStop);

            VertexStreams streams = new()
            {
                positions = newMesh.NewVertices
            };
            AddVertices(streams, positionOffset);

            Span<int> inds = stackalloc int[3];

            var removed = 0;

            for (var i = 0; i < newMesh.NewTriangles.Count; i++)
            {
                var triangle = meshTriangles[i + triangleStart];

                inds[0] = triangle.X;
                inds[1] = triangle.Y;
                inds[2] = triangle.Z;

                if (deletedIndices.Contains(inds[0])
                 || deletedIndices.Contains(inds[1])
                 || deletedIndices.Contains(inds[2]))
                {
                    removed++;
                    continue;
                }

                var newTriangle = newMesh.NewTriangles[i];

                inds[0] = (int)newTriangle.X;
                inds[1] = (int)newTriangle.Y;
                inds[2] = (int)newTriangle.Z;


                if (group == "Default")
                {
                    var physicsSurfaces = mesh.Materials;
                    // + triangleStart because physicsSurfaces didnt also get reindexed
                    var surfacePropertyIndex = physicsSurfaces.Length > 0 ? physicsSurfaces[i + triangleStart] : desc.SurfacePropertyIndex;
                    var surfaceProperty = physicsSurfaceNames[surfacePropertyIndex];

                    material = surfaceProperty switch
                    {
                        "default" => "materials/tools/toolsnodraw.vmat", // default is just nodraw, ignore it
                        _ => materialNameProvider.Invoke(surfaceProperty)
                    };
                }

                AddFace(inds, material);
            }

            if (removed > 0)
            {
                ProgressReporter?.Report($"{nameof(HammerMeshBuilder)}: Total physics triangles removed: {removed}");
            }
        }

        public void AddRenderMesh(DmeMesh shape, Matrix4x4 transform)
        {
            var facesets = shape.FaceSets;

            var vertexdata = (DmeVertexData)shape.BaseStates[0];

            var hasTransform = !transform.IsIdentity;
            var normalMatrix = Matrix4x4.Identity;
            if (hasTransform && Matrix4x4.Invert(transform, out var inverse))
            {
                normalMatrix = Matrix4x4.Transpose(inverse);
            }

            var positions = GetElementArraySafe<Vector3>(vertexdata, "position$0");
            var texcoords = GetElementArraySafe<Vector2>(vertexdata, "texcoord$0");
            var texcoords1 = GetElementArraySafe<Vector2>(vertexdata, "texcoord$1");
            var normals = GetElementArraySafe<Vector3>(vertexdata, "normal$0");
            var tangents = GetElementArraySafe<Vector4>(vertexdata, "tangent$0");
            var VertexPaintBlendParams = GetElementArraySafe<Vector4>(vertexdata, "VertexPaintBlendParams$0");
            var VertexPaintTintColor = GetElementArraySafe<Vector4>(vertexdata, "VertexPaintTintColor$0");

            if (positions == null || positions.Count == 0)
            {
                throw new InvalidDataException("AddRenderMesh() trying to process a mesh with no vertices!");
            }

            List<(int[] Indices, DmeFaceSet FaceSet)> faceList = [];
            Dictionary<int, int> newVertexStreamsIndexDict = [];
            List<Vector3> newVertices = [];
            List<Vector2> newTexcoords = [];
            List<Vector2> newTexcoords1 = [];
            List<Vector3> newNormals = [];
            List<Vector4> newTangents = [];
            List<Vector4> newVertexPaintBlendParams = [];
            List<Vector4> newVertexPaintTintColor = [];

            // Only scan when the position buffer changes
            if (PhysicsVertexMatcher != null && PhysicsVertexMatcher.LastPositions != positions)
            {
                PhysicsVertexMatcher.LastPositions = positions;
                PhysicsVertexMatcher.ScanPhysicsPointCloudForMatches([.. positions], ProgressReporter);
            }

            List<int> inds = new(capacity: 3);

            foreach (var faceset in facesets.Cast<DmeFaceSet>())
            {
                var facesetIndices = faceset.Faces;

                var newIndexCounter = -1;
                foreach (var index in facesetIndices)
                {
                    if (index != -1)
                    {
                        inds.Add(index);
                        continue;
                    }

                    // if all the indices are the same abort
                    // this takes care of the padding meshlets have
                    if (inds[0] == inds[1] && inds[0] == inds[2])
                    {
                        inds.Clear();
                        continue;
                    }

                    //PhysicsVertexMatcher?.TryMatchRenderTriangleToPhysics(CollectionsMarshal.AsSpan(inds));

                    List<int> newFaceInds = new(capacity: 3);

                    foreach (var faceIndex in inds)
                    {
                        if (!newVertexStreamsIndexDict.TryGetValue(faceIndex, out var newIndex))
                        {
                            newIndex = ++newIndexCounter;
                            newVertexStreamsIndexDict.Add(faceIndex, newIndexCounter);
                        }

                        newFaceInds.Add(newIndex);
                    }

                    faceList.Add(([.. newFaceInds], faceset));
                    inds.Clear();
                }
            }

            foreach (var kv in newVertexStreamsIndexDict)
            {
                if (positions != null && positions.Count != 0)
                {
                    newVertices.Add(positions[kv.Key]);
                }

                if (texcoords != null && texcoords.Count != 0)
                {
                    newTexcoords.Add(texcoords[kv.Key]);
                }

                if (texcoords1 != null && texcoords1.Count != 0)
                {
                    newTexcoords1.Add(texcoords1[kv.Key]);
                }

                if (normals != null && normals.Count != 0)
                {
                    newNormals.Add(normals[kv.Key]);
                }

                if (tangents != null && tangents.Count != 0)
                {
                    newTangents.Add(tangents[kv.Key]);
                }

                if (VertexPaintBlendParams != null && VertexPaintBlendParams.Count != 0)
                {
                    newVertexPaintBlendParams.Add(VertexPaintBlendParams[kv.Key]);
                }

                if (VertexPaintTintColor != null && VertexPaintTintColor.Count != 0)
                {
                    newVertexPaintTintColor.Add(VertexPaintTintColor[kv.Key]);
                }
            }

            if (hasTransform)
            {
                TransformVertexStreams(newVertices, newNormals, newTangents, transform, normalMatrix);
            }

            VertexStreams streams = new()
            {
                positions = newVertices,
                texcoords = newTexcoords,
                texcoords1 = newTexcoords1,
                normals = newNormals,
                tangents = newTangents,
                VertexPaintBlendParams = newVertexPaintBlendParams,
                VertexPaintTintColor = newVertexPaintTintColor,
            };

            AddVertices(streams);

            foreach (var (faceIndices, faceSet) in faceList)
            {
                AddFace(faceIndices, faceSet.Material.MaterialName);
            }
        }

        private static void TransformVertexStreams(List<Vector3> positions, List<Vector3> normals, List<Vector4> tangents, Matrix4x4 transform, Matrix4x4 normalMatrix)
        {
            for (var i = 0; i < positions.Count; i++)
            {
                positions[i] = Vector3.Transform(positions[i], transform);
            }

            for (var i = 0; i < normals.Count; i++)
            {
                normals[i] = Vector3.Normalize(Vector3.TransformNormal(normals[i], normalMatrix));
            }

            for (var i = 0; i < tangents.Count; i++)
            {
                var tangent = tangents[i];
                var direction = Vector3.Normalize(Vector3.TransformNormal(new Vector3(tangent.X, tangent.Y, tangent.Z), transform));
                tangents[i] = new Vector4(direction, tangent.W);
            }
        }

        private bool VerifyIndicesWithinBounds(ReadOnlySpan<int> indices)
        {
            foreach (var index in indices)
            {
                if (index < 0 || index >= Vertices.Count)
                {
                    return false;
                }
            }

            return true;
        }

        private Vector3 CalculateNormal(HalfEdgeHandle hEdge)
        {
            var v1 = Positions[hEdge.Vertex];
            var v2 = Positions[hEdge.NextEdge.Vertex];
            var v3 = Positions[hEdge.OppositeEdge.Vertex];

            var normal = Vector3.Normalize(Vector3.Cross(v2 - v1, v3 - v1));

            return normal;
        }

        private static Vector4 CalculateTangentFromNormal(Vector3 normal)
        {
            var tangent1 = Vector3.Cross(normal, Vector3.UnitY);
            var tangent2 = Vector3.Cross(normal, Vector3.UnitZ);
            return new Vector4(tangent1.Length() > tangent2.Length() ? tangent1 : tangent2, 1.0f);
        }

        private static Vector2 CalculateTriplanarUVs(Vector3 vertexPos, Vector3 normal, float textureScale = 0.03125f)
        {
            var weights = Vector3.Abs(normal);
            var top = new Vector2(vertexPos.X, -vertexPos.Y) * weights.Z;
            var front = new Vector2(vertexPos.X, -vertexPos.Z) * weights.Y;
            var side = new Vector2(vertexPos.Y, -vertexPos.Z) * weights.X;

            var UV = (top + front + side);

            return UV * textureScale;
        }

        private static bool AreVerticesCollinear(Vector3 v1, Vector3 v2, Vector3 v3)
        {
            // Calculate the cross product of the vectors
            var vector1 = v2 - v1;
            var vector2 = v3 - v1;

            var crossProduct = Vector3.Cross(vector1, vector2);

            // Check if the magnitude of the cross product is close to zero
            const float epsilon = 1e-10f;
            return crossProduct.Length() < epsilon;
        }

        public static (List<Vector3> NewTriangles, List<Vector3> NewVertices) ReindexTriangleMesh(ReadOnlySpan<Vector3> vertices, ReadOnlySpan<Triangle> triangles, int trianglesRangeStart, int trianglesRangeEnd)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(vertices.Length, 1, "ReindexMesh vertices can't be empty");
            ArgumentOutOfRangeException.ThrowIfLessThan(triangles.Length, 1, "ReindexMesh triangles can't be empty");

            ArgumentOutOfRangeException.ThrowIfLessThan(trianglesRangeStart, 0, "ReindexMesh indexRangeStart can't be less than zero");
            ArgumentOutOfRangeException.ThrowIfGreaterThan(trianglesRangeEnd, triangles.Length, "ReindexMesh indexRangeEnd can't be more than index count");
            ArgumentOutOfRangeException.ThrowIfGreaterThan(trianglesRangeStart, trianglesRangeEnd, "ReindexMesh trianglesRangeStart can't be bigger than indexRangeEnd");

            var trianglesCount = trianglesRangeEnd - trianglesRangeStart;

            List<Vector3> newTriangles = new(trianglesCount);
            // possible over allocation but might be better for speed than underallocation?
            List<Vector3> newVertices = new(trianglesCount * 3);
            Dictionary<int, int> oldToNewIndex = new(trianglesCount * 3);

            var nextNewIndex = 0;

            Span<int> currentTriangleIndices = stackalloc int[3];
            Span<int> newIndices = stackalloc int[3];

            for (var i = trianglesRangeStart; i < trianglesRangeEnd; i++)
            {
                var originalTriangle = triangles[i];
                currentTriangleIndices[0] = (int)originalTriangle.X;
                currentTriangleIndices[1] = (int)originalTriangle.Y;
                currentTriangleIndices[2] = (int)originalTriangle.Z;

                for (var j = 0; j < currentTriangleIndices.Length; j++)
                {
                    var index = currentTriangleIndices[j];
                    if (!oldToNewIndex.TryGetValue(index, out var mappedIndex))
                    {
                        mappedIndex = nextNewIndex++;
                        oldToNewIndex[index] = mappedIndex;
                        newVertices.Add(vertices[index]);
                    }

                    newIndices[j] = mappedIndex;
                }

                newTriangles.Add(new Vector3(newIndices[0], newIndices[1], newIndices[2]));
            }

            return (newTriangles, newVertices);
        }

        public static CDmePolygonMeshDataStream<T> CreateStream<TArray, T>(int dataStateFlags, string name, string? standardAttributeName = null, params T[] data)
            where TArray : Array<T>, new()
            where T : notnull
        {

            var dmArray = new TArray();
            foreach (var item in data)
            {
                dmArray.Add(item);
            }


            var stream = new CDmePolygonMeshDataStream<T>
            {
                Name = name,
                StandardAttributeName = string.IsNullOrEmpty(standardAttributeName) ? name[..^2] : standardAttributeName,
                SemanticName = name[..^2],
                SemanticIndex = int.Parse(name[^1].ToString(), CultureInfo.InvariantCulture),
                VertexBufferLocation = 0,
                DataStateFlags = dataStateFlags,
                SubdivisionBinding = null,
                Data = dmArray
            };

            return stream;
        }

        static IList<T>? GetElementArraySafe<T>(Element Element, string elementName)
        {
            if (Element.ContainsKey(elementName))
            {
                Element.TryGetValue(elementName, out var arrayElement);
                if (arrayElement == null)
                {
                    return null;
                }

                if (arrayElement is IList<T> typedArrayElement)
                {
                    return typedArrayElement;
                }
            }

            return null;
        }
    }
}
