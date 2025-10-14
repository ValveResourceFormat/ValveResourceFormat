using ValveResourceFormat.CompiledShader;

namespace ValveResourceFormat.IO
{
    /// <summary>
    /// Interface for loading game resource files.
    /// </summary>
    public interface IFileLoader
    {
        /// <summary>
        /// Loads a resource file.
        /// </summary>
        /// <param name="file">Path to the file to load.</param>
        /// <returns>Loaded resource, or null if not found.</returns>
        public Resource? LoadFile(string file);
        /// <summary>
        /// Same as <see cref="LoadFile"/> but appends <b>"_c"</b> to the end of the string.
        /// </summary>
        /// <param name="file">Path to the file to load (without _c suffix).</param>
        /// <returns>Loaded compiled resource, or null if not found.</returns>
        public Resource? LoadFileCompiled(string file);

        /// <summary>
        /// Loads a shader collection by name.
        /// </summary>
        /// <param name="shaderName">Name of the shader to load.</param>
        /// <returns>Loaded shader collection, or null if not found.</returns>
        public ShaderCollection? LoadShader(string shaderName);
    }
}
