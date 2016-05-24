using System;
using System.IO;
using NUnit.Framework;
using ValveResourceFormat;

namespace Tests
{
    public class FontTest
    {
        [Test]
        public void Test()
        {
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Fonts");
            var files = Directory.GetFiles(path, "*.vfont");

            foreach (var file in files)
            {
                var shader = new ValveFont();
                var decryptedFont = shader.Read(file);

                using (var decryptedStream = new MemoryStream(decryptedFont))
                using (var expected = new FileStream(Path.ChangeExtension(file, "ttf"), FileMode.Open, FileAccess.Read))
                {
                    FileAssert.AreEqual(expected, decryptedStream);
                }
            }
        }
    }
}
