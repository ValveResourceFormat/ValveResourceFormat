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
using ValveResourceFormat.IO;

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
        [GeneratedRegex(@"^(?<SourceFile>[0-9]+)\((?<Line>[0-9]+)\) ?: error", RegexOptions.Multiline)]
        private static partial Regex NvidiaGlslError();

        [GeneratedRegex(@"ERROR: (?<SourceFile>[0-9]+):(?<Line>[0-9]+):", RegexOptions.Multiline)]
        private static partial Regex AmdGlslError();

        [GeneratedRegex(@"^(?<SourceFile>[0-9]+):(?<Line>[0-9]+)\((?<Column>[0-9]+)\): error", RegexOptions.Multiline)]
        private static partial Regex Mesa3dGlslError();

        private readonly Dictionary<ulong, Shader> CachedShaders = [];

        /// <summary>Gets the number of compiled shader variants currently held in the cache.</summary>
        public int ShaderCount => CachedShaders.Count;

        /// <summary>
        /// Gets every reserved texture sampler declared by the loaded shaders. Read from the source, so it is known before
        /// linking. Unlike <see cref="Shader.ReservedTexturesUsed"/>, it keeps samplers behind dropped combos. Only grows,
        /// so a renderer can poll it for additions; see <see cref="MaterialLoader.ShaderTextures"/>.
        /// </summary>
        public HashSet<string> DeclaredReservedTextures { get; } = [];

        private static readonly Dictionary<string, byte> EmptyArgs = [];
        private static readonly Lock ParserLock = new();
        private static readonly Dictionary<string, Lazy<ParsedShaderData>> ParsedCache = [];

        private static readonly ShaderParser Parser = new();

        [ThreadStatic]
        private static ShaderParser? threadParser;

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
            /// Gets or sets whether the source is marked with <c>#pragma IgnoreMaterialData</c>, see
            /// <see cref="Shader.IgnoreMaterialData"/>.
            /// </summary>
            public bool IgnoreMaterialData { get; set; }

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

            /// <summary>Gets or sets the sampler uniform annotated with <c>// RepresentativeTexture</c>.</summary>
            public string? RepresentativeTexture { get; set; }

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
                foreach (var shader in Parser.AvailableShaders.Keys)
                {
                    try
                    {
                        GetOrParseShader(shader);
                    }
                    catch (Exception e)
                    {
                        Console.Error.WriteLine(e.ToString());
                        Parser.ClearBuilder();

#if DEBUG
                        System.Diagnostics.Debugger.Break();
#endif
                    }
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
        /// <param name="shaderName">The Source 2 shader name (e.g. <c>complex.vfx</c>), or a renderer shader file name that must exist (e.g. <c>grid</c>). See <see cref="GetShaderFileByName(string, Game)"/>.</param>
        /// <param name="combos">Static combo name/value pairs to activate.</param>
        public Shader LoadShader(string shaderName, params (string ComboName, byte ComboValue)[] combos)
        {
            var args = combos.ToDictionary(c => c.ComboName, c => c.ComboValue);
            return LoadShader(shaderName, args);
        }

        /// <summary>Loads or retrieves a cached shader compiled with the given argument dictionary.</summary>
        /// <param name="shaderName">The Source 2 shader name (e.g. <c>complex.vfx</c>), or a renderer shader file name that must exist (e.g. <c>grid</c>). See <see cref="GetShaderFileByName(string, Game)"/>.</param>
        /// <param name="arguments">Static combo parameter overrides, or <see langword="null"/> for defaults.</param>
        /// <param name="blocking">When <see langword="true"/>, waits for linking to complete before returning.</param>
        public Shader LoadShader(string shaderName, IReadOnlyDictionary<string, byte>? arguments = null, bool blocking = false)
        {
            arguments ??= EmptyArgs;

            // Read on every load, as the game can be detected from a file loaded after this loader was created
            var game = RendererContext.FileLoader.GameInfo.Game;
            var shaderFileName = GetShaderFileByName(shaderName, game);

            return LoadShaderFile(shaderName, shaderFileName, game, arguments, blocking);
        }

        private Shader LoadShaderFile(string shaderName, string shaderFileName, Game game, IReadOnlyDictionary<string, byte> arguments, bool blocking)
        {
            var parsedData = GetOrParseShader(shaderFileName);
            var shaderCacheHash = CalculateShaderCacheHash(shaderName, shaderFileName, game, parsedData.Defines, arguments);

            if (CachedShaders.TryGetValue(shaderCacheHash, out var cachedShader))
            {
                return cachedShader;
            }

            var shader = CompileAndLinkShader(shaderName, shaderFileName, parsedData, arguments, blocking: blocking);
            CachedShaders[shaderCacheHash] = shader;
            return shader;
        }

        /// <summary>
        /// Collects the link status of every shader loaded so far, which loading does not wait for.
        /// Until this runs a shader is linked by the first draw that uses it, and a draw call is queued before that
        /// happens: run it before rendering a frame that has to see each shader's final state, such as a pre-warm
        /// pass. Must be called on the thread holding the GL context.
        /// </summary>
        public void LinkLoadedShaders()
        {
            foreach (var shader in CachedShaders.Values)
            {
                shader.EnsureLoaded();
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
            Lazy<ParsedShaderData>? parsed;

            using (ParserLock.EnterScope())
            {
                if (!ParsedCache.TryGetValue(shaderFileName, out parsed))
                {
                    var availableStages = Parser.AvailableShaders.GetValueOrDefault(shaderFileName)
                        ?? throw new FileNotFoundException($"Shader '{shaderFileName}' does not exist.");

                    // Not caching a failure, so the next load parses again
                    parsed = new(() => ParseShader(shaderFileName, availableStages), LazyThreadSafetyMode.PublicationOnly);
                    ParsedCache[shaderFileName] = parsed;
                }
            }

            return parsed.Value;
        }

        private static ParsedShaderData ParseShader(string shaderFileName, bool[] availableStages)
        {
            var parsedData = new ParsedShaderData();
            var parser = threadParser ??= new();

            if (availableStages.Length == 0
            || !availableStages[(int)ShaderProgramType.Vertex] && !availableStages[(int)ShaderProgramType.Compute])
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

                var shaderSource = parser.PreprocessShader(nameWithExtension, parsedData);
                parsedData.Sources[@type] = shaderSource;
                parser.ClearBuilder();
            }

            parsedData.GlobalsLayout = GlobalsLayout.Build(parsedData.GlobalsDeclarations);

            return parsedData;
        }

        private Shader CompileAndLinkShader(string shaderName, string shaderFileName, ParsedShaderData parsedData, IReadOnlyDictionary<string, byte> arguments, bool blocking = true)
        {
            {
                var sources = parsedData.Sources;

                if (shaderName == "depth_only" && arguments.Count == 0)
                {
                    sources = new(sources);
                    sources.Remove(ShaderProgramType.Fragment);
                }

                var shaderObjects = new int[sources.Count];
                var shaderSources = new string[sources.Count];
                var s = 0;
                foreach (var (stage, source) in sources)
                {
                    shaderObjects[s] = GraphicsDevice.CreateShader(stage, shaderFileName);
                    shaderSources[s] = source!;
                    s++;
                }

                CompileShaderObjects(shaderObjects, shaderSources, shaderName, arguments, parsedData);

                var shaderProgram = GraphicsDevice.CreateProgram(shaderFileName);

                // Known before linking, as the renderer binds textures before the first draw that samples them.
                // Only grows.
                DeclaredReservedTextures.UnionWith(parsedData.ReservedTextures);

                var shader = new Shader(shaderName, RendererContext)
                {
#if DEBUG
                    FileName = shaderFileName,
#endif

                    SourceFile = shaderFileName,
                    Parameters = arguments,
                    Defines = parsedData.Defines,
                    GlobalsLayout = parsedData.GlobalsLayout,
                    Program = shaderProgram,
                    ShaderObjects = shaderObjects,
                    RenderModes = parsedData.RenderModes,
                    IgnoreMaterialData = parsedData.IgnoreMaterialData,
                    UniformNames = parsedData.Uniforms,
                    SrgbUniforms = parsedData.SrgbUniforms,
                    SamplerUserConfigUniforms = parsedData.SamplerUserConfigUniforms,
                    RepresentativeTexture = parsedData.RepresentativeTexture,

                    // Copied, because the parsed data is shared by every variant of this shader while the
                    // linker trims each variant's own set down to what its combos kept.
                    ReservedTexturesUsed = [.. parsedData.ReservedTextures],
                };

                foreach (var shaderObj in shaderObjects)
                {
                    GL.AttachShader(shader.Program, shaderObj);
                }

                // Not getting link status straight away allows the driver to perform parallelized shader compilation
                GL.LinkProgram(shader.Program);

                // Not getting link status straight away allows the driver to perform parallelized shader compilation
                if (blocking)
                {
                    shader.EnsureLoaded();
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
        }

        internal static void ThrowLinkFailure(Shader shader)
        {
            var shaderFileName = shader.SourceFile;
            var parsedData = GetOrParseShader(shaderFileName);
            var argsDescription = GetArgumentDescription(SortAndFilterArguments(parsedData.Defines, shader.Parameters));

            ThrowShaderError(shader.FailureLog ?? string.Empty, string.Concat(shaderFileName, argsDescription), shader.Name, "Failed to link shader", parsedData);
        }

        private static void CompileShaderObjects(int[] shaderObjects, string[] shaderSources, string originalShaderName, IReadOnlyDictionary<string, byte> arguments, ParsedShaderData parsedData)
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
                CompileShaderObject(shaderObjects[i], headerText, shaderSources[i]);
            }
        }

        private static void CompileShaderObject(int shader, string headerText, string shaderText)
        {
            string[] sources = [headerText, shaderText];
            int[] lengths = [sources[0].Length, sources[1].Length];

            GL.ShaderSource(shader, sources.Length, sources, lengths);

            GL.CompileShader(shader);
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
        /// <param name="shaderFilePath">The full path or file name of the shader (e.g. <c>/path/fallback.vert.slang</c>).</param>
        /// <returns>The shader name without directory or extension (e.g. <c>fallback</c>).</returns>
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
        private const string CoreShaderFolder = "core";
        private const string FallbackShaderFile = "fallback";

        /// <summary>
        /// Resolves a shader name to the renderer shader file that draws it. Mappings registered in
        /// <see cref="ShaderRegistry"/> take priority over the built-in ones.
        /// </summary>
        /// <param name="shaderName">
        /// A Source 2 shader name ending in <c>.vfx</c>, mapped to the closest renderer shader, or <c>fallback</c>
        /// if unknown. Any other name is a renderer shader file to load directly, and must exist.
        /// </param>
        /// <returns>The renderer shader name without stage or extension (e.g. <c>fallback</c>).</returns>
        public static string GetShaderFileByName(string shaderName) => GetShaderFileByName(shaderName, Game.Unknown);

        /// <summary>
        /// Resolves a shader name to the renderer shader file that draws it in a game. A Source 2 shader name is looked
        /// up as <c>&lt;game&gt;/&lt;name&gt;</c> in the folder of the game, then in the folders of <see cref="GameFallbackOrder"/>,
        /// then as <c>core/&lt;name&gt;</c> for the shaders every game ships, and falls back to <c>fallback</c>.
        /// </summary>
        /// <param name="shaderName">A Source 2 shader name ending in <c>.vfx</c>, or a renderer shader file name.</param>
        /// <param name="game">The game the shader is drawn for.</param>
        /// <returns>The renderer shader name without stage or extension (e.g. <c>hlvr/vr_simple</c>).</returns>
        public static string GetShaderFileByName(string shaderName, Game game)
        {
            if (ShaderRegistry.Mappings.TryGetValue(shaderName, out var customShaderFile))
            {
                return customShaderFile;
            }

            // TODO: Consider naming renderer shaders with a .slang extension, so that they read as explicitly as .vfx names do
            if (!IsVfxShaderName(shaderName))
            {
                // Not a Valve name, so it is a renderer shader file.
                // Unknown names throw instead of falling back to 'fallback'.
                return shaderName.StartsWith(VrfInternalShaderPrefix, StringComparison.Ordinal)
                    ? shaderName[VrfInternalShaderPrefix.Length..]
                    : shaderName;
            }

            return FindShaderFile(shaderName, game) ?? FallbackShaderFile;
        }

        /// <summary>
        /// The game folders searched, in this order, for a shader the folder of the current game does not have. The
        /// games first are the ones whose files draw an unknown game and a game without a file best.
        /// </summary>
        public static readonly IReadOnlyList<Game> GameFallbackOrder =
        [
            Game.Dota, Game.Csgo, Game.Citadel, Game.Hlx, Game.Steampal, Game.Vr, Game.Steamtours, Game.Hlvr,
        ];

        // The shaders of a game are in the folder named after its Game member in lowercase, which is its mod folder
        private static string GetGameShaderFolder(Game game) => game.ToString().ToLowerInvariant();

        private static string GetShaderFile(string folder, string name)
            => string.Create(CultureInfo.InvariantCulture, $"{folder}/{name}");

        private static string? FindShaderFile(string shaderName, Game game)
        {
            var name = shaderName[..^VfxExtension.Length];
            var available = Parser.AvailableShaders;
            List<string> folders = [];

            if (game != Game.Unknown)
            {
                folders.Add(GetGameShaderFolder(game));
            }

            foreach (var fallbackGame in GameFallbackOrder)
            {
                folders.Add(GetGameShaderFolder(fallbackGame));
            }

            folders.Add(CoreShaderFolder);

            foreach (var folder in folders)
            {
                var file = GetShaderFile(folder, name);

                if (available.ContainsKey(file))
                {
                    return file;
                }
            }

            return null;
        }

        /// <summary>The file extension of Source 2 shader names (<c>.vfx</c>).</summary>
        public const string VfxExtension = ".vfx";

        private static bool IsVfxShaderName(string shaderName)
        {
            return shaderName.EndsWith(VfxExtension, StringComparison.Ordinal);
        }

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

        private static List<KeyValuePair<string, byte>> SortAndFilterArguments(Dictionary<string, byte> defines, IReadOnlyDictionary<string, byte> arguments)
        {
            var result = new List<KeyValuePair<string, byte>>(arguments.Count);

            foreach (var argument in arguments)
            {
                // Shader defines should already default to zero
                if (argument.Value != 0 && defines.ContainsKey(argument.Key))
                {
                    result.Add(argument);
                }
            }

            result.Sort(static (a, b) => string.CompareOrdinal(a.Key, b.Key));

            return result;
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

        private static ulong CalculateShaderCacheHash(string shaderName, string shaderFileName, Game game, Dictionary<string, byte> defines, IReadOnlyDictionary<string, byte> arguments)
        {
            var hash = new XxHash3();
            hash.Append(MemoryMarshal.AsBytes(shaderName.AsSpan()));
            hash.Append(MemoryMarshal.AsBytes(shaderFileName.AsSpan()));

            Span<byte> valueSpan = stackalloc byte[1];
            valueSpan[0] = (byte)game;
            hash.Append(valueSpan);

            var argsOrdered = SortAndFilterArguments(defines, arguments);

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

            var changedFile = name?.Replace(Path.DirectorySeparatorChar, '/');
            HashSet<string> staleShaders = [];

            foreach (var (shaderFileName, parsed) in ParsedCache)
            {
                if (changedFile == null || parsed.IsValueCreated && parsed.Value.SourceFiles.Contains(changedFile))
                {
                    staleShaders.Add(shaderFileName);
                }
            }

            foreach (var shaderFileName in staleShaders)
            {
                ParsedCache.Remove(shaderFileName);
            }

            List<(Shader Shader, Shader Replacement)> compiled = [];
            List<string> failures = [];

            try
            {
                foreach (var shader in CachedShaders.Values)
                {
                    if (changedFile != null && !staleShaders.Contains(shader.SourceFile))
                    {
                        continue;
                    }

                    var fileName = GetShaderFileByName(shader.Name, RendererContext.FileLoader.GameInfo.Game);
                    var parsed = GetOrParseShader(fileName);

                    try
                    {
                        compiled.Add((shader, CompileAndLinkShader(shader.Name, fileName, parsed, shader.Parameters, blocking: false)));
                    }
                    catch (ShaderCompilerException e)
                    {
                        failures.Add(e.Message);
                    }
                }
            }
            catch
            {
                foreach (var (_, replacement) in compiled)
                {
                    replacement.Delete();
                }

                throw;
            }

            // Compiled as a batch first, so that the driver links them in parallel. A shader whose
            // replacement fails to compile or link keeps its previous program, so the scene still draws.
            List<Shader> reloaded = [];

            foreach (var (shader, replacement) in compiled)
            {
                GL.GetProgram(replacement.Program, GetProgramParameterName.LinkStatus, out var linkStatus);

                if (linkStatus == 1)
                {
                    shader.ReplaceWith(replacement);
                    reloaded.Add(shader);
                    continue;
                }

                try
                {
                    replacement.EnsureLoaded();
                }
                catch (ShaderCompilerException e)
                {
                    failures.Add(e.Message);
                }

                replacement.Delete();
            }

            foreach (var shader in reloaded)
            {
                shader.EnsureLoaded();
            }

            if (failures.Count > 0)
            {
                throw new ShaderCompilerException(string.Join("\n\n", failures));
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
