using System.IO;
using System.Threading.Tasks;
using ValveResourceFormat.ToolsAssetInfo;

namespace Tests
{
    public class ToolsAssetInfoTest
    {
        [Test]
        public async Task ParseToolsAssetV15()
        {
            var file = Path.Combine(TestContext.TestDirectory!, "Files", "readonly_tools_asset_info_v15.bin");

            var assetsInfo = new ToolsAssetInfo();
            assetsInfo.Read(file);
            assetsInfo.ToString();

            await Assert.That(assetsInfo.Files).ContainsKey("maps/content_examples/lighting_info.vmap");
            await Assert.That(assetsInfo.Files).ContainsKey("sounds/interior_01.vsnd");
        }

        [Test]
        public async Task ParseToolsAssetV14()
        {
            var file = Path.Combine(TestContext.TestDirectory!, "Files", "readonly_tools_asset_info_v14.bin");

            var assetsInfo = new ToolsAssetInfo();
            assetsInfo.Read(file);
            assetsInfo.ToString();

            await Assert.That(assetsInfo.Files).ContainsKey("panorama/images/custom_game/button_audio_off_psd.vtex");
            await Assert.That(assetsInfo.Files).ContainsKey("panorama/scripts/custom_game/custom_ui_manifest.vjs");
        }

        [Test]
        public async Task ParseToolsAssetV13()
        {
            var file = Path.Combine(TestContext.TestDirectory!, "Files", "readonly_tools_asset_info_v13.bin");

            var assetsInfo = new ToolsAssetInfo();
            assetsInfo.Read(file);
            assetsInfo.ToString();

            await Assert.That(assetsInfo.Files).ContainsKey("panorama/images/control_icons/double_arrow_left_png.vtex");
            await Assert.That(assetsInfo.Files).ContainsKey("soundevents/creatures/game_sounds_zombie.vsndevts");
        }

        [Test]
        public async Task ParseToolsAssetV12()
        {
            var file = Path.Combine(TestContext.TestDirectory!, "Files", "readonly_tools_asset_info_v12.bin");

            var assetsInfo = new ToolsAssetInfo();
            assetsInfo.Read(file);

            await Assert.That(assetsInfo.Files).ContainsKey("panorama/images/control_icons/double_arrow_left_png.vtex");
            await Assert.That(assetsInfo.Files).ContainsKey("soundevents/creatures/game_sounds_zombie.vsndevts");
        }

        [Test]
        public async Task ParseToolsAssetV11()
        {
            var file = Path.Combine(TestContext.TestDirectory!, "Files", "readonly_tools_asset_info_v11.bin");

            var assetsInfo = new ToolsAssetInfo();
            assetsInfo.Read(file);

            await Assert.That(assetsInfo.Files).ContainsKey("panorama/images/control_icons/double_arrow_left_png.vtex");
            await Assert.That(assetsInfo.Files).ContainsKey("soundevents/creatures/game_sounds_zombie.vsndevts");
        }

        [Test]
        public async Task ParseToolsAssetV10()
        {
            var file = Path.Combine(TestContext.TestDirectory!, "Files", "readonly_tools_asset_info_v10.bin");

            var assetsInfo = new ToolsAssetInfo();
            assetsInfo.Read(file);

            await Assert.That(assetsInfo.Files).ContainsKey("panorama/images/control_icons/double_arrow_left_png.vtex");
            await Assert.That(assetsInfo.Files).ContainsKey("soundevents/creatures/game_sounds_zombie.vsndevts");
        }

        [Test]
        public async Task ParseToolsAssetV9()
        {
            var file = Path.Combine(TestContext.TestDirectory!, "Files", "readonly_tools_asset_info_v9.bin");

            var assetsInfo = new ToolsAssetInfo();
            assetsInfo.Read(file);

            await Assert.That(assetsInfo.Files).ContainsKey("panorama/images/control_icons/double_arrow_left_png.vtex");
            await Assert.That(assetsInfo.Files).ContainsKey("scripts/npc/herolist.txt");
        }
    }
}
