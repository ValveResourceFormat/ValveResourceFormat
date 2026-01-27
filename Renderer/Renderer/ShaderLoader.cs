using System.Globalization;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;
using SlangCompiler;
using static SlangCompiler.SlangBindings;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Shader stage types in the rendering pipeline.
    /// </summary>
    public enum ShaderProgramType
    {
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
        Vertex = 0,
        Fragment = 1,
        Compute = 2,
        Max = 3,
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
    }

    /// <summary>
    /// Compiles and caches OpenGL shader programs from source files.
    /// </summary>
    public partial class ShaderLoader : IDisposable
    {
        [GeneratedRegex(@"^(?<SourceFile>[0-9]+)\((?<Line>[0-9]+)\) ?: error")]
        private static partial Regex NvidiaGlslError();

        [GeneratedRegex(@"ERROR: (?<SourceFile>[0-9]+):(?<Line>[0-9]+):")]
        private static partial Regex AmdGlslError();

        [GeneratedRegex(@"^(?<SourceFile>[0-9]+):(?<Line>[0-9]+)\((?<Column>[0-9]+)\):")]
        private static partial Regex Mesa3dGlslError();

        private readonly Dictionary<ulong, Shader> CachedShaders = [];
        public int ShaderCount => CachedShaders.Count;
        private readonly Dictionary<string, Dictionary<string, byte>> ShaderDefines = [];

        private static readonly Dictionary<string, byte> EmptyArgs = [];
        private static readonly Lock ParserLock = new();
        private static readonly Dictionary<string, ParsedShaderData> ParsedCache = [];

        private static readonly ShaderParser Parser = new();

        private readonly RendererContext RendererContext;

#if DEBUG
        private HashSet<string> LastShaderVariantNames = [];

        // TODO: This probably should be ParsedShaderData so we can access it for non-blocking linking
        private List<List<string>> LastShaderSourceLines = [];
#endif

        /// <summary>
        /// Preprocessed shader source with defines, uniforms, and compiled stage code.
        /// </summary>
        public class ParsedShaderData
        {
            public Dictionary<string, byte> Defines { get; } = [];
            public HashSet<string> RenderModes { get; } = [];
            public HashSet<string> Uniforms { get; } = [];
            public HashSet<string> SrgbUniforms { get; } = [];
            public Dictionary<ShaderProgramType, string> Sources { get; } = [];

#if DEBUG
            public HashSet<string> ShaderVariants { get; } = [];
#endif
        }

        static ShaderLoader()
        {
            if (slangSession.isNull())
                setupSlangCompiler();

            Task.Run(() =>
            {
                foreach (var shader in Parser.AvailableShaders.Keys)
                {
                        GetOrParseShader(shader);
                }
            });
        }

        public ShaderLoader(RendererContext rendererContext)
        {
            RendererContext = rendererContext;

            if(slangSession.isNull())
                setupSlangCompiler();

        }

        static void setupSlangCompiler()
        {
            createGlslCompatibleGlobalSession(out globalSlangSession);

            SessionDesc slangSessionDesc = new SessionDesc();
            slangSessionDesc.allowGLSLSyntax = true;

            TargetDesc targetDesc = new TargetDesc();

            targetDesc.format = SlangCompileTarget.SLANG_GLSL;
            targetDesc.profile = globalSlangSession.findProfile("glsl_460");
            unsafe
            {
                slangSessionDesc.targets = &targetDesc;
            }
            slangSessionDesc.targetCount = 1;
            slangSessionDesc.defaultMatrixLayoutMode = SlangMatrixLayoutMode.SLANG_MATRIX_LAYOUT_COLUMN_MAJOR;


            //slangSessionDesc.targets = Marshal.AllocHGlobal(Marshal.SizeOf<TargetDesc>());
            globalSlangSession.createSession(slangSessionDesc, out slangSession);
        }

        static IGlobalSession globalSlangSession = new IGlobalSession(new IGlobalSessionPtr());
        static ISession slangSession = new ISession(new ISessionPtr());



        public Shader LoadShader(string shaderName, params (string ComboName, byte ComboValue)[] combos)
        {
            var args = combos.ToDictionary(c => c.ComboName, c => c.ComboValue);
            return LoadShader(shaderName, args);
        }

        public Shader LoadShader(string shaderName, IReadOnlyDictionary<string, byte>? arguments = null, bool blocking = true)
        {
            arguments ??= EmptyArgs;

            if (ShaderDefines.ContainsKey(shaderName))
            {
                var shaderCacheHash = CalculateShaderCacheHash(shaderName, arguments);

                if (CachedShaders.TryGetValue(shaderCacheHash, out var cachedShader))
                {
                    return cachedShader;
                }
            }

            var shader = CompileAndLinkShader(shaderName, arguments, blocking: blocking);
            var newShaderCacheHash = CalculateShaderCacheHash(shaderName, arguments);
            CachedShaders[newShaderCacheHash] = shader;
            return shader;
        }

        private static ParsedShaderData GetOrParseShader(string shaderFileName)
        {
            if (File.Exists(ShaderParser.GetShaderDiskPath($"{shaderFileName}.slang")))
            {
                return GetOrParseSlangShader(shaderFileName);
            }
            else
            {
                return GetOrParseGlslShader(shaderFileName);
            }
        }

        private static ParsedShaderData GetOrParseGlslShader(string shaderFileName)
        {
            using var _ = ParserLock.EnterScope();
            if (ParsedCache.TryGetValue(shaderFileName, out var cached))
            {
                return cached;
            }

            var parsedData = new ParsedShaderData();

            var availableStages = Parser.AvailableShaders.GetValueOrDefault(shaderFileName)
                ?? throw new FileNotFoundException($"Shader '{shaderFileName}' does not exist.");

            if (availableStages.Length == 0
            || availableStages[(int)ShaderProgramType.Vertex] == false && availableStages[(int)ShaderProgramType.Compute] == false)
            {
                throw new InvalidDataException($"Shader '{shaderFileName}' does not have a vertex or compute stage.");
            }

            foreach (var (@type, extension) in ShaderParser.ProgramTypeToExtension)
            {
                if (!availableStages[(int)@type])
                {
                    continue;
                }

                var nameWithExtension = $"{shaderFileName}.{extension}.glSlang";

                var shaderSource = Parser.PreprocessShader(nameWithExtension, parsedData);
                parsedData.Sources[@type] = shaderSource;
                Parser.ClearBuilder();
            }

            ParsedCache[shaderFileName] = parsedData;
            return parsedData;
        }

        private static ParsedShaderData GetOrParseSlangShader(string shaderFileName)
        {
            using var _ = ParserLock.EnterScope();
            if (ParsedCache.TryGetValue(shaderFileName, out var cached))
            {
                return cached;
            }

            var parsedData = new ParsedShaderData();

            var availableStages = Parser.AvailableShaders.GetValueOrDefault(shaderFileName)
                ?? throw new FileNotFoundException($"Shader '{shaderFileName}' does not exist.");

            if (availableStages.Length == 0
            || availableStages[(int)ShaderProgramType.Vertex] == false && availableStages[(int)ShaderProgramType.Compute] == false)
            {
                throw new InvalidDataException($"Shader '{shaderFileName}' does not have a vertex or compute stage.");
            }

            //only loading one file. Shaders with split vert and frag shaders must import the vertex shader.
            var nameWithExtension = $"{shaderFileName}.slang";

            IModule module = slangSession.loadModule(ShaderParser.GetShaderDiskPath($"{shaderFileName}.slang"), out ISlangBlob diagnostics);

            if (module.getDefinedEntryPointCount() > 0)
            {
                foreach (var (@type, extension) in ShaderParser.ProgramTypeToExtension)
                {
                    IEntryPoint entryPoint = new IEntryPoint(new IEntryPointPtr());

                    if (@type == ShaderProgramType.Vertex)
                        module.findEntryPointByName("vertexMain", out entryPoint);
                    else if (@type == ShaderProgramType.Fragment)
                        module.findEntryPointByName("fragmentMain", out entryPoint);

                    if (entryPoint.isNull())
                        continue;

                    //time to compose!
                    IComponentType[] componentTypes = { module, entryPoint };

                    slangSession.createCompositeComponentType(componentTypes, out IComponentType compositeType, out ISlangBlob linkDiagnostics);
                    compositeType.link(out IComponentType linkedProgram, out ISlangBlob linkingDiagnostics);
                    linkedProgram.getTargetCode(0, out ISlangBlob outBlob);
                    //module.getTargetCode(0, out ISlangBlob outBlob);
                    parsedData.Sources[@type] = outBlob.getString();




                }
            }

            ParsedCache[shaderFileName] = parsedData;
            return parsedData;
        }

        private Shader CompileAndLinkShader(string shaderName, IReadOnlyDictionary<string, byte> arguments, bool blocking = true)
        {
            var shaderProgram = -1;

            try
            {
                var shaderFileName = GetShaderFileByName(shaderName);
                var parsedData = GetOrParseShader(shaderFileName);

                var sources = parsedData.Sources;

                static ShaderType ToShaderType(ShaderProgramType type) => type switch
                {
                    ShaderProgramType.Vertex => ShaderType.VertexShader,
                    ShaderProgramType.Fragment => ShaderType.FragmentShader,
                    ShaderProgramType.Compute => ShaderType.ComputeShader,
                    _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
                };

                var shaderObjects = new int[sources.Count];
                var shaderSources = new string[sources.Count];
                var s = 0;
                foreach (var (stage, source) in sources)
                {
                    shaderObjects[s] = GL.CreateShader(ToShaderType(stage));
                    shaderSources[s] = source!;
                    s++;
                }

                CompileShaderObjects(shaderObjects, shaderSources, shaderFileName, shaderName, arguments, parsedData);

                shaderProgram = GL.CreateProgram();

#if DEBUG
                GL.ObjectLabel(ObjectLabelIdentifier.Program, shaderProgram, shaderFileName.Length, shaderFileName);
                for (var i = 0; i < shaderObjects.Length; i++)
                {
                    GL.ObjectLabel(ObjectLabelIdentifier.Shader, shaderObjects[i], shaderFileName.Length, shaderFileName);
                }
#endif

                var shader = new Shader(shaderName, RendererContext)
                {
#if DEBUG
                    FileName = shaderFileName,
#endif

                    Parameters = arguments,
                    Program = shaderProgram,
                    ShaderObjects = shaderObjects,
                    RenderModes = parsedData.RenderModes,
                    UniformNames = parsedData.Uniforms,
                    SrgbUniforms = parsedData.SrgbUniforms,
                    IsSlang = false
                };

                foreach (var shaderObj in shaderObjects)
                {
                    GL.AttachShader(shader.Program, shaderObj);
                }

                GL.LinkProgram(shader.Program);

                // Not getting link status straight away allows the driver to perform parallelized shader compilation
                // TODO: Ideally we want this to work for initial load too.
                if (blocking)
                {
                    if (!shader.EnsureLoaded())
                    {
                        GL.GetProgramInfoLog(shader.Program, out var log);
                        ThrowShaderError(log, string.Concat(shaderFileName, GetArgumentDescription(arguments)), shaderName, "Failed to link shader");
                    }
                }

                ShaderDefines[shaderName] = parsedData.Defines;

#if DEBUG
                LastShaderVariantNames = parsedData.ShaderVariants;
                LastShaderSourceLines = [.. Parser.SourceFileLines];
#endif

                var argsDescription = GetArgumentDescription(SortAndFilterArguments(shaderName, arguments));
                RendererContext.Logger.LogInformation("Shader '{ShaderName}' as '{ShaderFileName}'{ArgsDescription} compiled{CompiledStatus} successfully (program={Program})", shaderName, shaderFileName, argsDescription, blocking ? " and linked" : string.Empty, shader.Program);

                return shader;
            }
            catch (ShaderCompilerException)
            {
                if (shaderProgram > -1)
                {
                    GL.DeleteProgram(shaderProgram);
                }

                throw;
            }
            finally
            {
                Parser.ClearBuilder();
            }
        }

        private static void CompileShaderObjects(int[] shaderObjects, string[] shaderSources, string shaderFile, ReadOnlySpan<char> originalShaderName, IReadOnlyDictionary<string, byte> arguments, ParsedShaderData parsedData)
        {
            var header = new StringBuilder();

            if (!(shaderSources[0].Split("\n")[0] == "#version 460"))
            {
                header.Append(ShaderParser.ExpectedShaderVersion);
                header.Append('\n');
                // Append original shader name as a define
                header.Append("#define ");
                header.Append(Path.GetFileNameWithoutExtension(originalShaderName));
                header.Append("_vfx 1\n");

                // Add all defines (with argument overrides or defaults)
                foreach (var (defineName, defaultValue) in parsedData.Defines)
                {
                    var value = arguments.TryGetValue(defineName, out var argValue) ? argValue : defaultValue;
                    header.Append("#define ");
                    header.Append(defineName);
                    header.Append(' ');
                    header.Append(value.ToString(CultureInfo.InvariantCulture));
                    header.Append('\n');
                }
            }
            
            var headerText = header.ToString();

            for (var i = 0; i < shaderObjects.Length; i++)
            {
                CompileShaderObject(shaderObjects[i], shaderFile, originalShaderName, arguments, headerText, shaderSources[i]);
            }
        }

        private static void CompileShaderObject(int shader, string shaderFile, ReadOnlySpan<char> originalShaderName, IReadOnlyDictionary<string, byte> arguments, string headerText, string shaderText)
        {
            string[] sources = [headerText, shaderText];
            int[] lengths = [sources[0].Length, sources[1].Length];

            GL.ShaderSource(shader, sources.Length, sources, lengths);

            GL.CompileShader(shader);
            GL.GetShader(shader, ShaderParameter.CompileStatus, out var shaderStatus);

            if (shaderStatus != 1)
            {
                GL.GetShaderInfoLog(shader, out var log);

                ThrowShaderError(log, string.Concat(shaderFile, GetArgumentDescription(arguments)), originalShaderName, "Failed to set up shader");
            }
        }

        private static void ThrowShaderError(string info, string shaderFile, ReadOnlySpan<char> originalShaderName, string errorType)
        {
            // Attempt to parse error message to get the line number so we can print the actual line
            var errorMatch = NvidiaGlslError().Match(info);

            if (!errorMatch.Success)
            {
                errorMatch = AmdGlslError().Match(info);
            }

            if (!errorMatch.Success)
            {
                errorMatch = Mesa3dGlslError().Match(info);
            }

            string? sourceFile = null;
            var errorLine = -1;
            var errorSourceFile = -1;

            if (errorMatch.Success)
            {
                errorSourceFile = int.Parse(errorMatch.Groups["SourceFile"].Value, CultureInfo.InvariantCulture);
                errorLine = int.Parse(errorMatch.Groups["Line"].Value, CultureInfo.InvariantCulture);
                sourceFile = Parser.SourceFiles[errorSourceFile];
            }

#if DEBUG
            // Output GitHub Actions annotation https://docs.github.com/en/actions/reference/workflow-commands-for-github-actions
            if (IsCI)
            {
                var annotation = "::error ";

                if (sourceFile != null)
                {
                    annotation += $"file={ShaderParser.ShaderDirectory.Replace('.', '/')}{sourceFile},";
                    if (errorLine > 0)
                    {
                        annotation += $"line={errorLine},";
                    }
                }

                var errorMessage = $"{info}\n({shaderFile}, original={originalShaderName})";
                annotation += $"title={nameof(ShaderCompilerException)}::{errorMessage.Replace("\n", "%0A", StringComparison.Ordinal).Replace("\r", "%0D", StringComparison.Ordinal)}";

                Console.WriteLine(annotation);
            }
#endif

            if (sourceFile != null)
            {
                info += $"\nError in {sourceFile} on line {errorLine}";

#if DEBUG
                if (errorLine > 0 && errorLine <= Parser.SourceFileLines[errorSourceFile].Count)
                {
                    info += $":\n{Parser.SourceFileLines[errorSourceFile][errorLine - 1]}\n";
                }
#endif
            }

            throw new ShaderCompilerException($"{errorType} {shaderFile} (original={originalShaderName}):\n\n{info}");
        }

        public static string ShaderNameFromPath(string shaderFilePath)
        {
            return Path.GetFileName(shaderFilePath[..^ShaderFileExtension.Length]);
        }

        public const string SlangExtension = ".glSlang";
        public const string ShaderFileExtension = ".vert.glSlang";
        const string VrfInternalShaderPrefix = "vrf.";

        // Map Valve's shader names to shader files VRF has
        private static string GetShaderFileByName(string shaderName) => shaderName switch
        {
            "sky.vfx" => "sky",
            "tools_sprite.vfx" => "sprite",
            "global_lit_simple.vfx" => "global_lit_simple",
            "vr_black_unlit.vfx" or "csgo_black_unlit.vfx" => "vr_black_unlit",
            "water_dota.vfx" => "water",
            "csgo_water_fancy.vfx" => "water_csgo",
            "hero.vfx" or "hero_underlords.vfx" => "dota_hero",
            "multiblend.vfx" => "multiblend",
            "csgo_effects.vfx" => "csgo_effects",
            "csgo_environment.vfx" or "csgo_environment_blend.vfx" => "csgo_environment",

            _ when shaderName.StartsWith(VrfInternalShaderPrefix, StringComparison.Ordinal) => shaderName[VrfInternalShaderPrefix.Length..],
            _ => "complex",
        };

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            ShaderDefines.Clear();
            CachedShaders.Clear();
        }

        private IEnumerable<KeyValuePair<string, byte>> SortAndFilterArguments(string shaderName, IReadOnlyDictionary<string, byte> arguments)
        {
            var defines = ShaderDefines[shaderName];

            return arguments
                .Where(p => defines.ContainsKey(p.Key))
                .Where(static p => p.Value != 0) // Shader defines should already default to zero
                .OrderBy(static p => p.Key);
        }

        private static string GetArgumentDescription(IEnumerable<KeyValuePair<string, byte>> arguments)
        {
            var sb = new StringBuilder();
            var first = true;

            foreach (var param in arguments)
            {
                if (first)
                {
                    first = false;
                    sb.Append(" (");
                }
                else
                {
                    sb.Append(", ");
                }

                sb.Append(param.Key);

                if (param.Value != 1)
                {
                    sb.Append('=');
                    sb.Append(param.Value);
                }
            }

            if (!first)
            {
                sb.Append(')');
            }

            return sb.ToString();
        }

        private static readonly byte[] NewLineArray = "\n"u8.ToArray();

        private ulong CalculateShaderCacheHash(string shaderName, IReadOnlyDictionary<string, byte> arguments)
        {
            var hash = new XxHash3(StringToken.MURMUR2SEED);
            hash.Append(MemoryMarshal.AsBytes(shaderName.AsSpan()));

            var argsOrdered = SortAndFilterArguments(shaderName, arguments);
            Span<byte> valueSpan = stackalloc byte[1];

            foreach (var (key, value) in argsOrdered)
            {
                hash.Append(NewLineArray);
                hash.Append(MemoryMarshal.AsBytes(key.AsSpan()));
                hash.Append(NewLineArray);

                valueSpan[0] = value;
                hash.Append(valueSpan);
            }

            return hash.GetCurrentHashAsUInt64();
        }

#if DEBUG
        public void ReloadAllShaders(string? name = null)
        {
            Parser.Reset();

            if (name != null && (ShaderParser.ExtensionToProgramType.Keys.Any(ext => name.EndsWith($".{ext}.glSlang", StringComparison.Ordinal)) || ShaderParser.ExtensionToProgramType.Keys.Any(ext => name.EndsWith($".slang", StringComparison.Ordinal))))
            {
                // If a named shader changed (not an include), then we can only reload this shader
                name = ShaderNameFromPath(name!);
                ParsedCache.Remove(name!);
            }
            else
            {
                // Otherwise reload all shaders (common, etc)
                ParsedCache.Clear();
                name = null;
            }

            foreach (var shader in CachedShaders.Values)
            {
                if (name != null && shader.FileName != name)
                {
                    continue;
                }

                var newShader = CompileAndLinkShader(shader.Name, shader.Parameters, blocking: false);
                shader.ReplaceWith(newShader);
            }
        }

        public static void ValidateShaders(IProgress<string> progressReporter, ILogger logger, string? filter = null)
        {
            using var renderContext = new RendererContext(new ValveResourceFormat.IO.GameFileLoader(null, null), logger);
            var loader = renderContext.ShaderLoader;
            var folder = ShaderParser.GetShaderDiskPath(string.Empty);

            var vertShaders = Directory.GetFiles(folder, "*.vert.slang");
            var compShaders = Directory.GetFiles(folder, "*.comp.slang");
            var allShaders = vertShaders.Concat(compShaders).ToArray();

            // Apply filter if specified
            if (filter != null)
            {
                allShaders = [.. allShaders.Where(s => Path.GetFileName(s).Contains(filter, StringComparison.OrdinalIgnoreCase))];
            }

            var shaders = allShaders;

            GLEnvironment.Initialize(renderContext.Logger);

            foreach (var shader in shaders)
            {
                var shaderName = ShaderNameFromPath(shader);
                var vrfFileName = string.Concat(VrfInternalShaderPrefix, shaderName);

                if (IsCI)
                {
                    Console.WriteLine($"::group::Shader {shaderName}");
                }

                progressReporter.Report($"Compiling {vrfFileName}");

                if (shaderName == "texture_decode")
                {
                    loader.LoadShader(vrfFileName, new Dictionary<string, byte>
                    {
                        ["S_TYPE_TEXTURE2D"] = 1,
                    });
                    continue;
                }

                loader.LoadShader(vrfFileName);

                // Test all defines one by one
                var defines = loader.ShaderDefines[vrfFileName];
                var variants = loader.LastShaderVariantNames;
                var sourceLines = loader.LastShaderSourceLines;
                var maxValues = ExtractMaxDefineValues(defines, sourceLines);

                foreach (var define in defines.Keys)
                {
                    var maxValue = maxValues.GetValueOrDefault(define, 1);

                    for (var value = 1; value <= maxValue; value++)
                    {
                        progressReporter.Report($"Compiling {vrfFileName} with {define}={value}");

                        loader.LoadShader(vrfFileName, new Dictionary<string, byte>
                        {
                            [define] = (byte)value,
                        });
                    }
                }

                // Test all define(xxx_vfx) names
                foreach (var name in variants)
                {
                    var vfxName = string.Concat(name, ".vfx");
                    progressReporter.Report($"Compiling variant {vfxName}");

                    loader.LoadShader(vfxName);

                    // Test all defines one by one in combination with the shader variant name
                    defines = loader.ShaderDefines[vfxName];
                    foreach (var define in defines.Keys)
                    {
                        var maxValue = maxValues.GetValueOrDefault(define, 1);

                        // Test all values from 1 to maxValue
                        for (var value = 1; value <= maxValue; value++)
                        {
                            progressReporter.Report($"Compiling variant {vfxName} with {define}={value}");

                            loader.LoadShader(vfxName, new Dictionary<string, byte>
                            {
                                [define] = (byte)value,
                            });
                        }
                    }

                    // Test all defines at once with their maximum values
                    progressReporter.Report($"Compiling variant {vfxName} with all defines");

                    loader.LoadShader(vfxName, defines.Keys.ToDictionary(static d => d, d => (byte)maxValues.GetValueOrDefault(d, 1)));
                }

                if (IsCI)
                {
                    Console.WriteLine("::endgroup::");
                }
            }

            progressReporter.Report($"Validated {loader.CachedShaders.Count} shader variants");
        }

        private static bool? _isCI;
        private static bool IsCI => _isCI ??= Environment.GetEnvironmentVariable("CI") != null;

        [GeneratedRegex(@"(?<DefineName>(?:F|S|D)_\S+)\s*(?<Operator>>=|<=|>|<|==|!=)\s*(?<Value>\d+)")]
        private static partial Regex ShaderDefineConditions();

        private static Dictionary<string, int> ExtractMaxDefineValues(Dictionary<string, byte> defines, List<List<string>> allSourceLines)
        {
            var maxValues = new Dictionary<string, int>();

            foreach (var sourceLines in allSourceLines)
            {
                foreach (var line in sourceLines)
                {
                    var matches = ShaderDefineConditions().Matches(line);
                    foreach (Match match in matches)
                    {
                        var defineName = match.Groups["DefineName"].Value;
                        var operator_ = match.Groups["Operator"].Value;
                        var value = int.Parse(match.Groups["Value"].Value, CultureInfo.InvariantCulture);

                        if (defines.ContainsKey(defineName))
                        {
                            var testValue = operator_ switch
                            {
                                ">" => value + 1,
                                "<" => Math.Max(1, value - 1),
                                ">=" => value,
                                "<=" => value,
                                "==" => value,
                                "!=" => Math.Max(value + 1, 2),
                                _ => value
                            };

                            if (testValue > maxValues.GetValueOrDefault(defineName, 1))
                            {
                                maxValues[defineName] = testValue;
                            }
                        }
                    }
                }
            }

            return maxValues;
        }
#endif

        /// <summary>
        /// Exception thrown when shader compilation or linking fails.
        /// </summary>
        public class ShaderCompilerException : Exception
        {
            public ShaderCompilerException()
            {
            }

            public ShaderCompilerException(string message) : base(message)
            {
            }

            public ShaderCompilerException(string message, Exception innerException) : base(message, innerException)
            {
            }
        }
    }
}
