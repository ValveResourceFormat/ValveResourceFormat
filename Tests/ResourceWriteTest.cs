using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

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
            var meshGroupMasks = (KVObject)modelInfo.Data.Properties["m_refMeshGroupMasks"].Value!;
            meshGroupMasks.Properties["0"] = new KVValue(1337);
            meshGroupMasks.Properties["1"] = new KVValue(1338);

            modelInfo.Data.Properties["m_name"] = new KVValue(NewName);

            using (var fs = new FileStream(outputPath, FileMode.OpenOrCreate, FileAccess.Write))
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
