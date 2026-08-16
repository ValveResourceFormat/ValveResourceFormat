using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;

namespace Tests
{
    public class MapExtractTest
    {
        [Test]
        public async Task TestMapExtractVmapInit()
        {
            using var vmapResource = new Resource();
            vmapResource.Read(Path.Combine(TestContext.TestDirectory!, "Files", "dota.vmap_c"));

            var exception = Assert.ThrowsExactly<FileNotFoundException>(() => _ = new MapExtract(vmapResource, new NullFileLoader()));
            Debug.Assert(exception != null);
            await Assert.That(exception).IsNotNull();
            await Assert.That(exception.Message).Contains("Failed to find world resource");

            //var extract = new MapExtract(vmapResource, null);
            //Assert.AreEqual(extract.LumpFolder, Path.Combine("maps", "dota"));
        }

        [Test]
        public async Task TestMapExtractVwrldInit()
        {
            using var worldResource = new Resource();
            var worldPath = Path.Combine(TestContext.TestDirectory!, "Files", "world.vwrld_c");
            worldResource.Read(worldPath);

            var exception = Assert.ThrowsExactly<ArgumentNullException>(() => _ = new MapExtract(worldResource, null));
            Debug.Assert(exception != null);
            await Assert.That(exception).IsNotNull();
            await Assert.That(exception.Message).Contains("file loader must be provided to load the map's lumps");

            var extract = new MapExtract(worldResource, new NullFileLoader());
            await Assert.That(Path.GetFullPath(Path.GetDirectoryName(worldPath)!)).IsEqualTo(Path.GetFullPath(extract.LumpFolder));

            extract.ToValveMap();

            //var contentFile = extract.ToContentFile();
            //Assert.That(contentFile, Is.Not.Null);
            //Assert.That(contentFile.Data, Is.Not.Null);
            //Assert.That(contentFile.Data.Length, Is.GreaterThan(0));
        }

        [Test]
        public async Task TestMapExtractFromVpk()
        {
            var vpkPath = Path.Combine(TestContext.TestDirectory!, "Files", "small_map_with_material.vpk");

            using var package = new Package();
            package.Read(vpkPath);

            using var loader = new GameFileLoader(package, vpkPath);

            using var worldResource = loader.LoadFile("maps/ui/nametag.vmap_c");

            var extract = new MapExtract(worldResource!, loader);

            extract.ToValveMap();

            var contentFile = extract.ToContentFile();
            await Assert.That(contentFile).IsNotNull();
            await Assert.That(contentFile.Data).IsNotNull();
        }

        [Test]
        public async Task TestMapExtractFromVpkWithPhys()
        {
            var vpkPath = Path.Combine(TestContext.TestDirectory!, "Files", "dota_riverflow_fx.vpk");

            using var package = new Package();
            package.Read(vpkPath);

            using var loader = new GameFileLoader(package, vpkPath);

            using var worldResource = loader.LoadFile("maps/prefabs/dota_riverflow_fx.vmap_c");

            var extract = new MapExtract(worldResource!, loader);

            extract.ToValveMap();

            var contentFile = extract.ToContentFile();
            await Assert.That(contentFile).IsNotNull();
            await Assert.That(contentFile.Data).IsNotNull();
        }
    }
}
