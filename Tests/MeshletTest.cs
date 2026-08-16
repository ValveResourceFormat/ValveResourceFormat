using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using TUnit.Assertions.Enums;
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

        private static async Task<(List<MeshletDesc> Meshlets, byte[] Mslt)> LoadMeshlets(Resource resource)
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
            await Assert.That(block).IsAssignableTo<MeshletBuffer>();

            using var ms = new MemoryStream();
            block!.Serialize(ms);
            var mslt = ms.ToArray();
            await Assert.That(mslt).Count().IsEqualTo((int)block.Size);

            return (meshlets, mslt);
        }

        private static async Task<(Resource Resource, List<MeshletDesc> Meshlets, byte[] Mslt)> Read()
        {
            var file = Path.Combine(TestContext.TestDirectory!, "Files", "n0_lr0_agg_prop_plants001_0.vmdl_c");
            var resource = new Resource
            {
                FileName = file,
            };
            resource.Read(file);
            var (meshlets, mslt) = await LoadMeshlets(resource);
            return (resource, meshlets, mslt);
        }

        [Test]
        public async Task ParsesMeshletsAndBuffer()
        {
            var (loaded, meshlets, mslt) = await Read();
            using var resource = loaded;

            using (Assert.Multiple())
            {
                await Assert.That(meshlets).IsNotEmpty();
                await Assert.That(meshlets[0]).IsEqualTo(new MeshletDesc(0, 0, 66, 48));
                await Assert.That(mslt).Count().IsEqualTo(1736);
            }
        }

        // The MSLT buffer is pre-decoded packed indices: per-meshlet vertexCount uint32 entries,
        // each = (vertexListValue << 18) | triangle, triangle = three 6-bit references.
        [Test]
        public async Task ValidatesPackedIndexLayout()
        {
            var (loaded, meshlets, mslt) = await Read();
            using var resource = loaded;

            await Assert.That(mslt.Length % 4).IsZero();
            var words = MemoryMarshal.Cast<byte, uint>(mslt).ToArray();

            var cursor = 0;

            for (var i = 0; i < meshlets.Count; i++)
            {
                var m = meshlets[i];
                await Assert.That(cursor + m.VertexCount).IsLessThanOrEqualTo(words.Length).Because($"meshlet {i} overruns the buffer");

                var seg = words.AsMemory(cursor, m.VertexCount);

                // First entry is the canonical (0,1,2) first triangle => packed low-18 == 0x2040.
                await Assert.That(seg.Span[0] & 0x3FFFFu).IsEqualTo(0x2040u).Because($"meshlet {i} does not start with the (0,1,2) marker");

                for (var j = 0; j < m.TriangleCount; j++)
                {
                    await Assert.That(seg.Span[j] & 0x3FFFFu).IsNotZero().Because($"meshlet {i} triangle {j} is unexpectedly zero");
                }

                for (var j = m.TriangleCount; j < m.VertexCount; j++)
                {
                    await Assert.That(seg.Span[j] & 0x3FFFFu).IsZero().Because($"meshlet {i} padding entry {j} is non-zero");
                }

                cursor += m.VertexCount;
            }

            await Assert.That(cursor).IsEqualTo(words.Length).Because("meshlet segments do not cover the MSLT buffer exactly");
        }

        [Test]
        public async Task DecodesMeshlets()
        {
            var (loaded, meshlets, _) = await Read();
            using var resource = loaded;
            var block = (MeshletBuffer)resource.GetBlockByType(BlockType.MSLT)!;

            var totalIndices = 0;
            var entryOffset = 0; // segments tile by vertex count, not by the descriptor's vertex offset

            for (var i = 0; i < meshlets.Count; i++)
            {
                var m = meshlets[i];
                var vertices = new int[m.VertexCount];
                var indices = new int[m.TriangleCount * 3];
                block.DecodeMeshlet(entryOffset, m.VertexCount, m.TriangleCount, vertices, indices);

                // Vertex list is a 14-bit per-entry field.
                foreach (var v in vertices)
                {
                    await Assert.That(v).IsBetween(0, 0x3FFF).Because($"meshlet {i} vertex out of 14-bit range");
                }

                // Interop: every index addresses the vertex list, so vertices[index] is in bounds.
                foreach (var index in indices)
                {
                    await Assert.That(index).IsBetween(0, vertices.Length - 1).Because($"meshlet {i} index does not address the vertex list");
                }

                totalIndices += indices.Length;
                entryOffset += m.VertexCount;
            }

            await Assert.That(totalIndices).IsEqualTo(meshlets.Sum(m => m.TriangleCount) * 3);

            // The first meshlet has the identity vertex list, so its first triangle resolves to (0,1,2).
            var firstVertices = new int[meshlets[0].VertexCount];
            var firstIndices = new int[meshlets[0].TriangleCount * 3];
            block.DecodeMeshlet(0, meshlets[0].VertexCount, meshlets[0].TriangleCount, firstVertices, firstIndices);
            for (var j = 0; j < firstVertices.Length; j++)
            {
                await Assert.That(firstVertices[j]).IsEqualTo(j).Because($"vertex {j}");
            }

            using (Assert.Multiple())
            {
                await Assert.That(firstVertices[firstIndices[0]]).IsZero();
                await Assert.That(firstVertices[firstIndices[1]]).IsEqualTo(1);
                await Assert.That(firstVertices[firstIndices[2]]).IsEqualTo(2);
            }
        }

        // Ground truth: meshlet 0 has an identity vertex list, so resolving its local indices through the
        // vertex list must reproduce the real index buffer (MIDX). Validates the window/wrap and the interop.
        [Test]
        public async Task DecodesMeshlet0AgainstIndexBuffer()
        {
            var (loaded, meshlets, _) = await Read();
            using var resource = loaded;
            var block = (MeshletBuffer)resource.GetBlockByType(BlockType.MSLT)!;
            var indexBuffer = ((Model)resource.DataBlock!).GetEmbeddedMeshes().First().Mesh.VBIB.IndexBuffers[0];

            var m = meshlets[0];
            var vertices = new int[m.VertexCount];
            var indices = new int[m.TriangleCount * 3];
            block.DecodeMeshlet(0, m.VertexCount, m.TriangleCount, vertices, indices);
            var expected = GltfModelExporter.ReadIndices(indexBuffer, m.TriangleOffset * 3, m.TriangleCount * 3, 0);

            for (var t = 0; t < m.TriangleCount; t++)
            {
                // Resolve local indices through the vertex list, compare as sorted triples (ignore winding).
                var d = new[] { vertices[indices[t * 3]], vertices[indices[t * 3 + 1]], vertices[indices[t * 3 + 2]] };
                var e = new[] { expected[t * 3], expected[t * 3 + 1], expected[t * 3 + 2] };
                Array.Sort(d);
                Array.Sort(e);
                await Assert.That(d).IsEquivalentTo(e, CollectionOrdering.Matching).Because($"triangle {t}");
            }
        }
    }
}
