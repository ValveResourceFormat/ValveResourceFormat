using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using ValveResourceFormat.IO.Formats.ValveMap;

namespace ValveResourceFormat.IO
{
    internal class HammerMeshBuilder
    {
        public struct MeshVertex
        {
            public Vector3 Position { get; set; }
            public Vector2 TexCoord { get; set; }
        }

        public class MeshFace
        {
            public List<int> VertexIndices { get; set; }
            public string Material { get; set; }
        }

        [Flags]
        public enum EdgeFlag
        {
            None = 0x0,
            SoftNormals = 0x1,
            HardNormals = 0x2,
        }

        private List<MeshVertex> VertexSoup { get; } = new();
        private List<MeshFace> Faces { get; } = new();

        public CDmePolygonMesh GenerateMesh()
        {
            var mesh = new CDmePolygonMesh();

            var faceMaterialIndices = CreateStream<Datamodel.IntArray, int>(8, "materialindex:0");
            var faceFlags = CreateStream<Datamodel.IntArray, int>(3, "flags:0");
            mesh.FaceData.Streams.Add(faceMaterialIndices);
            mesh.FaceData.Streams.Add(faceFlags);

            var textureCoords = CreateStream<Datamodel.Vector2Array, Vector2>(1, "texcoord:0");
            var normals = CreateStream<Datamodel.Vector3Array, Vector3>(1, "normal:0");
            var tangent = CreateStream<Datamodel.Vector4Array, Vector4>(1, "tangent:0");
            //mesh.FaceVertexData.Streams.Add(textureCoords);
            mesh.FaceVertexData.Streams.Add(normals);
            //mesh.FaceVertexData.Streams.Add(tangent);

            var vertexPositions = CreateStream<Datamodel.Vector3Array, Vector3>(3, "position:0");
            mesh.VertexData.Streams.Add(vertexPositions);

            var edgeFlags = CreateStream<Datamodel.IntArray, int>(3, "flags:0");
            mesh.EdgeData.Streams.Add(edgeFlags);

            foreach (var vertex in VertexSoup)
            {
                AddVertex(mesh, vertex, associatedEdge: -1);
            }

            foreach (var face in Faces)
            {
                var faceDataIndex = mesh.FaceData.Size;
                mesh.FaceDataIndices.Add(faceDataIndex);
                mesh.FaceData.Size++;

                var materialIndex = mesh.Materials.IndexOf(face.Material);
                if (materialIndex == -1 && face.Material != null)
                {
                    materialIndex = mesh.Materials.Count;
                    mesh.Materials.Add(face.Material);
                }

                faceMaterialIndices.Data.Add(materialIndex);
                faceFlags.Data.Add(0);

                var firstInner = mesh.EdgeVertexIndices.Count;
                var lastInner = -1;

                var edge = mesh.EdgeDataIndices.Count > firstInner
                    ? mesh.EdgeDataIndices[firstInner]
                    : mesh.EdgeDataIndices.Count / 2;

                // For each vertex pair, build an edge (half+twin)
                for (var i = 0; i < face.VertexIndices.Count; i++)
                {
                    var v1 = face.VertexIndices[i];
                    var v2 = face.VertexIndices[(i + 1) % face.VertexIndices.Count];

                    var half = lastInner = edge * 2;
                    var twin = half + 1;

                    // Associate vertex with inner edge
                    if (mesh.VertexEdgeIndices[v1] == -1)
                    {
                        mesh.VertexEdgeIndices[v1] = half;
                    }
                    else
                    {
                        // Vertex already tied to an edge. TODO: Join edge.
                    }

                    // Inner
                    mesh.EdgeVertexIndices.Add(v2);
                    mesh.EdgeOppositeIndices.Add(twin);
                    mesh.EdgeNextIndices.Add(twin + 1); // Last one will need to be set to firstInner
                    mesh.EdgeFaceIndices.Add(faceDataIndex);
                    mesh.EdgeDataIndices.Add(edge);

                    mesh.EdgeVertexDataIndices.Add(v2);

                    // Outer
                    mesh.EdgeVertexIndices.Add(v1);
                    mesh.EdgeOppositeIndices.Add(half);
                    mesh.EdgeNextIndices.Add(-1); // Will update these at the end
                    mesh.EdgeFaceIndices.Add(-1); // Since this is a brand new edge, outside will be facing void
                    mesh.EdgeDataIndices.Add(edge);
                    // Indices probably dont matter here. Just data being correct.
                    // So need to figure out what this data is for/how's it laid out
                    mesh.EdgeVertexDataIndices.Add(i + face.VertexIndices.Count);

                    // Fill with dummy data
                    textureCoords.Data.Add(VertexSoup[v1].TexCoord);
                    normals.Data.Add(new Vector3(0, -0.44f, 0.89f));
                    tangent.Data.Add(new Vector4(1, 0, 0, -1));
                    textureCoords.Data.Add(VertexSoup[v2].TexCoord);
                    normals.Data.Add(new Vector3(0, -0.44f, 0.89f));
                    tangent.Data.Add(new Vector4(1, 0, 0, -1));

                    edgeFlags.Data.Add((int)EdgeFlag.None);
                    mesh.FaceVertexData.Size += 2;
                    mesh.EdgeData.Size++;
                    edge++;
                }

                mesh.FaceEdgeIndices.Add(lastInner);

                // Update the last inner edge to point to the first inner edge
                mesh.EdgeNextIndices[lastInner] = firstInner;

                // Update nexts of the opposite edges
                for (var n = 0; n < mesh.EdgeNextIndices.Count; n++)
                {
                    if (mesh.EdgeNextIndices[n] != -1)
                    {
                        continue;
                    }

                    // Populate next indices for outer halves.
                    // It should be the twin of twin's previous edge.
                    var twin = mesh.EdgeOppositeIndices[n];
                    var previousOfTwin = mesh.EdgeNextIndices.IndexOf(twin);
                    var twinOfTwinPrevious = mesh.EdgeOppositeIndices[previousOfTwin];
                    mesh.EdgeNextIndices[n] = twinOfTwinPrevious;
                }
            }

            for (var v = 0; v < mesh.VertexData.Size;)
            {
                var edge = mesh.VertexEdgeIndices[v];
                if (edge == -1)
                {
                    RemoveVertex(mesh, v);
                    continue;
                }

                v++;
            }

            AddSubdivisionStuff(mesh);

            return mesh;

            void AddVertex(CDmePolygonMesh mesh, MeshVertex vertex, int associatedEdge = -1)
            {
                var vertexDataIndex = mesh.VertexData.Size;
                mesh.VertexEdgeIndices.Add(associatedEdge);
                mesh.VertexDataIndices.Add(vertexDataIndex);
                mesh.VertexData.Size++;

                vertexPositions.Data.Add(vertex.Position);
            }

            void RemoveVertex(CDmePolygonMesh mesh, int vertexIndex)
            {
                var associatedEdge = mesh.VertexEdgeIndices[vertexIndex];
                var vertexDataIndex = mesh.VertexDataIndices[vertexIndex];

                mesh.VertexEdgeIndices.RemoveAt(vertexIndex);
                mesh.VertexDataIndices.RemoveAt(vertexIndex);
                mesh.VertexData.Size--;

                vertexPositions.Data.RemoveAt(vertexDataIndex);

                // Update data indices
                for (var i = vertexIndex; i < mesh.VertexDataIndices.Count; i++)
                {
                    mesh.VertexDataIndices[i]--;
                }

                // Update edges
                for (var i = 0; i < mesh.EdgeVertexIndices.Count; i++)
                {
                    if (mesh.EdgeVertexIndices[i] > vertexIndex)
                    {
                        mesh.EdgeVertexIndices[i]--;
                    }
                }

                // TODO: EdgeVertexData indices too

                if (associatedEdge != -1)
                {
                    // Remove affected edges
                }
            }
        }

        public MeshFace AddFace(MeshFace face)
        {
            if (!VerifyIndicesWithinBounds(face.VertexIndices))
            {
                throw new ArgumentOutOfRangeException(nameof(face.VertexIndices), "Invalid! Face contains an out of bounds vertex index.");
            }

            Faces.Add(face);
            return face;
        }

        public MeshFace AddFace(string material, params int[] vertexIndices)
        {
            if (!VerifyIndicesWithinBounds(vertexIndices))
            {
                throw new ArgumentOutOfRangeException(nameof(vertexIndices), "Invalid! Face contains an out of bounds vertex index.");
            }

            var face = new MeshFace
            {
                VertexIndices = new List<int>(vertexIndices),
                Material = material,
            };

            return AddFace(face);
        }

        public MeshFace AddFace(string material, params Vector3[] vertices)
        {
            var face = new MeshFace
            {
                VertexIndices = new List<int>(),
                Material = material,
            };

            foreach (var vertex in vertices)
            {
                var index = VertexSoup.Count;

                VertexSoup.Add(new MeshVertex
                {
                    Position = vertex,
                    TexCoord = Vector2.Zero,
                });

                face.VertexIndices.Add(index);
            }

            return AddFace(face);
        }

        private bool VerifyIndicesWithinBounds(IEnumerable<int> indices)
        {
            foreach (var index in indices)
            {
                if (index < 0 || index >= VertexSoup.Count)
                {
                    return false;
                }
            }

            return true;
        }

        private static void AddSubdivisionStuff(CDmePolygonMesh mesh, int count = 8)
        {
            for (int i = 0; i < count; i++)
            {
                mesh.SubdivisionData.SubdivisionLevels.Add(0);
            }
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
