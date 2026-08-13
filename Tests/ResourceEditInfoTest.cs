using System.IO;
using NUnit.Framework;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;

namespace Tests
{
    [TestFixture]
    public class ResourceEditInfoTest
    {
        private static readonly string[] AlchemistChildResources =
        [
            "models/heroes/alchemist/alchemist_model.vmesh",
            "models/heroes/alchemist/alchemist_lod.vmesh",
            "models/heroes/alchemist/asset_sequences_e7fec448.vagrp",
        ];

        [Test]
        public void ReadsChildResourceIds()
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "alchemist.vmdl_c"));

            var editInfo = (ResourceEditInfo)resource.GetBlockByType(BlockType.REDI)!;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(editInfo.ChildResourceList, Is.EqualTo(AlchemistChildResources));

                Assert.That(editInfo.ChildResourceIds, Has.Count.EqualTo(editInfo.ChildResourceList.Count));
                Assert.That(editInfo.ChildResourceIds, Is.All.Not.Zero);
            }
        }

        [Test]
        public void ChildResourceIdsAreEmptyForKeyValuesEditInfo()
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "dynamic_images_ui_misc.vpdi_c"));

            var editInfo = (ResourceEditInfo2)resource.GetBlockByType(BlockType.RED2)!;

            Assert.That(editInfo.ChildResourceIds, Is.Empty);
        }
    }
}
