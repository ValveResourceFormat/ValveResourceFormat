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
                Assert.That(reparsed.Names, Has.Count.EqualTo(block.Names.Count));

                for (var i = 0; i < block.Names.Count; i++)
                {
                    Assert.That(reparsed.Names[i].Name, Is.EqualTo(block.Names[i].Name));
                    Assert.That(reparsed.Names[i].Unknown1, Is.EqualTo(block.Names[i].Unknown1));
                    Assert.That(reparsed.Names[i].Unknown2, Is.EqualTo(block.Names[i].Unknown2));
                }
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
