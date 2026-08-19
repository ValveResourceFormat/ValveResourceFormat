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

namespace ValveResourceFormat.Renderer.Shaders
{
    /// <summary>
    /// Shader stage types in the rendering pipeline.
    /// </summary>
    public enum ShaderProgramType
    {
        /// <summary>Vertex shader stage.</summary>
        Vertex = 0,
        /// <summary>Fragment (pixel) shader stage.</summary>
        Fragment = 1,
        /// <summary>Compute shader stage.</summary>
        Compute = 2,
        /// <summary>Sentinel value equal to the number of shader stages.</summary>
        Max = 3,
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

        /// <summary>Gets the number of compiled shader variants currently held in the cache.</summary>
        public int ShaderCount => CachedShaders.Count;

        /// <summary>
        /// Gets every reserved texture sampler declared by the shaders loaded so far. Read off the shader source, so
        /// unlike a shader's own <see cref="Shader.ReservedTexturesUsed"/> it is known before the program links, and
        /// it includes samplers behind a combo the linker went on to drop. Grow only, so a renderer can pick up what
        /// was added since it last looked; see <see cref="MaterialLoader.ShaderTextures"/>.
        /// </summary>
        public HashSet<string> DeclaredReservedTextures { get; } = [];

        private static readonly Dictionary<string, byte> EmptyArgs = [];
        private static readonly Lock ParserLock = new();
        private static readonly Dictionary<string, ParsedShaderData> ParsedCache = [];

        private static readonly ShaderParser Parser = new();

        private readonly RendererContext RendererContext;

        /// <summary>
        /// Preprocessed shader source with defines, uniforms, and compiled stage code.
        /// </summary>
        public class ParsedShaderData
        {
            /// <summary>Gets the map of define names to their default byte values extracted from the shader source.</summary>
            public Dictionary<string, byte> Defines { get; } = [];

            /// <summary>Gets the set of render mode names declared in the shader source.</summary>
            public HashSet<string> RenderModes { get; } = [];

            /// <summary>Gets the set of uniform names declared in the shader source.</summary>
            public HashSet<string> Uniforms { get; } = [];

            /// <summary>
            /// Gets the <c>#extension</c> directives hoisted out of the shader source. They have to precede
            /// every non-preprocessor token, and the packed uniform block is one, so the header emits them.
            /// </summary>
            public HashSet<string> Extensions { get; } = [];

            /// <summary>Gets the packable uniform declarations collected from every stage, in source order.</summary>
            public List<GlobalsDeclaration> GlobalsDeclarations { get; } = [];

            /// <summary>
            /// Gets the packed layout of <see cref="GlobalsDeclarations"/>. Built once all stages have been
            /// preprocessed, and shared by every static combo variant compiled from this source.
            /// </summary>
            public GlobalsLayout GlobalsLayout { get; set; } = GlobalsLayout.Empty;

            /// <summary>Gets the set of uniform names annotated with <c>// SrgbRead(true)</c>.</summary>
            public HashSet<string> SrgbUniforms { get; } = [];

            /// <summary>Gets the set of sampler uniform names annotated with <c>// Sampler(UserConfig)</c>.</summary>
            public HashSet<string> SamplerUserConfigUniforms { get; } = [];

            /// <summary>
            /// Gets the reserved texture samplers declared in the shader source. A superset of what the linker
            /// keeps, since the source is read before any combo is resolved; see <see cref="Shader.ReservedTexturesUsed"/>.
            /// </summary>
            public HashSet<string> ReservedTextures { get; } = [];

            /// <summary>Gets the preprocessed GLSL source text for each shader stage.</summary>
            public Dictionary<ShaderProgramType, string> Sources { get; } = [];

            /// <summary>Gets the ordered list of source file names included during preprocessing, used for error reporting.</summary>
            public List<string> SourceFiles { get; } = [];
#if DEBUG
            /// <summary>Gets the per-file line lists used to display source lines in error messages (debug builds only).</summary>
            public List<List<string>> SourceFileLines { get; } = [];
#endif
        }

        static ShaderLoader()
        {
            Task.Run(() =>
            {
                try
                {
                    foreach (var shader in Parser.AvailableShaders.Keys)
                    {
                        GetOrParseShader(shader);
                    }
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine(e.ToString());
                    Parser.ClearBuilder();

#if DEBUG
                    System.Diagnostics.Debugger.Break();
#endif
                }
            });
        }

        /// <summary>Initializes a new instance of the <see cref="ShaderLoader"/> class.</summary>
        /// <param name="rendererContext">The renderer context that owns this loader.</param>
        public ShaderLoader(RendererContext rendererContext)
        {
            RendererContext = rendererContext;
        }

        /// <summary>Loads or retrieves a cached shader compiled with the specified static combos.</summary>
        /// <param name="shaderName">The Source 2 shader name (e.g. <c>complex.vfx</c>), or a renderer shader file name that must exist (e.g. <c>grid</c>). See <see cref="GetShaderFileByName"/>.</param>
        /// <param name="combos">Static combo name/value pairs to activate.</param>
        public Shader LoadShader(string shaderName, params (string ComboName, byte ComboValue)[] combos)
        {
            var args = combos.ToDictionary(c => c.ComboName, c => c.ComboValue);
            return LoadShader(shaderName, args);
        }

        /// <summary>Loads or retrieves a cached shader compiled with the given argument dictionary.</summary>
        /// <param name="shaderName">The Source 2 shader name (e.g. <c>complex.vfx</c>), or a renderer shader file name that must exist (e.g. <c>grid</c>). See <see cref="GetShaderFileByName"/>.</param>
        /// <param name="arguments">Static combo parameter overrides, or <see langword="null"/> for defaults.</param>
        /// <param name="blocking">When <see langword="true"/>, waits for linking to complete before returning.</param>
        public Shader LoadShader(string shaderName, IReadOnlyDictionary<string, byte>? arguments = null, bool blocking = true)
        {
            arguments ??= EmptyArgs;

            var shaderFileName = GetShaderFileByName(shaderName);
            var parsedData = GetOrParseShader(shaderFileName);
            var shaderCacheHash = CalculateShaderCacheHash(shaderName, parsedData.Defines, arguments);

            if (CachedShaders.TryGetValue(shaderCacheHash, out var cachedShader))
            {
                return cachedShader;
            }

            var shader = CompileAndLinkShader(shaderName, shaderFileName, parsedData, arguments, blocking: blocking);
            CachedShaders[shaderCacheHash] = shader;
            return shader;
        }

        /// <summary>
        /// Collects the link status of every shader loaded so far, which materials load without waiting for.
        /// Until this runs a shader is linked by the first draw that uses it, and a draw call is queued before that
        /// happens: run it before rendering a frame that has to see each shader's final state, such as a pre-warm
        /// pass. Must be called on the thread holding the GL context.
        /// </summary>
        public void LinkLoadedShaders()
        {
            foreach (var shader in CachedShaders.Values)
            {
                if (!shader.EnsureLoaded())
                {
                    GL.GetProgramInfoLog(shader.Program, out var log);
                    RendererContext.Logger.LogError("Shader '{ShaderName}' failed to link: {Log}", shader.Name, log);
                }
            }
        }

        /// <summary>
        /// Drops all preprocessed shader sources and rediscovers the available shader files. Called when the
        /// <see cref="ShaderRegistry"/> changes; shader programs that have already been compiled are not affected.
        /// </summary>
        internal static void InvalidateParsedShaders()
        {
            using var _ = ParserLock.EnterScope();

            ParsedCache.Clear();
            Parser.ClearBuilder();
            Parser.RefreshAvailableShaders();
        }

        private static ParsedShaderData GetOrParseShader(string shaderFileName)
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

                var nameWithExtension = $"{shaderFileName}.{extension}.slang";

                var shaderSource = Parser.PreprocessShader(nameWithExtension, parsedData);
                parsedData.Sources[@type] = shaderSource;
                Parser.ClearBuilder();
            }

            parsedData.GlobalsLayout = GlobalsLayout.Build(parsedData.GlobalsDeclarations);

            ParsedCache[shaderFileName] = parsedData;
            return parsedData;
        }

        private Shader CompileAndLinkShader(string shaderName, string shaderFileName, ParsedShaderData parsedData, IReadOnlyDictionary<string, byte> arguments, bool blocking = true)
        {
            var shaderProgram = -1;

            try
            {
                var sources = parsedData.Sources;

                if (shaderName == "depth_only" && arguments.Count == 0)
                {
                    sources = new(sources);
                    sources.Remove(ShaderProgramType.Fragment);
                }

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

                // What the source declares is known before the program links, and the renderer needs it that
                // early to have a texture bound by the first draw that samples it. Only ever grows.
                DeclaredReservedTextures.UnionWith(parsedData.ReservedTextures);

                var shader = new Shader(shaderName, RendererContext)
                {
#if DEBUG
                    FileName = shaderFileName,
#endif

                    Parameters = arguments,
                    GlobalsLayout = parsedData.GlobalsLayout,
                    Program = shaderProgram,
                    ShaderObjects = shaderObjects,
                    RenderModes = parsedData.RenderModes,
                    UniformNames = parsedData.Uniforms,
                    SrgbUniforms = parsedData.SrgbUniforms,
                    SamplerUserConfigUniforms = parsedData.SamplerUserConfigUniforms,

                    // Copied, because the parsed data is shared by every variant of this shader while the
                    // linker trims each variant's own set down to what its combos kept.
                    ReservedTexturesUsed = [.. parsedData.ReservedTextures],
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
                        ThrowShaderError(log, string.Concat(shaderFileName, GetArgumentDescription(arguments)), shaderName, "Failed to link shader", parsedData);
                    }
                }

                var argsDescription = GetArgumentDescription(SortAndFilterArguments(parsedData.Defines, arguments));
                var compiledStatus = blocking ? " and linked" : string.Empty;

                // Only Valve shader names are resolved to a different file, so naming both would be noise
                if (IsVfxShaderName(shaderName))
                {
                    RendererContext.Logger.LogInformation("Shader '{ShaderName}' as '{ShaderFileName}'{ArgsDescription} compiled{CompiledStatus} successfully (program={Program})", shaderName, shaderFileName, argsDescription, compiledStatus, shader.Program);
                }
                else
                {
                    RendererContext.Logger.LogInformation("Shader '{ShaderName}'{ArgsDescription} compiled{CompiledStatus} successfully (program={Program})", shaderName, argsDescription, compiledStatus, shader.Program);
                }

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
        }

        private static void CompileShaderObjects(int[] shaderObjects, string[] shaderSources, string shaderFile, string originalShaderName, IReadOnlyDictionary<string, byte> arguments, ParsedShaderData parsedData)
        {
            var header = new StringBuilder();
            header.Append(ShaderParser.ExpectedShaderVersion);
            header.Append('\n');

            header.Append("#extension GL_KHR_shader_subgroup_arithmetic : enable\n");
            header.Append("#extension GL_KHR_shader_subgroup_vote : enable\n");

            foreach (var extension in parsedData.Extensions)
            {
                header.Append(extension);
                header.Append('\n');
            }

            // Only Valve shader names activate a shader variant, renderer shader files are loaded as themselves
            var variantName = IsVfxShaderName(originalShaderName)
                ? $"GameVfx_{Path.GetFileNameWithoutExtension(originalShaderName)}"
                : null;

            // Add all defines (with argument overrides or defaults)
            foreach (var (defineName, defaultValue) in parsedData.Defines)
            {
                var value = defaultValue;

                if (defineName == variantName)
                {
                    value = 1;
                }
                else if (arguments.TryGetValue(defineName, out var argValue))
                {
                    value = argValue;
                }

                header.Append("#define ");
                header.Append(defineName);
                header.Append(' ');
                header.Append(value.ToString(CultureInfo.InvariantCulture));
                header.Append('\n');
            }

            header.Append(parsedData.GlobalsLayout.BlockSource);

            var headerText = header.ToString();

            for (var i = 0; i < shaderObjects.Length; i++)
            {
                CompileShaderObject(shaderObjects[i], shaderFile, originalShaderName, arguments, headerText, shaderSources[i], parsedData);
            }
        }

        private static void CompileShaderObject(int shader, string shaderFile, ReadOnlySpan<char> originalShaderName, IReadOnlyDictionary<string, byte> arguments, string headerText, string shaderText, ParsedShaderData parsedData)
        {
            string[] sources = [headerText, shaderText];
            int[] lengths = [sources[0].Length, sources[1].Length];

            GL.ShaderSource(shader, sources.Length, sources, lengths);

            GL.CompileShader(shader);
            GL.GetShader(shader, ShaderParameter.CompileStatus, out var shaderStatus);

            if (shaderStatus != 1)
            {
                GL.GetShaderInfoLog(shader, out var log);

                ThrowShaderError(log, string.Concat(shaderFile, GetArgumentDescription(arguments)), originalShaderName, "Failed to set up shader", parsedData);
            }
        }

        private static void ThrowShaderError(string info, string shaderFile, ReadOnlySpan<char> originalShaderName, string errorType, ParsedShaderData parsedData)
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

                if (errorSourceFile >= 0 && errorSourceFile < parsedData.SourceFiles.Count)
                {
                    sourceFile = parsedData.SourceFiles[errorSourceFile];
                }
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
                if (errorLine > 0 && errorLine <= parsedData.SourceFileLines[errorSourceFile].Count)
                {
                    info += $":\n{parsedData.SourceFileLines[errorSourceFile][errorLine - 1]}\n";
                }
#endif
            }

            throw new ShaderCompilerException($"{errorType} {shaderFile} (original={originalShaderName}):\n\n{info}");
        }

        /// <summary>Returns the bare shader name from a full shader file path by stripping the trailing stage extension (<c>.vert.slang</c>, <c>.frag.slang</c>, or <c>.comp.slang</c>).</summary>
        /// <param name="shaderFilePath">The full path or file name of the shader (e.g. <c>/path/complex.vert.slang</c>).</param>
        /// <returns>The shader name without directory or extension (e.g. <c>complex</c>).</returns>
        public static string ShaderNameFromPath(string shaderFilePath)
        {
            return Path.GetFileName(shaderFilePath[..^ShaderFileExtension.Length]);
        }

        /// <summary>The file extension for Slang shader source files (<c>.slang</c>).</summary>
        public const string SlangExtension = ".slang";

        /// <summary>The file extension used to identify vertex shader entry points (<c>.vert.slang</c>).</summary>
        public const string ShaderFileExtension = ".vert.slang";
        // No longer used by the renderer itself, kept so that names that were required before still resolve
        const string VrfInternalShaderPrefix = "vrf.";

        /// <summary>
        /// Resolves a shader name to the renderer shader file that draws it. Mappings registered in
        /// <see cref="ShaderRegistry"/> take priority over the built-in ones.
        /// </summary>
        /// <param name="shaderName">
        /// A Source 2 shader name ending in <c>.vfx</c>, which is mapped to the renderer shader that best matches it and
        /// falls back to <c>complex</c> when it is unknown. Any other name is the renderer shader file to load directly,
        /// and must exist.
        /// </param>
        /// <returns>The renderer shader name without stage or extension (e.g. <c>complex</c>).</returns>
        public static string GetShaderFileByName(string shaderName)
        {
            if (ShaderRegistry.Mappings.TryGetValue(shaderName, out var customShaderFile))
            {
                return customShaderFile;
            }

            // TODO: Consider naming renderer shaders with a .slang extension, so that they read as explicitly as .vfx names do
            if (!IsVfxShaderName(shaderName))
            {
                // Not a Valve shader name, so it names a renderer shader file directly.
                // Unknown names are not silently drawn with 'complex', loading them throws instead.
                return shaderName.StartsWith(VrfInternalShaderPrefix, StringComparison.Ordinal)
                    ? shaderName[VrfInternalShaderPrefix.Length..]
                    : shaderName;
            }

            return GetBuiltinShaderFileByName(shaderName);
        }

        /// <summary>The file extension of Source 2 shader names (<c>.vfx</c>).</summary>
        public const string VfxExtension = ".vfx";

        private static bool IsVfxShaderName(string shaderName)
        {
            return shaderName.EndsWith(VfxExtension, StringComparison.Ordinal);
        }

        // Map Valve's shader names to shader files VRF has
        private static string GetBuiltinShaderFileByName(string shaderName) => shaderName switch
        {
            "sky.vfx" => "sky",
            "tools_sprite.vfx" => "sprite",
            "global_lit_simple.vfx" => "global_lit_simple",
            "vr_black_unlit.vfx" or "csgo_black_unlit.vfx" => "vr_black_unlit",
            "vr_unlit.vfx" => "vr_unlit",
            "vr_standard.vfx" => "vr_standard",
            "water_dota.vfx" => "water",
            "csgo_water_fancy.vfx" => "water_csgo",
            "hero.vfx" or "hero_underlords.vfx" => "dota_hero",
            "multiblend.vfx" => "multiblend",
            "csgo_effects.vfx" => "csgo_effects",
            "csgo_environment.vfx" or "csgo_environment_blend.vfx" => "csgo_environment",
            "environment_blend.vfx" => "environment_blend",
            "pbr.vfx" => "pbr",
            "citadel_overlay.vfx" => "citadel_overlay",

            _ => "complex",
        };

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>Releases managed resources held by this loader.</summary>
        /// <param name="disposing"><see langword="true"/> when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            DeleteCachedShaders();
        }

        /// <summary>
        /// Deletes every compiled shader program and empties the cache. Shaders handed out before this runs are
        /// left pointing at a deleted program, so only call it once nothing can draw with them.
        /// </summary>
        private void DeleteCachedShaders()
        {
            foreach (var shader in CachedShaders.Values)
            {
                shader.Delete();
            }

            CachedShaders.Clear();
        }

        private static IEnumerable<KeyValuePair<string, byte>> SortAndFilterArguments(Dictionary<string, byte> defines, IReadOnlyDictionary<string, byte> arguments)
        {
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

        private static ulong CalculateShaderCacheHash(string shaderName, Dictionary<string, byte> defines, IReadOnlyDictionary<string, byte> arguments)
        {
            var hash = new XxHash3(StringToken.MURMUR2SEED);
            hash.Append(MemoryMarshal.AsBytes(shaderName.AsSpan()));

            var argsOrdered = SortAndFilterArguments(defines, arguments);
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
        private static bool? _isCI;
        private static bool IsCI => _isCI ??= Environment.GetEnvironmentVariable("CI") != null;

        /// <summary>Recompiles all cached shaders, or only those derived from the given file if specified (debug builds only).</summary>
        /// <param name="name">Optional shader file name that changed; when <see langword="null"/> all shaders are reloaded.</param>
        public void ReloadAllShaders(string? name = null)
        {
            Parser.ClearBuilder();

            // Picks up shader files that were created after startup, including ones in mounted directories
            Parser.RefreshAvailableShaders();

            if (name != null && ShaderParser.ExtensionToProgramType.Keys.Any(ext => name.EndsWith($".{ext}.slang", StringComparison.Ordinal)))
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

                var fileName = GetShaderFileByName(shader.Name);
                var parsed = GetOrParseShader(fileName);
                var newShader = CompileAndLinkShader(shader.Name, fileName, parsed, shader.Parameters, blocking: false);
                shader.ReplaceWith(newShader);
            }
        }
#endif

        /// <summary>
        /// Exception thrown when shader compilation or linking fails.
        /// </summary>
        public class ShaderCompilerException : Exception
        {
            /// <summary>Initializes a new instance of the <see cref="ShaderCompilerException"/> class.</summary>
            public ShaderCompilerException()
            {
            }

            /// <summary>Initializes a new instance of the <see cref="ShaderCompilerException"/> class with a message.</summary>
            /// <param name="message">The error message describing the compilation or link failure.</param>
            public ShaderCompilerException(string message) : base(message)
            {
            }

            /// <summary>Initializes a new instance of the <see cref="ShaderCompilerException"/> class with a message and an inner exception.</summary>
            /// <param name="message">The error message describing the compilation or link failure.</param>
            /// <param name="innerException">The exception that caused this exception.</param>
            public ShaderCompilerException(string message, Exception innerException) : base(message, innerException)
            {
            }
        }
    }
}
