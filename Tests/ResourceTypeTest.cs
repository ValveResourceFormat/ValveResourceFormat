using System.Text;
using NUnit.Framework;
using ValveResourceFormat;

namespace Tests
{
    public class ResourceTypeTest
    {
        [Test]
        public void ReturnsCorrectExtension()
        {
            Assert.Multiple(() =>
            {
                Assert.That(ResourceType.Unknown.GetExtension(), Is.Null);
                Assert.That(ResourceType.Animation.GetExtension(), Is.EqualTo("vanim"));
                Assert.That(ResourceType.Panorama.GetExtension(), Is.EqualTo("vtxt"));

                Assert.That(((ResourceType)1333337).GetExtension(), Is.Null);
            });
        }

        [Test]
        public void DeterminesResourceTypeByFileExtension()
        {
            Assert.Multiple(() =>
            {
                Assert.That(ResourceTypeExtensions.DetermineByFileExtension(".vcss_c"), Is.EqualTo(ResourceType.PanoramaStyle));
                Assert.That(ResourceTypeExtensions.DetermineByFileExtension(".vanim_c"), Is.EqualTo(ResourceType.Animation));
                Assert.That(ResourceTypeExtensions.DetermineByFileExtension(".vanim"), Is.EqualTo(ResourceType.Animation));
                Assert.That(ResourceTypeExtensions.DetermineByFileExtension(".vsmart_c"), Is.EqualTo(ResourceType.SmartProp));
                Assert.That(ResourceTypeExtensions.DetermineByFileExtension(".vanim_C"), Is.EqualTo(ResourceType.Unknown));
                Assert.That(ResourceTypeExtensions.DetermineByFileExtension(".anim"), Is.EqualTo(ResourceType.Unknown));
                Assert.That(ResourceTypeExtensions.DetermineByFileExtension(".anim_c"), Is.EqualTo(ResourceType.Unknown));
                Assert.That(ResourceTypeExtensions.DetermineByFileExtension("."), Is.EqualTo(ResourceType.Unknown));
                Assert.That(ResourceTypeExtensions.DetermineByFileExtension("."), Is.EqualTo(ResourceType.Unknown));
                Assert.That(ResourceTypeExtensions.DetermineByFileExtension(""), Is.EqualTo(ResourceType.Unknown));
                Assert.That(ResourceTypeExtensions.DetermineByFileExtension(null), Is.EqualTo(ResourceType.Unknown));
            });
        }

        [Test]
        public void BlockTypesHaveCorrectFourCcValues()
        {
            var blockTypes = Enum.GetValues<BlockType>();

            foreach (var blockType in blockTypes)
            {
                var enumName = Enum.GetName(blockType)!;

                if (enumName == "Undefined")
                {
                    Assert.That((uint)blockType, Is.EqualTo(0));
                    continue;
                }

                var value = (uint)blockType;
                var bytes = BitConverter.GetBytes(value);
                var actualFourCc = Encoding.ASCII.GetString(bytes);

                Assert.That(enumName, Is.EqualTo(actualFourCc));

                var calculatedValue = 0u;
                for (var i = 0; i < enumName.Length && i < 4; i++)
                {
                    calculatedValue |= (uint)(byte)enumName[i] << (i * 8);
                }

                Assert.That(calculatedValue, Is.EqualTo(value));
            }
        }
    }
}
