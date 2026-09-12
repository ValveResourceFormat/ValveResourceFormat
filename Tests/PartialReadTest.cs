using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ValveResourceFormat;

namespace Tests
{
    public class PartialReadTest
    {
        private static string TestFile(string name) => Path.Combine(TestContext.TestDirectory!, "Files", name);

        [Test]
        public async Task PartialReadDefersAndMaterializesBlocks()
        {
            using var fullResource = new Resource();
            fullResource.Read(TestFile("alchemist.vmdl_c"));

            using var partialResource = new Resource();
            partialResource.Read(TestFile("alchemist.vmdl_c"), new ResourceReadOptions { IncludeBlocks = [BlockType.RERL] });

            var deferredData = partialResource.Blocks.First(block => block.Type == BlockType.DATA);
            await Assert.That(deferredData.IsRead).IsFalse();

            var dataBlock = partialResource.DataBlock;

            using (Assert.Multiple())
            {
                await Assert.That(dataBlock).IsNotNull();
                await Assert.That(deferredData.IsRead).IsTrue();
            }

            await Assert.That(dataBlock!.ToString()).IsEqualTo(fullResource.DataBlock!.ToString());

            var fullReferences = fullResource.ExternalReferences?.ResourceRefInfoList.Select(static reference => reference.Name).ToList();
            var partialReferences = partialResource.ExternalReferences?.ResourceRefInfoList.Select(static reference => reference.Name).ToList();
            await Assert.That(partialReferences).IsEquivalentTo(fullReferences!);
        }

        [Test]
        public async Task DefaultOptionsParseEverything()
        {
            using var resource = new Resource();
            resource.Read(TestFile("alchemist.vmdl_c"), default);

            await Assert.That(resource.Blocks.All(static block => block.IsRead)).IsTrue();
        }

        [Test]
        public async Task ForcedBlocksAlwaysParse()
        {
            using var resource = new Resource();
            resource.Read(TestFile("alchemist.vmdl_c"), new ResourceReadOptions { IncludeBlocks = [] });

            using (Assert.Multiple())
            {
                await Assert.That(resource.ResourceType).IsEqualTo(ResourceType.Model);
                await Assert.That(resource.EditInfo).IsNotNull();
                await Assert.That(resource.Blocks
                    .Where(static block => block.Type is BlockType.REDI or BlockType.RED2 or BlockType.NTRO)
                    .All(static block => block.IsRead)).IsTrue();
            }
        }

        [Test]
        public async Task ExcludeBlocksSkipsListedTypes()
        {
            using var resource = new Resource();
            resource.Read(TestFile("export_test.vmdl_c"), new ResourceReadOptions { ExcludeBlocks = [BlockType.MBUF] });

            var meshBuffer = resource.Blocks.First(static block => block.Type == BlockType.MBUF);

            using (Assert.Multiple())
            {
                await Assert.That(meshBuffer.IsRead).IsFalse();
                await Assert.That(resource.DataBlock!.IsRead).IsTrue();
            }
        }

        [Test]
        public async Task OverlappingIncludeExcludeThrows()
        {
            using var resource = new Resource();

            await Assert.That(() => resource.Read(TestFile("alchemist.vmdl_c"), new ResourceReadOptions
            {
                IncludeBlocks = [BlockType.DATA],
                ExcludeBlocks = [BlockType.DATA],
            })).Throws<ArgumentException>();
        }
    }
}
