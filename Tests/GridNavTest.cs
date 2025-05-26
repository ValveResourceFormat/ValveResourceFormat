using System.IO;
using NUnit.Framework;
using ValveResourceFormat.MapFormats;

namespace Tests
{
    public class GridNavTest
    {
        [Test]
        public void ParsesGridNavFile()
        {
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "test_basic.gnv");
            var nav = new GridNavFile();
            nav.Read(path);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(nav.EdgeSize, Is.EqualTo(64));
                Assert.That(nav.Height, Is.EqualTo(64));
                Assert.That(nav.OffsetX, Is.EqualTo(32));
                Assert.That(nav.OffsetY, Is.EqualTo(32));
                Assert.That(nav.Grid, Has.Length.EqualTo(4096));
            }
        }
    }
}
