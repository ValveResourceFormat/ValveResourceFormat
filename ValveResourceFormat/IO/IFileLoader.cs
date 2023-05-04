using ValveResourceFormat.CompiledShader;

namespace ValveResourceFormat.IO
{
    public interface IFileLoader
    {
        public Resource LoadFile(string file);
        public ShaderFile LoadShader(string shaderName);
    }
}
