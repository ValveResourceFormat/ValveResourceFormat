using System;
using System.Drawing.Imaging;
using System.IO;
using NUnit.Framework;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

namespace Tests
{
    public class TextureTests
    {
        [Test]
        [Ignore("Need a better way of testing images rather than comparing the files directly")]
        public void Test()
        {
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Textures");
            var files = Directory.GetFiles(path, "*.vtex_c");

            foreach (var file in files)
            {
                var resource = new Resource();
                resource.Read(file);

                var bitmap = ((Texture)resource.Blocks[BlockType.DATA]).GenerateBitmap();

                using (var ms = new MemoryStream())
                {
                    bitmap.Save(ms, ImageFormat.Png);

                    using (var expected = new FileStream(Path.ChangeExtension(file, "png"), FileMode.Open, FileAccess.Read))
                    {
                        FileAssert.AreEqual(expected, ms);
                    }
                }
            }
        }
    }
}
