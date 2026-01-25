using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using static ValveResourceFormat.Renderer.ShaderLoader;

namespace ValveResourceFormat.Renderer
{
    public partial class ShaderParser
    {
        public const string ShaderDirectory = "Renderer.Shaders.";
        public const string ExpectedShaderVersion = "#version 460";
        private const string RenderModeDefinePrefix = "renderMode_";

        [GeneratedRegex("^#include \"(?<IncludeName>[^\"]+)\"")]
        private static partial Regex RegexInclude();
        [GeneratedRegex("^#define (?<ParamName>(?:renderMode|F|S|D)_\\S+) (?<DefaultValue>[0-9]+)")]
        private static partial Regex RegexDefine();

#if DEBUG
        [GeneratedRegex(@"defined\((?<Name>\w+)_vfx\)")]
        private static partial Regex RegexIsVfxDefined();
#endif

        // regex that detects
        // uniform sampler{dim} x;
        // uniform sampler{dim} a; // SrgbRead(true)
        // uniform vec{dim} b = vec3(1.0); // SrgbRead(true)
        [GeneratedRegex("^uniform (?<Type>(?:sampler|vec)\\S+) (?<Name>\\S+)(?:\\s*=\\s*[^;]+)?;[ \t]*(?<SrgbRead>// SrgbRead\\(true\\))?")]
        private static partial Regex RegexUniform();

        private readonly StringBuilder builder = new(1024);
        private int sourceFileNumber;
        public List<string> SourceFiles { get; } = [];

#if DEBUG
        public List<List<string>> SourceFileLines { get; } = [];
#endif

        public void ClearBuilder()
        {
            builder.Clear();
        }

        public void Reset()
        {
            ClearBuilder();
            sourceFileNumber = 0;
            SourceFiles.Clear();

#if DEBUG
            SourceFileLines.Clear();
#endif
        }

        public string PreprocessShader(string shaderFile, ParsedShaderData parsedData)
        {
            var resolvedIncludes = new HashSet<string>(4);

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
                SourceFiles.Add(shaderFileToLoad);

#if DEBUG
                var currentSourceLines = new List<string>();
                SourceFileLines.Add(currentSourceLines);
#endif

                builder.EnsureCapacity(builder.Length + (int)stream.Length);

                while ((line = reader.ReadLine()) != null)
                {
                    lineNum++;

#if DEBUG
                    if (!line.All(static c => char.IsAscii(c)))
                    {
                        // At least on nvidia, trying to compile GLSL with non ascii characters will throw bizzare errors like
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
                                        Debug.Assert(false); /// Add to <see cref="RenderModes.Items"/> if this assert is hit

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

                        // sRGB uniforms or samplers
                        match = RegexUniform().Match(line);
                        if (match.Success)
                        {
                            var uniformType = match.Groups["Type"].Value;
                            var uniformName = match.Groups["Name"].Value;

                            parsedData.Uniforms.Add(uniformName);
                            if (match.Groups["SrgbRead"].Success)
                            {
                                parsedData.SrgbUniforms.Add(uniformName);
                            }
                        }

#if DEBUG
                        // defined(shader_vfx)
                        match = RegexIsVfxDefined().Match(line);
                        if (match.Success)
                        {
                            var shaderName = match.Groups["Name"].Value;
                            parsedData.ShaderVariants.Add(shaderName);
                        }
#endif
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

            return builder.ToString();
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

        public Dictionary<string, bool[]> AvailableShaders { get; }

        public ShaderParser()
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

#if DEBUG
            var dirInfo = new DirectoryInfo(ShaderRootDirectory);
            var files = dirInfo.GetFiles($"*{SlangExtension}", SearchOption.TopDirectoryOnly);

            foreach (var file in files)
            {
                var shaderName = ShaderNameFromPath(file.FullName);
                var shaderType = GetTypeFromFileName(file.Name);
                LogShaderStage(shaderName, shaderType);
            }
#else
            var resources = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceNames()
                .Where(static r => r.StartsWith(ShaderDirectory, StringComparison.Ordinal))
                .Where(static r => r.EndsWith(SlangExtension, StringComparison.Ordinal));
            foreach (var resource in resources)
            {
                var shaderName = resource[ShaderDirectory.Length..^ShaderFileExtension.Length];
                var shaderType = GetTypeFromFileName(resource);
                LogShaderStage(shaderName, shaderType);
            }
#endif
            return availableShaders;
        }

#if !DEBUG
        private static Stream GetShaderStream(string name)
        {
            var resourceName = $"{ShaderDirectory}{name.Replace('/', '.')}";
            var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            ArgumentNullException.ThrowIfNull(stream);
            return stream;
        }
#else
        // Path to the folder where the ValveResourceFormat.slnx is on disk (parent of the GUI folder)
        private static readonly string SolutionRootDirector = GetSolutionRootDirectory();
        private static readonly string ShaderRootDirectory = Path.Combine(SolutionRootDirector, ShaderDirectory.Replace('.', Path.DirectorySeparatorChar));

        private static FileStream GetShaderStream(string name)
        {
            var path = GetShaderDiskPath(name);

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

        public static string GetShaderDiskPath(string name)
        {
            return Path.Combine(ShaderRootDirectory, name);
        }

        private static string GetSolutionRootDirectory()
        {
            var root = AppContext.BaseDirectory;
            var failsafe = 10;
            var fileName = string.Empty;

            do
            {
                root = Path.GetDirectoryName(root);

                if (root == null || failsafe-- == 0)
                {
                    throw new DirectoryNotFoundException("Failed to find the project root folder for the shaders, are you debugging in some unconventional setup?");
                }

                fileName = Path.Join(root, "ValveResourceFormat.slnx");
            }
            while (!File.Exists(fileName));

            return root;
        }
#endif
    }
}
