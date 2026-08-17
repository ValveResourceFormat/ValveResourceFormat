using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using static ValveResourceFormat.Renderer.Shaders.ShaderLoader;

namespace ValveResourceFormat.Renderer.Shaders
{
    /// <summary>
    /// Preprocesses shader source files and extracts defines and render modes.
    /// </summary>
    public partial class ShaderParser
    {
        /// <summary>The embedded-resource namespace prefix used to locate shader files in the assembly manifest.</summary>
        public const string ShaderDirectory = "Renderer.Shaders.";

        /// <summary>The GLSL version directive that must appear as the first line of every shader file.</summary>
        public const string ExpectedShaderVersion = "#version 460";
        private const string RenderModeDefinePrefix = "renderMode_";

        [GeneratedRegex("^#include \"(?<IncludeName>[^\"]+)\"")]
        private static partial Regex RegexInclude();
        [GeneratedRegex("^#define (?<ParamName>(?:renderMode|GameVfx|F|S|D)_\\S+) (?<DefaultValue>[0-9]+)")]
        private static partial Regex RegexDefine();

        // regex that detects
        // uniform sampler{dim} x;
        // uniform sampler{dim} a; // SrgbRead(true)
        // uniform vec{dim} b = vec3(1.0); // SrgbRead(true)
        // uniform sampler{dim} c; // SrgbRead(true) Sampler(UserConfig)
        // uniform sampler{dim} d; // Sampler(UserConfig)
        // layout(binding = n) uniform sampler{dim} e;
        // uniform layout(binding = n) sampler{dim} f;
        [GeneratedRegex("^(?:layout\\s*\\([^)]*\\)\\s*)?uniform (?:layout\\s*\\([^)]*\\)\\s*)?(?<Type>\\S+) (?<Name>[A-Za-z_][A-Za-z0-9_]*)(?<Array>\\[[^\\]]*\\])?(?:\\s*=\\s*(?<Default>[^;]+?))?\\s*;[ \t]*(?:// )?(?<SrgbRead>SrgbRead\\(true\\))?[ \t]*(?<SamplerUserConfig>Sampler\\(UserConfig\\))?")]
        private static partial Regex RegexUniform();

        [GeneratedRegex("^#extension .+$")]
        private static partial Regex RegexExtension();

        [GeneratedRegex("^#define (?<From>(?:g|F)_[A-Za-z0-9_]+) (?<To>[A-Za-z_][A-Za-z0-9_]*)$")]
        private static partial Regex RegexUniformAlias();

        // An attribute declaration of a vertex shader, for example "in vec4 vCOLOR;". Multiline, because the
        // locations are stamped over the whole assembled source rather than line by line.
        [GeneratedRegex(@"^in\s+[a-z0-9]+\s+(?<Name>[A-Za-z_][A-Za-z0-9_]*)\s*;", RegexOptions.Multiline)]
        private static partial Regex RegexVertexAttribute();

        // A declaration that places itself, which the allocation has to work around rather than stamp
        [GeneratedRegex(@"^layout\s*\(\s*location\s*=\s*(?<Location>[0-9]+)\s*\)\s*in\s+[a-z0-9]+\s+(?<Name>[A-Za-z_][A-Za-z0-9_]*)\s*;", RegexOptions.Multiline)]
        private static partial Regex RegexLocatedVertexAttribute();

        /// <summary>
        /// Writes each attribute's location into its declaration, once every declaration in the shader is
        /// known. A custom attribute takes a slot this set leaves free, so a vertex struct declaring the same
        /// set reaches the same numbers without either side naming one.
        /// </summary>
        private static string StampAttributeLocations(string source, HashSet<string> declaredAttributes)
        {
            var pinned = RegexLocatedVertexAttribute().Matches(source)
                .ToDictionary(match => match.Groups["Name"].Value, match => int.Parse(match.Groups["Location"].Value, CultureInfo.InvariantCulture), StringComparer.Ordinal);

            var locations = VertexAttributeLocations.Allocate(declaredAttributes.Concat(pinned.Keys), pinned);

            return RegexVertexAttribute().Replace(source, match =>
                $"layout (location = {locations[match.Groups["Name"].Value].ToString(CultureInfo.InvariantCulture)}) {match.Value}");
        }

        private static bool IsPackableUniformName(string name)
            => name.StartsWith("g_", StringComparison.Ordinal) || name.StartsWith("F_", StringComparison.Ordinal);

        private readonly StringBuilder builder = new(1024);

        /// <summary>Clears the internal <see cref="StringBuilder"/> so it is ready for the next preprocessing pass.</summary>
        public void ClearBuilder()
        {
            builder.Clear();
        }

        /// <summary>Reads and preprocesses a shader source file, resolving includes and extracting defines, render modes, and uniform names into <paramref name="parsedData"/>.</summary>
        /// <param name="shaderFile">The shader file path or embedded resource name (e.g. <c>complex.vert.slang</c>).</param>
        /// <param name="parsedData">The data container that accumulates extracted metadata across all stages.</param>
        /// <returns>The preprocessed GLSL source text ready for driver compilation.</returns>
        public string PreprocessShader(string shaderFile, ParsedShaderData parsedData)
        {
            var sourceFileNumber = parsedData.SourceFiles.Count;
            var resolvedIncludes = new HashSet<string>(4);
            var isVertexStage = GetTypeFromFileName(shaderFile) == ShaderProgramType.Vertex;
            var declaredAttributes = new HashSet<string>(StringComparer.Ordinal);

            var uniformAliases = new Dictionary<string, string>(0);

            void AppendLineNumber(int a, int b)
            {
                builder.Append("#line ");
                builder.Append(a.ToString(CultureInfo.InvariantCulture));
                builder.Append(' ');
                builder.Append(b.ToString(CultureInfo.InvariantCulture));
                builder.Append('\n');
            }

            // simulate first time compile
            // builder.Append($"// {Guid.CreateVersion7()}");

            void LoadShaderString(string shaderFileToLoad, string? parentFile, bool isInclude)
            {
                if (parentFile != null)
                {
                    var folder = Path.GetDirectoryName(parentFile);

                    if (!string.IsNullOrEmpty(folder))
                    {
                        shaderFileToLoad = Path.Combine(folder, shaderFileToLoad);
                    }

                    var constPath = AppContext.BaseDirectory;
                    shaderFileToLoad = Path.GetFullPath(shaderFileToLoad, constPath);
                    shaderFileToLoad = Path.GetRelativePath(constPath, shaderFileToLoad);
                    shaderFileToLoad = shaderFileToLoad.Replace(Path.DirectorySeparatorChar, '/');

                    if (!resolvedIncludes.Add(shaderFileToLoad))
                    {
                        return;
                    }
                }

                using var stream = GetShaderStream(shaderFileToLoad);
                using var reader = new StreamReader(stream);
                string? line;
                var lineNum = 1;
                var currentSourceFileNumber = sourceFileNumber++;
                parsedData.SourceFiles.Add(shaderFileToLoad);

#if DEBUG
                var currentSourceLines = new List<string>();
                parsedData.SourceFileLines.Add(currentSourceLines);
#endif

                builder.EnsureCapacity(builder.Length + (int)stream.Length);

                while ((line = reader.ReadLine()) != null)
                {
                    lineNum++;

#if DEBUG
                    if (!line.All(static c => char.IsAscii(c)))
                    {
                        // At least on nvidia, trying to compile GLSL with non ascii characters will throw bizarre errors like
                        // wrong #line source-line error, or EOF.
                        throw new ShaderCompilerException($"Line {lineNum} in '{shaderFileToLoad}' contains non-ASCII characters.");
                    }
#endif

                    if (lineNum == 2)
                    {
                        if (line != ExpectedShaderVersion)
                        {
                            throw new ShaderCompilerException($"First line must be '{ExpectedShaderVersion}' in '{shaderFileToLoad}'");
                        }

#if DEBUG
                        currentSourceLines.Add($"// :VrfPreprocessed {line}");
#endif

                        builder.Append('\n');

                        AppendLineNumber(lineNum, currentSourceFileNumber);

                        // We add #version even in includes so that they can be compiled individually for better editing experience
                        // Skip #version for main shader - will be prepended in header
                        continue;
                    }

#if DEBUG
                    currentSourceLines.Add(line);
#endif

                    {
                        line = line.Trim(); // we will be outputting trimmed lines to compile too

                        // Includes
                        var match = RegexInclude().Match(line);

                        if (match.Success)
                        {
                            // Recursively append included shaders

                            var includeName = match.Groups["IncludeName"].Value;

                            AppendLineNumber(1, sourceFileNumber);
                            LoadShaderString(includeName, shaderFileToLoad, isInclude: true);
                            AppendLineNumber(lineNum, currentSourceFileNumber);

                            continue;
                        }

                        // Defines
                        match = RegexDefine().Match(line);

                        if (match.Success)
                        {
                            var defineName = match.Groups["ParamName"].Value;
                            var defaultValueStr = match.Groups["DefaultValue"].Value;
                            var value = byte.Parse(defaultValueStr, CultureInfo.InvariantCulture);

                            if (defineName.StartsWith(RenderModeDefinePrefix, StringComparison.Ordinal))
                            {
                                var renderMode = defineName[RenderModeDefinePrefix.Length..];

                                parsedData.RenderModes.Add(renderMode);

                                value = RenderModes.GetShaderId(renderMode);

                                if (value == 0)
                                {
                                    var renderModeObj = new RenderModes.RenderMode(renderMode);
                                    var index = RenderModes.Items.IndexOf(renderModeObj);

                                    if (index == -1)
                                    {
                                        Debug.Assert(false); // Add to <see cref="RenderModes.Items"/> if this assert is hit

                                        RenderModes.Items = RenderModes.Items.Add(renderModeObj);
                                        index = RenderModes.Items.IndexOf(renderModeObj);
                                    }

                                    value = (byte)index;
                                    RenderModes.AddShaderId(renderMode, value);
                                }

                                builder.Append("#define ");
                                builder.Append(defineName);
                                builder.Append(' ');
                                builder.Append(value.ToString(CultureInfo.InvariantCulture));
                                builder.Append(" // :VrfPreprocessed\n");

                                continue;
                            }

                            // Defines are removed from source code and will be prepended later
                            if (!parsedData.Defines.TryAdd(defineName, value))
                            {
                                // Defines can be shared between vert and frag
                                if (parsedData.Defines[defineName] != value)
                                {
                                    throw new ShaderCompilerException($"Line {lineNum} in '{shaderFileToLoad}' contains a duplicate define '{defineName}' with different default value");
                                }
                            }

                            continue;
                        }

                        match = RegexExtension().Match(line);

                        if (match.Success)
                        {
                            parsedData.Extensions.Add(line);

                            builder.Append("// :VrfHoisted ");
                            builder.Append(line);
                            builder.Append('\n');
                            continue;
                        }

                        match = RegexUniformAlias().Match(line);

                        if (match.Success)
                        {
                            uniformAliases[match.Groups["From"].Value] = match.Groups["To"].Value;
                        }

                        // sRGB uniforms or samplers
                        match = RegexUniform().Match(line);
                        if (match.Success)
                        {
                            var uniformType = match.Groups["Type"].Value;
                            var uniformName = match.Groups["Name"].Value;

                            if (uniformAliases.TryGetValue(uniformName, out var aliasedName))
                            {
                                uniformName = aliasedName;
                            }

                            parsedData.Uniforms.Add(uniformName);

                            if (uniformType.StartsWith("sampler", StringComparison.Ordinal) && MaterialLoader.IsReservedTexture(uniformName))
                            {
                                parsedData.ReservedTextures.Add(uniformName);
                            }

                            if (match.Groups["SrgbRead"].Success)
                            {
                                parsedData.SrgbUniforms.Add(uniformName);
                            }
                            if (match.Groups["SamplerUserConfig"].Success)
                            {
                                parsedData.SamplerUserConfigUniforms.Add(uniformName);
                            }

                            if (!match.Groups["Array"].Success
                            && IsPackableUniformName(uniformName)
                            && GlobalsLayout.TryGetType(uniformType, out var constantType))
                            {
                                var defaultGroup = match.Groups["Default"];

                                parsedData.GlobalsDeclarations.Add(new GlobalsDeclaration(uniformName, constantType,
                                    defaultGroup.Success ? defaultGroup.Value : null, match.Groups["SrgbRead"].Success));

                                builder.Append("// :VrfPacked ");
                                builder.Append(line);
                                builder.Append('\n');
                                continue;
                            }
                        }

                        // Collected now, located once the whole declaring set is known
                        if (isVertexStage)
                        {
                            match = RegexVertexAttribute().Match(line);

                            if (match.Success)
                            {
                                declaredAttributes.Add(match.Groups["Name"].Value);
                            }
                        }
                    }

                    builder.Append(line);
                    builder.Append('\n');

                    if (line.Contains("#endif", StringComparison.Ordinal))
                    {
                        // Fix an issue where #include is inside of an #if, which messes up line numbers
                        AppendLineNumber(lineNum, currentSourceFileNumber);
                    }
                }
            }

            LoadShaderString(shaderFile, null, isInclude: false);

            return declaredAttributes.Count > 0 ? StampAttributeLocations(builder.ToString(), declaredAttributes) : builder.ToString();
        }

        internal static readonly Dictionary<ShaderProgramType, string> ProgramTypeToExtension = new()
        {
            { ShaderProgramType.Vertex, "vert" },
            { ShaderProgramType.Fragment, "frag" },
            { ShaderProgramType.Compute, "comp" }
        };

        internal static readonly Dictionary<string, ShaderProgramType> ExtensionToProgramType = ProgramTypeToExtension
            .ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.Ordinal);

        private static ShaderProgramType GetTypeFromFileName(string fileName)
        {
            var ext = fileName[^(ShaderFileExtension.Length - 1)..^SlangExtension.Length];
            return ExtensionToProgramType.GetValueOrDefault(ext, ShaderProgramType.Max);
        }

        /// <summary>Gets the map from shader base names to a bool array indicating which pipeline stages (vertex, fragment, compute) exist in a mounted shader directory, on disk, or in the assembly manifest.</summary>
        public Dictionary<string, bool[]> AvailableShaders { get; private set; }

        /// <summary>Initializes a new instance of the <see cref="ShaderParser"/> class and discovers all available shader files.</summary>
        public ShaderParser()
        {
            AvailableShaders = GetAvailableShaders();
        }

        /// <summary>Rediscovers the available shader files, picking up changes to <see cref="ShaderRegistry"/>.</summary>
        internal void RefreshAvailableShaders()
        {
            AvailableShaders = GetAvailableShaders();
        }

        private static Dictionary<string, bool[]> GetAvailableShaders()
        {
            Dictionary<string, bool[]> availableShaders = [];

            void LogShaderStage(string shaderName, ShaderProgramType shaderType)
            {
                if (shaderType >= ShaderProgramType.Max)
                {
                    return;
                }

                if (!availableShaders.TryGetValue(shaderName, out var stages))
                {
                    stages = new bool[3];
                    availableShaders[shaderName] = stages;
                }

                stages[(int)shaderType] = true;
            }

            foreach (var file in ShaderRegistry.EnumerateShaderFiles($"*{SlangExtension}"))
            {
                var fileName = Path.GetFileName(file);

                if (fileName.Length <= ShaderFileExtension.Length)
                {
                    continue;
                }

                LogShaderStage(ShaderNameFromPath(file), GetTypeFromFileName(fileName));
            }

            if (ShaderSourceDirectory != null)
            {
                var dirInfo = new DirectoryInfo(ShaderSourceDirectory);
                var files = dirInfo.GetFiles($"*{SlangExtension}", SearchOption.TopDirectoryOnly);

                foreach (var file in files)
                {
                    var shaderName = ShaderNameFromPath(file.FullName);
                    var shaderType = GetTypeFromFileName(file.Name);
                    LogShaderStage(shaderName, shaderType);
                }
            }
            else
            {
                var resources = Assembly.GetExecutingAssembly().GetManifestResourceNames()
                    .Where(static r => r.StartsWith(ShaderDirectory, StringComparison.Ordinal))
                    .Where(static r => r.EndsWith(SlangExtension, StringComparison.Ordinal));
                foreach (var resource in resources)
                {
                    var shaderName = resource[ShaderDirectory.Length..^ShaderFileExtension.Length];
                    var shaderType = GetTypeFromFileName(resource);
                    LogShaderStage(shaderName, shaderType);
                }
            }

            return availableShaders;
        }

        /// <summary>
        /// Opens a shader source file, preferring the directories mounted through <see cref="ShaderRegistry"/> over the
        /// shaders shipped with the renderer.
        /// </summary>
        /// <param name="name">Root relative shader file name using forward slashes (e.g. <c>common/lighting.slang</c>).</param>
        private static Stream GetShaderStream(string name)
        {
            return ShaderRegistry.TryOpenShaderFile(name) ?? GetBuiltinShaderStream(name);
        }

        private const string ShaderSourceDirectoryMetadataKey = "ShaderSourceDirectory";

        /// <summary>
        /// Gets the folder holding the shaders shipped with the renderer as loose files on disk, or <see langword="null"/>
        /// when only the copies embedded in this assembly are available.
        /// </summary>
        /// <remarks>
        /// Debug builds record the absolute path of the shader folder at compile time so that shader files can be edited
        /// and hot reloaded without rebuilding. It is not recorded in release builds, and points at a folder that does not
        /// exist when the assembly is used on another machine, in which case the embedded shaders are used instead.
        /// </remarks>
        public static string? ShaderSourceDirectory { get; } = FindShaderSourceDirectory();

        private static string? FindShaderSourceDirectory()
        {
            foreach (var metadata in typeof(ShaderParser).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
            {
                if (metadata.Key == ShaderSourceDirectoryMetadataKey
                && !string.IsNullOrEmpty(metadata.Value)
                && Directory.Exists(metadata.Value))
                {
                    return metadata.Value;
                }
            }

            return null;
        }

        private static Stream GetBuiltinShaderStream(string name)
        {
            if (ShaderSourceDirectory != null)
            {
                var path = Path.Combine(ShaderSourceDirectory, name);

                if (File.Exists(path))
                {
                    return OpenShaderFile(path);
                }
            }

            var resourceName = $"{ShaderDirectory}{name.Replace('/', '.')}";
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
                ?? throw new FileNotFoundException($"Shader file '{name}' was not found on disk or among the embedded shaders.", name);

            return stream;
        }

        private static FileStream OpenShaderFile(string path)
        {
            try
            {
                return File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            }
            catch (IOException e)
            {
                Console.Error.WriteLine(e.Message);

                // Sometimes hot reloading shaders throws "The process cannot access the file x because it is being used by another process."
                // Just sleep for a second and try again
                System.Threading.Thread.Sleep(TimeSpan.FromSeconds(1));

                return File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            }
        }
    }
}
