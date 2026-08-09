using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using ValveResourceFormat.Renderer.Buffers;

namespace ValveResourceFormat.Renderer.Shaders;

/// <summary>
/// The std140 layout of the scene-wide reserved samplers, held as bindless handles in one buffer the whole
/// renderer shares. Unlike <see cref="GlobalsLayout"/> this is the same for every shader, so the block can be
/// declared in every one of them and the buffer bound once a frame.
/// </summary>
/// <remarks>
/// Only the samplers the renderer sets once for a pass are in here. The ones it picks per draw, such as the
/// environment map and the light probe volumes, stay on their texture units: their handle would have to be
/// written and uploaded per draw, which costs more than the bind it replaces.
/// </remarks>
public static class SceneTexturesLayout
{
    /// <summary>The name of the generated GLSL uniform block.</summary>
    public const string BlockName = "SceneTextures";

    /// <summary>Gets the members by sampler uniform name.</summary>
    public static FrozenDictionary<string, GlobalsMember> Members { get; }

    /// <summary>Gets the members in the order they are laid out.</summary>
    public static ImmutableArray<GlobalsMember> OrderedMembers { get; }

    /// <summary>Gets the size of the buffer in bytes.</summary>
    public static int Size { get; }

    /// <summary>Gets the GLSL declaration of the uniform block, prepended to every stage of every shader.</summary>
    public static string BlockSource { get; }

    static SceneTexturesLayout()
    {
        var builder = new StringBuilder(1024);
        builder.Append(CultureInfo.InvariantCulture, $"layout(std140, binding = {(int)ReservedBufferSlots.SceneTextures}) uniform {BlockName}\n{{\n");

        var members = ImmutableArray.CreateBuilder<GlobalsMember>();
        var offset = 0;

        foreach (var sampler in MaterialLoader.ReservedSamplers)
        {
            if (sampler.PerInstance)
            {
                continue;
            }

            members.Add(new GlobalsMember(sampler.Name, GlobalsType.Sampler, offset, sampler.Kind));
            offset += sizeof(long);

            builder.Append("    ");
            builder.Append(GlobalsLayout.GetGlslName(sampler.Kind));
            builder.Append(' ');
            builder.Append(sampler.Name);
            builder.Append(";\n");
        }

        builder.Append("};\n");

        OrderedMembers = members.DrainToImmutable();
        Members = OrderedMembers.ToFrozenDictionary(static member => member.Name, StringComparer.Ordinal);
        Size = offset;
        BlockSource = builder.ToString();
    }

    /// <summary>Returns whether the named sampler is read through this buffer rather than a texture unit.</summary>
    /// <param name="samplerName">The sampler uniform name.</param>
    public static bool Contains(string samplerName) => GLEnvironment.BindlessTextures && Members.ContainsKey(samplerName);
}
