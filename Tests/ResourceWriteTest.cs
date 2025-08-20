using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using ValveResourceFormat;

namespace Tests
{
    [TestFixture]
    public partial class ResourceWriteTest
    {
        [Test]
        public void Write()
        {
            var file = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "default_ents_kv3_v4_zstd.vents_c");
            using var resource = new Resource
            {
                FileName = file,
            };
            resource.Read(file);

            var ms = new MemoryStream();
            resource.Serialize(ms);
            ms.Position = 0;

            // Now try to parse what we just wrote
            using var newResource = new Resource
            {
                FileName = file,
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
    }
}
