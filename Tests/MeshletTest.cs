using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests
{
    public class MeshletTest
    {
        private readonly record struct MeshletDesc(int VertexOffset, int TriangleOffset, int VertexCount, int TriangleCount);

        private static List<MeshletDesc> LoadMeshlets(Resource resource, out byte[] mslt)
        {
            var mesh = (Mesh)resource.GetBlockByType(BlockType.MDAT)!;
            var meshlets = new List<MeshletDesc>();

            foreach (var sceneObject in mesh.Data.GetArray("m_sceneObjects"))
            {
                var meshletArray = sceneObject.GetArray("m_meshlets");
                if (meshletArray == null)
                {
                    continue;
                }

                foreach (var meshlet in meshletArray)
                {
                    meshlets.Add(new MeshletDesc(
                        meshlet.GetInt32Property("m_nVertexOffset"),
                        meshlet.GetInt32Property("m_nTriangleOffset"),
                        meshlet.GetInt32Property("m_nVertexCount"),
                        meshlet.GetInt32Property("m_nTriangleCount")));
                }
            }

            var block = resource.GetBlockByType(BlockType.MSLT);
            Assert.That(block, Is.InstanceOf<MeshletBuffer>());

            using var ms = new MemoryStream();
            block!.Serialize(ms);
            mslt = ms.ToArray();
            Assert.That(mslt, Has.Length.EqualTo((int)block.Size));

            return meshlets;
        }

        private static Resource Read(out List<MeshletDesc> meshlets, out byte[] mslt)
        {
            var file = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "n0_lr0_agg_prop_plants001_0.vmdl_c");
            var resource = new Resource
            {
                FileName = file,
            };
            resource.Read(file);
            meshlets = LoadMeshlets(resource, out mslt);
            return resource;
        }

        [Test]
        public void ParsesMeshletsAndBuffer()
        {
            using var resource = Read(out var meshlets, out var mslt);

            Assert.That(meshlets, Is.Not.Empty);
            Assert.That(meshlets[0], Is.EqualTo(new MeshletDesc(0, 0, 66, 48)));
            Assert.That(mslt, Has.Length.EqualTo(1736));
        }

        // The MSLT buffer is pre-decoded packed indices: per-meshlet vertexCount uint32 entries,
        // each = (vertexListValue << 18) | triangle, triangle = three 6-bit references.
        [Test]
        public void ValidatesPackedIndexLayout()
        {
            using var resource = Read(out var meshlets, out var mslt);

            Assert.That(mslt.Length % 4, Is.Zero);
            var words = MemoryMarshal.Cast<byte, uint>(mslt);

            var cursor = 0;

            for (var i = 0; i < meshlets.Count; i++)
            {
                var m = meshlets[i];
                Assert.That(cursor + m.VertexCount, Is.LessThanOrEqualTo(words.Length), $"meshlet {i} overruns the buffer");

                var seg = words.Slice(cursor, m.VertexCount);

                // First entry is the canonical (0,1,2) first triangle => packed low-18 == 0x2040.
                Assert.That(seg[0] & 0x3FFFFu, Is.EqualTo(0x2040u), $"meshlet {i} does not start with the (0,1,2) marker");

                for (var j = 0; j < m.TriangleCount; j++)
                {
                    Assert.That(seg[j] & 0x3FFFFu, Is.Not.Zero, $"meshlet {i} triangle {j} is unexpectedly zero");
                }

                for (var j = m.TriangleCount; j < m.VertexCount; j++)
                {
                    Assert.That(seg[j] & 0x3FFFFu, Is.Zero, $"meshlet {i} padding entry {j} is non-zero");
                }

                cursor += m.VertexCount;
            }

            Assert.That(cursor, Is.EqualTo(words.Length), "meshlet segments do not cover the MSLT buffer exactly");
        }

        [Test]
        public void DecodesMeshlets()
        {
            using var resource = Read(out var meshlets, out _);
            var block = (MeshletBuffer)resource.GetBlockByType(BlockType.MSLT)!;

            var totalIndices = 0;
            var entryOffset = 0; // segments tile by vertex count, not by the descriptor's vertex offset

            for (var i = 0; i < meshlets.Count; i++)
            {
                var m = meshlets[i];
                var (vertices, indices) = block.DecodeMeshlet(entryOffset, m.VertexCount, m.TriangleCount);

                Assert.That(vertices, Has.Length.EqualTo(m.VertexCount));
                Assert.That(indices, Has.Length.EqualTo(m.TriangleCount * 3));

                // Vertex list is a 14-bit per-entry field.
                foreach (var v in vertices)
                {
                    Assert.That(v, Is.InRange(0, 0x3FFF), $"meshlet {i} vertex out of 14-bit range");
                }

                // Interop: every index addresses the vertex list, so vertices[index] is in bounds.
                foreach (var index in indices)
                {
                    Assert.That(index, Is.InRange(0, vertices.Length - 1), $"meshlet {i} index does not address the vertex list");
                }

                totalIndices += indices.Length;
                entryOffset += m.VertexCount;
            }

            Assert.That(totalIndices, Is.EqualTo(meshlets.Sum(m => m.TriangleCount) * 3));

            // The first meshlet has the identity vertex list, so its first triangle resolves to (0,1,2).
            var (firstVertices, firstIndices) = block.DecodeMeshlet(0, meshlets[0].VertexCount, meshlets[0].TriangleCount);
            for (var j = 0; j < firstVertices.Length; j++)
            {
                Assert.That(firstVertices[j], Is.EqualTo(j), $"vertex {j}");
            }

            Assert.That(firstVertices[firstIndices[0]], Is.Zero);
            Assert.That(firstVertices[firstIndices[1]], Is.EqualTo(1));
            Assert.That(firstVertices[firstIndices[2]], Is.EqualTo(2));
        }

        // Ground truth: meshlet 0 has an identity vertex list, so resolving its local indices through the
        // vertex list must reproduce the real index buffer (MIDX). Validates the window/wrap and the interop.
        [Test]
        public void DecodesMeshlet0AgainstIndexBuffer()
        {
            using var resource = Read(out var meshlets, out _);
            var block = (MeshletBuffer)resource.GetBlockByType(BlockType.MSLT)!;
            var indexBuffer = ((Model)resource.DataBlock!).GetEmbeddedMeshes().First().Mesh.VBIB.IndexBuffers[0];

            var m = meshlets[0];
            var (vertices, indices) = block.DecodeMeshlet(0, m.VertexCount, m.TriangleCount);
            var expected = GltfModelExporter.ReadIndices(indexBuffer, m.TriangleOffset * 3, m.TriangleCount * 3, 0);

            for (var t = 0; t < m.TriangleCount; t++)
            {
                // Resolve local indices through the vertex list, compare as sorted triples (ignore winding).
                var d = new[] { vertices[indices[t * 3]], vertices[indices[t * 3 + 1]], vertices[indices[t * 3 + 2]] };
                var e = new[] { expected[t * 3], expected[t * 3 + 1], expected[t * 3 + 2] };
                Array.Sort(d);
                Array.Sort(e);
                Assert.That(d, Is.EqualTo(e), $"triangle {t}");
            }
        }
    }
}
