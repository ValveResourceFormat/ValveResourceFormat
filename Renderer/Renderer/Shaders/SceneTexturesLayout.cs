using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using ValveResourceFormat.Renderer.Buffers;

namespace ValveResourceFormat.Renderer.Shaders;

/// <summary>
/// std140 layout of the scene-wide reserved samplers. The same for every shader, unlike
/// <see cref="GlobalsLayout"/>, so one buffer serves all of them.
/// </summary>
/// <remarks>
/// Only the samplers set once per pass. The environment map, light probe volumes and morph composite are
/// picked per draw, where writing and uploading a handle costs more than the bind it would replace.
/// </remarks>
public static class SceneTexturesLayout
{
    /// <summary>Name of the generated GLSL uniform block.</summary>
    public const string BlockName = "SceneTextures";

    /// <summary>Members by sampler uniform name. Every one is a 64 bit handle.</summary>
    public static FrozenDictionary<string, GlobalsMember> Members { get; }

    /// <summary>Size of the buffer in bytes.</summary>
    public static int Size { get; }

    /// <summary>GLSL declaration of the block, prepended to every stage of every shader.</summary>
    public static string BlockSource { get; }

    static SceneTexturesLayout()
    {
        var members = new Dictionary<string, GlobalsMember>(StringComparer.Ordinal);
        var builder = new StringBuilder(1024);

        builder.Append(CultureInfo.InvariantCulture, $"layout(std140, binding = {(int)ReservedBufferSlots.SceneTextures}) uniform {BlockName}\n{{\n");

        foreach (var sampler in MaterialLoader.ReservedSamplers)
        {
            if (sampler.PerInstance)
            {
                continue;
            }

            members.Add(sampler.Name, new GlobalsMember(sampler.Name, GlobalsType.Sampler, Size, sampler.Kind));
            Size += sizeof(long);

            builder.Append(CultureInfo.InvariantCulture, $"    {GlobalsLayout.GetGlslName(sampler.Kind)} {sampler.Name};\n");
        }

        builder.Append("};\n");

        Members = members.ToFrozenDictionary(StringComparer.Ordinal);
        BlockSource = builder.ToString();
    }

    /// <summary>Whether the named sampler is read through this buffer rather than a texture unit.</summary>
    public static bool Contains(string samplerName) => GLEnvironment.BindlessTextures && Members.ContainsKey(samplerName);
}
