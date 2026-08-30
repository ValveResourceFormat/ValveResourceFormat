using System.Globalization;
using System.Text;
using ValveResourceFormat.Renderer.Shaders;
using static ValveResourceFormat.Renderer.Shaders.ShaderLoader;

namespace ShaderCompilerBench;

/// <summary>A vertex/fragment source pair, already flattened to what would be handed to the driver.</summary>
internal sealed record SourcePair(string Name, string Vertex, string Fragment)
{
    public int Bytes => Vertex.Length + Fragment.Length;
    public int Lines => Vertex.AsSpan().Count('\n') + Fragment.AsSpan().Count('\n') + 2;
}

internal static class Sources
{
    /// <summary>
    /// Runs a renderer shader through <see cref="ShaderParser"/> and stamps the same header
    /// <see cref="ShaderLoader"/> builds, producing the exact GLSL the driver sees today.
    /// </summary>
    public static SourcePair FromRenderer(string shaderName)
    {
        // ShaderLoader's static constructor kicks off a background task that preprocesses every
        // shader through the same static render mode registry this call writes to, and the two
        // racing on a first-time render mode throws. Once the prewarm has seen the mode, the
        // registration is a read and the retry sticks.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return Preprocess(shaderName);
            }
            catch (ArgumentException) when (attempt < 20)
            {
                Thread.Sleep(50);
            }
        }
    }

    private static SourcePair Preprocess(string shaderName)
    {
        var parser = new ShaderParser();
        var parsedData = new ParsedShaderData();

        var vertex = parser.PreprocessShader($"{shaderName}.vert.slang", parsedData);
        parser.ClearBuilder();
        var fragment = parser.PreprocessShader($"{shaderName}.frag.slang", parsedData);
        parser.ClearBuilder();

        parsedData.GlobalsLayout = GlobalsLayout.Build(parsedData.GlobalsDeclarations);

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

        foreach (var (defineName, defaultValue) in parsedData.Defines)
        {
            header.Append(CultureInfo.InvariantCulture, $"#define {defineName} {defaultValue}\n");
        }

        header.Append(parsedData.GlobalsLayout.BlockSource);

        var headerText = header.ToString();
        return new SourcePair(shaderName, headerText + vertex, headerText + fragment);
    }

    /// <summary>
    /// The renderer shaders that have both a vertex and a fragment stage, which is what the
    /// Slang-free paths can compile.
    /// </summary>
    public static IEnumerable<string> RendererShaders()
        => new ShaderParser().AvailableShaders
            .Where(shader => shader.Value.Length > 1 && shader.Value[0] && shader.Value[1])
            .Select(shader => shader.Key)
            .Order(StringComparer.Ordinal);

    /// <summary>The directory the bench's own shader files are copied to next to the executable.</summary>
    public static string ShaderDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "Shaders");

    public static string Read(string fileName)
        => File.ReadAllText(Path.Combine(ShaderDirectory, fileName));

    public static SourcePair Glsl(string name)
        => new(name, Read($"{name}.vert"), Read($"{name}.frag"));
}
