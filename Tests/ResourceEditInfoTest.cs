using System.IO;
using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;

namespace Tests
{
    public class ResourceEditInfoTest
    {
        private static readonly string[] AlchemistChildResources =
        [
            "models/heroes/alchemist/alchemist_model.vmesh",
            "models/heroes/alchemist/alchemist_lod.vmesh",
            "models/heroes/alchemist/asset_sequences_e7fec448.vagrp",
        ];

        [Test]
        public async Task ReadsChildResourceIds()
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TestContext.TestDirectory!, "Files", "alchemist.vmdl_c"));

            var editInfo = (ResourceEditInfo)resource.GetBlockByType(BlockType.REDI)!;

            using (Assert.Multiple())
            {
                await Assert.That(editInfo.ChildResourceList).IsEquivalentTo(AlchemistChildResources, CollectionOrdering.Matching);

                await Assert.That(editInfo.ChildResourceIds).Count().IsEqualTo(editInfo.ChildResourceList.Count);
                await Assert.That(editInfo.ChildResourceIds).All(x => x != 0);
            }
        }

        [Test]
        public async Task ChildResourceIdsAreEmptyForKeyValuesEditInfo()
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TestContext.TestDirectory!, "Files", "dynamic_images_ui_misc.vpdi_c"));

            var editInfo = (ResourceEditInfo2)resource.GetBlockByType(BlockType.RED2)!;

            await Assert.That(editInfo.ChildResourceIds).IsEmpty();
        }
    }
}
