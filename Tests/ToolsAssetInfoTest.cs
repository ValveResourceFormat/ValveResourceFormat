using System;
using System.IO;
using NUnit.Framework;
using ValveResourceFormat.ToolsAssetInfo;

namespace Tests
{
    public class ToolsAssetInfoTest
    {
        [Test]
        public void ParseToolsAsset()
        {
            var file = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "readonly_tools_asset_info.bin");

            var assetsInfo = new ToolsAssetInfo();
            assetsInfo.Read(file);

            Assert.That(assetsInfo.Mods.Count, Is.EqualTo(1));
            Assert.That(assetsInfo.Directories.Count, Is.EqualTo(12));
            Assert.That(assetsInfo.Filenames.Count, Is.EqualTo(32));
            Assert.That(assetsInfo.Extensions.Count, Is.EqualTo(9));
            Assert.That(assetsInfo.EditInfoKeys.Count, Is.EqualTo(1));
            Assert.That(assetsInfo.MiscStrings.Count, Is.EqualTo(6));
            Assert.That(assetsInfo.ConstructedFilepaths.Count, Is.EqualTo(37));

            Assert.That(assetsInfo.ConstructedFilepaths[0], Is.EqualTo("addoninfo.txt"));
            Assert.That(assetsInfo.ConstructedFilepaths[36], Is.EqualTo("particles/basic_rope/basic_rope_readme.txt"));
        }
    }
}
