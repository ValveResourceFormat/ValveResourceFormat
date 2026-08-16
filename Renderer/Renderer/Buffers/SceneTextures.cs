using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer.Buffers;

/// <summary>
/// Holds the bindless handles of the textures the renderer supplies for the whole scene, laid out by
/// <see cref="SceneTexturesLayout"/>. One per renderer, bound in place of the texture unit each of these
/// used to occupy.
/// </summary>
public sealed class SceneTextures : Buffer
{
    private readonly byte[] bytes = new byte[SceneTexturesLayout.Size];
    private readonly MaterialLoader materialLoader;

    /// <summary>Initializes the buffer with every member pointing at a null texture of its own sampler type.</summary>
    /// <param name="materialLoader">The loader the null textures are created by.</param>
    public SceneTextures(MaterialLoader materialLoader)
        : base(BufferTarget.UniformBuffer, (int)ReservedBufferSlots.SceneTextures, nameof(SceneTextures))
    {
        this.materialLoader = materialLoader;

        if (!GLEnvironment.BindlessTextures)
        {
            return; // Inert: the shaders declare these as loose samplers and read them off a texture unit.
        }

        Size = SceneTexturesLayout.Size;
        GL.NamedBufferData(Handle, Size, IntPtr.Zero, BufferUsageHint.DynamicDraw);

        // A scene sets only the textures it has, and a shader reading one it did not set still has to read
        // something the GPU can sample. There is no handle that means "nothing", so they all start out here.
        foreach (var member in SceneTexturesLayout.Members.Values)
        {
            Reset(member);
        }
    }

    /// <summary>
    /// Points a sampler at a texture, substituting the null texture of the sampler's own type for one of the
    /// wrong target or one already deleted. Does nothing when the name is not one this buffer holds, which is
    /// how the callers that also feed per draw samplers tell the two apart.
    /// </summary>
    /// <param name="samplerName">The sampler uniform name.</param>
    /// <param name="texture">The texture to sample.</param>
    /// <param name="sampler">A sampler object to read the texture through, or 0 for its own parameters.</param>
    /// <returns><see langword="true"/> when the sampler is held here.</returns>
    public bool SetTexture(string samplerName, RenderTexture texture, int sampler = 0)
    {
        if (!SceneTexturesLayout.Contains(samplerName))
        {
            return false;
        }

        var member = SceneTexturesLayout.Members[samplerName];

        // A deleted texture is one the scene teardown order let outlive the buffer naming it.
        if (texture.Handle != 0 && texture.Target == MaterialLoader.GetTextureTarget(member.Sampler))
        {
            Write(member, texture.GetBindlessHandle(sampler));
            return true;
        }

        Debug.Assert(texture.Handle == 0,
            $"'{samplerName}' is a {member.Sampler} sampler, but a {texture.Target} texture was set on it.");

        Reset(member);
        return true;
    }

    /// <summary>
    /// Binds this buffer to <see cref="ReservedBufferSlots.SceneTextures"/>. Called by <see cref="Shader.Use"/>,
    /// since which buffer occupies the slot is state of the GL context rather than of this object.
    /// </summary>
    public void Bind()
    {
        if (Size > 0)
        {
            BindBufferBase();
        }
    }

    private void Reset(in GlobalsMember member)
        => Write(member, materialLoader.GetNullTexture(member.Sampler).GetBindlessHandle());

    private void Write(in GlobalsMember member, long handle)
    {
        Span<byte> staged = stackalloc byte[sizeof(long)];
        MemoryMarshal.Write(staged, handle);

        var target = bytes.AsSpan(member.Offset, staged.Length);

        // Written far more often than it changes: a pass sets the same scene textures it set last time, and
        // only a resize or a scene load puts a different handle in one of them.
        if (target.SequenceEqual(staged))
        {
            return;
        }

        staged.CopyTo(target);

        GL.NamedBufferSubData(Handle, member.Offset, staged.Length, ref bytes[member.Offset]);
    }
}
