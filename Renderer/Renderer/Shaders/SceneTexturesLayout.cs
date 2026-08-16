using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using ValveResourceFormat.Renderer.Buffers;

namespace ValveResourceFormat.Renderer.Shaders;

/// <summary>
/// The std140 layout of the scene-wide reserved samplers, held as bindless handles in one buffer the whole
/// renderer shares. Unlike <see cref="GlobalsLayout"/> this is the same for every shader, so the block is
/// declared in all of them and the buffer is bound once rather than per material.
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

    /// <summary>Gets the members by sampler uniform name. Every one of them is a handle, so 8 bytes.</summary>
    public static FrozenDictionary<string, GlobalsMember> Members { get; }

    /// <summary>Gets the size of the buffer in bytes.</summary>
    public static int Size { get; }

    /// <summary>Gets the GLSL declaration of the uniform block, prepended to every stage of every shader.</summary>
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

    /// <summary>Returns whether the named sampler is read through this buffer rather than a texture unit.</summary>
    /// <param name="samplerName">The sampler uniform name.</param>
    public static bool Contains(string samplerName) => GLEnvironment.BindlessTextures && Members.ContainsKey(samplerName);
}
