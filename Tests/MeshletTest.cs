using System.IO;
using NUnit.Framework;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests
{
    public class MeshletTest
    {
        private readonly record struct MeshletDesc(int VertexOffset, int TriangleOffset, int VertexCount, int TriangleCount);

        [Test]
        public void ParsesMeshletsAndBuffer()
        {
            var file = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "n0_lr0_agg_prop_plants001_0.vmdl_c");
            using var resource = new Resource
            {
                FileName = file,
            };
            resource.Read(file);

            // Meshlet descriptors live in the MDAT (Mesh) data block, under each scene object.
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

            Assert.That(meshlets, Is.Not.Empty);
            Assert.That(meshlets[0], Is.EqualTo(new MeshletDesc(0, 0, 66, 48)));

            // The packed meshlet buffer that the descriptors index into.
            var block = resource.GetBlockByType(BlockType.MSLT);
            Assert.That(block, Is.InstanceOf<MeshletBuffer>());

            using var ms = new MemoryStream();
            block!.Serialize(ms);
            var data = ms.ToArray();
            Assert.That(data, Has.Length.EqualTo((int)block.Size));

            // Each meshlet's meshopt blob needs at least this many bytes (ctrl + codes + gap).
            var minBytes = 0;
            foreach (var m in meshlets)
            {
                var codesSize = (m.TriangleCount + 1) / 2;
                var ctrlSize = (m.VertexCount + 3) / 4;
                var gapSize = (codesSize + ctrlSize < 16) ? 16 - (codesSize + ctrlSize) : 0;
                minBytes += codesSize + ctrlSize + gapSize;
            }

            Assert.That(data, Has.Length.GreaterThanOrEqualTo(minBytes));

            TestContext.Out.WriteLine($"meshlets={meshlets.Count} msltSize={data.Length} minBytes={minBytes}");
        }
    }
}
