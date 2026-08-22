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

    /// <summary>
    /// Builds a Hammer editable mesh out of faces, and writes it out as a <see cref="CDmePolygonMesh"/>.
    /// </summary>
    /// <remarks>
    /// Most of the work is handled by HalfEdgeMesh, which builds the mesh and keeps it valid. All attribute data
    /// lives in data streams attached to the mesh components: position per vertex, corner data per half edge,
    /// material per face. <see cref="GenerateMesh"/> then loops through the mesh and writes the vmap format.
    /// </remarks>
    public class HammerMeshBuilder
    {
        /// <summary>
        /// How an edge shades across its two faces.
        /// </summary>
        [Flags]
        public enum EdgeFlag
        {
            /// <summary>Shading follows the smoothing angle.</summary>
            None = 0x0,

            /// <summary>Normals are averaged across the edge.</summary>
            SoftNormals = 0x1,

            /// <summary>Normals break at the edge.</summary>
            HardNormals = 0x2,
        }

        /// <summary>
        /// Per vertex source data handed to <see cref="AddVertices"/>. Every stream is either empty or
        /// the same length as <see cref="Positions"/>, and is indexed by input vertex index.
        /// </summary>
        public class VertexStreams
        {
            /// <summary>Vertex positions. The only required stream.</summary>
            public List<Vector3> Positions { get; } = [];

            /// <summary>First texture coordinate channel.</summary>
            public List<Vector2> TexCoords { get; } = [];

            /// <summary>Second texture coordinate channel.</summary>
            public List<Vector2> TexCoords1 { get; } = [];

            /// <summary>Vertex normals, used to decide whether an edge is soft or hard.</summary>
            public List<Vector3> Normals { get; } = [];

            /// <summary>Vertex tangents.</summary>
            public List<Vector4> Tangents { get; } = [];

            /// <summary>Vertex paint blend parameters.</summary>
            public List<Vector4> VertexPaintBlendParams { get; } = [];

            /// <summary>Vertex paint tint colors.</summary>
            public List<Vector4> VertexPaintTintColor { get; } = [];
        }

        /// <summary>
        /// Number of faces dropped while building, either degenerate or non manifold.
        /// </summary>
        public int FacesRemoved { get; private set; }

        /// <summary>
        /// Number of faces handed to the builder, including the ones it dropped.
        /// </summary>
        public int OriginalFaceCount { get; private set; }

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
        private readonly FaceData<bool> Extracted;

        private readonly List<string> Materials = [];
        private readonly Dictionary<string, int> MaterialIds = [];

        // Source data for the vertices added through AddVertices(), indexed by input vertex index across
        // every call, read in order to propagate the vertex data onto the half edges
        private readonly VertexStreams SourceStreams = new();

        /// <summary>
        /// Matcher that reports which physics vertices a render mesh already covers.
        /// </summary>
        public PhysicsVertexMatcher? PhysicsVertexMatcher { get; init; }

        /// <summary>
        /// Receives diagnostics about faces the builder had to drop.
        /// </summary>
        public IProgress<string>? ProgressReporter { get; init; }

        /// <summary>
        /// Initializes a new instance of the <see cref="HammerMeshBuilder"/> class.
        /// </summary>
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
            Extracted = HalfEdgeMesh.CreateFaceData<bool>(nameof(Extracted));

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

        /// <summary>
        /// Writes everything added so far out as a Hammer mesh.
        /// </summary>
        public CDmePolygonMesh GenerateMesh()
        {
#if DEBUG
            if (FacesRemoved > 0)
            {
                ProgressReporter?.Report($"{nameof(HammerMeshBuilder)}: Removed '{FacesRemoved}' of '{OriginalFaceCount - FacesRemoved}' faces");
            }
#endif

            // merge coplanar triangle pairs into quads before writing to the vmap
            // currently merging faces by material, if materials differ the triangles won't be merged into a quad
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

            // merging and collapsing frees vertices too
            var vertexRemap = new int[HalfEdgeMesh.VertexCount];
            var activeVertexCount = 0;
            for (var i = 0; i < HalfEdgeMesh.VertexCount; i++)
            {
                vertexRemap[i] = HalfEdgeMesh.IsVertexAllocated(i) ? activeVertexCount++ : -1;
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
                if (vertexRemap[i] == -1)
                {
                    continue;
                }

                var vertexDataIndex = mesh.VertexData.Size;

                var hVertex = new VertexHandle(i, HalfEdgeMesh); // by index, several input vertices share one mesh vertex after merging
                var vertexEdge = hVertex.Edge.Index;
                mesh.VertexEdgeIndices.Add(vertexEdge == -1 ? -1 : halfEdgeRemap[vertexEdge]);

                mesh.VertexDataIndices.Add(vertexDataIndex);
                mesh.VertexData.Size++;

                vertexPositions.Data.Add(Positions[hVertex]);
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

                mesh.EdgeVertexIndices.Add(vertexRemap[hEdge.Vertex.Index]);
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

        /// <summary>
        /// Writes everything added so far out as Hammer meshes, one per island of faces connected through shared
        /// vertices. Faces that were extracted because they didn't fit the topology are collected into one extra
        /// mesh. Each island is copied into its own builder and written with <see cref="GenerateMesh"/>.
        /// </summary>
        public List<CDmePolygonMesh> GenerateMeshes()
        {
            var islands = FindIslands();
            var meshes = new List<CDmePolygonMesh>(islands.Count);

            foreach (var islandFaces in islands)
            {
                var islandBuilder = new HammerMeshBuilder
                {
                    ProgressReporter = ProgressReporter,
                };

                islandBuilder.CopyFacesFrom(this, islandFaces);
                meshes.Add(islandBuilder.GenerateMesh());
            }

#if DEBUG
            if (meshes.Count > 1)
            {
                ProgressReporter?.Report($"{nameof(HammerMeshBuilder)}: Split into {meshes.Count} meshes");
            }
#endif

            return meshes;
        }

        // groups the faces into islands connected through shared vertices, extracted faces all count as one island
        private List<List<FaceHandle>> FindIslands()
        {
            var parent = new int[HalfEdgeMesh.VertexCount];
            for (var i = 0; i < parent.Length; i++)
            {
                parent[i] = i;
            }

            int Find(int i)
            {
                while (parent[i] != i)
                {
                    parent[i] = parent[parent[i]];
                    i = parent[i];
                }

                return i;
            }

            void Union(int a, int b)
            {
                a = Find(a);
                b = Find(b);

                if (a != b)
                {
                    parent[a] = b;
                }
            }

            var extractedRoot = -1;
            foreach (var hFace in HalfEdgeMesh.FaceHandles)
            {
                var first = hFace.Edge.Vertex.Index;

                var hEdge = hFace.Edge;
                do
                {
                    Union(hEdge.Vertex.Index, first);
                    hEdge = hEdge.NextEdge;
                }
                while (hEdge != hFace.Edge);

                if (Extracted[hFace])
                {
                    if (extractedRoot == -1)
                    {
                        extractedRoot = first;
                    }
                    else
                    {
                        Union(first, extractedRoot);
                    }
                }
            }

            var islandByRoot = new Dictionary<int, List<FaceHandle>>();
            var islands = new List<List<FaceHandle>>();

            foreach (var hFace in HalfEdgeMesh.FaceHandles)
            {
                var root = Find(hFace.Edge.Vertex.Index);

                if (!islandByRoot.TryGetValue(root, out var island))
                {
                    island = [];
                    islandByRoot.Add(root, island);
                    islands.Add(island);
                }

                island.Add(hFace);
            }

            return islands;
        }

        // copies the given faces of another builder, with their half edges, vertices, data streams and materials, into this empty builder
        private void CopyFacesFrom(HammerMeshBuilder source, IReadOnlyCollection<FaceHandle> faces)
        {
            HalfEdgeMesh.AppendComponentsFromMesh(source.HalfEdgeMesh, faces, out var newVertices, out var newHalfEdges, out var newFaces);

            foreach (var (hVertex, hNewVertex) in newVertices)
            {
                Positions[hNewVertex] = source.Positions[hVertex];
                Vertices.Add(hNewVertex);
            }

            foreach (var (hEdge, hNewEdge) in newHalfEdges)
            {
                TextureCoords[hNewEdge] = source.TextureCoords[hEdge];
                TextureCoords1[hNewEdge] = source.TextureCoords1[hEdge];
                Normals[hNewEdge] = source.Normals[hEdge];
                Tangents[hNewEdge] = source.Tangents[hEdge];
                VertexPaintBlendParams[hNewEdge] = source.VertexPaintBlendParams[hEdge];
                VertexPaintTintColor[hNewEdge] = source.VertexPaintTintColor[hEdge];
            }

            foreach (var (hFace, hNewFace) in newFaces)
            {
                var materialIndex = source.MaterialIndex[hFace];
                MaterialIndex[hNewFace] = materialIndex >= 0 ? AddMaterial(source.Materials[materialIndex]) : -1;
                Extracted[hNewFace] = source.Extracted[hFace];
            }
        }

        /// <summary>
        /// Merges every vertex of the mesh that lies within <paramref name="maxDistance"/> of another one, the
        /// Hammer "merge vertices by distance" operation. Use after all faces were added: the input vertex indices
        /// handed out by <see cref="AddVertices"/> no longer apply afterwards.
        /// </summary>
        /// <param name="maxDistance">Largest distance between two vertices that still get merged.</param>
        /// <returns>Number of vertices merged away.</returns>
        public int MergeVerticesWithinDistance(float maxDistance)
        {
            var vertices = HalfEdgeMesh.VertexHandles.ToList();
            var merged = MergeVerticesWithinDistance(vertices, maxDistance, averagePositions: false, out _);

#if DEBUG
            if (merged > 0)
            {
                ProgressReporter?.Report($"{nameof(HammerMeshBuilder)}: Merged {merged} of {vertices.Count} vertices within {maxDistance} units");
            }
#endif

            return merged;
        }

        /// <summary>
        /// Merges the given vertices into groups of vertices lying within <paramref name="maxDistance"/> of each other
        /// </summary>
        /// <param name="originalVertices">Vertices to consider for merging.</param>
        /// <param name="maxDistance">Largest distance between two vertices that still get merged, negative merges all of them into one.</param>
        /// <param name="averagePositions">Move each merged vertex to the average of the positions it merged, otherwise the first vertex of a group keeps its position.</param>
        /// <param name="finalVertices">The vertices left over after merging.</param>
        /// <returns>Number of vertices merged away.</returns>
        public int MergeVerticesWithinDistance(IReadOnlyList<VertexHandle> originalVertices, float maxDistance, bool averagePositions, out List<VertexHandle> finalVertices)
        {
            finalVertices = [];
            var useDistance = maxDistance >= 0.0f;
            var distance = new Vector3(maxDistance, maxDistance, maxDistance);
            var maxIterations = 10;
            var maxDistanceSquared = useDistance ? (maxDistance * maxDistance) : float.MaxValue;

            var verticesToMerge = new List<VertexHandle>(originalVertices.Count);
            foreach (var hVertex in originalVertices)
            {
                if (hVertex.IsValid)
                {
                    verticesToMerge.Add(hVertex);
                }
            }

            var numOriginalVertices = verticesToMerge.Count;
            if (numOriginalVertices < 2)
            {
                return 0;
            }

            var numTotalVerticesMerged = 0;

            finalVertices.EnsureCapacity(numOriginalVertices);

            // Assign the vertices to groups based on their positions. Each group will contain all of the
            // vertices within the specified maximum distance of the first vertex in the group.
            var verticesSortedByGroup = new List<VertexHandle>(numOriginalVertices);
            var groupVertexCounts = new int[numOriginalVertices];
            var groupOffsets = new int[numOriginalVertices];

            for (var iteration = 0; iteration < maxIterations; ++iteration)
            {
                // Stop if there are not at least two vertices left.
                var numVerticesToMerge = verticesToMerge.Count;
                if (numVerticesToMerge < 2)
                {
                    break;
                }

                var vertexGroupAssignments = new List<int>(numVerticesToMerge);
                vertexGroupAssignments.AddRange(Enumerable.Repeat(-1, numVerticesToMerge));
                verticesSortedByGroup.Clear();

                var numGroups = 0;

                if (useDistance)
                {
                    // Build an array of the positions specifically for the vertices
                    // to merge instead of all the positions in the mesh
                    var vertexPositions = new List<Vector3>(numVerticesToMerge);
                    for (var iVertex = 0; iVertex < numVerticesToMerge; ++iVertex)
                    {
                        vertexPositions.Add(Positions[verticesToMerge[iVertex]]);
                    }

                    // Build a kd-tree of the vertex positions that we can use to find nearby vertices more efficiently
                    var vertexPositionTree = new VertexKDTree();
                    vertexPositionTree.BuildMidpoint(vertexPositions);

                    for (var iVertex = 0; iVertex < numVerticesToMerge; ++iVertex)
                    {
                        // Check to see if the vertex has already been added to a group.
                        if (vertexGroupAssignments[iVertex] >= 0)
                        {
                            continue;
                        }

                        // If the vertex has not been assigned to a group assign it the next available group.
                        var hVertexA = verticesToMerge[iVertex];
                        var groupPosition = Positions[hVertexA];
                        var groupIndex = numGroups++;
                        vertexGroupAssignments[iVertex] = groupIndex;

                        // Set the index of the start of the group in the sorted vertex array.
                        groupOffsets[groupIndex] = verticesSortedByGroup.Count;

                        // Add the vertex to the sorted array
                        verticesSortedByGroup.Add(hVertexA);

                        // Search the the rest of the vertices to see if there are any which have not yet been
                        // assigned a group that are close enough to the current vertex to be grouped with it.
                        var groupMin = groupPosition - distance;
                        var groupMax = groupPosition + distance;
                        var verticesInBox = vertexPositionTree.FindVertsInBox(groupMin, groupMax);
                        var numVerticesInBox = verticesInBox.Count;

                        // There are some cases where the behavior of merging is order dependent, ideally it
                        // wouldn't be, but it is due to the constraint of not being able to connect more
                        // than two faces at a single vertex by merging. So to maintain the same behavior
                        // as the old approach we need to add the vertices in the order they were supplied
                        // in the input list.
                        verticesInBox.Sort();

                        for (var iVertexInBox = 0; iVertexInBox < numVerticesInBox; ++iVertexInBox)
                        {
                            var vertexIndexB = verticesInBox[iVertexInBox];
                            var vertexPosition = vertexPositions[vertexIndexB];

                            if (Vector3.DistanceSquared(vertexPosition, groupPosition) < maxDistanceSquared)
                            {
                                if (vertexGroupAssignments[vertexIndexB] < 0)
                                {
                                    var hVertexB = verticesToMerge[vertexIndexB];
                                    verticesSortedByGroup.Add(hVertexB);
                                    vertexGroupAssignments[vertexIndexB] = groupIndex;
                                }
                            }
                        }

                        // Compute the number of vertices that were assigned to the group
                        groupVertexCounts[groupIndex] = verticesSortedByGroup.Count - groupOffsets[groupIndex];
                    }
                }
                else
                {
                    // If not using the distance just add all the vertices to a single group for merging
                    numGroups = 1;
                    verticesSortedByGroup = new(verticesToMerge);

                    for (var i = 0; i < vertexGroupAssignments.Count; i++)
                    {
                        vertexGroupAssignments[i] = 0;
                    }

                    groupVertexCounts[0] = numVerticesToMerge;
                    groupOffsets[0] = 0;
                }

                var groupsMergedVertexCount = new int[numGroups]; // Number of vertices in each group that were successfully merged
                var groupsSumPosition = new Vector3[numGroups]; // Average position of the vertices in the group that were merged
                var groupsTargetVertex = new VertexHandle[numGroups]; // Target vertex with which other vertices in the group should be merged.

                for (var iGroup = 0; iGroup < numGroups; ++iGroup)
                {
                    var groupVertexOffset = groupOffsets[iGroup];
                    var hFirstVertex = verticesSortedByGroup[groupVertexOffset];

                    groupsMergedVertexCount[iGroup] = 1;
                    groupsTargetVertex[iGroup] = hFirstVertex;
                    groupsSumPosition[iGroup] = Positions[hFirstVertex];

                    // Clear the first vertex in the group, it does not need to be merged.
                    verticesSortedByGroup[groupVertexOffset] = VertexHandle.Invalid;
                }

                // Merge all of the vertices in each group. Multiple iterations are done until all of the vertices
                // in all of the groups have been merged or until no vertices were merged in the previous iteration.
                for (var iGroupPass = 0; iGroupPass < numVerticesToMerge; ++iGroupPass)
                {
                    var numMerged = 0;
                    var numUnmerged = 0;

                    for (var iGroup = 0; iGroup < numGroups; ++iGroup)
                    {
                        var groupVertexCount = groupVertexCounts[iGroup];
                        if (groupVertexCount < 2)
                        {
                            continue;
                        }

                        var groupVertexOffset = groupOffsets[iGroup];
                        var numUnmergedInGroup = 0;

                        var hTargetVertex = groupsTargetVertex[iGroup];

                        for (var iVertex = 1; iVertex < groupVertexCount; ++iVertex)
                        {
                            var hMergeVertex = verticesSortedByGroup[groupVertexOffset + iVertex];
                            if (!hMergeVertex.IsValid)
                            {
                                continue;
                            }

                            // Get the position of the vertex to be merged before
                            // merging it, which will delete the vertex.
                            var mergeVertexPosition = Positions[hMergeVertex];

                            // If averaging positions, set the merge interpolation parameter to 0.5f,
                            // otherwise set it to 1.0 so that the data of the merge vertex is preserved.
                            var param = averagePositions ? 0.5f : 1.0f;

                            if (MergeVertices(hTargetVertex, hMergeVertex, param, out var hNewVertex))
                            {
                                // Add the position of the vertex to the group sum position
                                groupsSumPosition[iGroup] += mergeVertexPosition;
                                groupsMergedVertexCount[iGroup] += 1;

                                // Update the merged vertex of the group
                                groupsTargetVertex[iGroup] = hNewVertex;

                                // Update the target vertex to be the new vertex since the target vertex has
                                // be removed, if we don't update the target, then there is no way to merge
                                // the remaining vertices in this pass.
                                hTargetVertex = hNewVertex;

                                // Set the original vertex in the group to invalid so we
                                // don't try to merge it again in subsequent passes.
                                verticesSortedByGroup[groupVertexOffset + iVertex] = VertexHandle.Invalid;

                                ++numMerged;
                            }
                            else
                            {
                                ++numUnmerged;
                                ++numUnmergedInGroup;
                            }
                        }

                        // If all of the vertices in the group were merged mark the group as not having any
                        // vertices so that it is not touched in any future iterations.
                        if (numUnmergedInGroup == 0)
                        {
                            groupVertexCounts[iGroup] = -1;
                        }
                    }

                    if ((numUnmerged == 0) || (numMerged == 0))
                    {
                        break;
                    }
                }

                // Set the merged vertex positions to the average position
                var numVerticesMerged = 0;
                for (var iGroup = 0; iGroup < numGroups; ++iGroup)
                {
                    if (averagePositions)
                    {
                        var hVertex = groupsTargetVertex[iGroup];
                        if (hVertex.IsValid)
                        {
                            var averagePosition = groupsSumPosition[iGroup] / groupsMergedVertexCount[iGroup];
                            Positions[hVertex] = averagePosition;
                        }
                    }

                    var numVerticesMergedInGroup = groupsMergedVertexCount[iGroup];
                    if (numVerticesMergedInGroup > 1)
                    {
                        numVerticesMerged += numVerticesMergedInGroup;
                    }
                }

                // Add the merged vertices from the groups
                for (var iGroup = 0; iGroup < numGroups; ++iGroup)
                {
                    if (groupsTargetVertex[iGroup].IsValid)
                    {
                        finalVertices.Add(groupsTargetVertex[iGroup]);
                    }
                }

                // Build the remaining list of vertices to merge
                verticesToMerge.Clear();
                for (var iVertex = 0; iVertex < numVerticesToMerge; ++iVertex)
                {
                    if (verticesSortedByGroup[iVertex].IsValid)
                    {
                        verticesToMerge.Add(verticesSortedByGroup[iVertex]);
                    }
                }

                numTotalVerticesMerged += numVerticesMerged;
            }

            // Add all of the vertices which were not merged
            finalVertices.AddRange(verticesToMerge);

            return numTotalVerticesMerged;
        }

        // Merges two vertices, interpolating the position by param (0 keeps the first vertex, 1 the second).
        // Ported from S&box PolygonMesh.MergeVertices.
        private bool MergeVertices(VertexHandle hVertexA, VertexHandle hVertexB, float param, out VertexHandle hOutNewVertex)
        {
            // If there is an edge connecting the vertices, just call edge collapse so that
            // the proper interpolation is done for the face vertices of the merged edge.
            var hEdge = HalfEdgeMesh.FindHalfEdgeConnectingVertices(hVertexA, hVertexB);
            if (hEdge != HalfEdgeHandle.Invalid)
            {
                return CollapseEdge(hEdge, param, out hOutNewVertex);
            }

            // Interpolate the data on the two vertices and store a copy before they are destroyed
            var newVertex = Vector3.Lerp(Positions[hVertexA], Positions[hVertexB], param);

            // Merge the two vertices and create a new one with
            // the interpolated values of the original vertices.
            if (HalfEdgeMesh.MergeVertices(hVertexA, hVertexB, out hOutNewVertex))
            {
                Positions[hOutNewVertex] = newVertex;
                return true;
            }

            return false;
        }

        // Collapses an edge into one vertex, interpolating the position by param. Ported from S&box PolygonMesh.CollapseEdge.
        private bool CollapseEdge(HalfEdgeHandle hHalfEdgeA, float param, out VertexHandle hOutNewVertex)
        {
            var hHalfEdgeB = HalfEdgeMesh.GetOppositeHalfEdge(hHalfEdgeA);

            // Get the vertices connected to the edge and average the values
            var hVertexA = HalfEdgeMesh.GetEndVertexConnectedToEdge(hHalfEdgeB);
            var hVertexB = HalfEdgeMesh.GetEndVertexConnectedToEdge(hHalfEdgeA);

            var newVertex = Vector3.Lerp(Positions[hVertexA], Positions[hVertexB], param);
            var hEdge = HalfEdgeMesh.GetFullEdgeForHalfEdge(hHalfEdgeA);
            var removed = HalfEdgeMesh.CollapseEdge(hEdge, out hOutNewVertex, out _);

            if (hOutNewVertex != VertexHandle.Invalid)
            {
                Positions[hOutNewVertex] = newVertex;
            }

            return removed;
        }
        /// <summary>
        /// Adds the vertices of one source mesh. Faces added afterwards index into these vertices, offset by
        /// the returned base index when several source meshes are added to one builder.
        /// </summary>
        /// <param name="streams">Per vertex source data.</param>
        /// <param name="positionOffset">Offset added to every position.</param>
        /// <returns>Index of the first added vertex, to add to the indices handed to <see cref="AddFace"/>.</returns>
        public int AddVertices(VertexStreams streams, Vector3 positionOffset = new Vector3())
        {
            var baseVertex = Vertices.Count;
            var count = streams.Positions.Count;

            AppendSourceStream(SourceStreams.Positions, streams.Positions, baseVertex, count);
            AppendSourceStream(SourceStreams.TexCoords, streams.TexCoords, baseVertex, count);
            AppendSourceStream(SourceStreams.TexCoords1, streams.TexCoords1, baseVertex, count);
            AppendSourceStream(SourceStreams.Normals, streams.Normals, baseVertex, count);
            AppendSourceStream(SourceStreams.Tangents, streams.Tangents, baseVertex, count);
            AppendSourceStream(SourceStreams.VertexPaintBlendParams, streams.VertexPaintBlendParams, baseVertex, count);
            AppendSourceStream(SourceStreams.VertexPaintTintColor, streams.VertexPaintTintColor, baseVertex, count);

            Vertices.EnsureCapacity(baseVertex + count);

            Vertices.AddRange(HalfEdgeMesh.AddVertices(count));

            for (var i = 0; i < count; i++)
            {
                Positions[Vertices[baseVertex + i]] = streams.Positions[i] + positionOffset;
            }

            return baseVertex;
        }

        // Keeps a source stream indexable by absolute input vertex index across AddVertices() calls.
        // A stream a batch doesn't provide is padded with defaults, as long as any batch provides it.
        private static void AppendSourceStream<T>(List<T> accumulated, List<T> incoming, int baseVertex, int count) where T : struct
        {
            if (incoming.Count == 0 && accumulated.Count == 0)
            {
                return;
            }

            if (accumulated.Count < baseVertex)
            {
                accumulated.AddRange(Enumerable.Repeat(default(T), baseVertex - accumulated.Count));
            }

            if (incoming.Count > 0)
            {
                accumulated.AddRange(incoming);
            }
            else
            {
                accumulated.AddRange(Enumerable.Repeat(default(T), count));
            }
        }

        /// <summary>
        /// Adds one face. Faces that would leave the mesh non manifold are dropped and counted in
        /// <see cref="FacesRemoved"/>.
        /// </summary>
        /// <param name="indices">Corner vertices, as indices into the vertices added so far.</param>
        /// <param name="material">Material the face uses.</param>
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

            // AddFace will validate the face against all topology rules, if it fails, we duplicate its vertices, extracting the face
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

                var normal = SourceStreams.Normals.Count > 0
                    ? SourceStreams.Normals[sourceIndex]
                    : CalculateNormal(hEdge);

                var tangent = SourceStreams.Tangents.Count > 0
                    ? SourceStreams.Tangents[sourceIndex]
                    : CalculateTangentFromNormal(normal);

                var position = Positions[hEdge.Vertex];

                Normals[hEdge] = normal;
                Tangents[hEdge] = tangent;

                TextureCoords[hEdge] = SourceStreams.TexCoords.Count > 0
                    ? SourceStreams.TexCoords[sourceIndex]
                    : CalculateTriplanarUVs(position, normal);

                TextureCoords1[hEdge] = SourceStreams.TexCoords1.Count > 0
                    ? SourceStreams.TexCoords1[sourceIndex]
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

        // Faces which can't be integrated into the existing topology (they would create a nonmanifold edge or vertex)
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

            // the duplicated vertices are isolated, so this can't fail
            HalfEdgeMesh.AddFace(out var hFace, vertices);
            Extracted[hFace] = true;

            // need to write new half edge stream data
            WriteFaceData(hFace, indices, material);
        }

        /// <summary>
        /// Adds a physics hull as mesh faces.
        /// </summary>
        /// <param name="desc">Hull to add.</param>
        /// <param name="phys">Physics data the hull belongs to, read for its collision attributes.</param>
        /// <param name="materialNameProvider">Maps a surface property to the material to use.</param>
        /// <param name="positionOffset">Offset added to every position.</param>
        /// <param name="materialOverride">Material to use instead of the one the surface property picks.</param>
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
            VertexStreams streams = new();
            streams.Positions.AddRange(hull.GetVertexPositions());
            var baseVertex = AddVertices(streams, positionOffset);

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

                    inds[indexCount] = baseVertex + hullEdges[he].Origin;
                    he = hullEdges[he].Next;
                    indexCount++;
                }
                while (he != startHe);

                AddFace(inds[..indexCount], material);
            }
        }

        /// <summary>
        /// Adds a physics mesh as mesh faces.
        /// </summary>
        /// <param name="desc">Mesh to add.</param>
        /// <param name="phys">Physics data the mesh belongs to, read for its collision attributes.</param>
        /// <param name="materialNameProvider">Maps a surface property to the material to use.</param>
        /// <param name="deletedIndices">Vertices to leave out, usually the ones a render mesh already covers.</param>
        /// <param name="positionOffset">Offset added to every position.</param>
        /// <param name="materialOverride">Material to use instead of the one the surface property picks.</param>
        /// <param name="triangleRangeMin">First triangle to add when <paramref name="useTriangleRange"/> is set.</param>
        /// <param name="triangleRangeMax">Triangle to stop before when <paramref name="useTriangleRange"/> is set.</param>
        /// <param name="useTriangleRange">Whether to add only the given triangle range.</param>
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

            // drop the triangles a render mesh already covers before reindexing, so only the vertices
            // of the surviving triangles end up in the mesh and no loose vertices are written
            var keptTriangles = new List<Triangle>(triangleStop - triangleStart);
            var keptTriangleIndices = new List<int>(triangleStop - triangleStart);
            var removed = 0;

            for (var i = triangleStart; i < triangleStop; i++)
            {
                var triangle = meshTriangles[i];

                if (deletedIndices.Contains(triangle.X)
                 || deletedIndices.Contains(triangle.Y)
                 || deletedIndices.Contains(triangle.Z))
                {
                    removed++;
                    continue;
                }

                keptTriangles.Add(triangle);
                keptTriangleIndices.Add(i);
            }

            if (removed > 0)
            {
                ProgressReporter?.Report($"{nameof(HammerMeshBuilder)}: Total physics triangles removed: {removed}");
            }

            if (keptTriangles.Count == 0)
            {
                return;
            }

            var newMesh = ReindexTriangleMesh(mesh.GetVertices(), keptTriangles.ToArray(), 0, keptTriangles.Count);

            VertexStreams streams = new();
            streams.Positions.AddRange(newMesh.NewVertices);
            var baseVertex = AddVertices(streams, positionOffset);

            Span<int> inds = stackalloc int[3];

            for (var i = 0; i < newMesh.NewTriangles.Count; i++)
            {
                var newTriangle = newMesh.NewTriangles[i];

                inds[0] = baseVertex + (int)newTriangle.X;
                inds[1] = baseVertex + (int)newTriangle.Y;
                inds[2] = baseVertex + (int)newTriangle.Z;

                if (group == "Default")
                {
                    var physicsSurfaces = mesh.Materials;
                    // physicsSurfaces didn't get reindexed, look it up by the original triangle index
                    var surfacePropertyIndex = physicsSurfaces.Length > 0 ? physicsSurfaces[keptTriangleIndices[i]] : desc.SurfacePropertyIndex;
                    var surfaceProperty = physicsSurfaceNames[surfacePropertyIndex];

                    material = surfaceProperty switch
                    {
                        "default" => "materials/tools/toolsnodraw.vmat", // default is just nodraw, ignore it
                        _ => materialNameProvider.Invoke(surfaceProperty)
                    };
                }

                AddFace(inds, material);
            }
        }

        /// <summary>
        /// Adds a render mesh, splitting it per face set so each keeps its own material.
        /// </summary>
        /// <param name="shape">Mesh to add.</param>
        /// <param name="transform">Transform applied to positions, normals and tangents.</param>
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

            VertexStreams streams = new();
            streams.Positions.AddRange(newVertices);
            streams.TexCoords.AddRange(newTexcoords);
            streams.TexCoords1.AddRange(newTexcoords1);
            streams.Normals.AddRange(newNormals);
            streams.Tangents.AddRange(newTangents);
            streams.VertexPaintBlendParams.AddRange(newVertexPaintBlendParams);
            streams.VertexPaintTintColor.AddRange(newVertexPaintTintColor);

            var baseVertex = AddVertices(streams);

            foreach (var (faceIndices, faceSet) in faceList)
            {
                for (var i = 0; i < faceIndices.Length; i++)
                {
                    faceIndices[i] += baseVertex;
                }

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
            var vector1 = v2 - v1;
            var vector2 = v3 - v1;

            var crossProduct = Vector3.Cross(vector1, vector2);

            const float epsilon = 1e-10f;
            return crossProduct.Length() < epsilon;
        }

        /// <summary>
        /// Rebuilds a triangle range against a vertex list holding only the vertices that range uses.
        /// </summary>
        /// <param name="vertices">Vertices the triangles index into.</param>
        /// <param name="triangles">Triangles to reindex.</param>
        /// <param name="trianglesRangeStart">First triangle to take.</param>
        /// <param name="trianglesRangeEnd">Triangle to stop before.</param>
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

        /// <summary>
        /// Creates a named mesh data stream, optionally filled with initial values.
        /// </summary>
        /// <typeparam name="TArray">Datamodel array type backing the stream.</typeparam>
        /// <typeparam name="T">Element type of the stream.</typeparam>
        /// <param name="dataStateFlags">Flags describing how the stream is stored.</param>
        /// <param name="name">Stream name, in "semantic:index" form.</param>
        /// <param name="standardAttributeName">Name Hammer knows the stream by, defaults to the semantic.</param>
        /// <param name="data">Values to seed the stream with.</param>
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
