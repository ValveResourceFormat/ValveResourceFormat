using System.IO;
using System.Linq;
using NUnit.Framework;
using ValveResourceFormat;

namespace Tests
{
    [TestFixture]
    public class PartialReadTest
    {
        private static string TestFile(string name) => Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", name);

        [Test]
        public void PartialReadDefersAndMaterializesBlocks()
        {
            using var fullResource = new Resource();
            fullResource.Read(TestFile("alchemist.vmdl_c"));

            using var partialResource = new Resource();
            partialResource.Read(TestFile("alchemist.vmdl_c"), new ResourceReadOptions { IncludeBlocks = [BlockType.RERL] });

            var deferredData = partialResource.Blocks.First(b => b.Type == BlockType.DATA);
            Assert.That(deferredData.IsRead, Is.False);

            var dataBlock = partialResource.DataBlock;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(dataBlock, Is.Not.Null);
                Assert.That(deferredData.IsRead, Is.True, "Accessing the data block should have materialized it");
            }

            Assert.That(dataBlock!.ToString(), Is.EqualTo(fullResource.DataBlock!.ToString()));

            var fullRefs = fullResource.ExternalReferences?.ResourceRefInfoList.Select(r => r.Name).ToList();
            var partialRefs = partialResource.ExternalReferences?.ResourceRefInfoList.Select(r => r.Name).ToList();
            Assert.That(partialRefs, Is.EqualTo(fullRefs));
        }

        [Test]
        public void DefaultOptionsParseEverything()
        {
            using var resource = new Resource();
            resource.Read(TestFile("alchemist.vmdl_c"), default(ResourceReadOptions));

            Assert.That(resource.Blocks, Has.All.Property(nameof(Block.IsRead)).True);
        }

        [Test]
        public void ForcedBlocksAlwaysParse()
        {
            using var resource = new Resource();
            resource.Read(TestFile("alchemist.vmdl_c"), new ResourceReadOptions { IncludeBlocks = [] });

            using (Assert.EnterMultipleScope())
            {
                Assert.That(resource.ResourceType, Is.EqualTo(ResourceType.Model));
                Assert.That(resource.EditInfo, Is.Not.Null);
                Assert.That(
                    resource.Blocks.Where(b => b.Type is BlockType.REDI or BlockType.RED2 or BlockType.NTRO),
                    Has.All.Property(nameof(Block.IsRead)).True);
            }
        }

        [Test]
        public void ExcludeBlocksSkipsListedTypes()
        {
            using var resource = new Resource();
            resource.Read(TestFile("export_test.vmdl_c"), new ResourceReadOptions { ExcludeBlocks = [BlockType.MBUF] });

            var meshBuffer = resource.Blocks.First(b => b.Type == BlockType.MBUF);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(meshBuffer.IsRead, Is.False);
                Assert.That(resource.DataBlock!.IsRead, Is.True);
            }
        }

        [Test]
        public void OverlappingIncludeExcludeThrows()
        {
            using var resource = new Resource();

            Assert.Throws<ArgumentException>(() => resource.Read(TestFile("alchemist.vmdl_c"), new ResourceReadOptions
            {
                IncludeBlocks = [BlockType.DATA],
                ExcludeBlocks = [BlockType.DATA],
            }));
        }
    }
}
