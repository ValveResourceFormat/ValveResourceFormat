using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;

namespace ValveResourceFormat.Renderer.Shaders
{
    /// <summary>
    /// Registry of user provided shader directories and shader name mappings.
    ///
    /// <para>
    /// Directories added here are mounted in front of the shaders shipped with the renderer, similar to how search paths
    /// work in <see cref="ValveResourceFormat.IO.GameFileLoader"/>. Any file placed in a mounted directory overrides the
    /// built-in file with the same relative path, which applies to shader entry points (<c>complex.vert.slang</c>) as well
    /// as anything pulled in with <c>#include</c> (<c>common/lighting.slang</c>).
    /// </para>
    ///
    /// <para>
    /// Mappings translate a Source 2 shader name (<c>shader.vfx</c>) to the renderer shader file that will be used to draw
    /// it, taking priority over the built-in mapping table.
    /// </para>
    ///
    /// <para>
    /// Configure this before creating a <see cref="RendererContext"/>. Changing the registry drops the parsed shader cache,
    /// but shader programs that have already been compiled by an existing loader keep running the old code.
    /// </para>
    /// </summary>
    public static class ShaderRegistry
    {
        private static readonly Lock RegistryLock = new();
        private static ImmutableArray<string> directories = [];
        private static ImmutableDictionary<string, string> mappings = ImmutableDictionary.Create<string, string>(StringComparer.Ordinal);

        /// <summary>Gets the mounted shader directories, highest priority first.</summary>
        public static ImmutableArray<string> Directories => directories;

        /// <summary>Gets the registered shader name mappings, keyed by the Source 2 shader name (e.g. <c>shader.vfx</c>).</summary>
        public static ImmutableDictionary<string, string> Mappings => mappings;

        /// <summary>
        /// Mounts a directory containing shader files. The most recently added directory has the highest priority, and all
        /// mounted directories take priority over the shaders shipped with the renderer.
        /// </summary>
        /// <param name="path">Path to the directory that mirrors the layout of the built-in <c>Renderer/Shaders</c> folder.</param>
        /// <returns><see langword="true"/> if the directory was added, <see langword="false"/> if it was already mounted.</returns>
        /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
        public static bool AddShaderDirectory(string path)
        {
            ArgumentException.ThrowIfNullOrEmpty(path);

            var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

            if (!Directory.Exists(fullPath))
            {
                throw new DirectoryNotFoundException($"Shader directory '{fullPath}' does not exist.");
            }

            using (RegistryLock.EnterScope())
            {
                if (directories.Any(dir => string.Equals(dir, fullPath, StringComparison.Ordinal)))
                {
                    return false;
                }

                directories = directories.Insert(0, fullPath);
            }

            ShaderLoader.InvalidateParsedShaders();
            return true;
        }

        /// <summary>
        /// Maps a Source 2 shader name to the renderer shader file used to draw it. Overrides the built-in mapping when the
        /// same name is already known to the renderer.
        /// </summary>
        /// <param name="shaderName">The Source 2 shader name, including the extension (e.g. <c>shader.vfx</c>).</param>
        /// <param name="shaderFileName">The renderer shader name without stage or extension (e.g. <c>my_shader</c> for <c>my_shader.vert.slang</c>).</param>
        public static void AddShaderMapping(string shaderName, string shaderFileName)
        {
            ArgumentException.ThrowIfNullOrEmpty(shaderName);
            ArgumentException.ThrowIfNullOrEmpty(shaderFileName);

            using (RegistryLock.EnterScope())
            {
                mappings = mappings.SetItem(shaderName, shaderFileName);
            }

            ShaderLoader.InvalidateParsedShaders();
        }

        /// <summary>Removes all mounted directories and registered mappings.</summary>
        public static void Reset()
        {
            using (RegistryLock.EnterScope())
            {
                directories = [];
                mappings = mappings.Clear();
            }

            ShaderLoader.InvalidateParsedShaders();
        }

        /// <summary>
        /// Opens a shader file from the mounted directories, or returns <see langword="null"/> when no mounted directory
        /// provides it and the built-in shader should be used instead.
        /// </summary>
        /// <param name="name">Root relative shader file name using forward slashes (e.g. <c>common/lighting.slang</c>).</param>
        internal static Stream? TryOpenShaderFile(string name)
        {
            var mounted = directories;

            if (mounted.IsEmpty)
            {
                return null;
            }

            var relativePath = name.Replace('/', Path.DirectorySeparatorChar);

            foreach (var directory in mounted)
            {
                var path = Path.Combine(directory, relativePath);

                if (!File.Exists(path))
                {
                    continue;
                }

                return File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            }

            return null;
        }

        /// <summary>Enumerates the shader entry point files in the mounted directories.</summary>
        /// <param name="searchPattern">The file search pattern to match against file names.</param>
        internal static IEnumerable<string> EnumerateShaderFiles(string searchPattern)
        {
            foreach (var directory in directories)
            {
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(directory, searchPattern, SearchOption.TopDirectoryOnly))
                {
                    yield return file;
                }
            }
        }
    }
}
