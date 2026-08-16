using System.IO;
using System.Threading.Tasks;
using ValveResourceFormat;
using ValveResourceFormat.IO;

namespace Tests
{
    public class SnapshotTest
    {
        [Test]
        public async Task TestVsnapExtract()
        {
            var file = Path.Combine(TestContext.TestDirectory!, "Files", "test.vsnap_c");
            using var resource = new Resource
            {
                FileName = file,
            };
            resource.Read(file);

            var vsnapExtract = new SnapshotExtract(resource);

            await Assert.That(vsnapExtract.ToValveSnap()).IsNotEmpty();
        }
    }
}
