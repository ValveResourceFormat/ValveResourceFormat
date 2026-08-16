using System.IO;
using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

namespace Tests
{
    public partial class ResourceWriteTest
    {
        [Test]
        public async Task Write()
        {
            using var resource = GetTestResource("default_ents_kv3_v4_zstd.vents_c");

            var ms = new MemoryStream();
            resource.Serialize(ms);
            ms.Position = 0;

            // Now try to parse what we just wrote
            using var newResource = new Resource
            {
                FileName = resource.FileName,
            };
            newResource.Read(ms);

            using (Assert.Multiple())
            {
                await Assert.That(newResource.Version).IsEqualTo(resource.Version);
                await Assert.That(newResource.ResourceType).IsEqualTo(resource.ResourceType);
                await Assert.That(newResource.Blocks).Count().IsEqualTo(resource.Blocks.Count);

                for (var i = 0; i < newResource.Blocks.Count; i++)
                {
                    await Assert.That(newResource.Blocks[i].Type).IsEqualTo(resource.Blocks[i].Type);
                }
            }
        }

        [Test]
        public async Task ResourceModification()
        {
            const string NewName = "modified_worldnode.vmdl";

            using var resource = GetTestResource("n0_lr0_c0_s_cb_b_nomerge236.vmdl_c");

            var modelInfo = (Model)resource.DataBlock!;
            var meshGroupMasks = modelInfo.Data["m_refMeshGroupMasks"];
            var newMasks = KVObject.Array();
            newMasks.Add((ulong)1337);
            for (var i = 1; i < meshGroupMasks.Count; i++)
            {
                newMasks.Add(meshGroupMasks[i]!);
            }
            modelInfo.Data["m_refMeshGroupMasks"] = newMasks;

            modelInfo.Data["m_name"] = new KVObject(NewName);

            using var ms = new MemoryStream();
            resource.Serialize(ms);
            ms.Position = 0;

            // Now try to parse what we just wrote
            using var newResource = new Resource
            {
                FileName = $"{NewName}_c",
            };

            newResource.Read(ms);
            var newModelInfo = (Model)newResource.DataBlock!;
            await Assert.That(newModelInfo.Name).IsEqualTo(NewName);
        }

        [Test]
        public async Task SerializePanoramaLayout()
        {
            using var resource = GetTestResource("dashboard_page_credits.vxml_c");
            var block = (Panorama)resource.DataBlock!;

            var ms = new MemoryStream();
            block.Serialize(ms);
            ms.Position = 0;

            using var reader = new BinaryReader(ms);
            var reparsed = new PanoramaLayout { Resource = resource, Size = (uint)ms.Length };
            reparsed.Read(reader);

            using (Assert.Multiple())
            {
                await Assert.That(reparsed.CRC32).IsEqualTo(block.CRC32);
                await Assert.That(reparsed.Data).IsEquivalentTo(block.Data, CollectionOrdering.Matching);
                await Assert.That(reparsed.Images).Count().IsEqualTo(block.Images.Count);

                for (var i = 0; i < block.Images.Count; i++)
                {
                    await Assert.That(reparsed.Images[i].Name).IsEqualTo(block.Images[i].Name);
                    await Assert.That(reparsed.Images[i].Width).IsEqualTo(block.Images[i].Width);
                    await Assert.That(reparsed.Images[i].Height).IsEqualTo(block.Images[i].Height);
                    await Assert.That(reparsed.Images[i].CRC32).IsEqualTo(block.Images[i].CRC32);
                }
            }
        }

        [Test]
        public async Task SerializePanoramaStyleVersion2()
        {
            // Version 2 image entries have no per-image CRC32
            using var resource = GetTestResource("thelab_debugger_v2.vcss_c");
            var block = (Panorama)resource.DataBlock!;

            using (Assert.Multiple())
            {
                await Assert.That(block.Images).Count().IsEqualTo(2);
                await Assert.That(block.Images[0].Name).IsEqualTo("panorama/images/collapse_tga.vtex");
                await Assert.That(block.Images[1].Name).IsEqualTo("panorama/images/expand_tga.vtex");

                foreach (var image in block.Images)
                {
                    await Assert.That(image.Width).IsEqualTo((ushort)9);
                    await Assert.That(image.Height).IsEqualTo((ushort)9);
                    await Assert.That(image.CRC32).IsZero();
                }
            }

            var ms = new MemoryStream();
            block.Serialize(ms);
            ms.Position = 0;

            using var reader = new BinaryReader(ms);
            var reparsed = new PanoramaStyle { Resource = resource, Size = (uint)ms.Length };
            reparsed.Read(reader);

            using (Assert.Multiple())
            {
                await Assert.That(reparsed.CRC32).IsEqualTo(block.CRC32);
                await Assert.That(reparsed.Data).IsEquivalentTo(block.Data, CollectionOrdering.Matching);
                await Assert.That(reparsed.Images).Count().IsEqualTo(block.Images.Count);
            }
        }

        private static Resource GetTestResource(string resourceName)
        {
            var file = Path.Combine(TestContext.TestDirectory!, "Files", resourceName);
            var resource = new Resource
            {
                FileName = file,
            };

            resource.Read(file);
            return resource;
        }
    }
}
