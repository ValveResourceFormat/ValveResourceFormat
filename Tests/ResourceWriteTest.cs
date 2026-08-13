using System.IO;
using NUnit.Framework;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

namespace Tests
{
    [TestFixture]
    public partial class ResourceWriteTest
    {
        [Test]
        public void Write()
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

            using (Assert.EnterMultipleScope())
            {
                Assert.That(newResource.Version, Is.EqualTo(resource.Version));
                Assert.That(newResource.ResourceType, Is.EqualTo(resource.ResourceType));
                Assert.That(newResource.Blocks, Has.Count.EqualTo(resource.Blocks.Count));

                for (var i = 0; i < newResource.Blocks.Count; i++)
                {
                    Assert.That(newResource.Blocks[i].Type, Is.EqualTo(resource.Blocks[i].Type));
                }
            }
        }

        [Test]
        public void ResourceModification()
        {
            const string NewName = "modified_worldnode.vmdl";

            using var resource = GetTestResource("n0_lr0_c0_s_cb_b_nomerge236.vmdl_c");
            var outputPath = $"{TestContext.CurrentContext.WorkDirectory}/{NewName}_c";

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

            using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            {
                resource.Serialize(fs);
            }

            // Now try to parse what we just wrote
            using var newResource = new Resource
            {
                FileName = outputPath,
            };

            newResource.Read(outputPath);
            var newModelInfo = (Model)newResource.DataBlock!;
            Assert.That(newModelInfo.Name, Is.EqualTo(NewName));
        }

        [Test]
        public void SerializePanoramaLayout()
        {
            using var resource = GetTestResource("dashboard_page_credits.vxml_c");
            var block = (Panorama)resource.DataBlock!;

            var ms = new MemoryStream();
            block.Serialize(ms);
            ms.Position = 0;

            using var reader = new BinaryReader(ms);
            var reparsed = new PanoramaLayout { Resource = resource, Size = (uint)ms.Length };
            reparsed.Read(reader);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(reparsed.CRC32, Is.EqualTo(block.CRC32));
                Assert.That(reparsed.Data, Is.EqualTo(block.Data));
                Assert.That(reparsed.Images, Has.Count.EqualTo(block.Images.Count));

                for (var i = 0; i < block.Images.Count; i++)
                {
                    Assert.That(reparsed.Images[i].Name, Is.EqualTo(block.Images[i].Name));
                    Assert.That(reparsed.Images[i].Width, Is.EqualTo(block.Images[i].Width));
                    Assert.That(reparsed.Images[i].Height, Is.EqualTo(block.Images[i].Height));
                    Assert.That(reparsed.Images[i].CRC32, Is.EqualTo(block.Images[i].CRC32));
                }
            }
        }

        [Test]
        public void SerializePanoramaStyleVersion2()
        {
            // Version 2 image entries have no per-image CRC32
            using var resource = GetTestResource("thelab_debugger_v2.vcss_c");
            var block = (Panorama)resource.DataBlock!;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(block.Images, Has.Count.EqualTo(2));
                Assert.That(block.Images[0].Name, Is.EqualTo("panorama/images/collapse_tga.vtex"));
                Assert.That(block.Images[1].Name, Is.EqualTo("panorama/images/expand_tga.vtex"));

                foreach (var image in block.Images)
                {
                    Assert.That(image.Width, Is.EqualTo(9));
                    Assert.That(image.Height, Is.EqualTo(9));
                    Assert.That(image.CRC32, Is.Zero);
                }
            }

            var ms = new MemoryStream();
            block.Serialize(ms);
            ms.Position = 0;

            using var reader = new BinaryReader(ms);
            var reparsed = new PanoramaStyle { Resource = resource, Size = (uint)ms.Length };
            reparsed.Read(reader);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(reparsed.CRC32, Is.EqualTo(block.CRC32));
                Assert.That(reparsed.Data, Is.EqualTo(block.Data));
                Assert.That(reparsed.Images, Has.Count.EqualTo(block.Images.Count));
            }
        }

        private static Resource GetTestResource(string resourceName)
        {
            var file = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", resourceName);
            var resource = new Resource
            {
                FileName = file,
            };

            resource.Read(file);
            return resource;
        }
    }
}
