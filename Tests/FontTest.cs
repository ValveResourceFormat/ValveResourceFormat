using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveResourceFormat.ValveFont;

namespace Tests
{
    public class FontTest
    {
        [Test]
        public async Task DecryptFonts()
        {
            var path = Path.Combine(TestContext.TestDirectory!, "Files", "Fonts");
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
            var path = Path.Combine(TestContext.TestDirectory!, "Files", "Fonts", "broadcast.uifont");

            var fontPackage = new UIFontFilePackage();
            fontPackage.Read(path);

            await Assert.That(fontPackage.FontFiles).Count().IsEqualTo(1);
            await Assert.That(fontPackage.FontFiles[0].FileName).IsEqualTo("broadcast.otf");

            var actualHash = Convert.ToHexString(SHA256.HashData(fontPackage.FontFiles[0].OpenTypeFontData));
            await Assert.That(actualHash).IsEqualTo("E67DDF8C385E538B5CC80DFC0E7AC15B1BEE2C59280A626321C5F8BAE467CEC0");
        }
    }
}
