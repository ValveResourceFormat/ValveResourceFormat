using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using SteamDatabase.ValvePak;
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
            Debug.Assert(exception != null);
            Assert.That(exception, Is.Not.Null);
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
            Debug.Assert(exception != null);
            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.Message, Contains.Substring("file loader must be provided to load the map's lumps"));

            var extract = new MapExtract(worldResource, new NullFileLoader());
            Assert.That(Path.GetFullPath(Path.GetDirectoryName(worldPath)!), Is.EqualTo(Path.GetFullPath(extract.LumpFolder)));

            extract.ToValveMap();

            //var contentFile = extract.ToContentFile();
            //Assert.That(contentFile, Is.Not.Null);
            //Assert.That(contentFile.Data, Is.Not.Null);
            //Assert.That(contentFile.Data.Length, Is.GreaterThan(0));
        }

        [Test]
        public void TestMapExtractFromVpk()
        {
            var vpkPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "small_map_with_material.vpk");

            using var package = new Package();
            package.Read(vpkPath);

            using var loader = new GameFileLoader(package, vpkPath);

            using var worldResource = loader.LoadFile("maps/ui/nametag.vmap_c");

            var extract = new MapExtract(worldResource, loader);

            extract.ToValveMap();

            var contentFile = extract.ToContentFile();
            Assert.That(contentFile, Is.Not.Null);
            Assert.That(contentFile.Data, Is.Not.Null);
        }
    }
}
