using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ValveResourceFormat;

namespace Tests
{
    public class PartialReadTest
    {
        private static string TestFile(string name) => Path.Combine(TestContext.TestDirectory!, "Files", name);

        private static Resource Read(string name, BlockParsing parsing = BlockParsing.Deferred)
        {
            var resource = new Resource();
            resource.Read(TestFile(name), parsing);
            return resource;
        }

        private static byte[] ReadWithCorruptData(string name)
        {
            var bytes = File.ReadAllBytes(TestFile(name));

            using var resource = new Resource();
            resource.Read(new MemoryStream(bytes));

            var data = resource.UnparsedBlocks.First(static block => block.Type == BlockType.DATA);
            bytes.AsSpan((int)data.Offset, (int)data.Size).Fill(0xFF);

            return bytes;
        }

        [Test]
        public async Task DeferredReadDefersAndMaterializesBlocks()
        {
            using var fullResource = Read("alchemist.vmdl_c", BlockParsing.Runtime);
            using var partialResource = Read("alchemist.vmdl_c");

            var deferredData = partialResource.UnparsedBlocks.First(block => block.Type == BlockType.DATA);
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
        public async Task RuntimeReadLeavesOnlyToolsDataUnparsed()
        {
            using var resource = Read("alchemist.vmdl_c", BlockParsing.Runtime);

            using (Assert.Multiple())
            {
                await Assert.That(resource.UnparsedBlocks.Where(static block => block.Type is not (BlockType.REDI or BlockType.RED2)).All(static block => block.IsRead)).IsTrue();
                await Assert.That(resource.UnparsedBlocks.First(static block => block.Type is BlockType.REDI or BlockType.RED2).IsRead).IsFalse();
            }
        }

        [Test]
        public async Task RuntimeReadParsesMaterialEditInfo()
        {
            using var resource = Read("reflectivity_90b.vmat_c", BlockParsing.Runtime);

            await Assert.That(resource.UnparsedBlocks.All(static block => block.IsRead)).IsTrue();
        }

        [Test]
        public async Task EnumeratingBlocksParsesThem()
        {
            using var resource = Read("alchemist.vmdl_c");

            await Assert.That(resource.UnparsedBlocks.Any(static block => !block.IsRead)).IsTrue();
            await Assert.That(resource.Blocks.All(static block => block.IsRead)).IsTrue();
        }

        [Test]
        public async Task DeferredVDataIsSpecialized()
        {
            using var fullResource = Read("abilities_kv3_v5_zstd.vdata_c", BlockParsing.Runtime);
            using var partialResource = Read("abilities_kv3_v5_zstd.vdata_c");

            using (Assert.Multiple())
            {
                await Assert.That(partialResource.UnparsedBlocks.First(static block => block.Type == BlockType.DATA).IsRead).IsTrue();
                await Assert.That(partialResource.DataBlock!.GetType()).IsEqualTo(fullResource.DataBlock!.GetType());
            }
        }

        [Test]
        [Arguments("alchemist.vmdl_c", BlockType.NTRO)]
        [Arguments("box_creature_model.vmdl_c", BlockType.CTRL)]
        public async Task DependencyBlocksParseOnFirstUse(string file, BlockType dependency)
        {
            using var fullResource = Read(file, BlockParsing.Runtime);
            using var resource = Read(file);
            var dependencyBlock = resource.UnparsedBlocks.First(block => block.Type == dependency);

            using (Assert.Multiple())
            {
                await Assert.That(resource.ResourceType).IsEqualTo(ResourceType.Model);
                await Assert.That(dependencyBlock.IsRead).IsFalse();
            }

            await Assert.That(resource.DataBlock!.ToString()).IsEqualTo(fullResource.DataBlock!.ToString());
            await Assert.That(resource.GetBlockByType(dependency)!.ToString()).IsEqualTo(fullResource.GetBlockByType(dependency)!.ToString());
        }

        [Test]
        public async Task DeferredEditInfoParsesOnFirstUse()
        {
            using var fullResource = Read("reflectivity_90b.vmat_c", BlockParsing.Runtime);
            using var partialResource = Read("reflectivity_90b.vmat_c");
            var editInfoBlock = partialResource.UnparsedBlocks.First(static block => block.Type is BlockType.REDI or BlockType.RED2);

            await Assert.That(editInfoBlock.IsRead).IsFalse();

            var editInfo = partialResource.EditInfo;

            using (Assert.Multiple())
            {
                await Assert.That(editInfoBlock.IsRead).IsTrue();
                await Assert.That(editInfo!.ToString()).IsEqualTo(fullResource.EditInfo!.ToString());
            }
        }

        [Test]
        public async Task DeferredEditInfoDeterminesUnknownType()
        {
            using var fullStream = File.OpenRead(TestFile("reflectivity_90b.vmat_c"));
            using var fullResource = new Resource();
            fullResource.Read(fullStream, leaveOpen: true, BlockParsing.Runtime);

            using var partialStream = File.OpenRead(TestFile("reflectivity_90b.vmat_c"));
            using var partialResource = new Resource();
            partialResource.Read(partialStream, leaveOpen: true);

            using (Assert.Multiple())
            {
                await Assert.That(fullResource.ResourceType).IsEqualTo(ResourceType.Material);
                await Assert.That(partialResource.ResourceType).IsEqualTo(ResourceType.Material);
                await Assert.That(partialResource.DataBlock!.ToString()).IsEqualTo(fullResource.DataBlock!.ToString());
            }
        }

        [Test]
        public async Task ConcurrentMaterializationMatchesFullRead()
        {
            using var fullResource = Read("export_test.vmdl_c", BlockParsing.Runtime);
            var expected = fullResource.Blocks.Select(static block => block.ToString()).ToList();

            using var partialResource = Read("export_test.vmdl_c");

            await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            {
                for (var i = partialResource.Blocks.Count - 1; i >= 0; i--)
                {
                    partialResource.GetBlockByIndex(i);
                }
            })));

            await Assert.That(partialResource.Blocks.Select(static block => block.ToString()).ToList()).IsEquivalentTo(expected);
        }

        [Test]
        public async Task MaterializingAfterDisposeThrows()
        {
            var resource = Read("alchemist.vmdl_c");
            var data = resource.UnparsedBlocks.First(static block => block.Type == BlockType.DATA);
            resource.Dispose();

            await Assert.That(data.EnsureRead).Throws<InvalidOperationException>();
        }

        [Test]
        public async Task CorruptBlockThrowsOnFirstAccess()
        {
            var bytes = ReadWithCorruptData("alchemist.vmdl_c");

            using var resource = new Resource { FileName = "alchemist.vmdl_c" };
            resource.Read(new MemoryStream(bytes), parsing: BlockParsing.Deferred);

            using (Assert.Multiple())
            {
                await Assert.That(resource.ResourceType).IsEqualTo(ResourceType.Model);
                await Assert.That(() => resource.DataBlock).ThrowsException();
                await Assert.That(() => resource.DataBlock).ThrowsException();
            }
        }

        [Test]
        public async Task CorruptBlockThrowsFromRuntimeRead()
        {
            var bytes = ReadWithCorruptData("alchemist.vmdl_c");

            using var resource = new Resource { FileName = "alchemist.vmdl_c" };

            await Assert.That(() => resource.Read(new MemoryStream(bytes), parsing: BlockParsing.Runtime)).ThrowsException();
        }
    }
}
