using System.IO;
using NUnit.Framework;
using ValveResourceFormat;
using ValveResourceFormat.IO;

namespace Tests
{
    [TestFixture]
    public class SnapshotTest
    {
        [Test]
        public void TestVsnapExtract()
        {
            var file = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "test.vsnap_c");
            using var resource = new Resource
            {
                FileName = file,
            };
            resource.Read(file);

            var vsnapExtract = new SnapshotExtract(resource);

            Assert.That(vsnapExtract.ToValveSnap(), Is.Not.Empty);
        }
    }
}
