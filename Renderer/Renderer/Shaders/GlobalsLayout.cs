using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using ValveResourceFormat.Renderer.Buffers;

namespace ValveResourceFormat.Renderer.Shaders;

/// <summary>
/// GLSL types of loose uniforms that can be packed into the globals constant buffer.
/// </summary>
public enum GlobalsType
{
    /// <summary>32 bit float scalar.</summary>
    Float,
    /// <summary>32 bit signed integer scalar.</summary>
    Int,
    /// <summary>32 bit unsigned integer scalar.</summary>
    Uint,
    /// <summary>Boolean scalar, stored as a 32 bit 0 or 1.</summary>
    Bool,
    /// <summary>Two component float vector.</summary>
    Vec2,
    /// <summary>Three component float vector.</summary>
    Vec3,
    /// <summary>Four component float vector.</summary>
    Vec4,
    /// <summary>Two component signed integer vector.</summary>
    IVec2,
    /// <summary>Three component signed integer vector.</summary>
    IVec3,
    /// <summary>Four component signed integer vector.</summary>
    IVec4,
    /// <summary>Two component unsigned integer vector.</summary>
    UVec2,
    /// <summary>Three component unsigned integer vector.</summary>
    UVec3,
    /// <summary>Four component unsigned integer vector.</summary>
    UVec4,
    /// <summary>Two component boolean vector.</summary>
    BVec2,
    /// <summary>Three component boolean vector.</summary>
    BVec3,
    /// <summary>Four component boolean vector.</summary>
    BVec4,
    /// <summary>Four by four column major float matrix.</summary>
    Mat4,
    /// <summary>
    /// Opaque texture sampler, stored as a 64 bit bindless handle. Only packed when the driver
    /// supports <c>GL_ARB_bindless_texture</c>; see <see cref="GLEnvironment.BindlessTexturesSupported"/>.
    /// </summary>
    Sampler,
}

/// <summary>
/// The sampler types whose handles can be packed into a constant buffer. Integer and multisample samplers
/// are left out: they are bound by the renderer itself rather than by a material, and each would need a null
/// texture of its own to stay crash safe.
/// </summary>
public enum SamplerKind
{
    /// <summary>Not a sampler.</summary>
    None,
    /// <summary><c>sampler2D</c>.</summary>
    Texture2D,
    /// <summary><c>sampler3D</c>.</summary>
    Texture3D,
    /// <summary><c>samplerCube</c>.</summary>
    TextureCube,
    /// <summary><c>sampler2DArray</c>.</summary>
    Texture2DArray,
    /// <summary><c>samplerCubeArray</c>.</summary>
    TextureCubeArray,
    /// <summary><c>sampler2DShadow</c>, a depth texture read through its comparison function.</summary>
    Texture2DShadow,
    /// <summary><c>sampler2DArrayShadow</c>, a layered depth texture read through its comparison function.</summary>
    Texture2DArrayShadow,
}

/// <summary>A single uniform packed into the globals constant buffer.</summary>
/// <param name="Name">The uniform name as declared in the shader source.</param>
/// <param name="Type">The declared GLSL type.</param>
/// <param name="Offset">The byte offset of this member within the constant buffer.</param>
/// <param name="Sampler">The sampler type, when <paramref name="Type"/> is <see cref="GlobalsType.Sampler"/>.</param>
public readonly record struct GlobalsMember(string Name, GlobalsType Type, int Offset, SamplerKind Sampler = SamplerKind.None)
{
    /// <summary>Gets the number of scalar components, 16 for <see cref="GlobalsType.Mat4"/>.</summary>
    public int ComponentCount => GlobalsLayout.GetComponentCount(Type);

    /// <summary>Gets a value indicating whether the components are floats rather than integers.</summary>
    public bool IsFloat => GlobalsLayout.IsFloatType(Type);

    /// <summary>Gets the size of this member in bytes.</summary>
    public int Size => ComponentCount * sizeof(float);
}

/// <summary>A packable uniform declaration extracted from shader source.</summary>
/// <param name="Name">The uniform name.</param>
/// <param name="Type">The declared GLSL type.</param>
/// <param name="Initializer">The default value expression, or <see langword="null"/> when the declaration has none.</param>
/// <param name="SrgbRead">Whether the declaration is annotated with <c>// SrgbRead(true)</c>.</param>
/// <param name="Sampler">The sampler type, when <paramref name="Type"/> is <see cref="GlobalsType.Sampler"/>.</param>
public readonly record struct GlobalsDeclaration(string Name, GlobalsType Type, string? Initializer, bool SrgbRead, SamplerKind Sampler = SamplerKind.None);

/// <summary>
/// The std140 layout of a shader's loose global uniforms after they have been packed into a single
/// uniform block. Built once per shader file from its parsed source, and therefore shared by every
/// static combo variant of that shader.
/// </summary>
public sealed class GlobalsLayout
{
    /// <summary>The name of the generated GLSL uniform block.</summary>
    public const string BlockName = "Globals";

    /// <summary>The largest constant buffer the GL specification guarantees to be available.</summary>
    private const int MaxBlockSize = 16384;

    /// <summary>Gets the layout used by shaders that declare no packable uniforms.</summary>
    public static GlobalsLayout Empty { get; } = new([]);

    private readonly Dictionary<string, GlobalsMember> members = [];
    private readonly List<GlobalsMember> samplers = [];
    private readonly Dictionary<string, float> floatDefaults = [];
    private readonly Dictionary<string, long> intDefaults = [];
    private readonly Dictionary<string, Vector4> vectorDefaults = [];
    private readonly Dictionary<string, Matrix4x4> matrixDefaults = [];
    private readonly byte[] defaultBytes;

    /// <summary>Gets the packed members by uniform name.</summary>
    public IReadOnlyDictionary<string, GlobalsMember> Members => members;

    /// <summary>
    /// Gets the sampler members, which hold a bindless texture handle each. Empty unless
    /// <see cref="GLEnvironment.BindlessTexturesSupported"/>, in which case the samplers stay loose
    /// uniforms bound to a texture unit.
    /// </summary>
    /// <remarks>
    /// Every one of these has to be written on each fill: the buffer starts out zeroed for them, and a
    /// zero handle is not a texture the GPU can sample from.
    /// </remarks>
    public IReadOnlyList<GlobalsMember> Samplers => samplers;

    /// <summary>Gets the size of the constant buffer in bytes, zero when there is nothing to pack.</summary>
    public int Size { get; }

    /// <summary>
    /// Gets a value indicating whether sampler handles were packed into this layout. The shader source
    /// is compiled against this rather than against the driver capability directly, so that a layout built
    /// before the GL context was queried cannot disagree with the source it is compiled into.
    /// </summary>
    public bool PacksSamplers { get; }

    /// <summary>Gets the GLSL declaration of the uniform block, prepended to every stage of the shader.</summary>
    public string BlockSource { get; } = string.Empty;

    /// <summary>Gets the buffer contents a material starts from, with every member set to its shader source default.</summary>
    public ReadOnlySpan<byte> DefaultBytes => defaultBytes;

    /// <summary>Gets the source defaults of the scalar float members.</summary>
    public IReadOnlyDictionary<string, float> FloatDefaults => floatDefaults;

    /// <summary>Gets the source defaults of the scalar integer and boolean members.</summary>
    public IReadOnlyDictionary<string, long> IntDefaults => intDefaults;

    /// <summary>Gets the source defaults of the vector members.</summary>
    public IReadOnlyDictionary<string, Vector4> VectorDefaults => vectorDefaults;

    /// <summary>Gets the source defaults of the matrix members.</summary>
    public IReadOnlyDictionary<string, Matrix4x4> MatrixDefaults => matrixDefaults;

    /// <summary>
    /// Merges the declarations collected from every stage of one shader and computes the packed layout.
    /// </summary>
    /// <param name="declarations">Declarations in source order; duplicates across stages are merged.</param>
    /// <param name="packSamplers">
    /// Whether sampler declarations are packed as bindless handles. When they are not, they are left out
    /// of the block entirely and the shader source keeps its loose <c>uniform sampler</c> declarations.
    /// </param>
    /// <exception cref="ShaderLoader.ShaderCompilerException">A uniform is declared with conflicting types or defaults.</exception>
    public static GlobalsLayout Build(IEnumerable<GlobalsDeclaration> declarations, bool packSamplers)
    {
        var merged = new Dictionary<string, GlobalsDeclaration>();

        foreach (var declaration in declarations)
        {
            if (declaration.Type == GlobalsType.Sampler && !packSamplers)
            {
                continue;
            }

            if (!merged.TryGetValue(declaration.Name, out var existing))
            {
                merged.Add(declaration.Name, declaration);
                continue;
            }

            if (existing.Type != declaration.Type || existing.Sampler != declaration.Sampler)
            {
                throw new ShaderLoader.ShaderCompilerException(
                    $"Uniform '{declaration.Name}' is declared as both '{GetGlslName(existing)}' and '{GetGlslName(declaration)}'");
            }

            var srgbRead = existing.SrgbRead || declaration.SrgbRead;

            if (declaration.Initializer == null)
            {
                merged[declaration.Name] = existing with { SrgbRead = srgbRead };
                continue;
            }

            if (existing.Initializer == null)
            {
                merged[declaration.Name] = declaration with { SrgbRead = srgbRead };
                continue;
            }

            if (existing.Initializer != declaration.Initializer)
            {
                throw new ShaderLoader.ShaderCompilerException(
                    $"Uniform '{declaration.Name}' has conflicting default values '{existing.Initializer}' and '{declaration.Initializer}'");
            }

            merged[declaration.Name] = existing with { SrgbRead = srgbRead };
        }

        return merged.Count == 0 ? Empty : new GlobalsLayout([.. merged.Values], packSamplers);
    }

    private GlobalsLayout(List<GlobalsDeclaration> declarations, bool packSamplers = false)
    {
        PacksSamplers = packSamplers;

        if (declarations.Count == 0)
        {
            defaultBytes = [];
            return;
        }

        declarations.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));

        List<GlobalsDeclaration> matrices = [], quads = [], triples = [], pairs = [], scalars = [];

        foreach (var declaration in declarations)
        {
            var bucket = GetComponentCount(declaration.Type) switch
            {
                16 => matrices,
                4 => quads,
                3 => triples,
                2 => pairs,
                _ => scalars,
            };

            bucket.Add(declaration);
        }

        var builder = new StringBuilder(declarations.Count * 32);
        builder.Append(CultureInfo.InvariantCulture, $"layout(std140, binding = {(int)ReservedBufferSlots.Globals}) uniform {BlockName}\n{{\n");

        var offset = 0;

        void Place(GlobalsDeclaration declaration)
        {
            offset = Align(offset, GetBaseAlignment(declaration.Type));

            var member = new GlobalsMember(declaration.Name, declaration.Type, offset, declaration.Sampler);
            members.Add(declaration.Name, member);

            if (declaration.Type == GlobalsType.Sampler)
            {
                samplers.Add(member);
            }

            offset += GetComponentCount(declaration.Type) * sizeof(float);

            builder.Append("    ");
            builder.Append(GetGlslName(declaration));
            builder.Append(' ');
            builder.Append(declaration.Name);
            builder.Append(";\n");
        }

        foreach (var declaration in matrices)
        {
            Place(declaration);
        }

        foreach (var declaration in quads)
        {
            Place(declaration);
        }

        var scalarIndex = 0;

        foreach (var declaration in triples)
        {
            Place(declaration);

            if (scalarIndex < scalars.Count)
            {
                Place(scalars[scalarIndex++]);
            }
        }

        foreach (var declaration in pairs)
        {
            Place(declaration);
        }

        for (; scalarIndex < scalars.Count; scalarIndex++)
        {
            Place(scalars[scalarIndex]);
        }

        builder.Append("};\n");

        BlockSource = builder.ToString();
        Size = Align(offset, 16);

        if (Size > MaxBlockSize)
        {
            throw new ShaderLoader.ShaderCompilerException($"Packed uniforms are {Size} bytes, which exceeds the {MaxBlockSize} byte uniform block size all drivers support.");
        }

        defaultBytes = new byte[Size];

        foreach (var declaration in declarations)
        {
            WriteDefault(declaration);
        }
    }

    private void WriteDefault(GlobalsDeclaration declaration)
    {
        var constant = members[declaration.Name];

        if (constant.Type == GlobalsType.Sampler)
        {
            // There is no handle that can stand in for "no texture", so the zeroes stay and every fill
            // writes a real one over them. See <see cref="Samplers"/>.
            return;
        }

        Span<double> values = stackalloc double[16];
        ParseInitializer(declaration, constant, values);

        switch (constant.Type)
        {
            case GlobalsType.Float:
                floatDefaults[constant.Name] = (float)values[0];
                break;

            case GlobalsType.Int or GlobalsType.Uint or GlobalsType.Bool:
                intDefaults[constant.Name] = (long)values[0];
                break;

            case GlobalsType.Mat4:
                break;

            default:
                vectorDefaults[constant.Name] = new Vector4((float)values[0], (float)values[1], (float)values[2], (float)values[3]);
                break;
        }

        if (declaration.SrgbRead && constant.IsFloat && constant.ComponentCount is 3 or 4)
        {
            var linear = ColorSpace.SrgbGammaToLinear(new Vector3((float)values[0], (float)values[1], (float)values[2]));

            values[0] = linear.X;
            values[1] = linear.Y;
            values[2] = linear.Z;
        }

        var target = defaultBytes.AsSpan(constant.Offset, constant.Size);
        var componentCount = constant.ComponentCount;

        if (constant.IsFloat)
        {
            var floats = MemoryMarshal.Cast<byte, float>(target);

            for (var i = 0; i < componentCount; i++)
            {
                floats[i] = (float)values[i];
            }
        }
        else
        {
            var integers = MemoryMarshal.Cast<byte, int>(target);

            for (var i = 0; i < componentCount; i++)
            {
                integers[i] = (int)(long)values[i];
            }
        }

        if (constant.Type == GlobalsType.Mat4)
        {
            matrixDefaults[constant.Name] = MemoryMarshal.Read<Matrix4x4>(target);
        }
    }

    private static void ParseInitializer(GlobalsDeclaration declaration, GlobalsMember constant, Span<double> values)
    {
        values.Clear();

        if (string.IsNullOrEmpty(declaration.Initializer))
        {
            return;
        }

        var text = declaration.Initializer.AsSpan().Trim();
        var open = text.IndexOf('(');

        if (open < 0)
        {
            values[0] = ParseScalar(declaration, text);
            return;
        }

        if (!text.EndsWith(")", StringComparison.Ordinal))
        {
            throw new ShaderLoader.ShaderCompilerException($"Could not parse the default value '{declaration.Initializer}' of uniform '{declaration.Name}'");
        }

        var arguments = text[(open + 1)..^1];
        var count = 0;

        foreach (var argumentRange in arguments.Split(','))
        {
            if (count == values.Length)
            {
                throw new ShaderLoader.ShaderCompilerException($"The default value '{declaration.Initializer}' of uniform '{declaration.Name}' has too many components");
            }

            values[count++] = ParseScalar(declaration, arguments[argumentRange]);
        }

        if (count == constant.ComponentCount)
        {
            return;
        }

        if (count != 1)
        {
            throw new ShaderLoader.ShaderCompilerException($"The default value '{declaration.Initializer}' of uniform '{declaration.Name}' has {count} components, expected {constant.ComponentCount}");
        }

        if (constant.Type == GlobalsType.Mat4)
        {
            var diagonal = values[0];
            values.Clear();
            values[0] = values[5] = values[10] = values[15] = diagonal;
            return;
        }

        values.Slice(1, constant.ComponentCount - 1).Fill(values[0]);
    }

    private static double ParseScalar(GlobalsDeclaration declaration, ReadOnlySpan<char> text)
    {
        text = text.Trim();

        if (text.SequenceEqual("true"))
        {
            return 1d;
        }

        if (text.SequenceEqual("false"))
        {
            return 0d;
        }

        if (text.EndsWith("f", StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^1];
        }

        var negative = text.StartsWith("-", StringComparison.Ordinal);

        if (negative)
        {
            text = text[1..].TrimStart();
        }

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (!ulong.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
            {
                throw new ShaderLoader.ShaderCompilerException($"Could not parse the default value '{declaration.Initializer}' of uniform '{declaration.Name}'");
            }

            return negative ? -(double)hex : hex;
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            throw new ShaderLoader.ShaderCompilerException($"Could not parse the default value '{declaration.Initializer}' of uniform '{declaration.Name}'");
        }

        return negative ? -value : value;
    }

    private static int Align(int offset, int alignment) => (offset + alignment - 1) & ~(alignment - 1);

    /// <summary>
    /// Returns the number of scalar components in the given type, 16 for <see cref="GlobalsType.Mat4"/>
    /// and 2 for <see cref="GlobalsType.Sampler"/>, whose handle is a 64 bit value laid out like a
    /// <c>uvec2</c> would be.
    /// </summary>
    internal static int GetComponentCount(GlobalsType type) => type switch
    {
        GlobalsType.Mat4 => 16,
        GlobalsType.Sampler => 2,
        GlobalsType.Vec4 or GlobalsType.IVec4 or GlobalsType.UVec4 or GlobalsType.BVec4 => 4,
        GlobalsType.Vec3 or GlobalsType.IVec3 or GlobalsType.UVec3 or GlobalsType.BVec3 => 3,
        GlobalsType.Vec2 or GlobalsType.IVec2 or GlobalsType.UVec2 or GlobalsType.BVec2 => 2,
        _ => 1,
    };

    /// <summary>Returns a value indicating whether the components of the given type are floats rather than integers.</summary>
    internal static bool IsFloatType(GlobalsType type) => type
        is GlobalsType.Float
        or GlobalsType.Vec2
        or GlobalsType.Vec3
        or GlobalsType.Vec4
        or GlobalsType.Mat4;

    private static int GetBaseAlignment(GlobalsType type) => GetComponentCount(type) switch
    {
        1 => 4,
        2 => 8,
        _ => 16,
    };

    /// <summary>Returns the GLSL keyword the given declaration is written with.</summary>
    internal static string GetGlslName(GlobalsDeclaration declaration)
        => declaration.Type == GlobalsType.Sampler ? GetGlslName(declaration.Sampler) : GetGlslName(declaration.Type);

    /// <summary>Returns the GLSL keyword for the given sampler type.</summary>
    internal static string GetGlslName(SamplerKind kind) => kind switch
    {
        SamplerKind.Texture2D => "sampler2D",
        SamplerKind.Texture3D => "sampler3D",
        SamplerKind.TextureCube => "samplerCube",
        SamplerKind.Texture2DArray => "sampler2DArray",
        SamplerKind.TextureCubeArray => "samplerCubeArray",
        SamplerKind.Texture2DShadow => "sampler2DShadow",
        SamplerKind.Texture2DArrayShadow => "sampler2DArrayShadow",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>Maps a GLSL sampler keyword onto the sampler type it names, when its handle can be packed.</summary>
    /// <param name="glslType">The type keyword as it appears in the shader source.</param>
    /// <param name="kind">The matching sampler type when the keyword names one that can be packed.</param>
    /// <returns><see langword="true"/> when the keyword names a sampler whose handle can be packed.</returns>
    internal static bool TryGetSamplerKind(ReadOnlySpan<char> glslType, out SamplerKind kind)
    {
        switch (glslType)
        {
            case "sampler2D": kind = SamplerKind.Texture2D; return true;
            case "sampler3D": kind = SamplerKind.Texture3D; return true;
            case "samplerCube": kind = SamplerKind.TextureCube; return true;
            case "sampler2DArray": kind = SamplerKind.Texture2DArray; return true;
            case "samplerCubeArray": kind = SamplerKind.TextureCubeArray; return true;
            case "sampler2DShadow": kind = SamplerKind.Texture2DShadow; return true;
            case "sampler2DArrayShadow": kind = SamplerKind.Texture2DArrayShadow; return true;
            default: kind = SamplerKind.None; return false;
        }
    }

    /// <summary>Returns the GLSL keyword for the given type.</summary>
    internal static string GetGlslName(GlobalsType type) => type switch
    {
        GlobalsType.Float => "float",
        GlobalsType.Int => "int",
        GlobalsType.Uint => "uint",
        GlobalsType.Bool => "bool",
        GlobalsType.Vec2 => "vec2",
        GlobalsType.Vec3 => "vec3",
        GlobalsType.Vec4 => "vec4",
        GlobalsType.IVec2 => "ivec2",
        GlobalsType.IVec3 => "ivec3",
        GlobalsType.IVec4 => "ivec4",
        GlobalsType.UVec2 => "uvec2",
        GlobalsType.UVec3 => "uvec3",
        GlobalsType.UVec4 => "uvec4",
        GlobalsType.BVec2 => "bvec2",
        GlobalsType.BVec3 => "bvec3",
        GlobalsType.BVec4 => "bvec4",
        GlobalsType.Mat4 => "mat4",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    /// <summary>Maps a GLSL type keyword onto the packable type it represents.</summary>
    /// <param name="glslType">The type keyword as it appears in the shader source.</param>
    /// <param name="type">The matching type when the keyword is packable.</param>
    /// <returns><see langword="true"/> when the keyword names a type that can be packed.</returns>
    internal static bool TryGetType(ReadOnlySpan<char> glslType, out GlobalsType type)
    {
        switch (glslType)
        {
            case "float": type = GlobalsType.Float; return true;
            case "int": type = GlobalsType.Int; return true;
            case "uint": type = GlobalsType.Uint; return true;
            case "bool": type = GlobalsType.Bool; return true;
            case "vec2": type = GlobalsType.Vec2; return true;
            case "vec3": type = GlobalsType.Vec3; return true;
            case "vec4": type = GlobalsType.Vec4; return true;
            case "ivec2": type = GlobalsType.IVec2; return true;
            case "ivec3": type = GlobalsType.IVec3; return true;
            case "ivec4": type = GlobalsType.IVec4; return true;
            case "uvec2": type = GlobalsType.UVec2; return true;
            case "uvec3": type = GlobalsType.UVec3; return true;
            case "uvec4": type = GlobalsType.UVec4; return true;
            case "bvec2": type = GlobalsType.BVec2; return true;
            case "bvec3": type = GlobalsType.BVec3; return true;
            case "bvec4": type = GlobalsType.BVec4; return true;
            case "mat4": type = GlobalsType.Mat4; return true;
            default: type = default; return false;
        }
    }
}
