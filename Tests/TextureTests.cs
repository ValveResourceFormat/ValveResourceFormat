using System.IO;
using NUnit.Framework;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

namespace Tests
{
    public class TextureTests
    {
        [Test]
        public void ExportTextures()
        {
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Textures");
            var files = Directory.GetFiles(path, "*.vtex_c");

            foreach (var file in files)
            {
                var resource = new Resource();
                resource.Read(file);

                using var _ = ((Texture)resource.DataBlock).GenerateBitmap();
            }
        }
    }
}
