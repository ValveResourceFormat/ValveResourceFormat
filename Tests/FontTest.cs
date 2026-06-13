using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using ValveResourceFormat.ValveFont;

namespace Tests
{
    public class FontTest
    {
        [Test]
        public void DecryptFonts()
        {
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Fonts");
            var files = Directory.GetFiles(path, "*.vfont");

            foreach (var file in files)
            {
                var font = new ValveFont();
                var decryptedFont = font.Read(file);
                var expected = File.ReadAllBytes(Path.ChangeExtension(file, "ttf"));

                Assert.That(decryptedFont, Is.EqualTo(expected));
            }
        }

        [Test]
        public void DecryptUIFonts()
        {
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Fonts", "broadcast.uifont");

            var fontPackage = new UIFontFilePackage();
            fontPackage.Read(path);

            Assert.That(fontPackage.FontFiles, Has.Count.EqualTo(1));
            Assert.That(fontPackage.FontFiles[0].FileName, Is.EqualTo("broadcast.otf"));

            var actualHash = Convert.ToHexString(SHA256.HashData(fontPackage.FontFiles[0].OpenTypeFontData));
            Assert.That(actualHash, Is.EqualTo("E67DDF8C385E538B5CC80DFC0E7AC15B1BEE2C59280A626321C5F8BAE467CEC0"));
        }
    }
}
