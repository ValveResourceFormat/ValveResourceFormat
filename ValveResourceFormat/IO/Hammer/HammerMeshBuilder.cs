using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
    /// Builds a Hammer editable mesh and writes it out as a <see cref="CDmePolygonMesh"/>.
    /// </summary>
    /// 
    /// <remarks>
    /// <para>
    /// Add vertices with <see cref="AddVertices"/> and faces with <see cref="AddFace"/> (or use one of the adders for
    /// render and physics meshes), then write the result with <see cref="GenerateMesh"/>.
    ///
    /// There are options for features such as <see cref="Untriangulate"/> to join
    /// triangle pairs into quads or <see cref="GenerateMeshes"/> to split the mesh by mesh connectivity.
    /// </para>
    /// 
    /// <para>
    /// The mesh itself is a <see cref="PolygonMesh"/>, a half edge topology with the data of a Hammer mesh, position
    /// per vertex, corner data per half edge, material per face. <see cref="WriteMesh"/> loops through it and writes
    /// the vmap format.
    /// </para>
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
        /// Data of one face corner, as the source mesh had it. A stream the source doesn't have is left null and
        /// gets a computed value: the face normal, a tangent from it, and planar mapped texture coordinates.
        /// </summary>
        public readonly record struct Corner(
            Vector2? TexCoord = null,
            Vector2? TexCoord1 = null,
            Vector3? Normal = null,
            Vector4? Tangent = null,
            Vector4? VertexPaintBlendParams = null,
            Vector4? VertexPaintTintColor = null);

        /// <summary>
        /// Number of faces dropped while building, either degenerate or non manifold.
        /// </summary>
        public int FacesRemoved { get; private set; }

        /// <summary>
        /// Number of faces handed to the builder, including the ones it dropped.
        /// </summary>
        public int OriginalFaceCount { get; private set; }

        /// <summary>
        /// The mesh being built. Its editing operations (merging, dissolving, splitting into islands) can be used
        /// once all faces were added, before the mesh is written out.
        /// </summary>
        public PolygonMesh Mesh { get; } = new();

        // input vertex index to mesh vertex
        private readonly List<VertexHandle> Vertices = [];

        /// <summary>
        /// Matcher that reports which physics vertices a render mesh already covers.
        /// </summary>
        public PhysicsVertexMatcher? PhysicsVertexMatcher { get; init; }

        /// <summary>
        /// Receives diagnostics about faces the builder had to drop.
        /// </summary>
        public IProgress<string>? ProgressReporter { get; init; }

        /// <summary>
        /// Join coplanar triangle pairs into quads when writing the mesh out, where they use the same material and
        /// agree on their corner data along the shared edge. Off by default, the faces are written as they were added.
        /// </summary>
        public bool Untriangulate { get; init; }

        /// <summary>
        /// Returns the size in texels of the texture a material uses, for faces added without texture coordinates:
        /// their texture is projected onto them in texels, like Hammer does. Null for materials it doesn't know, which
        /// are projected with <see cref="DefaultProjectedTextureSize"/>.
        /// </summary>
        public Func<string, Vector2?>? TextureSizeProvider { get; init; }

        /// <summary>
        /// Texture size assumed for a projected face when <see cref="TextureSizeProvider"/> gives none: a texture
        /// that repeats every 128 units at <see cref="PolygonMesh.DefaultTextureScale"/>, the usual Hammer world mapping.
        /// </summary>
        public Vector2 DefaultProjectedTextureSize { get; init; } = new(128f / PolygonMesh.DefaultTextureScale);

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

            if (Untriangulate)
            {
                // merge coplanar triangle pairs into quads before writing to the vmap
                // TODO: there may possibly be smarter heuristics to merge by
                var quadsMerged = Mesh.Untriangulate();

#if DEBUG
                if (quadsMerged > 0)
                {
                    ProgressReporter?.Report($"{nameof(HammerMeshBuilder)}: Untriangulated '{quadsMerged}' triangle pairs into quads");
                }
#endif
            }

            return WriteMesh(Mesh);
        }

        /// <summary>
        /// Writes everything added so far out as Hammer meshes, one per island of faces connected through shared
        /// edges. Faces that were extracted because they didn't fit the topology are grouped by coinciding
        /// positions. Each island is copied into its own mesh and written out, untriangulated when <see cref="Untriangulate"/> is set.
        /// </summary>
        public List<CDmePolygonMesh> GenerateMeshes()
        {
            var islands = Mesh.FindIslands();
            var meshes = new List<CDmePolygonMesh>(islands.Count);

            foreach (var islandFaces in islands)
            {
                var island = new PolygonMesh();
                island.MergeMesh(Mesh, islandFaces, out _, out _, out _);

                if (Untriangulate)
                {
                    island.Untriangulate();
                }

                meshes.Add(WriteMesh(island));
            }

#if DEBUG
            if (meshes.Count > 1)
            {
                ProgressReporter?.Report($"{nameof(HammerMeshBuilder)}: Split into {meshes.Count} meshes");
            }
#endif

            return meshes;
        }

        /// <summary>
        /// Writes a polygon mesh out as a Hammer mesh.
        /// </summary>
        /// <param name="polygonMesh">Mesh to write.</param>
        public static CDmePolygonMesh WriteMesh(PolygonMesh polygonMesh)
        {
            // dissolving edges leaves holes in the component lists, build remap tables so the vmap gets dense indices
            // twin half edges are freed in whole pairs, so surviving pairs stay adjacent and both halves map to newIndex / 2
            var halfEdgeRemap = new int[polygonMesh.Topology.HalfEdgeCount];
            var activeHalfEdgeCount = 0;
            for (var i = 0; i < polygonMesh.Topology.HalfEdgeCount; i++)
            {
                halfEdgeRemap[i] = polygonMesh.Topology.IsHalfEdgeAllocated(i) ? activeHalfEdgeCount++ : -1;
            }

            var faceRemap = new int[polygonMesh.Topology.FaceCount];
            var activeFaceCount = 0;
            for (var i = 0; i < polygonMesh.Topology.FaceCount; i++)
            {
                faceRemap[i] = polygonMesh.Topology.IsFaceAllocated(i) ? activeFaceCount++ : -1;
            }

            // merging and collapsing frees vertices too
            var vertexRemap = new int[polygonMesh.Topology.VertexCount];
            var activeVertexCount = 0;
            for (var i = 0; i < polygonMesh.Topology.VertexCount; i++)
            {
                vertexRemap[i] = polygonMesh.Topology.IsVertexAllocated(i) ? activeVertexCount++ : -1;
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

            for (var i = 0; i < polygonMesh.Topology.VertexCount; i++)
            {
                if (vertexRemap[i] == -1)
                {
                    continue;
                }

                var vertexDataIndex = mesh.VertexData.Size;

                var hVertex = new VertexHandle(i, polygonMesh.Topology); // by index, several input vertices share one mesh vertex after merging
                var vertexEdge = hVertex.Edge.Index;
                mesh.VertexEdgeIndices.Add(vertexEdge == -1 ? -1 : halfEdgeRemap[vertexEdge]);

                mesh.VertexDataIndices.Add(vertexDataIndex);
                mesh.VertexData.Size++;

                vertexPositions.Data.Add(polygonMesh.Positions[hVertex]);
            }

            for (var i = 0; i < activeHalfEdgeCount / 2; i++)
            {
                mesh.EdgeData.Size++;
                edgeFlags.Data.Add((int)EdgeFlag.None);
            }

            for (var i = 0; i < polygonMesh.Topology.HalfEdgeCount; i++)
            {
                var newIndex = halfEdgeRemap[i];
                if (newIndex == -1)
                {
                    continue;
                }

                var hEdge = new HalfEdgeHandle(i, polygonMesh.Topology);

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
                normals.Data.Add(polygonMesh.Normals[hEdge]);
                tangents.Data.Add(polygonMesh.Tangents[hEdge]);
                texcoords.Data.Add(polygonMesh.TextureCoords[hEdge]);
                texcoords1.Data.Add(polygonMesh.TextureCoords1[hEdge]);
                vertexpaintblendparams.Data.Add(polygonMesh.VertexPaintBlendParams[hEdge]);
                vertexpainttintcolor.Data.Add(polygonMesh.VertexPaintTintColor[hEdge]);
            }

            foreach (var material in polygonMesh.Materials)
            {
                mesh.Materials.Add(material);
            }

            for (var i = 0; i < polygonMesh.Topology.FaceCount; i++)
            {
                if (faceRemap[i] == -1)
                {
                    continue;
                }

                var hFace = new FaceHandle(i, polygonMesh.Topology);

                var faceDataIndex = mesh.FaceData.Size;
                mesh.FaceDataIndices.Add(faceDataIndex);
                mesh.FaceData.Size++;

                faceMaterialIndices.Data.Add(polygonMesh.MaterialIndex[hFace]);
                faceFlags.Data.Add(0);

                mesh.FaceEdgeIndices.Add(halfEdgeRemap[hFace.Edge.Index]);
            }

            mesh.SubdivisionData.SubdivisionLevels.AddRange(Enumerable.Repeat(0, 8));

            return mesh;
        }

        /// <summary>
        /// Adds the vertices of one source mesh. Faces added afterwards index into these vertices, offset by
        /// the returned base index when several source meshes are added to one builder.
        /// </summary>
        /// <param name="positions">Vertex positions.</param>
        /// <param name="positionOffset">Offset added to every position.</param>
        /// <returns>Index of the first added vertex, to add to the indices handed to <see cref="AddFace"/>.</returns>
        public int AddVertices(ReadOnlySpan<Vector3> positions, Vector3 positionOffset = new Vector3())
        {
            var baseVertex = Vertices.Count;

            var hVertices = Mesh.AddVertices(positions);
            Vertices.AddRange(hVertices);

            if (positionOffset != Vector3.Zero)
            {
                foreach (var hVertex in hVertices)
                {
                    Mesh.Positions[hVertex] += positionOffset;
                }
            }

            return baseVertex;
        }

        /// <summary>
        /// Adds one face. Faces that would leave the mesh non manifold are added on duplicated vertices instead
        /// and counted in <see cref="FacesRemoved"/>, degenerate faces are dropped.
        /// </summary>
        /// <param name="indices">Corner vertices, as indices into the vertices added so far.</param>
        /// <param name="material">Material the face uses.</param>
        /// <param name="corners">Corner data, one per index, or empty to compute it from the face.</param>
        public void AddFace(ReadOnlySpan<int> indices, string material, ReadOnlySpan<Corner> corners = default)
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
                    Mesh.Positions[Vertices[indices[0]]],
                    Mesh.Positions[Vertices[indices[1]]],
                    Mesh.Positions[Vertices[indices[2]]]))
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
            if (Mesh.Topology.AddFace(out var hFace, vertices))
            {
                WriteCorners(hFace, material, corners);
                return;
            }

            ExtractFace(indices, material, corners);
        }

        // writes the material and the corner data of a new face, computing what the source didn't have
        private void WriteCorners(FaceHandle hFace, string material, ReadOnlySpan<Corner> corners)
        {
            Mesh.SetFaceMaterial(hFace, material);

            var faceNormal = Vector3.Zero;
            if (corners.IsEmpty || corners[0].Normal is null)
            {
                Mesh.ComputeFaceNormal(hFace, out faceNormal);
            }

            // the face edge points at the half edge ending at the first input vertex,
            // so walking the loop visits the corners in input order
            var hEdge = hFace.Edge;
            var i = 0;
            var missingTexCoords = false;
            var missingTexCoords1 = false;
            var missingTangents = false;

            do
            {
                var corner = i < corners.Length ? corners[i] : default;

                Mesh.Normals[hEdge] = corner.Normal ?? faceNormal;
                Mesh.VertexPaintBlendParams[hEdge] = corner.VertexPaintBlendParams ?? default;
                Mesh.VertexPaintTintColor[hEdge] = corner.VertexPaintTintColor ?? default;

                if (corner.TexCoord is { } texCoord)
                {
                    Mesh.TextureCoords[hEdge] = texCoord;
                }
                else
                {
                    missingTexCoords = true;
                }

                if (corner.TexCoord1 is { } texCoord1)
                {
                    Mesh.TextureCoords1[hEdge] = texCoord1;
                }
                else
                {
                    missingTexCoords1 = true;
                }

                if (corner.Tangent is { } tangent)
                {
                    Mesh.Tangents[hEdge] = tangent;
                }
                else
                {
                    missingTangents = true;
                }

                hEdge = hEdge.NextEdge;
                i++;
            }
            while (hEdge != hFace.Edge);

            // a face without texture coordinates gets Hammer's default world aligned projection
            if (missingTexCoords)
            {
                Mesh.TextureAlignToGrid(hFace, TextureSizeProvider?.Invoke(material) ?? DefaultProjectedTextureSize);
            }

            if (!missingTexCoords1 && !missingTangents)
            {
                return;
            }

            // the second texture coordinates fall back to the first, and tangents follow from the face's
            // texture mapping, which needs every corner's texture coordinates in place
            hEdge = hFace.Edge;
            i = 0;

            do
            {
                var corner = i < corners.Length ? corners[i] : default;

                if (corner.TexCoord1 is null)
                {
                    Mesh.TextureCoords1[hEdge] = Mesh.TextureCoords[hEdge];
                }

                if (corner.Tangent is null)
                {
                    Mesh.ComputeFaceVertexTangent(hEdge, Mesh.Normals[hEdge], out var tangent);
                    Mesh.Tangents[hEdge] = tangent;
                }

                hEdge = hEdge.NextEdge;
                i++;
            }
            while (hEdge != hFace.Edge);
        }

        // Faces which can't be integrated into the existing topology (they would create a nonmanifold edge or vertex)
        // are added as a disconnected island with duplicated vertices, so no geometry is lost
        private void ExtractFace(ReadOnlySpan<int> indices, string material, ReadOnlySpan<Corner> corners)
        {
            FacesRemoved++;

#if DEBUG
            ProgressReporter?.Report($"{nameof(HammerMeshBuilder)}: Face '{Mesh.Topology.FaceCount}' did not fit into the mesh topology, extracting it with duplicated vertices");
#endif

            var vertices = new VertexHandle[indices.Length];

            for (var i = 0; i < indices.Length; i++)
            {
                var hVertex = Mesh.AddVertex(Mesh.Positions[Vertices[indices[i]]]);
                Vertices.Add(hVertex);
                vertices[i] = hVertex;
            }

            // the duplicated vertices are isolated, so this can't fail
            Mesh.Topology.AddFace(out var hFace, vertices);

            WriteCorners(hFace, material, corners);
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
            var baseVertex = AddVertices(hull.GetVertexPositions(), positionOffset);

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
            var baseVertex = AddVertices(CollectionsMarshal.AsSpan(newMesh.NewVertices), positionOffset);

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
            var blendParams = GetElementArraySafe<Vector4>(vertexdata, "VertexPaintBlendParams$0");
            var tintColors = GetElementArraySafe<Vector4>(vertexdata, "VertexPaintTintColor$0");

            if (positions == null || positions.Count == 0)
            {
                throw new InvalidDataException("AddRenderMesh() trying to process a mesh with no vertices!");
            }

            // Only scan when the position buffer changes
            if (PhysicsVertexMatcher != null && PhysicsVertexMatcher.LastPositions != positions)
            {
                PhysicsVertexMatcher.LastPositions = positions;
                PhysicsVertexMatcher.ScanPhysicsPointCloudForMatches([.. positions], ProgressReporter);
            }

            // gather the triangles, and the source vertices they use, compacted
            List<(int[] Indices, DmeFaceSet FaceSet)> faceList = [];
            Dictionary<int, int> compactIndex = [];
            List<int> sourceIndices = [];
            List<int> inds = new(capacity: 3);

            foreach (var faceset in facesets.Cast<DmeFaceSet>())
            {
                foreach (var index in faceset.Faces)
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

                    var faceIndices = new int[inds.Count];

                    for (var i = 0; i < inds.Count; i++)
                    {
                        if (!compactIndex.TryGetValue(inds[i], out var newIndex))
                        {
                            newIndex = sourceIndices.Count;
                            compactIndex.Add(inds[i], newIndex);
                            sourceIndices.Add(inds[i]);
                        }

                        faceIndices[i] = newIndex;
                    }

                    faceList.Add((faceIndices, faceset));
                    inds.Clear();
                }
            }

            // positions and corner data per compacted vertex, in the mesh's transform
            var newPositions = new Vector3[sourceIndices.Count];
            var corners = new Corner[sourceIndices.Count];

            for (var i = 0; i < sourceIndices.Count; i++)
            {
                var sourceIndex = sourceIndices[i];
                var position = positions[sourceIndex];
                var normal = Get(normals, sourceIndex);
                var tangent = Get(tangents, sourceIndex);

                if (hasTransform)
                {
                    position = Vector3.Transform(position, transform);

                    if (normal is { } n)
                    {
                        normal = Vector3.Normalize(Vector3.TransformNormal(n, normalMatrix));
                    }

                    if (tangent is { } t)
                    {
                        var direction = Vector3.Normalize(Vector3.TransformNormal(new Vector3(t.X, t.Y, t.Z), transform));
                        tangent = new Vector4(direction, t.W);
                    }
                }

                newPositions[i] = position;
                corners[i] = new Corner(Get(texcoords, sourceIndex), Get(texcoords1, sourceIndex), normal, tangent, Get(blendParams, sourceIndex), Get(tintColors, sourceIndex));
            }

            var baseVertex = AddVertices(newPositions);

            Span<Corner> faceCorners = stackalloc Corner[3];

            foreach (var (faceIndices, faceSet) in faceList)
            {
                for (var i = 0; i < faceIndices.Length; i++)
                {
                    faceCorners[i] = corners[faceIndices[i]];
                    faceIndices[i] += baseVertex;
                }

                AddFace(faceIndices, faceSet.Material.MaterialName, faceCorners[..faceIndices.Length]);
            }

            static T? Get<T>(IList<T>? stream, int index) where T : struct
                => stream is { Count: > 0 } ? stream[index] : null;
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
