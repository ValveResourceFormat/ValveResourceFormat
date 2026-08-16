using System.IO;
using System.Threading.Tasks;
using ValveResourceFormat.MapFormats;

namespace Tests
{
    public class GridNavTest
    {
        [Test]
        public async Task ParsesGridNavFile()
        {
            var path = Path.Combine(TestContext.TestDirectory!, "Files", "test_basic.gnv");
            var nav = new GridNavFile();
            nav.Read(path);

            using (Assert.Multiple())
            {
                await Assert.That(nav.EdgeSize).IsEqualTo(64);
                await Assert.That(nav.Height).IsEqualTo(64);
                await Assert.That(nav.OffsetX).IsEqualTo(32);
                await Assert.That(nav.OffsetY).IsEqualTo(32);
                await Assert.That(nav.Grid).Count().IsEqualTo(4096);
            }
        }
    }
}
