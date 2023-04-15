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

            var exception = Assert.Throws<InvalidDataException>(() => new MapExtract(vmapResource, null));
            Assert.That(exception.Message, Contains.Substring("filename does not match"));
            Assert.That(exception.Message, Contains.Substring("RERL-derived lump folder"));

            //var extract = new MapExtract(vmapResource, null);
            //Assert.AreEqual(extract.LumpFolder, Path.Combine("maps", "dota"));
        }

        [Test]
        public void TestMapExtractVwrldInit()
        {
            using var worldResource = new Resource();
            var worldPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "world.vwrld_c");
            worldResource.Read(worldPath);

            var extract = new MapExtract(worldResource, null);
            Assert.AreEqual(extract.LumpFolder, Path.GetDirectoryName(worldPath));

            var exception = Assert.Throws<InvalidOperationException>(() => extract.ToValveMap());
            Assert.That(exception.Message, Contains.Substring("file loader must be provided to load the map's lumps"));

            //var contentFile = extract.ToContentFile();
            //Assert.That(contentFile, Is.Not.Null);
            //Assert.That(contentFile.Data, Is.Not.Null);
            //Assert.That(contentFile.Data.Length, Is.GreaterThan(0));
        }
    }
}
