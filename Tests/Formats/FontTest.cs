using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveResourceFormat.IO;
using ValveResourceFormat.ValveFont;

namespace Tests.Formats
{
    public class FontTest
    {
        [Test]
        public async Task DecryptFonts()
        {
            var path = TestFixtures.Path("Fonts");
            var files = Directory.GetFiles(path, "*.vfont");

            foreach (var file in files)
            {
                var font = new ValveFont();
                var decryptedFont = font.Read(file);
                var expected = await File.ReadAllBytesAsync(Path.ChangeExtension(file, "ttf"));

                await Assert.That(decryptedFont).IsEquivalentTo(expected, CollectionOrdering.Matching);
            }
        }

        [Test]
        public async Task DecryptUIFonts()
        {
            var path = TestFixtures.Path("Fonts", "broadcast.uifont");

            var fontPackage = new UIFontFilePackage();
            fontPackage.Read(path);

            await Assert.That(fontPackage.FontFiles).Count().IsEqualTo(1);
            await Assert.That(fontPackage.FontFiles[0].FileName).IsEqualTo("broadcast.otf");

            var actualHash = Convert.ToHexString(SHA256.HashData(fontPackage.FontFiles[0].OpenTypeFontData));
            await Assert.That(actualHash).IsEqualTo("E67DDF8C385E538B5CC80DFC0E7AC15B1BEE2C59280A626321C5F8BAE467CEC0");
        }

        [Test]
        public async Task ExtractFontsFromStreams()
        {
            // Files inside of packages are only available as streams, which the caller still owns afterwards
            var vfontPath = TestFixtures.Path("Fonts", "hl2crosshairs.vfont");
            using var vfontStream = new MemoryStream(await File.ReadAllBytesAsync(vfontPath));
            using var vfont = FileExtract.ExtractNonResource(vfontStream, "resource/hl2crosshairs.vfont");

            var uifontPath = TestFixtures.Path("Fonts", "broadcast.uifont");
            using var uifontStream = new MemoryStream(await File.ReadAllBytesAsync(uifontPath));
            using var uifont = FileExtract.ExtractNonResource(uifontStream, "panorama/fonts/broadcast.uifont");

            using (Assert.Multiple())
            {
                await Assert.That(vfont!.FileName).IsEqualTo("resource/hl2crosshairs.ttf");
                await Assert.That(vfont.Data).IsEquivalentTo(await File.ReadAllBytesAsync(Path.ChangeExtension(vfontPath, "ttf")), CollectionOrdering.Matching);
                await Assert.That(vfontStream.CanRead).IsTrue();

                await Assert.That(uifont!.FileName).IsEqualTo("panorama/fonts/broadcast.otf");
                await Assert.That(Convert.ToHexString(SHA256.HashData(uifont.Data!))).IsEqualTo("E67DDF8C385E538B5CC80DFC0E7AC15B1BEE2C59280A626321C5F8BAE467CEC0");
            }
        }
    }
}
