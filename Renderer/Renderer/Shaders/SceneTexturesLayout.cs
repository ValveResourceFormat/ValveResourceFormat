using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using ValveResourceFormat.Renderer.Buffers;

namespace ValveResourceFormat.Renderer.Shaders;

/// <summary>
/// The std140 layout of the scene-wide samplers, held as bindless handles in one buffer the whole renderer
/// shares. Unlike <see cref="GlobalsLayout"/> this is the same for every shader, so the block is declared in
/// all of them and the buffer is bound once a frame.
/// </summary>
public static class SceneTexturesLayout
{
    /// <summary>The name of the generated GLSL uniform block.</summary>
    public const string BlockName = "SceneTextures";

    /// <summary>Gets the members by sampler uniform name.</summary>
    public static FrozenDictionary<string, GlobalsMember> Members { get; }

    /// <summary>Gets the size of the buffer in bytes.</summary>
    public static int Size { get; }

    /// <summary>Gets the GLSL declaration of the uniform block, prepended to every stage of every shader.</summary>
    public static string BlockSource { get; }

    static SceneTexturesLayout()
    {
        var builder = new StringBuilder(1024);
        builder.Append(CultureInfo.InvariantCulture, $"layout(std140, binding = {(int)ReservedBufferSlots.SceneTextures}) uniform {BlockName}\n{{\n");

        var members = new Dictionary<string, GlobalsMember>(StringComparer.Ordinal);
        var offset = 0;

        foreach (var sampler in ReservedSamplers.Scene)
        {
            members.Add(sampler.Name, new GlobalsMember(sampler.Name, GlobalsType.Sampler, offset, sampler.Kind));
            offset += sizeof(long);

            builder.Append("    ");
            builder.Append(GlobalsLayout.GetSamplerName(sampler.Kind));
            builder.Append(' ');
            builder.Append(sampler.Name);
            builder.Append(";\n");
        }

        builder.Append("};\n");

        Members = members.ToFrozenDictionary(StringComparer.Ordinal);

        // std140 rounds a block up to a multiple of 16, which is what the shader side reserves for it.
        Size = (offset + 15) & ~15;
        BlockSource = builder.ToString();
    }
}
