using System.IO;
using NUnit.Framework;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.TextureDecoders;

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

            var texture = (Texture)resource.DataBlock;

            using var _ = texture.GenerateBitmap();

            if (texture.IsHighDynamicRange)
            {
                using var __ = texture.GenerateBitmap(decodeFlags: TextureCodec.ForceLDR);
            }
        }
    }
}
