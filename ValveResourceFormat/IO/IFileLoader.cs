using ValveResourceFormat.CompiledShader;

namespace ValveResourceFormat.IO
{
    public interface IFileLoader
    {
        public Resource LoadFile(string file);
        public ShaderCollection LoadShader(string shaderName);
    }
}
