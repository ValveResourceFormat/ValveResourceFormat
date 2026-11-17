using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer.Buffers;

/// <summary>
/// Handles of the textures the renderer supplies for the whole scene, laid out by
/// <see cref="SceneTexturesLayout"/>. One per renderer, in place of the texture unit each of these held.
/// </summary>
public sealed class SceneTextures : Buffer
{
    private readonly byte[] bytes = new byte[SceneTexturesLayout.Size];
    private readonly MaterialLoader materialLoader;

    /// <summary>Starts every member off at a null texture of its own sampler type.</summary>
    /// <param name="materialLoader">The loader the null textures are created by.</param>
    public SceneTextures(MaterialLoader materialLoader)
        : base(BufferTarget.UniformBuffer, (int)ReservedBufferSlots.SceneTextures, nameof(SceneTextures))
    {
        this.materialLoader = materialLoader;

        if (!GLEnvironment.BindlessTextures)
        {
            return; // Inert: the shaders declare these as loose samplers on a texture unit instead.
        }

        Size = SceneTexturesLayout.Size;
        GL.NamedBufferData(Handle, Size, IntPtr.Zero, BufferUsageHint.DynamicDraw);

        // A scene sets only the textures it has, and there is no handle meaning "nothing", so a shader
        // reading one the scene never set has to find a null texture there.
        Reset();
    }

    /// <summary>
    /// Points a sampler at a texture, falling back to the null texture of its type for a wrong target or a
    /// deleted texture. Returns false when the name is not one this buffer holds, which is how the callers
    /// that also feed per draw samplers tell the two apart.
    /// </summary>
    /// <param name="samplerName">The sampler uniform name.</param>
    /// <param name="texture">The texture to sample.</param>
    /// <param name="sampler">A sampler object to read the texture through, or 0 for its own parameters.</param>
    public bool SetTexture(string samplerName, RenderTexture texture, int sampler = 0)
    {
        if (!SceneTexturesLayout.Contains(samplerName))
        {
            return false;
        }

        var member = SceneTexturesLayout.Members[samplerName];

        // A zero handle is a texture the teardown order let the buffer outlive.
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
    /// Points every member back at its null texture. Called when the textures a scene loaded are deleted,
    /// since a member the next scene does not set again would otherwise keep naming freed memory.
    /// </summary>
    public void Reset()
    {
        foreach (var member in SceneTexturesLayout.Members.Values)
        {
            Reset(member);
        }
    }

    /// <summary>Binds this buffer. Called by <see cref="Shader.Use"/>, since the slot is context state.</summary>
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

        // Written every pass, changed only by a resize or a scene load.
        if (target.SequenceEqual(staged))
        {
            return;
        }

        staged.CopyTo(target);

        GL.NamedBufferSubData(Handle, member.Offset, staged.Length, ref bytes[member.Offset]);
    }
}
