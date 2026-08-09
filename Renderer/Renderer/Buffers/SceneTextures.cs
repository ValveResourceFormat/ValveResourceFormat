using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer.Buffers;

/// <summary>
/// Holds the bindless handles of the textures the renderer supplies for the whole scene, laid out by
/// <see cref="SceneTexturesLayout"/>. One per renderer, bound once a frame in place of the texture unit each
/// of these used to occupy.
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
        foreach (var member in SceneTexturesLayout.OrderedMembers)
        {
            Reset(member.Name);
        }
    }

    /// <summary>
    /// Points a sampler at a texture. Does nothing when the name is not one this buffer holds, which is how
    /// the callers that also feed per draw samplers tell the two apart.
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

        if (texture.Target != MaterialLoader.GetTextureTarget(member.Sampler))
        {
            System.Diagnostics.Debug.Assert(false,
                $"'{samplerName}' is a {member.Sampler} sampler, but a {texture.Target} texture was set on it.");

            Reset(samplerName);
            return true;
        }

        if (texture.Handle == 0)
        {
            // Deleted out from under the scene, which the teardown order allows.
            Reset(samplerName);
            return true;
        }

        Write(member, texture.GetBindlessHandle(sampler));
        return true;
    }

    /// <summary>Points a sampler back at the null texture of its type, for a scene that has none of its own.</summary>
    /// <param name="samplerName">The sampler uniform name.</param>
    public void Reset(string samplerName)
    {
        if (!SceneTexturesLayout.Contains(samplerName))
        {
            return;
        }

        var member = SceneTexturesLayout.Members[samplerName];

        Write(member, materialLoader.GetNullTexture(member.Sampler).GetBindlessHandle());
    }

    /// <summary>
    /// Binds this buffer to <see cref="ReservedBufferSlots.SceneTextures"/>. Called by <see cref="Shader.Use"/>,
    /// since which buffer occupies the slot is state of the GL context rather than of this object.
    /// </summary>
    public void Bind()
    {
        if (Size == 0)
        {
            return;
        }

        BindBufferBase();
    }

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
