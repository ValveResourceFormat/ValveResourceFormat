using System.IO;
using NUnit.Framework;
using ValveResourceFormat.ToolsAssetInfo;

namespace Tests
{
    public class ToolsAssetInfoTest
    {
        [Test]
        public void ParseToolsAssetV14()
        {
            var file = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "readonly_tools_asset_info_v14.bin");

            var assetsInfo = new ToolsAssetInfo();
            assetsInfo.Read(file);
            assetsInfo.ToString();

            Assert.That(assetsInfo.Files, Contains.Key("panorama/images/custom_game/button_audio_off_psd.vtex"));
            Assert.That(assetsInfo.Files, Contains.Key("panorama/scripts/custom_game/custom_ui_manifest.vjs"));
        }

        [Test]
        public void ParseToolsAssetV13()
        {
            var file = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "readonly_tools_asset_info_v13.bin");

            var assetsInfo = new ToolsAssetInfo();
            assetsInfo.Read(file);
            assetsInfo.ToString();

            Assert.That(assetsInfo.Files, Contains.Key("panorama/images/control_icons/double_arrow_left_png.vtex"));
            Assert.That(assetsInfo.Files, Contains.Key("soundevents/creatures/game_sounds_zombie.vsndevts"));
        }

        [Test]
        public void ParseToolsAssetV12()
        {
            var file = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "readonly_tools_asset_info_v12.bin");

            var assetsInfo = new ToolsAssetInfo();
            assetsInfo.Read(file);

            Assert.That(assetsInfo.Files, Contains.Key("panorama/images/control_icons/double_arrow_left_png.vtex"));
            Assert.That(assetsInfo.Files, Contains.Key("soundevents/creatures/game_sounds_zombie.vsndevts"));
        }

        [Test]
        public void ParseToolsAssetV11()
        {
            var file = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "readonly_tools_asset_info_v11.bin");

            var assetsInfo = new ToolsAssetInfo();
            assetsInfo.Read(file);

            Assert.That(assetsInfo.Files, Contains.Key("panorama/images/control_icons/double_arrow_left_png.vtex"));
            Assert.That(assetsInfo.Files, Contains.Key("soundevents/creatures/game_sounds_zombie.vsndevts"));
        }

        [Test]
        public void ParseToolsAssetV10()
        {
            var file = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "readonly_tools_asset_info_v10.bin");

            var assetsInfo = new ToolsAssetInfo();
            assetsInfo.Read(file);

            Assert.That(assetsInfo.Files, Contains.Key("panorama/images/control_icons/double_arrow_left_png.vtex"));
            Assert.That(assetsInfo.Files, Contains.Key("soundevents/creatures/game_sounds_zombie.vsndevts"));
        }

        [Test]
        public void ParseToolsAssetV9()
        {
            var file = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "readonly_tools_asset_info_v9.bin");

            var assetsInfo = new ToolsAssetInfo();
            assetsInfo.Read(file);

            Assert.That(assetsInfo.Files, Contains.Key("panorama/images/control_icons/double_arrow_left_png.vtex"));
            Assert.That(assetsInfo.Files, Contains.Key("scripts/npc/herolist.txt"));
        }
    }
}
