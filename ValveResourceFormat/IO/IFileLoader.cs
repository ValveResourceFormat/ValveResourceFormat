using ValveResourceFormat.CompiledShader;

namespace ValveResourceFormat.IO
{
    /// <summary>
    /// Interface for loading compiled game resources.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This interface is intended for loading compiled resources (such as models, textures, materials)
    /// that need to be parsed as <see cref="Resource"/> objects. Implementations handle resource lookup
    /// across VPK packages and loose files.
    /// </para>
    /// </remarks>
    public interface IFileLoader
    {
        /// <summary>
        /// Loads a compiled resource file.
        /// </summary>
        /// <remarks>
        /// To read raw file bytes from a VPK package, use <c>Package.ReadEntry</c> instead.
        /// </remarks>
        /// <param name="file">Path to the resource file to load.</param>
        /// <returns>Loaded resource, or <c>null</c> if not found.</returns>
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

        /// <summary>
        /// Same as <see cref="LoadFileCompiled"/> but parses only the blocks selected by
        /// <paramref name="options"/>; the remaining blocks parse on demand while the resource
        /// stays undisposed. The returned resource is never cached - the caller owns and disposes it.
        /// </summary>
        /// <param name="file">Path to the file to load (without the _c suffix).</param>
        /// <param name="options">Options selecting which blocks to parse.</param>
        /// <returns>Loaded compiled resource, or null if not found.</returns>
        public Resource? LoadFilePartial(string file, ResourceReadOptions options);
    }
}
