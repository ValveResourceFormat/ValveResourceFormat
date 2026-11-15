using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using ValveResourceFormat.Serialization.VfxEval;
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

        // Marks a shader that draws every material the same way, see Shader.IgnoreMaterialData
        private const string IgnoreMaterialDataPragma = "#pragma IgnoreMaterialData";

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
        [GeneratedRegex("^(?:layout\\s*\\([^)]*\\)\\s*)?uniform (?:layout\\s*\\([^)]*\\)\\s*)?(?<Type>\\S+) (?<Name>[A-Za-z_][A-Za-z0-9_]*)(?<Array>\\[[^\\]]*\\])?(?:\\s*=\\s*(?<Default>[^;]+?))?\\s*;[ \t]*(?:// )?(?<SrgbRead>SrgbRead\\(true\\))?[ \t]*(?<SamplerUserConfig>Sampler\\(UserConfig\\))?[ \t]*(?<RepresentativeTexture>RepresentativeTexture)?")]
        private static partial Regex RegexUniform();

        [GeneratedRegex("^#extension .+$")]
        private static partial Regex RegexExtension();

        [GeneratedRegex("^#define (?<From>(?:g|F)_[A-Za-z0-9_]+) (?<To>[A-Za-z_][A-Za-z0-9_]*)$")]
        private static partial Regex RegexUniformAlias();

        // A generic function in Slang syntax, for example "T pow2<T : IFloat>(T val)".
        [GeneratedRegex(@"^(?<Head>[A-Za-z_][A-Za-z0-9_]*\s+[A-Za-z_][A-Za-z0-9_]*)\s*<\s*(?<Parameter>[A-Za-z_][A-Za-z0-9_]*)\s*:\s*IFloat\s*>\s*(?<Tail>\(.*)$")]
        private static partial Regex RegexGenericFunction();

        private static readonly string[] GenericFloatTypes = ["float", "vec2", "vec3", "vec4"];

        // An attribute declaration of a vertex shader, for example "in vec4 vCOLOR;". Multiline, because the
        // locations are stamped over the whole assembled source rather than line by line.
        [GeneratedRegex(@"^in\s+[a-z0-9]+\s+(?<Name>[A-Za-z_][A-Za-z0-9_]*)\s*;", RegexOptions.Multiline)]
        private static partial Regex RegexVertexAttribute();

        // A declaration with an explicit location, which the allocation keeps instead of stamping
        [GeneratedRegex(@"^layout\s*\(\s*location\s*=\s*(?<Location>[0-9]+)\s*\)\s*in\s+[a-z0-9]+\s+(?<Name>[A-Za-z_][A-Za-z0-9_]*)\s*;", RegexOptions.Multiline)]
        private static partial Regex RegexLocatedVertexAttribute();

        // [type] name = value, the statement of an expressions block
        [GeneratedRegex(@"^\s*(?:[A-Za-z_][A-Za-z0-9_]*\s+)?(?<Name>[A-Za-z_][A-Za-z0-9_]*)\s*=(?!=)(?<Value>.*)$", RegexOptions.Singleline)]
        private static partial Regex RegexExpressionStatement();

        [GeneratedRegex("//[^\n]*")]
        private static partial Regex RegexLineComment();

        // type name(, the start of a function definition in an expressions block
        [GeneratedRegex(@"\G\s*[A-Za-z_][A-Za-z0-9_]*\s+[A-Za-z_][A-Za-z0-9_]*\s*\(")]
        private static partial Regex RegexExpressionFunction();

        [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_]*")]
        private static partial Regex RegexIdentifier();

        /// <summary>
        /// Writes each attribute's location into its declaration, once every declaration in the shader is
        /// known. A custom attribute takes a slot that the declared attributes leave free. A vertex struct
        /// declaring the same attributes gets the same locations, so neither side names one.
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
        /// <param name="shaderFile">The shader file path or embedded resource name (e.g. <c>fallback.vert.slang</c>).</param>
        /// <param name="parsedData">The data container that accumulates extracted metadata across all stages.</param>
        /// <returns>The preprocessed GLSL source text ready for driver compilation.</returns>
        public string PreprocessShader(string shaderFile, ParsedShaderData parsedData)
        {
            var isVertexStage = GetTypeFromFileName(shaderFile) == ShaderProgramType.Vertex;
            var preprocessor = new ShaderPreprocessor(builder, parsedData, isVertexStage);

            // simulate first time compile
            // builder.Append($"// {Guid.CreateVersion7()}");

            preprocessor.LoadShaderString(shaderFile, null);

            return preprocessor.DeclaredAttributes.Count > 0 ? StampAttributeLocations(builder.ToString(), preprocessor.DeclaredAttributes) : builder.ToString();
        }

        /// <summary>
        /// Appends a shader file and the files it includes to the builder, collecting what they declare.
        /// </summary>
        private sealed class ShaderPreprocessor(StringBuilder builder, ParsedShaderData parsedData, bool isVertexStage)
        {
            // The line state of one source file, which an include interrupts
            private sealed class SourceFile(string name, int number)
            {
                public string Name { get; } = name;
                public int Number { get; } = number;
                public int LineNumber { get; set; } = 1;
                public Match? GenericHeader { get; set; }
                public List<string> GenericLines { get; } = [];
                public StringBuilder? ExpressionBlock { get; set; }
                public int ExpressionBlockLine { get; set; }
#if DEBUG
                public List<string> Lines { get; } = [];
#endif
            }

            private readonly HashSet<string> resolvedIncludes = new(4);
            private readonly Dictionary<string, string> uniformAliases = new(0);

            public HashSet<string> DeclaredAttributes { get; } = new(StringComparer.Ordinal);

            private void AppendLineNumber(int a, int b)
            {
                builder.Append("#line ");
                builder.Append(a.ToString(CultureInfo.InvariantCulture));
                builder.Append(' ');
                builder.Append(b.ToString(CultureInfo.InvariantCulture));
                builder.Append('\n');
            }

            public void LoadShaderString(string shaderFileToLoad, string? parentFile)
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
                var file = new SourceFile(shaderFileToLoad, parsedData.SourceFiles.Count);
                parsedData.SourceFiles.Add(shaderFileToLoad);

#if DEBUG
                parsedData.SourceFileLines.Add(file.Lines);
#endif

                builder.EnsureCapacity(builder.Length + (int)stream.Length);

                string? line;

                while ((line = reader.ReadLine()) != null)
                {
                    ProcessLine(file, line);
                }

                if (file.GenericHeader != null)
                {
                    throw new ShaderCompilerException($"Generic function '{file.GenericHeader.Groups["Head"].Value}' in '{shaderFileToLoad}' has no closing brace at the start of a line");
                }

                if (file.ExpressionBlock != null)
                {
                    throw new ShaderCompilerException($"Expressions block in '{shaderFileToLoad}' has no closing '{ExpressionBlockEnd}'");
                }
            }

            private void ProcessLine(SourceFile file, string line)
            {
                file.LineNumber++;

#if DEBUG
                if (!Ascii.IsValid(line))
                {
                    // At least on nvidia, trying to compile GLSL with non ascii characters will throw bizarre errors like
                    // wrong #line source-line error, or EOF.
                    throw new ShaderCompilerException($"Line {file.LineNumber} in '{file.Name}' contains non-ASCII characters.");
                }
#endif

                if (file.LineNumber == 2)
                {
                    if (line != ExpectedShaderVersion)
                    {
                        throw new ShaderCompilerException($"First line must be '{ExpectedShaderVersion}' in '{file.Name}'");
                    }

#if DEBUG
                    file.Lines.Add($"// :VrfPreprocessed {line}");
#endif

                    builder.Append('\n');

                    AppendLineNumber(file.LineNumber, file.Number);

                    // We add #version even in includes so that they can be compiled individually for better editing experience
                    // Skip #version for main shader - will be prepended in header
                    return;
                }

#if DEBUG
                file.Lines.Add(line);
#endif

                if (TryCollectExpressionBlock(file, line))
                {
                    return;
                }

                // Generic functions are held back until their closing brace, then written out per type
                var genericMatch = RegexGenericFunction().Match(line);

                if (genericMatch.Success || file.GenericHeader != null)
                {
                    AppendGenericFunctionLine(file, genericMatch, line);
                    return;
                }

                line = line.Trim(); // we will be outputting trimmed lines to compile too

                if (line == IgnoreMaterialDataPragma)
                {
                    parsedData.IgnoreMaterialData = true;
                    return;
                }

                if (TryLoadInclude(file, line))
                {
                    return;
                }

                if (TryAddDefine(file, line))
                {
                    return;
                }

                if (TryHoistExtension(line))
                {
                    return;
                }

                var aliasMatch = RegexUniformAlias().Match(line);

                if (aliasMatch.Success)
                {
                    uniformAliases[aliasMatch.Groups["From"].Value] = aliasMatch.Groups["To"].Value;
                }

                if (TryPackUniform(file, line))
                {
                    return;
                }

                // Collected now, located once the whole declaring set is known
                if (isVertexStage)
                {
                    var attributeMatch = RegexVertexAttribute().Match(line);

                    if (attributeMatch.Success)
                    {
                        DeclaredAttributes.Add(attributeMatch.Groups["Name"].Value);
                    }
                }

                builder.Append(line);
                builder.Append('\n');

                if (line.Contains("#endif", StringComparison.Ordinal))
                {
                    // Fix an issue where #include is inside of an #if, which messes up line numbers
                    AppendLineNumber(file.LineNumber, file.Number);
                }
            }

            // Assignments of uniforms computed per material, after the declarations they read
            private const string ExpressionBlockStart = "/* Expressions";
            private const string ExpressionBlockEnd = "*/";

            /// <summary>Collects the lines of an expressions block, and returns whether the line was part of one.</summary>
            private bool TryCollectExpressionBlock(SourceFile file, string line)
            {
                var trimmed = line.AsSpan().Trim();

                if (file.ExpressionBlock == null)
                {
                    if (!trimmed.SequenceEqual(ExpressionBlockStart))
                    {
                        return false;
                    }

                    file.ExpressionBlock = new StringBuilder(256);
                    file.ExpressionBlockLine = file.LineNumber + 1;
                }
                else if (trimmed.SequenceEqual(ExpressionBlockEnd))
                {
                    // Both stages see a block in a shared include
                    if (!parsedData.ExpressionBlocks.Exists(block => block.File == file.Name && block.Line == file.ExpressionBlockLine))
                    {
                        parsedData.ExpressionBlocks.Add(new(file.ExpressionBlock.ToString(), file.Name, file.ExpressionBlockLine));
                    }

                    file.ExpressionBlock = null;
                }
                else
                {
                    file.ExpressionBlock.Append(line).Append('\n');
                }

                // Blank lines keep the line numbers of the GLSL source
                builder.Append('\n');
                return true;
            }

            private void AppendGenericFunctionLine(SourceFile file, Match genericMatch, string line)
            {
                var header = file.GenericHeader ?? genericMatch;
                file.GenericHeader = header;
                file.GenericLines.Add(line.Trim());

                if (!line.StartsWith('}', StringComparison.Ordinal))
                {
                    return;
                }

                var genericLines = file.GenericLines;
                genericLines[0] = string.Concat(header.Groups["Head"].Value, header.Groups["Tail"].Value);

                var function = string.Join('\n', genericLines);
                var parameter = $@"\b{header.Groups["Parameter"].Value}\b";

                foreach (var type in GenericFloatTypes)
                {
                    AppendLineNumber(file.LineNumber - genericLines.Count, file.Number);
                    builder.Append(Regex.Replace(function, parameter, type)).Append('\n');
                }

                AppendLineNumber(file.LineNumber, file.Number);
                file.GenericHeader = null;
                genericLines.Clear();
            }

            private bool TryLoadInclude(SourceFile file, string line)
            {
                var match = RegexInclude().Match(line);

                if (!match.Success)
                {
                    return false;
                }

                // Recursively append included shaders
                var includeName = match.Groups["IncludeName"].Value;

                AppendLineNumber(1, parsedData.SourceFiles.Count);
                LoadShaderString(includeName, file.Name);
                AppendLineNumber(file.LineNumber, file.Number);

                return true;
            }

            private bool TryAddDefine(SourceFile file, string line)
            {
                var match = RegexDefine().Match(line);

                if (!match.Success)
                {
                    return false;
                }

                var defineName = match.Groups["ParamName"].Value;
                var defaultValueStr = match.Groups["DefaultValue"].Value;
                var value = byte.Parse(defaultValueStr, CultureInfo.InvariantCulture);

                if (defineName.StartsWith(RenderModeDefinePrefix, StringComparison.Ordinal))
                {
                    var renderMode = defineName[RenderModeDefinePrefix.Length..];

                    parsedData.RenderModes.Add(renderMode);

                    value = RenderModes.GetOrAddShaderId(renderMode);

                    builder.Append("#define ");
                    builder.Append(defineName);
                    builder.Append(' ');
                    builder.Append(value.ToString(CultureInfo.InvariantCulture));
                    builder.Append(" // :VrfPreprocessed\n");

                    return true;
                }

                // Defines are removed from source code and will be prepended later
                if (!parsedData.Defines.TryAdd(defineName, value))
                {
                    // Defines can be shared between vert and frag
                    if (parsedData.Defines[defineName] != value)
                    {
                        throw new ShaderCompilerException($"Line {file.LineNumber} in '{file.Name}' contains a duplicate define '{defineName}' with different default value");
                    }
                }

                return true;
            }

            private bool TryHoistExtension(string line)
            {
                if (!RegexExtension().IsMatch(line))
                {
                    return false;
                }

                parsedData.Extensions.Add(line);

                builder.Append("// :VrfHoisted ");
                builder.Append(line);
                builder.Append('\n');

                return true;
            }

            /// <summary>Records a uniform declaration, and returns whether it was packed into the globals block.</summary>
            private bool TryPackUniform(SourceFile file, string line)
            {
                // sRGB uniforms or samplers
                var match = RegexUniform().Match(line);

                if (!match.Success)
                {
                    return false;
                }

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
                if (match.Groups["RepresentativeTexture"].Success)
                {
                    if (parsedData.RepresentativeTexture != null && parsedData.RepresentativeTexture != uniformName)
                    {
                        throw new ShaderCompilerException($"Line {file.LineNumber} in '{file.Name}' marks '{uniformName}' as the representative texture, but '{parsedData.RepresentativeTexture}' already is");
                    }

                    parsedData.RepresentativeTexture = uniformName;
                }

                if (match.Groups["Array"].Success
                || !IsPackableUniformName(uniformName)
                || !GlobalsLayout.TryGetType(uniformType, out var constantType))
                {
                    return false;
                }

                var defaultGroup = match.Groups["Default"];

                parsedData.GlobalsDeclarations.Add(new GlobalsDeclaration(uniformName, constantType,
                    defaultGroup.Success ? defaultGroup.Value : null, match.Groups["SrgbRead"].Success));

                builder.Append("// :VrfPacked ");
                builder.Append(line);
                builder.Append('\n');

                return true;
            }
        }

        /// <summary>Compiles the expressions blocks into <see cref="ParsedShaderData.Expressions"/> and <see cref="ParsedShaderData.ExpressionInputs"/>.</summary>
        /// <param name="parsedData">The parsed stages of the shader, with the uniforms they declare.</param>
        public static void CompileExpressions(ParsedShaderData parsedData)
        {
            List<GlobalsDeclaration> inputs = [];
            var functions = new StringBuilder();
            var blocks = parsedData.ExpressionBlocks.ConvertAll(block => (block, Statements: ParseExpressionStatements(block, inputs, functions)));
            var functionSource = functions.ToString();

            foreach (var (block, statements) in blocks)
            {
                for (var i = 0; i < statements.Count; i++)
                {
                    var (name, value, line) = statements[i];

                    if (!parsedData.Uniforms.Contains(name))
                    {
                        continue;
                    }

                    var source = string.Concat(functionSource, GetLocalsReadBy(statements, i, parsedData.Uniforms), value);

                    try
                    {
                        if (!parsedData.Expressions.TryAdd(name, VfxEvalCompiler.Compile(source)))
                        {
                            throw new InvalidDataException($"'{name}' is assigned twice");
                        }
                    }
                    catch (InvalidDataException e)
                    {
                        throw new ShaderCompilerException($"Line {line} in '{block.File}': {e.Message}");
                    }
                }
            }

            parsedData.ExpressionInputs = GlobalsLayout.Build(inputs);
        }

        private static List<(string Name, string Value, int Line)> ParseExpressionStatements(ExpressionBlock block, List<GlobalsDeclaration> inputs,
            StringBuilder functions)
        {
            List<(string Name, string Value, int Line)> statements = [];

            // Comments go, their line breaks stay
            var source = RegexLineComment().Replace(block.Source, string.Empty);
            var line = block.Line;
            var start = 0;

            while (start < source.Length)
            {
                // Definitions are compiled with every statement, the compiler inlines them where called
                if (RegexExpressionFunction().IsMatch(source, start))
                {
                    var close = source.IndexOf('}', start, StringComparison.Ordinal);

                    if (close < 0)
                    {
                        throw new ShaderCompilerException($"Line {line} in '{block.File}': Function has no closing brace");
                    }

                    functions.Append(source, start, close + 1 - start).Append('\n');
                    line += source.AsSpan(start, close + 1 - start).Count('\n');
                    start = close + 1;
                    continue;
                }

                var end = source.IndexOf(';', start, StringComparison.Ordinal);
                var text = end < 0 ? source[start..] : source[start..end];

                var leading = text.Length - text.TrimStart().Length;
                line += source.AsSpan(start, leading).Count('\n');

                if (end < 0 && text.AsSpan().Trim().IsEmpty)
                {
                    break;
                }

                if (end < 0)
                {
                    throw new ShaderCompilerException($"Line {line} in '{block.File}': Expected ';'");
                }

                var trimmed = text.Trim();

                if (trimmed.StartsWith("uniform ", StringComparison.Ordinal))
                {
                    var declaration = RegexUniform().Match(string.Concat(trimmed, ";"));

                    if (!declaration.Success || !GlobalsLayout.TryGetType(declaration.Groups["Type"].Value, out var type))
                    {
                        throw new ShaderCompilerException($"Line {line} in '{block.File}': Expected 'uniform type name = default;'");
                    }

                    var initializer = declaration.Groups["Default"];
                    inputs.Add(new GlobalsDeclaration(declaration.Groups["Name"].Value, type, initializer.Success ? initializer.Value : null, SrgbRead: false));
                }
                else
                {
                    var match = RegexExpressionStatement().Match(text);

                    if (!match.Success)
                    {
                        throw new ShaderCompilerException($"Line {line} in '{block.File}': Expected 'name = expression;'");
                    }

                    statements.Add((match.Groups["Name"].Value, match.Groups["Value"].Value.Trim(), line));
                }

                line += text.AsSpan(leading).Count('\n');
                start = end + 1;
            }

            return statements;
        }

        // The local statements before a uniform's that its value reads, directly or through other locals
        private static string GetLocalsReadBy(List<(string Name, string Value, int Line)> statements, int index, HashSet<string> uniforms)
        {
            HashSet<string> read = [];
            AddIdentifiers(read, statements[index].Value);

            var locals = new List<string>();

            for (var i = index - 1; i >= 0; i--)
            {
                var (name, value, _) = statements[i];

                if (uniforms.Contains(name) || !read.Remove(name))
                {
                    continue;
                }

                AddIdentifiers(read, value);
                locals.Add($"{name} = {value};\n");
            }

            locals.Reverse();
            return string.Concat(locals);

            static void AddIdentifiers(HashSet<string> set, string value)
            {
                foreach (Match identifier in RegexIdentifier().Matches(value))
                {
                    set.Add(identifier.Value);
                }
            }
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

        /// <summary>Gets each shader base name with its available stages (vertex, fragment, compute), from mounted directories, disk or the assembly manifest.</summary>
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

            // Entry points are named by their folder and file, without stage and extension (e.g. hlvr/vr_simple)
            void LogShaderFile(string relativePath)
            {
                if (relativePath.Length <= ShaderFileExtension.Length)
                {
                    return;
                }

                LogShaderStage(relativePath[..^ShaderFileExtension.Length], GetTypeFromFileName(relativePath));
            }

            foreach (var file in ShaderRegistry.EnumerateShaderFiles($"*{SlangExtension}"))
            {
                LogShaderFile(file);
            }

            if (ShaderSourceDirectory != null)
            {
                foreach (var file in ShaderRegistry.EnumerateEntryFiles(ShaderSourceDirectory, $"*{SlangExtension}"))
                {
                    LogShaderFile(file);
                }
            }
            else
            {
                var resources = Assembly.GetExecutingAssembly().GetManifestResourceNames()
                    .Where(static r => r.StartsWith(ShaderDirectory, StringComparison.Ordinal))
                    .Where(static r => r.EndsWith(SlangExtension, StringComparison.Ordinal));
                foreach (var resource in resources)
                {
                    // Folders are separated by dots in resource names, shader names have none
                    var shaderName = resource[ShaderDirectory.Length..^ShaderFileExtension.Length].Replace('.', '/');
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
        /// Gets the folder of loose built-in shader files, or <see langword="null"/> when only the
        /// embedded copies are available.
        /// </summary>
        /// <remarks>
        /// Debug builds record the absolute shader folder path at compile time for hot reloading. Release builds do not,
        /// and a recorded path that is missing on another machine falls back to the embedded shaders.
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
                return File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            }
            catch (IOException e)
            {
                Console.Error.WriteLine(e.Message);

                // Sometimes hot reloading shaders throws "The process cannot access the file x because it is being used by another process."
                // Just sleep for a second and try again
                System.Threading.Thread.Sleep(TimeSpan.FromSeconds(1));

                return File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            }
        }
    }
}
