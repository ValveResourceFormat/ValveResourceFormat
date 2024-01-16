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
    }
}
