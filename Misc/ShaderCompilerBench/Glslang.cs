using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

// glslang is loaded by absolute path through a DllImportResolver, so the search path the runtime
// would otherwise use never comes into play. Declaring the safe set keeps CA5392 satisfied.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]

namespace ShaderCompilerBench;

/// <summary>Who the GLSL being compiled was written for, which decides how much glslang has to fix up.</summary>
internal enum GlslOrigin
{
    /// <summary>Hand written for the driver, with no SPIR-V layout decorations.</summary>
    ForTheDriver,

    /// <summary>Emitted by a compiler that already assigned every location and binding.</summary>
    ForSpirv,
}

/// <summary>
/// Compiles GLSL to OpenGL flavoured SPIR-V with the reference front end, through glslang's C API.
/// This is the path that skips Slang entirely: the shaders the renderer already has, compiled
/// offline into SPIR-V 1.0 and handed to the driver as a binary.
/// </summary>
internal static partial class Glslang
{
    private const string Library = "glslang";
    private const string ResourceLibrary = "glslang-default-resource-limits";

    private const int SourceGlsl = 1;
    private const int StageVertex = 0;
    private const int StageFragment = 4;
    private const int ClientOpenGl = 2;
    private const int ClientVersionOpenGl450 = 450;
    private const int TargetLanguageSpv = 1;

    /// <summary>
    /// glslang_target_language_version_t, which packs the SPIR-V major and minor version. OpenGL 4.6
    /// asks for 1.0, but shaders using subgroup operations need at least 1.3, and NVIDIA takes it.
    /// </summary>
    public static int TargetSpirvVersion { get; set; } = 1 << 16;

    private const int ProfileCore = 1 << 1;

    /// <summary>GLSLANG_MSG_SPV_RULES. Deliberately without GLSLANG_MSG_VULKAN_RULES, which is what makes the output OpenGL flavoured.</summary>
    private const int MessagesSpvRules = 1 << 3;

    /// <summary>
    /// GLSLANG_SHADER_AUTO_MAP_BINDINGS | GLSLANG_SHADER_AUTO_MAP_LOCATIONS. SPIR-V wants every
    /// uniform and interface variable to carry an explicit location or binding, which GLSL written
    /// for the driver's own front end has no reason to declare.
    /// </summary>
    private const int OptionsAutoMap = (1 << 0) | (1 << 1);

    /// <summary>
    /// Where the renderer's loose <c>uniform vec4 g_Foo;</c> declarations end up. OpenGL SPIR-V has
    /// no default uniform block, so glslang has to be told to gather them into a real one.
    /// </summary>
    public const string DefaultUniformBlockName = "g_DefaultUniformBlock";

    /// <summary>
    /// glslang_input_t. The field order has to match glslang's header exactly, including the three
    /// HLSL fields this bench never sets, or every field after <c>Code</c> lands on the wrong one.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public int Language;
        public int Stage;
        public int Client;
        public int ClientVersion;
        public int TargetLanguage;
        public int TargetLanguageVersion;
        public nint Code;
        public nint EntryPoint;
        public nint SourceEntryPoint;
        public int HlslFunctionality1;
        public int DefaultVersion;
        public int DefaultProfile;
        public int ForceDefaultVersionAndProfile;
        public int ForwardCompatible;
        public int Messages;
        public nint Resource;
        public nint IncludeLocal;
        public nint IncludeSystem;
        public nint FreeIncludeResult;
        public nint CallbacksContext;
    }

    /// <summary>The size glslang's header says <see cref="Input"/> has, checked once before use.</summary>
    private const int ExpectedInputSize = 112;

    [LibraryImport(Library)]
    private static partial int glslang_initialize_process();

    [LibraryImport(Library)]
    private static partial void glslang_finalize_process();

    [LibraryImport(Library)]
    private static partial nint glslang_shader_create(in Input input);

    [LibraryImport(Library)]
    private static partial void glslang_shader_delete(nint shader);

    [LibraryImport(Library)]
    private static partial int glslang_shader_preprocess(nint shader, in Input input);

    [LibraryImport(Library)]
    private static partial int glslang_shader_parse(nint shader, in Input input);

    [LibraryImport(Library)]
    private static partial nint glslang_shader_get_preprocessed_code(nint shader);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    private static partial void glslang_shader_set_preprocessed_code(nint shader, string code);

    [LibraryImport(Library)]
    private static partial nint glslang_shader_get_info_log(nint shader);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    private static partial void glslang_shader_set_default_uniform_block_name(nint shader, string name);

    [LibraryImport(Library)]
    private static partial void glslang_shader_set_default_uniform_block_set_and_binding(nint shader, uint set, uint binding);

    [LibraryImport(Library)]
    private static partial void glslang_shader_set_options(nint shader, int options);

    [LibraryImport(Library)]
    private static partial nint glslang_program_create();

    [LibraryImport(Library)]
    private static partial void glslang_program_delete(nint program);

    [LibraryImport(Library)]
    private static partial void glslang_program_add_shader(nint program, nint shader);

    [LibraryImport(Library)]
    private static partial int glslang_program_link(nint program, int messages);

    [LibraryImport(Library)]
    private static partial int glslang_program_map_io(nint program);

    [LibraryImport(Library)]
    private static partial nint glslang_glsl_resolver_create(nint program, int stage);

    [LibraryImport(Library)]
    private static partial void glslang_glsl_resolver_delete(nint resolver);

    [LibraryImport(Library)]
    private static partial nint glslang_glsl_mapper_create();

    [LibraryImport(Library)]
    private static partial void glslang_glsl_mapper_delete(nint mapper);

    [LibraryImport(Library)]
    private static partial int glslang_program_map_io_with_resolver_and_mapper(nint program, nint resolver, nint mapper);

    [LibraryImport(Library)]
    private static partial nint glslang_program_get_info_log(nint program);

    [LibraryImport(Library)]
    private static partial void glslang_program_SPIRV_generate(nint program, int stage);

    [LibraryImport(Library)]
    private static partial nuint glslang_program_SPIRV_get_size(nint program);

    [LibraryImport(Library)]
    private static partial void glslang_program_SPIRV_get(nint program, nint output);

    [LibraryImport(ResourceLibrary)]
    private static partial nint glslang_default_resource();

    private static bool initialized;

    /// <summary>
    /// Loads glslang, preferring the copy the Glslang.NET package puts next to the executable and
    /// falling back to the Vulkan SDK. Returns the reason it is unavailable, or
    /// <see langword="null"/> once it is ready to use.
    /// </summary>
    public static string? Initialize()
    {
        if (initialized)
        {
            return null;
        }

        // The package's build of glslang keeps the default resource limits in the same library and
        // links the C++ runtime statically, so it is preferred over the Vulkan SDK, which splits
        // them into a second library. Both names resolve here so nothing else has to care which
        // build it got.
        var fromPackage = NativeLibrary.TryLoad(Library, Assembly.GetExecutingAssembly(), null, out var packageHandle);
        var sdkDirectory = fromPackage ? null : FindSdkBinDirectory();

        if (!fromPackage && sdkDirectory == null)
        {
            return "glslang not found. It should come from the Glslang.NET package; failing that, set VULKAN_SDK.";
        }

        NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), (name, assembly, path) => name switch
        {
            Library when fromPackage => packageHandle,
            ResourceLibrary when fromPackage => packageHandle,
            Library or ResourceLibrary => NativeLibrary.Load(Path.Combine(sdkDirectory!, name + ".dll")),
            _ => nint.Zero,
        });

        try
        {
            if (Marshal.SizeOf<Input>() != ExpectedInputSize)
            {
                return $"glslang_input_t is {Marshal.SizeOf<Input>()} bytes here but should be {ExpectedInputSize}";
            }

            if (glslang_initialize_process() == 0)
            {
                return $"glslang from {Source} failed to initialize";
            }

            // Proves the resource limits resolved too, which is the half that differs between builds.
            if (glslang_default_resource() == nint.Zero)
            {
                return $"glslang from {Source} has no default resource limits";
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return $"glslang could not be loaded: {e.Message}";
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => glslang_finalize_process();
        initialized = true;
        return null;
    }

    /// <summary>The Vulkan SDK directory glslang was loaded from, or null when the package copy was used.</summary>
    public static string? SdkDirectory { get; private set; }

    /// <summary>Where glslang came from, for the report to name.</summary>
    public static string Source => SdkDirectory ?? "the Glslang.NET package";

    /// <summary>Turns a "1.3" style version into the packed form glslang wants.</summary>
    public static int PackSpirvVersion(string version)
    {
        var parts = version.Split('.');
        return (int.Parse(parts[0], CultureInfo.InvariantCulture) << 16)
             | (int.Parse(parts[1], CultureInfo.InvariantCulture) << 8);
    }

    private static string? FindSdkBinDirectory()
    {
        var candidates = new List<string>();

        var sdk = Environment.GetEnvironmentVariable("VULKAN_SDK");

        if (!string.IsNullOrEmpty(sdk))
        {
            candidates.Add(Path.Combine(sdk, "Bin"));
        }

        foreach (var entry in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            if (!string.IsNullOrWhiteSpace(entry))
            {
                candidates.Add(entry);
            }
        }

        foreach (var candidate in candidates)
        {
            if (File.Exists(Path.Combine(candidate, Library + ".dll"))
             && File.Exists(Path.Combine(candidate, ResourceLibrary + ".dll")))
            {
                SdkDirectory = candidate;
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Compiles one vertex/fragment pair, timing the front end, the link, and the SPIR-V writer
    /// apart from each other.
    /// </summary>
    /// <param name="written">
    /// Whether the GLSL was written for the driver's own front end, in which case glslang has to
    /// invent the locations and bindings SPIR-V requires and gather the loose uniforms into a block.
    /// GLSL that a compiler emitted for SPIR-V already declares all of that, and remapping it on top
    /// produces duplicate locations.
    /// </param>
    public static SpirvPair Compile(Timings timings, SourcePair sources, GlslOrigin written)
    {
        var vertex = CreateShader(timings, "vertex", StageVertex, sources.Vertex, written);
        var fragment = CreateShader(timings, "fragment", StageFragment, sources.Fragment, written);
        var program = glslang_program_create();

        try
        {
            timings.Measure("glslang: link program", () =>
            {
                glslang_program_add_shader(program, vertex);
                glslang_program_add_shader(program, fragment);

                if (glslang_program_link(program, MessagesSpvRules) == 0)
                {
                    throw new InvalidOperationException("glslang failed to link:\n" + Text(glslang_program_get_info_log(program)));
                }

                MapIo(program, written);
            });

            var vertexSpirv = timings.Measure("glslang: emit SPIR-V (vertex)", () => Generate(program, StageVertex));
            var fragmentSpirv = timings.Measure("glslang: emit SPIR-V (fragment)", () => Generate(program, StageFragment));

            return new SpirvPair(vertexSpirv, fragmentSpirv);
        }
        finally
        {
            glslang_program_delete(program);
            glslang_shader_delete(vertex);
            glslang_shader_delete(fragment);
        }
    }

    /// <summary>
    /// Assigns the locations and bindings OpenGL SPIR-V insists every interface has. The default
    /// resolver numbers each stage on its own, which gives the vertex and fragment stages the same
    /// location for the same uniform and makes the driver reject the program. The GLSL resolver
    /// shares one numbering across the whole program, which is what OpenGL expects.
    /// </summary>
    private static void MapIo(nint program, GlslOrigin written)
    {
        if (written == GlslOrigin.ForSpirv)
        {
            // Already numbered by whatever emitted it, so nothing to resolve.
            return;
        }

        var resolver = glslang_glsl_resolver_create(program, StageVertex);
        var mapper = glslang_glsl_mapper_create();

        try
        {
            if (glslang_program_map_io_with_resolver_and_mapper(program, resolver, mapper) == 0)
            {
                throw new InvalidOperationException("glslang failed to map io:\n" + Text(glslang_program_get_info_log(program)));
            }
        }
        finally
        {
            glslang_glsl_mapper_delete(mapper);
            glslang_glsl_resolver_delete(resolver);
        }
    }

    private static nint CreateShader(Timings timings, string stageName, int stage, string source, GlslOrigin written)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(source);

        try
        {
            var input = new Input
            {
                Language = SourceGlsl,
                Stage = stage,
                Client = ClientOpenGl,
                ClientVersion = ClientVersionOpenGl450,
                TargetLanguage = TargetLanguageSpv,
                TargetLanguageVersion = TargetSpirvVersion,
                Code = utf8,
                DefaultVersion = 460,
                DefaultProfile = ProfileCore,
                Messages = MessagesSpvRules,
                Resource = glslang_default_resource(),
            };

            var shader = glslang_shader_create(in input);

            if (written == GlslOrigin.ForTheDriver)
            {
                glslang_shader_set_options(shader, OptionsAutoMap);
                glslang_shader_set_default_uniform_block_name(shader, DefaultUniformBlockName);
                glslang_shader_set_default_uniform_block_set_and_binding(shader, 0, 0);
            }

            try
            {
                timings.Measure($"glslang: preprocess ({stageName})", () =>
                {
                    if (glslang_shader_preprocess(shader, in input) == 0)
                    {
                        throw new InvalidOperationException($"glslang failed to preprocess {stageName}:\n{Text(glslang_shader_get_info_log(shader))}");
                    }

                    // Hand the result back so the parse below does not preprocess a second time.
                    glslang_shader_set_preprocessed_code(shader, Text(glslang_shader_get_preprocessed_code(shader)));
                });

                timings.Measure($"glslang: parse ({stageName})", () =>
                {
                    if (glslang_shader_parse(shader, in input) == 0)
                    {
                        throw new InvalidOperationException($"glslang failed to parse {stageName}:\n{Text(glslang_shader_get_info_log(shader))}");
                    }
                });
            }
            catch
            {
                glslang_shader_delete(shader);
                throw;
            }

            return shader;
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }
    }

    private static byte[] Generate(nint program, int stage)
    {
        glslang_program_SPIRV_generate(program, stage);

        var words = (int)glslang_program_SPIRV_get_size(program);
        var spirv = new byte[words * sizeof(uint)];

        unsafe
        {
            fixed (byte* destination = spirv)
            {
                glslang_program_SPIRV_get(program, (nint)destination);
            }
        }

        return spirv;
    }

    private static string Text(nint utf8) => Marshal.PtrToStringUTF8(utf8) ?? string.Empty;

    public static string Describe(SpirvPair spirv) => string.Create(CultureInfo.InvariantCulture,
        $"emitted: vertex {spirv.Vertex.Length} SPIR-V bytes ({SpirvPair.Version(spirv.Vertex)}), fragment {spirv.Fragment.Length} SPIR-V bytes ({SpirvPair.Version(spirv.Fragment)})");
}
