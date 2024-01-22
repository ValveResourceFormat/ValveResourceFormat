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
                Assert.That(Resource.DetermineResourceTypeByFileExtension(".vcss_c"), Is.EqualTo(ResourceType.PanoramaStyle));
                Assert.That(Resource.DetermineResourceTypeByFileExtension(".vanim_c"), Is.EqualTo(ResourceType.Animation));
                Assert.That(Resource.DetermineResourceTypeByFileExtension(".vanim"), Is.EqualTo(ResourceType.Animation));
                Assert.That(Resource.DetermineResourceTypeByFileExtension(".vsmart_c"), Is.EqualTo(ResourceType.SmartProp));
                Assert.That(Resource.DetermineResourceTypeByFileExtension(".vanim_C"), Is.EqualTo(ResourceType.Unknown));
                Assert.That(Resource.DetermineResourceTypeByFileExtension(".anim"), Is.EqualTo(ResourceType.Unknown));
                Assert.That(Resource.DetermineResourceTypeByFileExtension(".anim_c"), Is.EqualTo(ResourceType.Unknown));
                Assert.That(Resource.DetermineResourceTypeByFileExtension("."), Is.EqualTo(ResourceType.Unknown));
                Assert.That(Resource.DetermineResourceTypeByFileExtension("."), Is.EqualTo(ResourceType.Unknown));
                Assert.That(Resource.DetermineResourceTypeByFileExtension(""), Is.EqualTo(ResourceType.Unknown));
                Assert.That(Resource.DetermineResourceTypeByFileExtension(null), Is.EqualTo(ResourceType.Unknown));
            });
        }
    }
}
