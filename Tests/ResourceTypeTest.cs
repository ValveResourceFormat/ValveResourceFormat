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
    }
}
