using System.Text;
using System.Threading.Tasks;
using ValveResourceFormat;

namespace Tests
{
    public class ResourceTypeTest
    {
        [Test]
        public async Task ReturnsCorrectExtension()
        {
            using (Assert.Multiple())
            {
                await Assert.That(ResourceType.Unknown.GetExtension()).IsNull();
                await Assert.That(ResourceType.Animation.GetExtension()).IsEqualTo("vanim");
                await Assert.That(ResourceType.Panorama.GetExtension()).IsEqualTo("vtxt");

                await Assert.That(((ResourceType)1333337).GetExtension()).IsNull();
            }
        }

        [Test]
        public async Task DeterminesResourceTypeByFileExtension()
        {
            using (Assert.Multiple())
            {
                await Assert.That(ResourceTypeExtensions.DetermineByFileExtension(".vcss_c")).IsEqualTo(ResourceType.PanoramaStyle);
                await Assert.That(ResourceTypeExtensions.DetermineByFileExtension(".vanim_c")).IsEqualTo(ResourceType.Animation);
                await Assert.That(ResourceTypeExtensions.DetermineByFileExtension(".vanim")).IsEqualTo(ResourceType.Animation);
                await Assert.That(ResourceTypeExtensions.DetermineByFileExtension(".vsmart_c")).IsEqualTo(ResourceType.SmartProp);
                await Assert.That(ResourceTypeExtensions.DetermineByFileExtension(".vanim_C")).IsEqualTo(ResourceType.Unknown);
                await Assert.That(ResourceTypeExtensions.DetermineByFileExtension(".anim")).IsEqualTo(ResourceType.Unknown);
                await Assert.That(ResourceTypeExtensions.DetermineByFileExtension(".anim_c")).IsEqualTo(ResourceType.Unknown);
                await Assert.That(ResourceTypeExtensions.DetermineByFileExtension(".")).IsEqualTo(ResourceType.Unknown);
                await Assert.That(ResourceTypeExtensions.DetermineByFileExtension(".")).IsEqualTo(ResourceType.Unknown);
                await Assert.That(ResourceTypeExtensions.DetermineByFileExtension("")).IsEqualTo(ResourceType.Unknown);
                await Assert.That(ResourceTypeExtensions.DetermineByFileExtension(null)).IsEqualTo(ResourceType.Unknown);
            }
        }

        [Test]
        public async Task BlockTypesHaveCorrectFourCcValues()
        {
            var blockTypes = Enum.GetValues<BlockType>();

            foreach (var blockType in blockTypes)
            {
                var enumName = Enum.GetName(blockType)!;

                if (enumName == "Undefined")
                {
                    await Assert.That((uint)blockType).IsZero();
                    continue;
                }

                var value = (uint)blockType;
                var bytes = BitConverter.GetBytes(value);
                var actualFourCc = Encoding.ASCII.GetString(bytes);

                await Assert.That(enumName).IsEqualTo(actualFourCc);

                var calculatedValue = 0u;
                for (var i = 0; i < enumName.Length && i < 4; i++)
                {
                    calculatedValue |= (uint)(byte)enumName[i] << (i * 8);
                }

                await Assert.That(calculatedValue).IsEqualTo(value);
            }
        }
    }
}
