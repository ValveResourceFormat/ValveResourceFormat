using System.IO;
using System.Threading.Tasks;
using GUI.Utils;

namespace GUI.Types.Viewers
{
    class SpirvBinary(VrfGuiContext vrfGuiContext) : IViewer
    {
        private string code = string.Empty;

        public static bool IsAccepted(uint magic)
        {
            // July 23, 2003, which is the date the OpenGL 2.0 specification was approved by the Khronos Group.
            return magic == 0x07230203u;
        }

        public static bool IsAccepted(uint magic, string fileName)
        {
            return IsAccepted(magic) || fileName.EndsWith(".spv", StringComparison.OrdinalIgnoreCase);
        }

        public async Task LoadAsync(Stream? stream)
        {
            byte[] input;

            if (stream == null)
            {
                input = await File.ReadAllBytesAsync(vrfGuiContext.FileName!).ConfigureAwait(false);
            }
            else
            {
                input = new byte[stream.Length];
                stream.ReadExactly(input);
            }

            var shaderFileVulkan = new ValveResourceFormat.CompiledShader.VfxShaderFileVulkan(input);
            code = shaderFileVulkan.GetDecompiledFile();
        }

        public ViewerContent GetContent()
        {
            return new ViewerContent.Tabs(
            [
                new("SPIR-V Cross", new ViewerContent.Text(code, HighlightLanguage.Shaders)),
            ]);
        }

        public void Dispose()
        {
            code = string.Empty;
        }
    }
}
