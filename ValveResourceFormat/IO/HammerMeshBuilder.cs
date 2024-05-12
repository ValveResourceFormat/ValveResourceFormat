using System.Linq;
using System.Runtime.InteropServices;
using ValveResourceFormat.IO.ContentFormats.DmxModel;
using ValveResourceFormat.IO.ContentFormats.ValveMap;
using ValveResourceFormat.ResourceTypes.RubikonPhysics;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;

#nullable enable
using HalfEdgeSlim = (int SrcVertexId, int DstVertexId);
using static ValveResourceFormat.IO.HammerMeshBuilder;
using ValveResourceFormat.Utils;
using System.Globalization;

namespace ValveResourceFormat.IO
{
    class HalfEdgeMeshModifier(HammerMeshBuilder Builder)
    {
        interface BaseModification
        {
            public abstract void Apply();
            public abstract void Revert();
        }

        struct HalfEdgeModification(HalfEdge HalfEdge, HalfEdgeProperty Property, int NewValue) : BaseModification
        {
            public HalfEdge HalfEdge { get; } = HalfEdge;
            public int OldValue { get; private set; } = -1;

            public void Apply()
            {
                if (Property == HalfEdgeProperty.Face)
                {
                    OldValue = HalfEdge.Face;
                    HalfEdge.Face = NewValue;
                }
                else if (Property == HalfEdgeProperty.Previous)
                {
                    OldValue = HalfEdge.Previous;
                    HalfEdge.Previous = NewValue;
                }
                else if (Property == HalfEdgeProperty.Next)
                {
                    OldValue = HalfEdge.Next;
                    HalfEdge.Next = NewValue;
                }
                else
                {
                    throw new NotImplementedException(nameof(Property));
                }
            }

            public readonly void Revert()
            {
                if (Property == HalfEdgeProperty.Face)
                {
                    HalfEdge.Face = OldValue;
                }
                else if (Property == HalfEdgeProperty.Previous)
                {
                    HalfEdge.Previous = OldValue;
                }
                else if (Property == HalfEdgeProperty.Next)
                {
                    HalfEdge.Next = OldValue;
                }
                else
                {
                    throw new NotImplementedException(nameof(Property));
                }
            }
        }

        struct VertexModification(Vertex Vertex, VertexProperty Property, int NewValue) : BaseModification
        {
            public Vertex Vertex { get; } = Vertex;
            public int OldValue { get; private set; } = -1;

            public void Apply()
            {
                switch (Property)
                {
                    case VertexProperty.OutGoingHalfEdge:
                        OldValue = Vertex.OutGoingHalfEdge;
                        Vertex.OutGoingHalfEdge = NewValue;
                        break;

                    case VertexProperty.RelatedEdge:
                        OldValue = Vertex.RelatedEdges.Count;
                        Vertex.RelatedEdges.Add(NewValue);
                        break;
                }
            }

            public readonly void Revert()
            {
                switch (Property)
                {
                    case VertexProperty.OutGoingHalfEdge:
                        Vertex.OutGoingHalfEdge = OldValue;
                        break;

                    case VertexProperty.RelatedEdge:
                        Vertex.RelatedEdges.Remove(NewValue);
                        break;
                }
            }
        }

        private readonly Stack<HalfEdgeModification> HalfEdgeModifications = [];
        private readonly Stack<HalfEdgeSlim> VertsToEdgeDictModifications = [];
        private readonly Stack<HalfEdge> HalfEdgeAdditions = [];
        private readonly Stack<VertexModification> VertexModifications = [];

        private Face? CurrentFace;

        public void SetNewFaceContext(Face face)
        {
            HalfEdgeModifications.Clear();
            VertsToEdgeDictModifications.Clear();
            HalfEdgeAdditions.Clear();
            VertexModifications.Clear();
            CurrentFace = face;
        }

        public enum HalfEdgeProperty
        {
            Face,
            Previous,
            Next
        }

        public enum VertexProperty
        {
            OutGoingHalfEdge,
            RelatedEdge
        }

        private void ChangeHalfEdgeProperty(HalfEdge he, HalfEdgeProperty property, int newValue)
        {
            var change = new HalfEdgeModification(he, property, newValue);
            change.Apply();
            HalfEdgeModifications.Push(change);
        }

        public void ChangeHalfEdgeFace(HalfEdge he, int face) => ChangeHalfEdgeProperty(he, HalfEdgeProperty.Face, face);
        public void ChangeHalfEdgePrev(HalfEdge he, int prev) => ChangeHalfEdgeProperty(he, HalfEdgeProperty.Previous, prev);
        public void ChangeHalfEdgeNext(HalfEdge he, int next) => ChangeHalfEdgeProperty(he, HalfEdgeProperty.Next, next);

        private void ChangeVertexProperty(Vertex vertex, VertexProperty property, int newValue)
        {
            var change = new VertexModification(vertex, property, newValue);
            change.Apply();
            VertexModifications.Push(change);
        }

        public void ChangeVertexOutGoing(Vertex vertex, int outGoingHalfEdge) => ChangeVertexProperty(vertex, VertexProperty.OutGoingHalfEdge, outGoingHalfEdge);
        public void AddVertexRelatedEdge(Vertex vertex, int relatedHalfEdge) => ChangeVertexProperty(vertex, VertexProperty.RelatedEdge, relatedHalfEdge);

        public bool TryAddVertsToEdgeDict(HalfEdgeSlim heKey, int heIndex)
        {
            var added = Builder.VertsToEdgeDict.TryAdd(heKey, heIndex);
            if (added)
            {
                VertsToEdgeDictModifications.Push(heKey);
            }

            return added;
        }

        public void AddHalfEdgeToHalfEdgesList(HalfEdge he)
        {
            HalfEdgeAdditions.Push(he);
            Builder.HalfEdges.Add(he);
        }

        // roll back
        public void RollBack(string error)
        {
            ArgumentNullException.ThrowIfNull(CurrentFace);
            Builder.FacesRemoved++;

            while (VertexModifications.TryPop(out var VertexModification))
            {
                VertexModification.Revert();
            }

            while (HalfEdgeModifications.TryPop(out var HalfEdgeModification))
            {
                HalfEdgeModification.Revert();
            }

            while (HalfEdgeAdditions.TryPop(out var HalfEdge))
            {
                Builder.HalfEdges.RemoveAt(HalfEdge.id);
            }

            while (VertsToEdgeDictModifications.TryPop(out var VertsToEdgeDictModification))
            {
                Builder.VertsToEdgeDict.Remove(VertsToEdgeDictModification);
            }

            Console.WriteLine("HammerMeshBuilder error: " + error + " Extracting face...");

            var baseVertex = Builder.Vertices.Count;
            var indexCount = CurrentFace.Indices.Count;
            Span<int> newIndices = indexCount < 32 ? stackalloc int[indexCount] : new int[indexCount];

            for (var i = 0; i < CurrentFace.Indices.Count; i++)
            {
                var vertex = CurrentFace.Indices[i];
                Vertex newVertex = new();
                newVertex.MasterStreamIndex = Builder.Vertices.Count;
                Builder.Vertices.Add(newVertex);

                var vertexStreams = Builder.vertexStreams;

                vertexStreams.positions.Add(vertexStreams.positions[vertex]);
                if (vertexStreams.texcoords.Count > 0)
                {
                    vertexStreams.texcoords.Add(vertexStreams.texcoords[vertex]);
                }
                if (vertexStreams.normals.Count > 0)
                {
                    vertexStreams.normals.Add(vertexStreams.normals[vertex]);
                }
                if (vertexStreams.tangents.Count > 0)
                {
                    vertexStreams.tangents.Add(vertexStreams.tangents[vertex]);
                }
                if (vertexStreams.VertexPaintBlendParams.Count > 0)
                {
                    vertexStreams.VertexPaintBlendParams.Add(vertexStreams.VertexPaintBlendParams[vertex]);
                }
            }

            for (var i = 0; i < newIndices.Length; i++)
            {
                newIndices[i] = baseVertex + i;
            }

            Builder.AddFace(newIndices, CurrentFace.MaterialName);
        }
    }

    public class PhysicsVertexMatcher
    {
        private readonly Vector3[] vertexPositions;
        public MeshDescriptor PhysicsMesh { get; }
        public Dictionary<int, int> RenderToPhys { get; } = [];
        public HashSet<int> DeletedVertexIndices { get; } = [];
        //public HashSet<(int, int, int)> DeletedTriangles { get; } = [];

        public PhysicsVertexMatcher(MeshDescriptor mesh)
        {
            PhysicsMesh = mesh;
            vertexPositions = mesh.Shape.GetVertices().ToArray();
            DeletedVertexIndices.EnsureCapacity(vertexPositions.Length / 4);
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

        public object? LastPositions { get; set; }
        public void ScanPhysicsPointCloudForMatches(ReadOnlySpan<Vector3> renderMeshPositions)
        {
            RenderToPhys.Clear();

            var localMatches = new HashSet<int>(capacity: renderMeshPositions.Length);
            RenderToPhys.EnsureCapacity(renderMeshPositions.Length);

            for (var j = 0; j < renderMeshPositions.Length; ++j)
            {
                if (j == 10 && localMatches.Count < 1
                 || j == 100 && localMatches.Count < 10)
                {
                    Console.WriteLine($"{nameof(PhysicsVertexMatcher)}: Bailing out due to low rate!");
                    return;
                }

                var pos = renderMeshPositions[j];
                const float epsilon = 0.016f;

                // Mirage: 22k faces with equality match, 5k with epsilon
                var match = Array.FindIndex(vertexPositions, v => (v - pos).LengthSquared() < epsilon);

                if (match == -1)
                {
                    continue;
                }

                localMatches.Add(match);
                //RenderToPhys[j] = match;
            }

            DeletedVertexIndices.UnionWith(localMatches);
            Console.WriteLine($"{nameof(PhysicsVertexMatcher)}: Matched {(float)localMatches.Count / renderMeshPositions.Length * 100f}% of rendermesh to physics vertices!");
        }
    }

    internal class HammerMeshBuilder
    {
        [Flags]
        public enum EdgeFlag
        {
            None = 0x0,
            SoftNormals = 0x1,
            HardNormals = 0x2,
        }

        public class Vertex
        {
            public int OutGoingHalfEdge = -1;
            public int MasterStreamIndex = -1;
            public List<int> RelatedEdges = [];
        }

        public class HalfEdge
        {
            public int Face = -1;
            public int Twin = -1;
            public int Next = -1;
            public int Previous = -1;
            public int destVert = -1;
            public int origVert = -1;
            public int id = -1;
            public bool OverrideOuter;
        }

        public class Face
        {
            public int HalfEdge = -1;
            public List<int> Indices = [];
            public string MaterialName = "unassigned";
        }

        public class VertexStreams
        {
            public List<Vector3> positions = new();
            public List<Vector2> texcoords = new();
            public List<Vector3> normals = new();
            public List<Vector4> tangents = new();
            public List<Vector4> VertexPaintBlendParams = new();
        }

        public int FacesRemoved;
        public int OriginalFaceCount;

        public List<Vertex> Vertices = [];
        public List<HalfEdge> HalfEdges = [];
        public List<Face> Faces = [];

        public Dictionary<HalfEdgeSlim, int> VertsToEdgeDict = [];

        public HalfEdgeMeshModifier halfEdgeModifier;
        public PhysicsVertexMatcher? PhysicsVertexMatcher { get; init; }

        private readonly IFileLoader FileLoader;

        public VertexStreams vertexStreams = new();

        public HammerMeshBuilder(IFileLoader fileLoader)
        {
            halfEdgeModifier = new(this);
            FileLoader = fileLoader;
        }

        public static string proceduralPhysMaterialsPath = "vrf/autogeneratedPhys/phys_";


        public CDmePolygonMesh GenerateMesh()
        {
            if (FacesRemoved > 0)
                Console.WriteLine($"HammerMeshBuilder: extracted '{FacesRemoved}' of '{OriginalFaceCount - FacesRemoved}' faces");

            var mesh = new CDmePolygonMesh();

            var faceMaterialIndices = CreateStream<Datamodel.IntArray, int>(8, "materialindex:0");
            var faceFlags = CreateStream<Datamodel.IntArray, int>(3, "flags:0");
            mesh.FaceData.Streams.Add(faceMaterialIndices);
            mesh.FaceData.Streams.Add(faceFlags);

            var texcoords = CreateStream<Datamodel.Vector2Array, Vector2>(1, "texcoord:0");
            var vertexpaintblendparams = CreateStream<Datamodel.Vector4Array, Vector4>(1, "VertexPaintBlendParams:0");
            var normals = CreateStream<Datamodel.Vector3Array, Vector3>(1, "normal:0");
            var tangents = CreateStream<Datamodel.Vector4Array, Vector4>(1, "tangent:0");
            mesh.FaceVertexData.Streams.Add(texcoords);
            mesh.FaceVertexData.Streams.Add(vertexpaintblendparams);
            mesh.FaceVertexData.Streams.Add(normals);
            mesh.FaceVertexData.Streams.Add(tangents);

            var vertexPositions = CreateStream<Datamodel.Vector3Array, Vector3>(3, "position:0");
            mesh.VertexData.Streams.Add(vertexPositions);

            var edgeFlags = CreateStream<Datamodel.IntArray, int>(3, "flags:0");
            mesh.EdgeData.Streams.Add(edgeFlags);

            foreach (var Vertex in Vertices)
            {
                var vertexDataIndex = mesh.VertexData.Size;

                mesh.VertexEdgeIndices.Add(Vertex.OutGoingHalfEdge);

                mesh.VertexDataIndices.Add(vertexDataIndex);
                mesh.VertexData.Size++;

                vertexPositions.Data.Add(vertexStreams.positions[Vertex.MasterStreamIndex]);
            }

            for (var i = 0; i < HalfEdges.Count / 2; i++)
            {
                mesh.EdgeData.Size++;
                edgeFlags.Data.Add((int)EdgeFlag.None);
            }

            var prevHalfEdge = -1;
            for (var i = 0; i < HalfEdges.Count; i++)
            {
                var halfEdge = HalfEdges[i];
                //EdgeData refers to a single edge, so its half of the total
                //of half edges and both halfs of the edge should have the same EdgeData Index
                if (halfEdge.Twin == prevHalfEdge)
                {
                    mesh.EdgeDataIndices.Add(prevHalfEdge / 2);
                }
                else
                {
                    mesh.EdgeDataIndices.Add(i / 2);
                }

                mesh.EdgeVertexIndices.Add(halfEdge.destVert);
                mesh.EdgeOppositeIndices.Add(halfEdge.Twin);
                mesh.EdgeNextIndices.Add(halfEdge.Next);
                mesh.EdgeFaceIndices.Add(halfEdge.Face);
                mesh.EdgeVertexDataIndices.Add(i);

                prevHalfEdge = i;

                mesh.FaceVertexData.Size += 1;

                var normal = Vector3.Zero;
                var tangent = Vector4.Zero;
                var uv = Vector2.Zero;
                var vertexPaintBlendParams = Vector4.Zero;

                if (halfEdge.Face != -1)
                {
                    var firstHalfEdgeInFace = Faces[halfEdge.Face].HalfEdge == i;

                    var startVertex = Vertices[halfEdge.destVert];

                    if (vertexStreams.normals.Count == 0)
                    {
                        normal = CalculateNormal(halfEdge.Next);
                    }
                    else
                    {
                        normal = vertexStreams.normals[startVertex.MasterStreamIndex];
                    }

                    if (vertexStreams.tangents.Count == 0)
                    {
                        tangent = CalculateTangentFromNormal(normal);
                    }
                    else
                    {
                        tangent = vertexStreams.tangents[startVertex.MasterStreamIndex];
                    }

                    if (vertexStreams.texcoords.Count == 0)
                    {
                        uv = CalculateTriplanarUVs(vertexStreams.positions[startVertex.MasterStreamIndex], normal);
                    }
                    else
                    {
                        uv = vertexStreams.texcoords[startVertex.MasterStreamIndex];
                    }

                    if (vertexStreams.VertexPaintBlendParams.Count != 0)
                    {
                        vertexPaintBlendParams = vertexStreams.VertexPaintBlendParams[startVertex.MasterStreamIndex];
                    };
                }

                normals.Data.Add(normal);
                tangents.Data.Add(tangent);
                texcoords.Data.Add(uv);
                vertexpaintblendparams.Data.Add(vertexPaintBlendParams);
            }

            foreach (var face in Faces)
            {
                var faceDataIndex = mesh.FaceData.Size;
                mesh.FaceDataIndices.Add(faceDataIndex);
                mesh.FaceData.Size++;

                var mat = face.MaterialName;
                        var materialIndex = mesh.Materials.IndexOf(mat);
                if (materialIndex == -1 && mat != null)
                {
                    materialIndex = mesh.Materials.Count;
                    faceMaterialIndices.Data.Add(materialIndex);
                    mesh.Materials.Add(mat);
                }

                faceFlags.Data.Add(0);

                mesh.FaceEdgeIndices.Add(face.HalfEdge);
            }

            mesh.SubdivisionData.SubdivisionLevels.AddRange(Enumerable.Repeat(0, 8));

            return mesh;
        }

        public void DefinePointCloud(VertexStreams streams, Vector3 positionOffset)
        {
            vertexStreams = streams;

            Vertices.EnsureCapacity(streams.positions.Count);

            for (var i = 0; i < streams.positions.Count; i++)
            {
                streams.positions[i] = streams.positions[i] + positionOffset;
                Vertex newVertex = new();
                newVertex.MasterStreamIndex = i;
                Vertices.Add(newVertex);
            }
        }

        public IEnumerable<int> VertexCirculator(int heIdx, bool forward)
        {
            //ArgumentOutOfRangeException.ThrowIfNegative(heIdx);
            if (heIdx < 0)
            {
                yield break;
            }

            var h = heIdx;
            var count = 0;
            do
            {
                yield return h;

                var twin = HalfEdges[HalfEdges[h].Twin];
                h = forward ? twin.Next : twin.Previous;

                if (h < 0) { throw new InvalidOperationException("Vertex circulator returned an invalid half edge index"); }
                if (count++ > 999) { throw new InvalidOperationException("Runaway vertex circulator"); }
            }
            while (h != heIdx);
        }


        public void AddFace(ReadOnlySpan<int> indices, string material)
        {
            var face = new Face();
            face.Indices.AddRange(indices);
            face.MaterialName = material;

            halfEdgeModifier.SetNewFaceContext(face);

            List<HalfEdge> newBoundaryHalfEdges = new(capacity: indices.Length);

            OriginalFaceCount++;

            if (!VerifyIndicesWithinBounds(indices))
            {
                Console.WriteLine($"HammerMeshBuilder error: failed to add face '{Faces.Count}', face has an index that is out of bounds.");
                FacesRemoved++;
                return;
            }

            // don't allow degenerate faces
            if (indices.Length < 3)
            {
                Console.WriteLine($"HammerMeshBuilder error: failed to add face '{Faces.Count}', face has less than 3 vertices.");
                FacesRemoved++;
                return;
            }

            // some map render meshes have faces with 0 area, check for that
            // only checking triangular faces because doing this for n-gons would be too expensive
            // and I doubt we'll ever get n-gons that are this fucked up
            if (indices.Length == 3)
            {
                Span<Vector3> vertexPositions = [
                    vertexStreams.positions[Vertices[indices[0]].MasterStreamIndex],
                    vertexStreams.positions[Vertices[indices[1]].MasterStreamIndex],
                    vertexStreams.positions[Vertices[indices[2]].MasterStreamIndex]
                ];

                if (AreVerticesCollinear(vertexPositions[0], vertexPositions[1], vertexPositions[2]))
                {
                    Console.WriteLine($"HammerMeshBuilder error: failed to add face '{Faces.Count}', face had 0 area");
                    FacesRemoved++;
                    return;
                }
            }

            var firstHalfEdgeId = -1;
            var previousHalfEdgeId = -1;

            Span<Vertex> v = new Vertex[2];

            for (var i = 0; i < indices.Length; i++)
            {
                var faceidx = Faces.Count;

                var v1idx = indices[i];
                var v2idx = indices[(i + 1) % indices.Length]; // will cause v2idx to wrap around

                v[0] = Vertices[v1idx];
                v[1] = Vertices[v2idx];

                var innerHeIndex = HalfEdges.Count; // since we haven't yet added this edge, its index is just the count of the list + newHalfEdges list
                var outerHeIndex = HalfEdges.Count + 1;

                var innerHeKey = new HalfEdgeSlim(v1idx, v2idx);
                var outerHeKey = new HalfEdgeSlim(v2idx, v1idx);

                HalfEdge? innerHe = null;
                HalfEdge? outerHe = null;

                var isFirstIteration = i == 0;
                var isLastIteration = i == indices.Length - 1;

                for (var j = 0; j < 2; j++)
                {
                    var openEdge = v[j].OutGoingHalfEdge == -1;

                    if (!openEdge)
                    {
                        foreach (var he in v[j].RelatedEdges)
                        {
                            if (HalfEdges[he].Face == -1)
                            {
                                openEdge = true;
                                //break;
                            }
                        }

                        if (!openEdge)
                        {
                            halfEdgeModifier.RollBack($"Removed face `{OriginalFaceCount - FacesRemoved}`, Face specified a vertex which had edges attached, but none that were open.");
                            return;
                        }
                    }
                }

                // check if the inner already exists
                if (!halfEdgeModifier.TryAddVertsToEdgeDict(innerHeKey, innerHeIndex))
                {
                    // failed to add the key to the dict, this either means we hit a set of inner twins
                    // or that the face being added is wrong

                    VertsToEdgeDict.TryGetValue(innerHeKey, out innerHeIndex); // get the half edge that already exists and assign to innerHeIndex

                    innerHe = HalfEdges[innerHeIndex];

                    if (innerHe.Face != -1)
                    {
                        halfEdgeModifier.RollBack($"Removed face `{OriginalFaceCount - FacesRemoved}`, Face specified an edge which already had two faces attached.");
                        return;
                    }

                    if (previousHalfEdgeId != -1)
                    {
                        var failsPrevious = HalfEdges[previousHalfEdgeId].OverrideOuter && innerHe.Previous != previousHalfEdgeId && HalfEdges[previousHalfEdgeId].Next != innerHeIndex;
                        var failsFirst = isLastIteration && HalfEdges[firstHalfEdgeId].OverrideOuter && innerHe.Next != firstHalfEdgeId && HalfEdges[firstHalfEdgeId].Previous != innerHeIndex;

                        if (failsPrevious || failsFirst)
                        {
                            halfEdgeModifier.RollBack($"Removed face `{OriginalFaceCount - FacesRemoved}`, Face specified two edges that are connected by a vertex but have or more existing edges separating them.");
                            return;
                        }
                    }

                    // already existing half edge doesn't have a face, which means this is a boundary, don't add a new half edge
                    // but instead just set its face to be the current face (turning it into an inner)
                    halfEdgeModifier.ChangeHalfEdgeFace(innerHe, faceidx);
                    innerHe.OverrideOuter = true;
                }
                else
                {
                    innerHe = new HalfEdge
                    {
                        Face = faceidx,
                        origVert = v1idx,
                        destVert = v2idx,
                        Twin = outerHeIndex,
                        id = innerHeIndex
                    };

                    outerHe = new HalfEdge
                    {
                        origVert = v2idx,
                        destVert = v1idx,
                        Twin = innerHeIndex,
                        id = outerHeIndex
                    };

                    halfEdgeModifier.TryAddVertsToEdgeDict(outerHeKey, outerHeIndex);

                    halfEdgeModifier.AddHalfEdgeToHalfEdgesList(innerHe);
                    halfEdgeModifier.AddHalfEdgeToHalfEdgesList(outerHe);

                    halfEdgeModifier.AddVertexRelatedEdge(v[0], innerHeIndex);
                    halfEdgeModifier.AddVertexRelatedEdge(v[0], outerHeIndex);
                    halfEdgeModifier.AddVertexRelatedEdge(v[1], innerHeIndex);
                    halfEdgeModifier.AddVertexRelatedEdge(v[1], outerHeIndex);

                    halfEdgeModifier.ChangeVertexOutGoing(v[1], outerHeIndex);

                    newBoundaryHalfEdges.Add(outerHe);
                }

                //link inners
                halfEdgeModifier.ChangeHalfEdgePrev(innerHe, previousHalfEdgeId);
                halfEdgeModifier.ChangeHalfEdgeNext(innerHe, firstHalfEdgeId);

                // link current inner with the previous half edge
                if (previousHalfEdgeId != -1)
                {
                    var lastinnerFaceHe = HalfEdges[previousHalfEdgeId];
                    halfEdgeModifier.ChangeHalfEdgeNext(lastinnerFaceHe, innerHeIndex);
                }

                // remember the first inner of the face
                if (isFirstIteration)
                {
                    firstHalfEdgeId = innerHeIndex;
                    face.HalfEdge = firstHalfEdgeId;
                }

                // if this is the end of the loop, link current inner with the first
                if (isLastIteration)
                {
                    var firstinnerFaceHe = HalfEdges[firstHalfEdgeId];
                    halfEdgeModifier.ChangeHalfEdgePrev(firstinnerFaceHe, innerHeIndex);
                    halfEdgeModifier.ChangeHalfEdgeNext(innerHe, firstHalfEdgeId);
                }

                previousHalfEdgeId = innerHeIndex;
            }

            // link boundary half edges
            // TODO: it would be nice to find a way to generalize this code so theres no code duplication for
            // linking prev/next boundaries, maybe even find a way to only have to link next with some smart logic
            // because right now if you have a triangle, processing 2/3 of its edges will also link the 3rd edge
            // but then the algorithm still goes over the 3rd edge linking it, couldn't figure out nice logic to avoid that
            // but i have obsessed over this too much for now
            foreach (var boundary in newBoundaryHalfEdges)
            {
                var origVert = Vertices[boundary.origVert];
                var destVert = Vertices[boundary.destVert];
                var boundaryIdx = boundary.id;

                //
                // link prev boundary
                //
                var totalPotentialPrevBoundary = 0;
                var potentialBoundaryWithAnInnerAsNextIdx = -1;
                var oppositePrevBoundary = -1;

                foreach (var heIdx in origVert.RelatedEdges)
                {
                    var he = HalfEdges[heIdx];

                    // dont loop over ourselves
                    if (he == boundary)
                    {
                        continue;
                    }

                    // only loop over boundaries
                    if (he.Face != -1)
                    {
                        continue;
                    }

                    // TODO: im sure there has to be a smarter way of writing this code, since currently we loop over all boundaries associated with a vertex
                    // but we only end up actually using less than half, the issue is that vertex circulation doesn't work here
                    // because while building the data structure, some half edges won't have data filled out yet, causing it to fail

                    // store the edge that opposes our current edge (they point into eachother)
                    // this will be useful for some trickery later
                    if (he.origVert == boundary.origVert)
                    {
                        oppositePrevBoundary = heIdx;
                    }

                    // if the destvert of the half edge related with the vertex is the same as our origin vertex
                    // and it doesn't have a next (valid prev)
                    // and it's next HAS A FACE (boundary that got overriden as inner)
                    // store the boundary with an inner as next for later
                    if (he.destVert == boundary.origVert)
                    {
                        totalPotentialPrevBoundary++;

                        if (he.Next != -1)
                        {
                            if (HalfEdges[he.Next].Face != -1)
                            {
                                potentialBoundaryWithAnInnerAsNextIdx = heIdx;
                            }
                        }
                    }
                }

                // more than two prev boundaries to choose here means we got more than 4 half edges on one vertex, invalid
                if (totalPotentialPrevBoundary > 2)
                {
                    halfEdgeModifier.RollBack($"Removed face `{OriginalFaceCount - FacesRemoved}`, a vertex specified by the face has multiple boundary edges but shares no existing edge.");
                    return;
                }

                // TODO: im sure theres a way to unify this logic, but i havent found one

                if (totalPotentialPrevBoundary == 2)
                {
                    // if totalPotentialPrevBoundary == 2 and we got a boundary that has an inner as next
                    // it means at least one edge merge happened (two faces sharing an edge) while adding this face
                    // we can use this information to get our prev for this boundary, since the boundary that has a next that has a face
                    // will always be our prev
                    if (potentialBoundaryWithAnInnerAsNextIdx != -1)
                    {
                        halfEdgeModifier.ChangeHalfEdgePrev(boundary, potentialBoundaryWithAnInnerAsNextIdx);
                        halfEdgeModifier.ChangeHalfEdgeNext(HalfEdges[potentialBoundaryWithAnInnerAsNextIdx], boundaryIdx);
                    }
                    // if no face merge happened, we can still get prev, but its a bit trickier
                    // we have to circulate away from the opposite boundary, until we find another boundary
                    /// checking for twin here because of how the circulator works
                    else
                    {
                        foreach (var heidx in VertexCirculator(oppositePrevBoundary, forward: true))
                        {
                            var possiblePrev = HalfEdges[heidx];
                            if (HalfEdges[possiblePrev.Twin].Face == -1)
                            {
                                halfEdgeModifier.ChangeHalfEdgePrev(boundary, possiblePrev.Twin);
                                halfEdgeModifier.ChangeHalfEdgeNext(HalfEdges[possiblePrev.Twin], boundaryIdx);
                                break;
                            }
                        }
                    }
                }

                // if there is only one potential prev, it means that this either a single separate face being added
                // or that this is the junction of two faces being merged
                if (totalPotentialPrevBoundary == 1)
                {
                    // if this is just a single face, we can hard code the prev
                    if (potentialBoundaryWithAnInnerAsNextIdx == -1)
                    {
                        var prev = HalfEdges[HalfEdges[boundary.Twin].Next].Twin;
                        halfEdgeModifier.ChangeHalfEdgePrev(boundary, prev);
                        halfEdgeModifier.ChangeHalfEdgeNext(HalfEdges[prev], boundaryIdx);
                    }
                    // else if its two faces joining, just use the boundary with inner as next as before
                    else
                    {
                        halfEdgeModifier.ChangeHalfEdgePrev(boundary, potentialBoundaryWithAnInnerAsNextIdx);
                        halfEdgeModifier.ChangeHalfEdgeNext(HalfEdges[potentialBoundaryWithAnInnerAsNextIdx], boundaryIdx);
                    }
                }

                //
                // link next boundary
                //

                // same awful logic as obove just flipped in order to connects nexts, but with the joy of code duplication

                var totalPotentialNextBoundary = 0;
                var potentialBoundaryWithAnInnerAsPrevIdx = -1;
                var oppositeNextBoundary = -1;

                foreach (var heIdx in destVert.RelatedEdges)
                {
                    var he = HalfEdges[heIdx];

                    // dont loop over ourselves
                    if (he == boundary)
                    {
                        continue;
                    }

                    // only loop over boundaries
                    if (he.Face != -1)
                    {
                        continue;
                    }

                    if (he.destVert == boundary.destVert)
                    {
                        oppositeNextBoundary = heIdx;
                    }

                    if (he.origVert == boundary.destVert)
                    {
                        totalPotentialNextBoundary++;

                        if (he.Previous != -1)
                        {
                            if (HalfEdges[he.Previous].Face != -1)
                            {
                                potentialBoundaryWithAnInnerAsPrevIdx = heIdx;
                            }
                        }
                    }
                }

                // more than two prev boundaries to choose here means we got more than 4 half edges on one vertex, invalid
                if (totalPotentialNextBoundary > 2)
                {
                    halfEdgeModifier.RollBack($"Removed face `{OriginalFaceCount - FacesRemoved}`, a vertex specified by the face has multiple boundary edges but shares no existing edge.");
                    return;
                }

                if (totalPotentialNextBoundary == 2)
                {
                    if (potentialBoundaryWithAnInnerAsPrevIdx != -1)
                    {
                        halfEdgeModifier.ChangeHalfEdgeNext(boundary, potentialBoundaryWithAnInnerAsPrevIdx);
                        halfEdgeModifier.ChangeHalfEdgePrev(HalfEdges[potentialBoundaryWithAnInnerAsPrevIdx], boundaryIdx);
                    }
                    else
                    {
                        foreach (var heidx in VertexCirculator(oppositeNextBoundary, forward: false))
                        {
                            var possibleNext = HalfEdges[heidx];
                            if (HalfEdges[possibleNext.Twin].Face == -1)
                            {
                                halfEdgeModifier.ChangeHalfEdgeNext(boundary, possibleNext.Twin);
                                halfEdgeModifier.ChangeHalfEdgePrev(HalfEdges[possibleNext.Twin], boundaryIdx);
                                break;
                            }
                        }
                    }
                }

                if (totalPotentialNextBoundary == 1)
                {
                    if (potentialBoundaryWithAnInnerAsPrevIdx == -1)
                    {
                        var next = HalfEdges[HalfEdges[boundary.Twin].Previous].Twin;
                        halfEdgeModifier.ChangeHalfEdgeNext(boundary, next);
                        halfEdgeModifier.ChangeHalfEdgePrev(HalfEdges[next], boundaryIdx);
                    }
                    else
                    {
                        halfEdgeModifier.ChangeHalfEdgeNext(boundary, potentialBoundaryWithAnInnerAsPrevIdx);
                        halfEdgeModifier.ChangeHalfEdgePrev(HalfEdges[potentialBoundaryWithAnInnerAsPrevIdx], boundaryIdx);
                    }
                }
            }

            Faces.Add(face);
        }

        public void AddPhysHull(HullDescriptor desc, PhysAggregateData phys, HashSet<string> proceduralPhysMaterialsToExtract, Vector3 positionOffset = new Vector3(), string? materialOverride = null)
        {
            var attributes = phys.CollisionAttributes[desc.CollisionAttributeIndex];
            var tags = attributes.GetArray<string>("m_InteractAsStrings") ?? attributes.GetArray<string>("m_PhysicsTagStrings");
            var group = attributes.GetStringProperty("m_CollisionGroupString");
            var material = materialOverride ?? MapExtract.GetToolTextureNameForCollisionTags(new ModelExtract.SurfaceTagCombo(group, tags));

            if (group == "Default")
            {
                var knownKeys = StringToken.InvertedTable;

                var physicsSurfaceNames = phys.SurfacePropertyHashes.Select(hash =>
                {
                    knownKeys.TryGetValue(hash, out var name);
                    return name ?? hash.ToString(CultureInfo.InvariantCulture);
                }).ToArray();

                var surfaceName = physicsSurfaceNames[desc.SurfacePropertyIndex];
                material = "maps/" + proceduralPhysMaterialsPath + surfaceName + ".vmat";
                proceduralPhysMaterialsToExtract.Add(surfaceName);
            }

            var hull = desc.Shape;
            VertexStreams streams = new();
            streams.positions = hull.GetVertexPositions().ToArray().ToList();
            DefinePointCloud(streams, positionOffset);

            var hullFaces = hull.GetFaces();
            var hullEdges = hull.GetEdges();

            Faces.EnsureCapacity(hullFaces.Length);
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

        public void AddPhysMesh(MeshDescriptor desc, PhysAggregateData phys, HashSet<string> proceduralPhysMaterialsToExtract, HashSet<int> deletedIndices,
            Vector3 positionOffset = new Vector3(), string? materialOverride = null)
        {
            var attributes = phys.CollisionAttributes[desc.CollisionAttributeIndex];
            var tags = attributes.GetArray<string>("m_InteractAsStrings") ?? attributes.GetArray<string>("m_PhysicsTagStrings");
            var group = attributes.GetStringProperty("m_CollisionGroupString");
            var material = materialOverride ?? MapExtract.GetToolTextureNameForCollisionTags(new ModelExtract.SurfaceTagCombo(group, tags));

            var mesh = desc.Shape;
            VertexStreams streams = new();
            streams.positions = mesh.GetVertices().ToArray().ToList();
            DefinePointCloud(streams, positionOffset);

            var meshTriangles = desc.Shape.GetTriangles();

            Faces.EnsureCapacity(meshTriangles.Length);
            Span<int> inds = stackalloc int[3];

            var removed = 0;

            foreach (var triangle in meshTriangles)
            {
                inds[0] = triangle.X;
                inds[1] = triangle.Y;
                inds[2] = triangle.Z;

                if (deletedIndices.Contains(inds[0])
                 || deletedIndices.Contains(inds[1])
                 || deletedIndices.Contains(inds[2]))
                {
                    continue;
                }

                AddFace(inds, material);
            }

            if (removed > 0)
            {
                Console.WriteLine($"{nameof(HammerMeshBuilder)}: Total physics triangles removed: {removed}");
            }
        }

        public void AddRenderMesh(DmeModel model, DmeMesh shape, Vector3 positionOffset = new Vector3())
        {
            var facesets = shape.FaceSets;

            var vertexdata = (DmeVertexData)shape.BaseStates[0];

            var baseVertex = Vertices.Count;

            Vector3[] positions = [];
            Vector2[] texcoords = [];
            Vector3[] normals = [];
            Vector4[] tangents = [];
            Vector4[] VertexPaintBlendParams = [];

            foreach (var stream in vertexdata)
            {
                if (stream.Key == "position$0")
                {
                    positions = (Vector3[])stream.Value;
                }

                if (stream.Key == "texcoord$0")
                {
                    texcoords = (Vector2[])stream.Value;
                }

                if (stream.Key == "normal$0")
                {
                    normals = (Vector3[])stream.Value;
                }

                if (stream.Key == "tangent$0")
                {
                    tangents = (Vector4[])stream.Value;
                }

                if (stream.Key == "VertexPaintBlendParams$0")
                {
                    VertexPaintBlendParams = (Vector4[])stream.Value;
                }
            }

            List<Tuple<List<int>, DmeFaceSet>> faceList = [];
            Dictionary<int, int> newVertexStreamsIndexDict = [];
            List<Vector3> newVertices = [];
            List<Vector2> newTexcoords = [];
            List<Vector3> newNormals = [];
            List<Vector4> newTangents = [];
            List<Vector4> newVertexPaintBlendParams = [];

            if (PhysicsVertexMatcher!.LastPositions != positions)
            {
                PhysicsVertexMatcher.LastPositions = positions;
                PhysicsVertexMatcher.ScanPhysicsPointCloudForMatches(positions.AsSpan());
            }

            foreach (var faceset in facesets.Cast<DmeFaceSet>())
            {
                var facesetIndices = faceset.Faces;

                List<int> inds = new(capacity: 3);

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
                        int newIndex;
                        if (!newVertexStreamsIndexDict.TryGetValue(faceIndex, out newIndex))
                        {
                            newIndex = ++newIndexCounter;
                            newVertexStreamsIndexDict.Add(faceIndex, newIndexCounter);
                        }

                        newFaceInds.Add(newIndex);
                    }
                    faceList.Add(new Tuple<List<int>, DmeFaceSet>(newFaceInds, faceset));
                    inds.Clear();
                }
            }

            foreach (var kv in newVertexStreamsIndexDict)
            {
                newVertices.Add(positions[kv.Key]);
                newTexcoords.Add(texcoords[kv.Key]);
                newNormals.Add(normals[kv.Key]);
                newTangents.Add(tangents[kv.Key]);
                if (VertexPaintBlendParams.Length != 0)
                {
                    newVertexPaintBlendParams.Add(VertexPaintBlendParams[kv.Key]);
                }
            }

            VertexStreams streams = new()
            {
                positions = newVertices,
                texcoords = newTexcoords,
                normals = newNormals,
                tangents = newTangents,
                VertexPaintBlendParams = newVertexPaintBlendParams
            };

            DefinePointCloud(streams, positionOffset);

            foreach (var face in faceList)
            {
                AddFace(CollectionsMarshal.AsSpan(face.Item1), face.Item2.Material.MaterialName);
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

        private Vector3 CalculateNormal(int halfEdge)
        {
            Span<int> normalHalfEdges = [halfEdge, HalfEdges[halfEdge].Next, HalfEdges[halfEdge].Previous];
            var v1 = vertexStreams.positions[Vertices[HalfEdges[normalHalfEdges[0]].origVert].MasterStreamIndex];
            var v2 = vertexStreams.positions[Vertices[HalfEdges[normalHalfEdges[1]].origVert].MasterStreamIndex];
            var v3 = vertexStreams.positions[Vertices[HalfEdges[normalHalfEdges[2]].origVert].MasterStreamIndex];

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

        public static CDmePolygonMeshDataStream<T> CreateStream<TArray, T>(int dataStateFlags, string name, params T[] data)
            where TArray : Datamodel.Array<T>, new()
        {

            var dmArray = new TArray();
            foreach (var item in data)
            {
                dmArray.Add(item);
            }

            var stream = new CDmePolygonMeshDataStream<T>
            {
                Name = name,
                StandardAttributeName = name[..^2],
                SemanticName = name[..^2],
                SemanticIndex = 0,
                VertexBufferLocation = 0,
                DataStateFlags = dataStateFlags,
                SubdivisionBinding = null,
                Data = dmArray
            };

            return stream;
        }
    }
}
