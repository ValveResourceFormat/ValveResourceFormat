using System.Linq;
using System.Threading.Tasks;
using ValveResourceFormat.ToolsAssetInfo;

namespace Tests.Formats
{
    public class ToolsAssetInfoTest
    {
        [Test]
        public async Task ParseToolsAssetV16()
        {
            var file = TestFixtures.Path("readonly_tools_asset_info_v16.bin");

            var assetsInfo = new ToolsAssetInfo();
            assetsInfo.Read(file);
            assetsInfo.ToString();

            await Assert.That(assetsInfo.Version).IsEqualTo(16u);

            var map = assetsInfo.Files["maps/cs_script_demo.vmap"];
            var vmap = map.SearchPathsContentRoot.Single();

            await Assert.That(vmap.Filename).IsEqualTo("csgo_addons/cs_script_demo/maps/cs_script_demo.vmap");
            await Assert.That(vmap.HasCRC).IsEqualTo(vmap.FileCRC != 0);
            await Assert.That(map.InputDependencies).Contains(d => d.Filename == "csgo_addons/cs_script_demo/maps/cs_script_demo.vmap"
                && d.FileCRC == 153883759
                && d.Location == ToolsAssetInfo.AssetLocation.Content);
        }

        [Test]
        public async Task ParseToolsAssetV15()
        {
            var file = TestFixtures.Path("readonly_tools_asset_info_v15.bin");

            var assetsInfo = new ToolsAssetInfo();
            assetsInfo.Read(file);
            assetsInfo.ToString();

            await Assert.That(assetsInfo.Files).ContainsKey("maps/content_examples/lighting_info.vmap");
            await Assert.That(assetsInfo.Files).ContainsKey("sounds/interior_01.vsnd");
        }

        [Test]
        public async Task ParseToolsAssetV14()
        {
            var file = TestFixtures.Path("readonly_tools_asset_info_v14.bin");

            var assetsInfo = new ToolsAssetInfo();
            assetsInfo.Read(file);
            assetsInfo.ToString();

            await Assert.That(assetsInfo.Files).ContainsKey("panorama/images/custom_game/button_audio_off_psd.vtex");
            await Assert.That(assetsInfo.Files).ContainsKey("panorama/scripts/custom_game/custom_ui_manifest.vjs");
        }

        [Test]
        public async Task ParseToolsAssetV13()
        {
            var file = TestFixtures.Path("readonly_tools_asset_info_v13.bin");

            var assetsInfo = new ToolsAssetInfo();
            assetsInfo.Read(file);
            assetsInfo.ToString();

            await Assert.That(assetsInfo.Files).ContainsKey("panorama/images/control_icons/double_arrow_left_png.vtex");
            await Assert.That(assetsInfo.Files).ContainsKey("soundevents/creatures/game_sounds_zombie.vsndevts");
        }

        [Test]
        public async Task ParseToolsAssetV12()
        {
            var file = TestFixtures.Path("readonly_tools_asset_info_v12.bin");

            var assetsInfo = new ToolsAssetInfo();
            assetsInfo.Read(file);

            await Assert.That(assetsInfo.Files).ContainsKey("panorama/images/control_icons/double_arrow_left_png.vtex");
            await Assert.That(assetsInfo.Files).ContainsKey("soundevents/creatures/game_sounds_zombie.vsndevts");
        }

        [Test]
        public async Task ParseToolsAssetV11()
        {
            var file = TestFixtures.Path("readonly_tools_asset_info_v11.bin");

            var assetsInfo = new ToolsAssetInfo();
            assetsInfo.Read(file);

            await Assert.That(assetsInfo.Files).ContainsKey("panorama/images/control_icons/double_arrow_left_png.vtex");
            await Assert.That(assetsInfo.Files).ContainsKey("soundevents/creatures/game_sounds_zombie.vsndevts");
        }

        [Test]
        public async Task ParseToolsAssetV10()
        {
            var file = TestFixtures.Path("readonly_tools_asset_info_v10.bin");

            var assetsInfo = new ToolsAssetInfo();
            assetsInfo.Read(file);

            await Assert.That(assetsInfo.Files).ContainsKey("panorama/images/control_icons/double_arrow_left_png.vtex");
            await Assert.That(assetsInfo.Files).ContainsKey("soundevents/creatures/game_sounds_zombie.vsndevts");
        }

        [Test]
        public async Task ParseToolsAssetV9()
        {
            var file = TestFixtures.Path("readonly_tools_asset_info_v9.bin");

            var assetsInfo = new ToolsAssetInfo();
            assetsInfo.Read(file);

            await Assert.That(assetsInfo.Files).ContainsKey("panorama/images/control_icons/double_arrow_left_png.vtex");
            await Assert.That(assetsInfo.Files).ContainsKey("scripts/npc/herolist.txt");
        }

        [Test]
        public async Task ParseToolsAssetV8()
        {
            var file = TestFixtures.Path("readonly_tools_asset_info_v8.bin");

            var assetsInfo = new ToolsAssetInfo();
            assetsInfo.Read(file);
            assetsInfo.ToString();

            await Assert.That(assetsInfo.Version).IsEqualTo(8u);
            await Assert.That(assetsInfo.Files).Count().IsEqualTo(30);

            var texture = assetsInfo.Files["materials/particle/basic_glow.vtex"];

            await Assert.That(texture.SearchPathsGameRoot.Single().Filename).IsEqualTo("dota_addons/addon_template/materials/particle/basic_glow.vtex_c");
            await Assert.That(texture.InputDependencies).Contains(d => d.Filename == "dota_addons/addon_template/materials/particle/basic_glow.tga"
                && d.FileCRC == 2438746482);
            await Assert.That(texture.SpecialDependencies).Contains(d => d.String == "Texture Compiler Version"
                && d.CompilerIdentifier == "CompileTexture");

            var particle = assetsInfo.Files["particles/basic_explosion/basic_explosion.vpcf"];

            await Assert.That(particle.ExternalReferences).Contains("particles/basic_explosion/basic_explosion_flash.vpcf");
        }
    }
}
