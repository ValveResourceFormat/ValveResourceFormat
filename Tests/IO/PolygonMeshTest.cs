using System.Linq;
using System.Threading.Tasks;
using ValveResourceFormat.IO;
using ValveResourceFormat.IO.ContentFormats.HalfEdgeMesh;
using ValveResourceFormat.IO.ContentFormats.ValveMap;

namespace Tests.IO
{
    public class PolygonMeshTest
    {
        // The triangles of every material of a mesh one after the other, with their vertices numbered through
        private sealed record Geometry(Vector3[] Positions, Vector3[] Normals, int[] Indices, PolygonMesh.BakedSubmesh[] Submeshes);

        // Converts a saved mesh into its runtime form, the way a map does after loading, and triangulates it
        private static Geometry Triangulate(CDmePolygonMesh stored)
        {
            var mesh = new PolygonMesh();
            stored.CopyToPolyMesh(mesh);

            var submeshes = mesh.Triangulate();
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var indices = new List<int>();

            foreach (var submesh in submeshes)
            {
                var firstVertex = positions.Count;

                positions.AddRange(submesh.Positions);
                normals.AddRange(submesh.Normals);
                indices.AddRange(submesh.Indices.Select(index => index + firstVertex));
            }

            return new Geometry([.. positions], [.. normals], [.. indices], submeshes);
        }

        /// <summary>
        /// A quad is cut along its shorter diagonal, unless that would leave a triangle outside a quad
        /// with a reflex corner.
        /// </summary>
        [Test]
        public async Task SplitsQuadsAlongTheBetterDiagonal()
        {
            var builder = new HammerMeshBuilder();

            builder.AddVertices(
            [
                new(0, 0, 0), new(4, 0, 0), new(5, 1, 0), new(0, 1, 0),
                new(10, 0, 0), new(14, 0, 0), new(11, 1, 0), new(10, 4, 0),
            ]);
            builder.AddFace([0, 1, 2, 3], "materials/a.vmat");
            builder.AddFace([4, 5, 6, 7], "materials/a.vmat");

            var geometry = Triangulate(builder.GenerateMesh());

            using (Assert.Multiple())
            {
                // From the second to the fourth corner, the shorter way across
                await Assert.That(string.Join(' ', geometry.Indices[..6])).IsEqualTo("0 1 3 1 2 3");

                // From the first corner to the reflex third one, the only way that stays inside
                await Assert.That(string.Join(' ', geometry.Indices[6..])).IsEqualTo("4 5 6 4 6 7");
            }
        }

        // Subdivides the faces of a mesh to a level each, 0 leaving a face as it is, and moves the point
        // at the centre of the subdivided ones by a displacement
        private static CDmePolygonMesh Subdivide(CDmePolygonMesh mesh, int[] faceLevels, Vector3 centreDisplacement)
        {
            var levels = mesh.SubdivisionData.SubdivisionLevels;
            var displacements = new Datamodel.Vector3Array();

            levels.Clear();
            levels.AddRange(Enumerable.Repeat(0, mesh.FaceVertexData.Size));

            for (var edge = 0; edge < mesh.EdgeFaceIndices.Count; edge++)
            {
                if (mesh.EdgeFaceIndices[edge] >= 0)
                {
                    levels[mesh.EdgeVertexDataIndices[edge]] = faceLevels[mesh.EdgeFaceIndices[edge]];
                }
            }

            // One patch per subdivided corner, in the order of their data
            foreach (var cornerLevel in levels.Where(static cornerLevel => cornerLevel > 0))
            {
                var patchSize = (1 << (cornerLevel - 1)) + 1;

                displacements.AddRange(Enumerable.Repeat(Vector3.Zero, (patchSize * patchSize) - 1));
                displacements.Add(centreDisplacement);
            }

            mesh.SubdivisionData.Streams.Add(new CDmePolygonMeshDataStream
            {
                Name = "displacement:0",
                StandardAttributeName = "displacement",
                SemanticName = "displacement",
                Data = displacements,
            });

            return mesh;
        }

        /// <summary>
        /// Each corner of a subdivided face has a grid of points that starts at it, reaches halfway to the
        /// corners after and before it, and ends at the centre of the face. The points are moved by a
        /// displacement that is stored relative to the face.
        /// </summary>
        [Test]
        public async Task SubdividedFacesDrawTheDisplacedPatchesOfTheirCorners()
        {
            const int PatchSize = 3;
            const int PatchPoints = PatchSize * PatchSize;

            var builder = new HammerMeshBuilder();

            builder.AddVertices([new(0, 0, 0), new(4, 0, 0), new(4, 4, 0), new(0, 4, 0)]);
            builder.AddFace([0, 1, 2, 3], "materials/a.vmat");

            // Half a unit along the line from the middle of the first edge of the face to its centre, and two up
            var geometry = Triangulate(Subdivide(builder.GenerateMesh(), [2], new Vector3(0.5f, 0, 2)));
            var positions = geometry.Positions;

            await Assert.That(positions.Length).IsEqualTo(4 * PatchPoints);
            await Assert.That(geometry.Indices.Length).IsEqualTo(4 * 4 * 6);

            var firstEdgeMiddle = (positions[0] + positions[PatchPoints]) / 2f;
            var centre = new Vector3(2, 2, 0);
            var displacedCentre = centre + (0.5f * Vector3.Normalize(centre - firstEdgeMiddle)) + (2 * Vector3.UnitZ);
            var facesUp = true;

            for (var i = 0; i < geometry.Indices.Length; i += 3)
            {
                var a = positions[geometry.Indices[i]];
                var b = positions[geometry.Indices[i + 1]];
                var c = positions[geometry.Indices[i + 2]];

                facesUp &= Vector3.Cross(b - a, c - a).Z > 0f;
            }

            using (Assert.Multiple())
            {
                await Assert.That(facesUp).IsTrue();

                for (var corner = 0; corner < 4; corner++)
                {
                    var patch = positions.AsMemory(corner * PatchPoints, PatchPoints);
                    var next = positions[(corner + 1) % 4 * PatchPoints];
                    var previous = positions[(corner + 3) % 4 * PatchPoints];
                    var start = patch.Span[0];

                    await Assert.That(start.Z).IsEqualTo(0f);
                    await Assert.That(Vector3.Distance(patch.Span[PatchSize - 1], (start + next) / 2f)).IsLessThan(0.001f);
                    await Assert.That(Vector3.Distance(patch.Span[PatchPoints - PatchSize], (start + previous) / 2f)).IsLessThan(0.001f);
                    await Assert.That(Vector3.Distance(patch.Span[^1], displacedCentre)).IsLessThan(0.001f);
                }
            }
        }

        /// <summary>
        /// The points two patches of a face share shade the same from both, and so does the centre of the
        /// face from all of them.
        /// </summary>
        [Test]
        public async Task SubdividedFacesShadeSmoothAcrossTheirPatches()
        {
            const int PatchSize = 5;
            const int PatchPoints = PatchSize * PatchSize;

            var builder = new HammerMeshBuilder();

            builder.AddVertices([new(0, 0, 0), new(4, 0, 0), new(4, 4, 0), new(0, 4, 0)]);
            builder.AddFace([0, 1, 2, 3], "materials/a.vmat");

            var geometry = Triangulate(Subdivide(builder.GenerateMesh(), [3], new Vector3(0, 0, 2)));
            var normals = geometry.Normals;

            using (Assert.Multiple())
            {
                await Assert.That(normals.All(static normal => MathF.Abs(normal.Length() - 1f) < 0.001f)).IsTrue();

                for (var corner = 0; corner < 4; corner++)
                {
                    var patch = corner * PatchPoints;
                    var nextPatch = (corner + 1) % 4 * PatchPoints;

                    // The raised centre is level, the points around it lean away from it
                    await Assert.That(Vector3.Distance(normals[patch + PatchPoints - 1], Vector3.UnitZ)).IsLessThan(0.001f);
                    await Assert.That(normals[patch + PatchPoints - 2].Z).IsLessThan(0.99f);

                    // The last column of a patch is the last row of the patch of the next corner
                    for (var i = 0; i < PatchSize; i++)
                    {
                        var onLastColumn = patch + (i * PatchSize) + PatchSize - 1;
                        var onLastRow = nextPatch + ((PatchSize - 1) * PatchSize) + i;

                        await Assert.That(Vector3.Distance(geometry.Positions[onLastColumn], geometry.Positions[onLastRow])).IsLessThan(0.001f);
                        await Assert.That(Vector3.Distance(normals[onLastColumn], normals[onLastRow])).IsLessThan(0.001f);
                    }
                }
            }
        }

        /// <summary>
        /// An edge between two faces that are not subdivided alike is drawn with the points of the finer
        /// side on both sides, so that no cracks show between them.
        /// </summary>
        [Test]
        public async Task FacesMeetFinerSubdividedOnesPointForPoint()
        {
            var builder = new HammerMeshBuilder();

            builder.AddVertices(
            [
                new(0, 0, 0), new(4, 0, 0), new(4, 4, 0), new(0, 4, 0),
                new(8, 0, 0), new(8, 4, 0), new(12, 0, 0), new(12, 4, 0),
            ]);
            builder.AddFace([0, 1, 2, 3], "materials/a.vmat");
            builder.AddFace([1, 4, 5, 2], "materials/a.vmat");
            builder.AddFace([4, 6, 7, 5], "materials/a.vmat");

            var geometry = Triangulate(Subdivide(builder.GenerateMesh(), [3, 1, 0], Vector3.Zero));
            var positions = geometry.Positions;

            // Four patches of 5 by 5 points, then four of 2 by 2 with three more points in each of the two
            // cells along the finer face, then four corners with one more point halfway along the middle face
            const int FineFace = 4 * 25;
            const int MiddleFace = (4 * 4) + (2 * 3);
            const int PlainFace = 4 + 1;

            await Assert.That(positions.Length).IsEqualTo(FineFace + MiddleFace + PlainFace);

            var area = 0f;
            var facesUp = true;

            for (var i = 0; i < geometry.Indices.Length; i += 3)
            {
                var a = positions[geometry.Indices[i]];
                var b = positions[geometry.Indices[i + 1]];
                var c = positions[geometry.Indices[i + 2]];
                var cross = Vector3.Cross(b - a, c - a);

                facesUp &= cross.Z > 0f;
                area += cross.Length() / 2f;
            }

            string PointsAlong(float x, Range face)
                => string.Join(' ', positions[face].Where(position => position.X == x).Select(static position => position.Y).Distinct().Order());

            using (Assert.Multiple())
            {
                await Assert.That(facesUp).IsTrue();
                await Assert.That(MathF.Abs(area - 48f)).IsLessThan(0.001f);

                await Assert.That(PointsAlong(4f, ..FineFace)).IsEqualTo("0 0.5 1 1.5 2 2.5 3 3.5 4");
                await Assert.That(PointsAlong(4f, FineFace..(FineFace + MiddleFace))).IsEqualTo("0 0.5 1 1.5 2 2.5 3 3.5 4");

                await Assert.That(PointsAlong(8f, FineFace..(FineFace + MiddleFace))).IsEqualTo("0 2 4");
                await Assert.That(PointsAlong(8f, (FineFace + MiddleFace)..)).IsEqualTo("0 2 4");
            }
        }

        /// <summary>
        /// An L shaped face has a reflex corner, so fanning it out from one vertex would cover area outside it.
        /// </summary>
        [Test]
        public async Task TriangulatesConcaveFacesInsideTheirOutline()
        {
            var builder = new HammerMeshBuilder();

            builder.AddVertices(
            [
                new(0, 0, 0), new(4, 0, 0), new(4, 2, 0), new(2, 2, 0), new(2, 4, 0), new(0, 4, 0),
                new(10, 0, 0), new(12, 0, 0), new(12, 2, 0), new(10, 2, 0),
            ]);
            builder.AddFace([0, 1, 2, 3, 4, 5], "materials/b.vmat");
            builder.AddFace([6, 7, 8, 9], "materials/a.vmat");

            var mesh = builder.GenerateMesh();
            var geometry = Triangulate(mesh);
            var areas = new Dictionary<string, float>();
            var facesUp = true;

            foreach (var submesh in geometry.Submeshes)
            {
                for (var i = 0; i < submesh.Indices.Length; i += 3)
                {
                    var a = submesh.Positions[submesh.Indices[i]];
                    var b = submesh.Positions[submesh.Indices[i + 1]];
                    var c = submesh.Positions[submesh.Indices[i + 2]];
                    var cross = Vector3.Cross(b - a, c - a);

                    // Every triangle keeps the winding of its face
                    facesUp &= cross.Z > 0f;
                    areas[submesh.Material] = areas.GetValueOrDefault(submesh.Material) + (cross.Length() / 2f);
                }
            }

            using (Assert.Multiple())
            {
                await Assert.That(facesUp).IsTrue();
                await Assert.That(geometry.Positions.Length).IsEqualTo(10);
                await Assert.That(geometry.Indices.Length).IsEqualTo((4 + 2) * 3);
                await Assert.That(geometry.Submeshes.Length).IsEqualTo(2);
                await Assert.That(MathF.Abs(areas["materials/b.vmat"] - 12f)).IsLessThan(0.001f);
                await Assert.That(MathF.Abs(areas["materials/a.vmat"] - 4f)).IsLessThan(0.001f);
                await Assert.That(geometry.Normals.All(static normal => Vector3.Distance(normal, Vector3.UnitZ) < 0.001f)).IsTrue();
            }

            // A face hidden in Hammer is not drawn, a face that was only selected still is
            var faceFlags = (Datamodel.IntArray)mesh.FaceData.Streams.OfType<CDmePolygonMeshDataStream>().Single(static stream => stream.SemanticName == "flags").Data!;
            faceFlags[0] = (int)PolygonMeshFaceFlags.Hidden;
            faceFlags[1] = (int)PolygonMeshFaceFlags.Selected;

            var withHiddenFace = Triangulate(mesh);

            using (Assert.Multiple())
            {
                await Assert.That(withHiddenFace.Indices.Length).IsEqualTo(2 * 3);
                await Assert.That(withHiddenFace.Submeshes.Single().Material).IsEqualTo("materials/a.vmat");
            }
        }

        private static CDmePolygonMesh MakeTwoQuads()
        {
            var builder = new HammerMeshBuilder();

            builder.AddVertices([new(0, 0, 0), new(4, 0, 0), new(4, 4, 0), new(0, 4, 0), new(8, 0, 0), new(8, 4, 0)]);
            builder.AddFace([0, 1, 2, 3], "materials/a.vmat");
            builder.AddFace([1, 4, 5, 2], "materials/b.vmat");

            return builder.GenerateMesh();
        }

        /// <summary>
        /// A stored mesh whose arrays do not agree on how many vertices, edges or faces there are is not read.
        /// </summary>
        [Test]
        public async Task MeshesWhoseArraysDisagreeAreLeftEmpty()
        {
            var shortOnFaceData = MakeTwoQuads();
            shortOnFaceData.FaceData.Size--;

            var shortOnEdges = MakeTwoQuads();
            shortOnEdges.EdgeNextIndices.RemoveAt(shortOnEdges.EdgeNextIndices.Count - 1);

            foreach (var stored in new[] { shortOnFaceData, shortOnEdges })
            {
                var mesh = new PolygonMesh();
                stored.CopyToPolyMesh(mesh);

                using (Assert.Multiple())
                {
                    await Assert.That(mesh.FaceHandles.Any()).IsFalse();
                    await Assert.That(mesh.VertexHandles.Any()).IsFalse();
                    await Assert.That(mesh.Materials.Count).IsEqualTo(2);
                }
            }
        }

        /// <summary>
        /// A mesh is stored again with its data in the order it was read in, whatever order its vertices, edges and
        /// faces are in.
        /// </summary>
        [Test]
        public async Task MeshesAreStoredWithTheirDataInTheOrderItWasReadIn()
        {
            var stored = MakeTwoQuads();

            // The data of the face corners back to front
            var cornerCount = stored.FaceVertexData.Size;

            for (var i = 0; i < cornerCount; i++)
            {
                stored.EdgeVertexDataIndices[i] = cornerCount - 1 - stored.EdgeVertexDataIndices[i];
            }

            foreach (var stream in stored.FaceVertexData.Streams.OfType<CDmePolygonMeshDataStream>())
            {
                var data = (System.Collections.IList)stream.Data!;
                var reversed = data.Cast<object>().Reverse().ToArray();

                for (var i = 0; i < reversed.Length; i++)
                {
                    data[i] = reversed[i];
                }
            }

            stored.FaceDataIndices[0] = 1;
            stored.FaceDataIndices[1] = 0;

            var materialIndices = (Datamodel.IntArray)stored.FaceData.Streams.OfType<CDmePolygonMeshDataStream>().Single(static stream => stream.SemanticName == "materialindex").Data!;
            (materialIndices[0], materialIndices[1]) = (materialIndices[1], materialIndices[0]);

            var expectedCornerIndices = stored.EdgeVertexDataIndices.ToArray();
            var expectedTexCoords = ((Datamodel.Vector2Array)stored.FaceVertexData.Streams.OfType<CDmePolygonMeshDataStream>().Single(static stream => stream.Name == "texcoord:0").Data!).ToArray();
            var expectedMaterialIndices = materialIndices.ToArray();

            var mesh = new PolygonMesh();
            stored.CopyToPolyMesh(mesh);

            var written = new CDmePolygonMesh();
            written.CopyFromPolyMesh(mesh);

            using (Assert.Multiple())
            {
                await Assert.That(string.Join(' ', written.EdgeVertexDataIndices)).IsEqualTo(string.Join(' ', expectedCornerIndices));
                await Assert.That(string.Join(' ', written.FaceDataIndices)).IsEqualTo("1 0");
                await Assert.That(string.Join(' ', (Datamodel.Vector2Array)written.FaceVertexData.Streams.OfType<CDmePolygonMeshDataStream>().Single(static stream => stream.Name == "texcoord:0").Data!)).IsEqualTo(string.Join(' ', expectedTexCoords));
                await Assert.That(string.Join(' ', (Datamodel.IntArray)written.FaceData.Streams.OfType<CDmePolygonMeshDataStream>().Single(static stream => stream.SemanticName == "materialindex").Data!)).IsEqualTo(string.Join(' ', expectedMaterialIndices));
                await Assert.That(mesh.Materials[mesh.MaterialIndex[0]]).IsEqualTo("materials/a.vmat");
            }
        }

        /// <summary>
        /// The streams of a stored mesh go by the type of their values: vectors of two, three and four numbers,
        /// then integers.
        /// </summary>
        [Test]
        public async Task StreamsAreStoredByTheTypeOfTheirValues()
        {
            var stored = MakeTwoQuads();

            static string Types(CDmePolygonMeshDataArray array)
                => string.Join(' ', array.Streams.OfType<CDmePolygonMeshDataStream>().Select(static stream => stream.Data!.GetType().Name.Replace("Array", string.Empty, StringComparison.Ordinal)));

            using (Assert.Multiple())
            {
                await Assert.That(Types(stored.FaceVertexData)).IsEqualTo("Vector2 Vector2 Vector3 Vector4 Vector4 Vector4");
                await Assert.That(Types(stored.FaceData)).IsEqualTo("Vector2 Vector4 Vector4 Int Int Int");
            }
        }

        /// <summary>
        /// A face whose loop of half edges is broken is removed when a mesh is cleaned up, along with the edges
        /// and vertices only it used. What is left still draws.
        /// </summary>
        [Test]
        public async Task RemovesWhatDoesNotHoldTogether()
        {
            var stored = MakeTwoQuads();
            var secondFaceEdge = stored.FaceEdgeIndices[1];

            // A half edge of the second face that claims to belong to no face
            stored.EdgeFaceIndices[stored.EdgeNextIndices[secondFaceEdge]] = -1;

            var mesh = new PolygonMesh();
            stored.CopyToPolyMesh(mesh);

            await Assert.That(mesh.FaceHandles.Count()).IsEqualTo(2);

            mesh.RemoveInvalidComponents();
            mesh.RemoveIsolatedVertices();

            var geometry = mesh.Triangulate();

            using (Assert.Multiple())
            {
                await Assert.That(mesh.FaceHandles.Count()).IsEqualTo(1);
                await Assert.That(mesh.VertexHandles.Count()).IsEqualTo(4);
                await Assert.That(geometry.Single().Indices.Length).IsEqualTo(6);
                await Assert.That(geometry.Single().Material).IsEqualTo("materials/a.vmat");
            }
        }

        /// <summary>
        /// A vertex no edge is connected to is removed when a mesh is cleaned up.
        /// </summary>
        [Test]
        public async Task RemovesVerticesNoEdgeIsConnectedTo()
        {
            var mesh = new PolygonMesh();
            MakeTwoQuads().CopyToPolyMesh(mesh);

            mesh.Topology.AddVertex();
            await Assert.That(mesh.VertexHandles.Count()).IsEqualTo(7);

            mesh.RemoveInvalidComponents();
            mesh.RemoveIsolatedVertices();

            using (Assert.Multiple())
            {
                await Assert.That(mesh.VertexHandles.Count()).IsEqualTo(6);
                await Assert.That(mesh.FaceHandles.Count()).IsEqualTo(2);
            }
        }

        /// <summary>
        /// The corners of a face are all subdivided to the level of its first one. The patch of a corner that was at
        /// a finer level is thinned out, and that of one at a coarser level interpolated.
        /// </summary>
        [Test]
        public async Task CornersAreBroughtToTheLevelOfTheFirstCornerOfTheirFace()
        {
            var builder = new HammerMeshBuilder();

            builder.AddVertices([new(0, 0, 0), new(4, 0, 0), new(4, 4, 0), new(0, 4, 0)]);
            builder.AddFace([0, 1, 2, 3], "materials/a.vmat");

            var stored = builder.GenerateMesh();
            var firstCorner = stored.EdgeVertexDataIndices[stored.FaceEdgeIndices[0]];
            var levels = stored.SubdivisionData.SubdivisionLevels;
            var displacements = new Datamodel.Vector3Array();

            levels.Clear();
            levels.AddRange(Enumerable.Repeat(0, stored.FaceVertexData.Size));

            // The corners of the face at levels 2, 3, 1 and 2, starting with the first one
            var cornerLevels = new[] { 2, 3, 1, 2 };
            var edge = stored.FaceEdgeIndices[0];

            foreach (var level in cornerLevels)
            {
                levels[stored.EdgeVertexDataIndices[edge]] = level;
                edge = stored.EdgeNextIndices[edge];
            }

            // Each patch holds the same slope, sampled as finely as its level has points
            foreach (var level in levels.Where(static level => level > 0))
            {
                var cells = 1 << (level - 1);

                for (var row = 0; row <= cells; row++)
                {
                    for (var column = 0; column <= cells; column++)
                    {
                        displacements.Add(new Vector3((float)column / cells, (float)row / cells, 0));
                    }
                }
            }

            stored.SubdivisionData.Streams.Add(new CDmePolygonMeshDataStream
            {
                Name = "displacement:0",
                StandardAttributeName = "displacement",
                SemanticName = "displacement",
                Data = displacements,
            });

            var mesh = new PolygonMesh();
            stored.CopyToPolyMesh(mesh);
            mesh.InitFaceSubdivisionLevels();

            var written = new CDmePolygonMesh();
            written.CopyFromPolyMesh(mesh);

            var writtenDisplacements = (Datamodel.Vector3Array)written.SubdivisionData.Streams.OfType<CDmePolygonMeshDataStream>().Single().Data!;
            var expectedPatch = string.Join(' ', Enumerable.Range(0, 9).Select(static i => new Vector3((i % 3) / 2f, (i / 3) / 2f, 0)));

            using (Assert.Multiple())
            {
                await Assert.That(firstCorner).IsGreaterThanOrEqualTo(0);
                await Assert.That(written.SubdivisionData.SubdivisionLevels.Count(static level => level == 2)).IsEqualTo(4);
                await Assert.That(writtenDisplacements.Count).IsEqualTo(4 * 9);

                for (var patch = 0; patch < 4; patch++)
                {
                    await Assert.That(string.Join(' ', writtenDisplacements.Skip(patch * 9).Take(9))).IsEqualTo(expectedPatch);
                }
            }
        }

        /// <summary>
        /// The faces of each material are triangulated on their own, with vertices only they use.
        /// </summary>
        [Test]
        public async Task EachMaterialGetsVerticesAndTrianglesOfItsOwn()
        {
            var mesh = new PolygonMesh();
            MakeTwoQuads().CopyToPolyMesh(mesh);

            var submeshes = mesh.Triangulate();

            using (Assert.Multiple())
            {
                await Assert.That(string.Join(' ', submeshes.Select(static submesh => submesh.Material))).IsEqualTo("materials/a.vmat materials/b.vmat");
                await Assert.That(submeshes.All(static submesh => submesh.Positions.Length == 4 && submesh.Indices.Length == 6)).IsTrue();
                await Assert.That(submeshes.All(static submesh => submesh.Indices.All(index => (uint)index < (uint)submesh.Positions.Length))).IsTrue();
                await Assert.That(submeshes[1].Positions.Min(static position => position.X)).IsEqualTo(4f);
            }
        }

        private static void AddCornerStream(CDmePolygonMesh stored, string semanticName, int vertexBufferLocation, Func<int, Vector4> valueOfData, CDmePolygonMeshSubdivisiondataBinding? binding = null)
        {
            var data = new Datamodel.Vector4Array();
            data.AddRange(Enumerable.Range(0, stored.FaceVertexData.Size).Select(valueOfData));

            stored.FaceVertexData.Streams.Add(new CDmePolygonMeshDataStream
            {
                Name = $"{semanticName}:0",
                StandardAttributeName = semanticName,
                SemanticName = semanticName,
                VertexBufferLocation = vertexBufferLocation,
                DataStateFlags = 1,
                SubdivisionBinding = binding,
                Data = data,
            });
        }

        /// <summary>
        /// Every stream the mesh has for its face corners is carried onto the vertices, a stream the mesh has no
        /// property for included, along with the vertex buffer the stream names.
        /// </summary>
        [Test]
        public async Task EveryStreamIsCarriedOntoTheVertices()
        {
            var builder = new HammerMeshBuilder();

            builder.AddVertices([new(0, 0, 0), new(4, 0, 0), new(4, 4, 0), new(0, 4, 0)]);
            builder.AddFace([0, 1, 2, 3], "materials/a.vmat");

            var stored = builder.GenerateMesh();
            AddCornerStream(stored, "PerVertexLighting", 1, static data => new Vector4(data + 1, 0, 0, 1));

            var mesh = new PolygonMesh();
            stored.CopyToPolyMesh(mesh);

            var submesh = mesh.Triangulate().Single();
            var lighting = submesh.Streams.Single(static stream => stream.Stream.SemanticName == "PerVertexLighting");
            var texCoords = submesh.Streams.Single(static stream => stream.Stream.SemanticName == "texcoord" && stream.Stream.SemanticIndex == 0);

            // The value of each corner of the face, by where the file kept its data
            var expected = new List<float>();
            var edge = stored.FaceEdgeIndices[0];

            for (var i = 0; i < 4; i++)
            {
                expected.Add(stored.EdgeVertexDataIndices[edge] + 1);
                edge = stored.EdgeNextIndices[edge];
            }

            using (Assert.Multiple())
            {
                await Assert.That(lighting.Stream.VertexBufferLocation).IsEqualTo(1);
                await Assert.That(lighting.ComponentCount).IsEqualTo(4);
                await Assert.That(string.Join(' ', Enumerable.Range(0, 4).Select(i => lighting.Values[i * 4]))).IsEqualTo(string.Join(' ', expected));
                await Assert.That(texCoords.Stream.VertexBufferLocation).IsEqualTo(0);
                await Assert.That(texCoords.Values.Length).IsEqualTo(4 * 2);
                await Assert.That(submesh.Streams.Any(static stream => stream.Stream.SemanticName is "normal" or "tangent" or "position")).IsFalse();
            }
        }

        /// <summary>
        /// On a subdivided face a stream takes the values of the subdivision stream it is bound to. Paint that is
        /// bound to none is spread over the patches from the corners, and any other stream is left empty.
        /// </summary>
        [Test]
        public async Task SubdividedFacesTakeTheirStreamsFromWhatTheyAreBoundTo()
        {
            var builder = new HammerMeshBuilder();

            builder.AddVertices([new(0, 0, 0), new(4, 0, 0), new(4, 4, 0), new(0, 4, 0)]);
            builder.AddFace([0, 1, 2, 3], "materials/a.vmat");

            var stored = Subdivide(builder.GenerateMesh(), [2], Vector3.Zero);
            var patchPoints = 4 * 3 * 3;

            AddCornerStream(stored, "PerVertexLighting", 1, static _ => new Vector4(1, 1, 1, 1),
                new CDmePolygonMeshSubdivisiondataBinding { TargetDataType = (int)PolygonMeshDataType.Vector4, TargetStreamIndex = 0, StreamSourceType = (int)SubdivisionStreamSource.Stream });

            var boundValues = new Datamodel.Vector4Array();
            boundValues.AddRange(Enumerable.Repeat(new Vector4(7, 7, 7, 7), patchPoints));
            stored.SubdivisionData.Streams.Add(new CDmePolygonMeshDataStream { Name = "PerVertexLighting:0", SemanticName = "PerVertexLighting", Data = boundValues });

            AddCornerStream(stored, "VertexPaintDetailWeight", 1, static data => new Vector4(data, 0, 0, 0));
            AddCornerStream(stored, "Custom", 0, static _ => new Vector4(5, 5, 5, 5));

            var mesh = new PolygonMesh();
            stored.CopyToPolyMesh(mesh);

            var submesh = mesh.Triangulate().Single();

            float[] Values(string semanticName) => submesh.Streams.Single(stream => stream.Stream.SemanticName == semanticName).Values;

            var spread = Enumerable.Range(0, patchPoints).Select(i => Values("VertexPaintDetailWeight")[i * 4]).ToArray();

            // What the corners of the face hold, which is where the file kept the data of each
            var corners = new List<float>();
            var edge = stored.FaceEdgeIndices[0];

            for (var i = 0; i < 4; i++)
            {
                corners.Add(stored.EdgeVertexDataIndices[edge]);
                edge = stored.EdgeNextIndices[edge];
            }

            using (Assert.Multiple())
            {
                await Assert.That(submesh.Positions.Length).IsEqualTo(patchPoints);
                await Assert.That(Values("PerVertexLighting").All(static value => value == 7f)).IsTrue();
                await Assert.That(Values("Custom").All(static value => value == 0f)).IsTrue();

                // Each patch starts on the value of its corner and ends on the average of the face
                await Assert.That(string.Join(' ', Enumerable.Range(0, 4).Select(patch => spread[patch * 9]))).IsEqualTo(string.Join(' ', corners));
                await Assert.That(Enumerable.Range(0, 4).All(patch => MathF.Abs(spread[(patch * 9) + 8] - corners.Average()) < 0.001f)).IsTrue();
            }
        }
    }
}
