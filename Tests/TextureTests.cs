using System.IO;
using NUnit.Framework;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

namespace Tests
{
    public class TextureTests
    {
        private static string[] GetTextureFiles()
        {
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "Textures");
            return Directory.GetFiles(path, "*.vtex_c");
        }

        [Test, TestCaseSource(nameof(GetTextureFiles))]
        public void ExportTexture(string file)
        {
            using var resource = new Resource();
            resource.Read(file);

            using var _ = ((Texture)resource.DataBlock).GenerateBitmap();

        }
    }
}
