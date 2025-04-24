using ValveResourceFormat.CompiledShader;

namespace ValveResourceFormat.IO
{
    public interface IFileLoader
    {
        public Resource? LoadFile(string file);
        /// <summary>
        /// Same as <see cref="LoadFile"/> but appends <b>"_c"</b> to the end of the string.
        /// </summary>
        public Resource? LoadFileCompiled(string file);
        public ShaderCollection? LoadShader(string shaderName);
    }
}
