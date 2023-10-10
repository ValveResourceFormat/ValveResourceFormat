using System;
using System.IO;
using NUnit.Framework;
using ValveResourceFormat;
using ValveResourceFormat.IO;

namespace Tests
{
    [TestFixture]
    public class MapExtractTest
    {
        [Test]
        public void TestMapExtractVmapInit()
        {
            using var vmapResource = new Resource();
            vmapResource.Read(Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "dota.vmap_c"));

            var exception = Assert.Throws<FileNotFoundException>(() => new MapExtract(vmapResource, new NullFileLoader()));
            Assert.That(exception.Message, Contains.Substring("Failed to find world resource"));

            //var extract = new MapExtract(vmapResource, null);
            //Assert.AreEqual(extract.LumpFolder, Path.Combine("maps", "dota"));
        }

        [Test]
        public void TestMapExtractVwrldInit()
        {
            using var worldResource = new Resource();
            var worldPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "world.vwrld_c");
            worldResource.Read(worldPath);

            var exception = Assert.Throws<ArgumentNullException>(() => new MapExtract(worldResource, null));
            Assert.That(exception.Message, Contains.Substring("file loader must be provided to load the map's lumps"));

            var extract = new MapExtract(worldResource, new NullFileLoader());
            Assert.AreEqual(extract.LumpFolder, Path.GetDirectoryName(worldPath));

            extract.ToValveMap();

            //var contentFile = extract.ToContentFile();
            //Assert.That(contentFile, Is.Not.Null);
            //Assert.That(contentFile.Data, Is.Not.Null);
            //Assert.That(contentFile.Data.Length, Is.GreaterThan(0));
        }
    }
}
