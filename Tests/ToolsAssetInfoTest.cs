using System.IO;
using NUnit.Framework;
using ValveResourceFormat.ToolsAssetInfo;

namespace Tests
{
    public class ToolsAssetInfoTest
    {
        // Using "game/dota_addons/rpg_example/readonly_tools_asset_info.bin" from Dota 2's depot 373301 for all the versions

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
