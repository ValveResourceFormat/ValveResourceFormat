using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer.Shaders;

namespace ValveResourceFormat.Renderer.Buffers;

/// <summary>
/// Holds the bindless handles of the textures the renderer supplies for the whole scene, laid out by
/// <see cref="SceneTexturesLayout"/>. One per renderer context, bound once a frame.
/// </summary>
public sealed class SceneTextures : Buffer
{
    private readonly byte[] bytes = new byte[SceneTexturesLayout.Size];
    private readonly MaterialLoader materialLoader;

    /// <summary>Initializes the buffer with every member pointing at a null texture of its own sampler type.</summary>
    /// <param name="materialLoader">The loader the null textures come from.</param>
    public SceneTextures(MaterialLoader materialLoader)
        : base(BufferTarget.UniformBuffer, (int)ReservedBufferSlots.SceneTextures, nameof(SceneTextures))
    {
        this.materialLoader = materialLoader;

        Size = SceneTexturesLayout.Size;
        GL.NamedBufferData(Handle, Size, IntPtr.Zero, BufferUsageHint.DynamicDraw);

        // A scene sets only the textures it has, and a shader reading one it did not set still has to read
        // something samplable. There is no handle meaning "nothing", so they all start out here.
        foreach (var member in SceneTexturesLayout.Members.Values)
        {
            Reset(member);
        }
    }

    /// <summary>
    /// Points a sampler at a texture. Does nothing when the name is not one this buffer holds, which is how
    /// callers that also feed per-instance samplers tell the two apart.
    /// </summary>
    /// <param name="samplerName">The sampler uniform name.</param>
    /// <param name="texture">The texture to sample.</param>
    /// <param name="sampler">A sampler object to read the texture through, or zero for its own parameters.</param>
    /// <returns><see langword="true"/> when the sampler is held here.</returns>
    public bool SetTexture(string samplerName, RenderTexture texture, int sampler = 0)
    {
        if (!SceneTexturesLayout.Members.TryGetValue(samplerName, out var member))
        {
            return false;
        }

        // A zero handle is a texture deleted out from under the scene, which the teardown order allows.
        if (texture.Handle == 0 || texture.Target != ReservedSamplers.GetTextureTarget(member.Sampler))
        {
            Debug.Assert(texture.Handle == 0, $"'{samplerName}' is a {member.Sampler} sampler, but a {texture.Target} texture was set on it.");

            Reset(member);
            return true;
        }

        Write(member, texture.GetHandle(sampler));
        return true;
    }

    /// <summary>Points a sampler back at the null texture of its type, for a scene that has none of its own.</summary>
    private void Reset(in GlobalsMember member)
        => Write(member, materialLoader.GetNullTexture(member.Sampler).BindlessHandle);

    private void Write(in GlobalsMember member, long handle)
    {
        Span<byte> staged = stackalloc byte[sizeof(long)];
        MemoryMarshal.Write(staged, handle);

        var target = bytes.AsSpan(member.Offset, staged.Length);

        // Written far more often than it changes: a pass sets the same textures it set last time, and only a
        // resize or a scene load puts a different handle in one.
        if (target.SequenceEqual(staged))
        {
            return;
        }

        staged.CopyTo(target);

        GL.NamedBufferSubData(Handle, member.Offset, staged.Length, ref bytes[member.Offset]);
    }
}
