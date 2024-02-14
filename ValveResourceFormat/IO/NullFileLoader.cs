using ValveResourceFormat.CompiledShader;

namespace ValveResourceFormat.IO
{
    /// <summary>
    /// This file loader is primarily for testing, always returns null for any file load.
    /// </summary>
    public class NullFileLoader : IFileLoader
    {
        public Resource LoadFile(string file) => null;
        public Resource LoadFileCompiled(string file) => null;
        public ShaderCollection LoadShader(string shaderName) => null;
    }
}
