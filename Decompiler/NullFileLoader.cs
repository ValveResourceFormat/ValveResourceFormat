using ValveResourceFormat;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.IO;

namespace Decompiler
{
    public class NullFileLoader : IFileLoader
    {
        public Resource LoadFile(string file) => null;
        public ShaderCollection LoadShader(string shaderName) => null;
    }
}
