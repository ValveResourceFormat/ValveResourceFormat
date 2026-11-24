using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ValvePak;
using ValveResourceFormat.IO;

namespace Tests.IO
{
    public class ContentPathMapTest
    {
        [Test]
        public async Task MapsOnlyWholeValues()
        {
            var map = new ContentPathMap();
            await Assert.That(map.IsEmpty).IsTrue();

            map.Add("models/heroes/haze/haze.vmdl", "models/heroes/haze_orig/haze_orig.vmdl");
            map.AddFolder("maps/dust/", "maps/dust_orig");
            map.AddFolder("maps/dust/sub", "elsewhere/sub");
            map.Add("maps/dust/special.vmdl", "special/special.vmdl");

            using (Assert.Multiple())
            {
                await Assert.That(map.IsEmpty).IsFalse();

                await Assert.That(map.Map("models/heroes/haze/haze.vmdl")).IsEqualTo("models/heroes/haze_orig/haze_orig.vmdl");
                await Assert.That(map.Map(@"Models\Heroes\HAZE\haze.vmdl")).IsEqualTo("models/heroes/haze_orig/haze_orig.vmdl");
                await Assert.That(map.Map("/models/heroes/haze/haze.vmdl_c")).IsEqualTo("models/heroes/haze_orig/haze_orig.vmdl_c");

                await Assert.That(map.Map("models/heroes/haze/haze.vmdl.bak")).IsEqualTo("models/heroes/haze/haze.vmdl.bak");
                await Assert.That(map.Map("prefix models/heroes/haze/haze.vmdl")).IsEqualTo("prefix models/heroes/haze/haze.vmdl");
                await Assert.That(map.Map("models/heroes/haze")).IsEqualTo("models/heroes/haze");
                await Assert.That(map.TryMap(string.Empty, out _)).IsFalse();

                await Assert.That(map.Map("maps/dust")).IsEqualTo("maps/dust_orig");
                await Assert.That(map.Map(@"maps\dust\world.vwrld_c")).IsEqualTo("maps/dust_orig/world.vwrld_c");
                await Assert.That(map.Map("maps/dust/sub/a.vmdl")).IsEqualTo("elsewhere/sub/a.vmdl");
                await Assert.That(map.Map("maps/dust/special.vmdl")).IsEqualTo("special/special.vmdl");
                await Assert.That(map.Map("maps/dust2/world.vwrld")).IsEqualTo("maps/dust2/world.vwrld");
            }
        }

        [Test]
        public async Task MaterialReferencesAndTexturesFollowTheMap()
        {
            using var resource = TestFixtures.Load("player_start_kv3_v5.vmat_c");
            resource.FileName = "materials/models/editor_orig/player_start_orig.vmat";

            var map = new ContentPathMap();
            map.Add("materials/models/editor/player_start_tga_6597ddab.vtex", "materials/models/editor_orig/player_start_orig.vtex");

            using var content = FileExtract.Extract(resource, new NullFileLoader(), null, new FileExtractOptions { ReferenceMap = map });
            var vmat = Encoding.UTF8.GetString(content.Data!);

            using (Assert.Multiple())
            {
                await Assert.That(content.FileName).IsEqualTo("materials/models/editor_orig/player_start_orig.vmat");
                await Assert.That(vmat).Contains("\"TextureColor\"\t\"materials/models/editor_orig/player_start_orig_Color.png\"");
                await Assert.That(vmat).Contains("\"TextureNormal\"\t\"materials/default/default_normal_tga_7652cb_Normal.png\"");
                await Assert.That(content.AdditionalFiles.Select(static file => file.FileName)).IsEquivalentTo(
                    [
                        "materials/models/editor_orig/player_start_orig.vtex",
                        "materials/default/default_normal_tga_7652cb.vtex",
                        "materials/default/default_normal_tga_b3f4ec4c.vtex",
                    ],
                    StringComparer.Ordinal);
            }
        }

        [Test]
        public async Task ModelFilesAndTheirReferencesFollowTheMap()
        {
            using var resource = TestFixtures.Load("export_test.vmdl_c");
            resource.FileName = "models/test/export_test.vmdl";

            var map = new ContentPathMap();
            map.AddFolder("models/test", "models/test_orig");
            map.Add("materials/dev/gray_50.vmat", "materials/dev_orig/gray_50.vmat");

            using var content = FileExtract.Extract(resource, new NullFileLoader(), null, new FileExtractOptions { ReferenceMap = map });
            var vmdl = Encoding.UTF8.GetString(content.Data!);
            var dmx = Encoding.UTF8.GetString(content.SubFiles.Single().Extract!());

            using (Assert.Multiple())
            {
                await Assert.That(content.FileName).IsEqualTo("models/test_orig/export_test.vmdl");
                await Assert.That(content.SubFiles.Single().FileName).IsEqualTo("models/test_orig/export_test_export_test.dmx");
                await Assert.That(vmdl).Contains("filename = \"models/test_orig/export_test_export_test.dmx\"");
                await Assert.That(dmx).Contains("materials/dev_orig/gray_50.vmat");
                await Assert.That(dmx).DoesNotContain("materials/dev/gray_50.vmat");
            }
        }

        [Test]
        public async Task MapFilesAndTheirReferencesFollowTheMap()
        {
            var vpkPath = TestFixtures.Path("small_map_with_material.vpk");

            using var package = new Package();
            package.Read(vpkPath);

            using var loader = new GameFileLoader(package, vpkPath);
            using var resource = loader.LoadFile("maps/ui/nametag.vmap_c")!;

            var map = new ContentPathMap();
            map.AddFolder("maps/ui/nametag", "maps/ui/nametag_orig");
            map.Add("materials/cs_italy/ground/tile_floor_diamond_1.vmat", "materials/nametag_orig/tile_floor_diamond_1.vmat");

            using var content = FileExtract.Extract(resource, loader, null, new FileExtractOptions { ReferenceMap = map });
            var vmap = Encoding.UTF8.GetString(content.Data!);

            using (Assert.Multiple())
            {
                await Assert.That(content.FileName).IsEqualTo("maps/ui/nametag_orig_d.vmap");
                await Assert.That(content.AdditionalFiles).IsNotEmpty();
                await Assert.That(content.AdditionalFiles.All(static file => file.FileName.StartsWith("maps/ui/nametag_orig/", StringComparison.Ordinal))).IsTrue();
                await Assert.That(vmap).Contains("materials/nametag_orig/tile_floor_diamond_1.vmat");
                await Assert.That(vmap).DoesNotContain("materials/cs_italy/ground/tile_floor_diamond_1.vmat");
                await Assert.That(vmap).Contains("weapons/models/shared/nametag/nametag_module.vmdl");
            }
        }

        [Test]
        public async Task KeyValuesDumpsMapReferencesWithoutChangingTheResource()
        {
            using var resource = TestFixtures.Load("chen_weapon.vmesh_c");
            resource.FileName = "models/heroes/chen/chen_weapon.vmesh";

            var map = new ContentPathMap();
            map.Add("materials/models/heroes/chen/chen_weapon_color.vmat", "materials/chen_orig/chen_weapon_color.vmat");

            using var mapped = FileExtract.Extract(resource, new NullFileLoader(), null, new FileExtractOptions { ReferenceMap = map });
            using var unmapped = FileExtract.Extract(resource, new NullFileLoader());

            using (Assert.Multiple())
            {
                await Assert.That(Encoding.UTF8.GetString(mapped.Data!)).Contains("m_material = resource:\"materials/chen_orig/chen_weapon_color.vmat\"");
                await Assert.That(Encoding.UTF8.GetString(unmapped.Data!)).Contains("m_material = resource:\"materials/models/heroes/chen/chen_weapon_color.vmat\"");
            }
        }
    }
}
