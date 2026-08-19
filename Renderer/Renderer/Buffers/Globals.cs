using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer.Buffers;

/// <summary>
/// Holds the packed values of a shader's loose global uniforms, as laid out by
/// <see cref="GlobalsLayout"/>. Every <see cref="RenderMaterial"/> owns one, filled once from the
/// shader defaults plus the material's own parameters, so drawing only has to bind it.
/// </summary>
public sealed class Globals : Buffer
{
    private byte[] bytes = [];
    private int dirtyStart = int.MaxValue;
    private int dirtyEnd;
    private bool filling;

    /// <summary>Initializes a new constant buffer bound to <see cref="ReservedBufferSlots.Globals"/>.</summary>
    /// <param name="name">Debug name, normally the shader or material this buffer belongs to.</param>
    public Globals(string name) : base(BufferTarget.UniformBuffer, (int)ReservedBufferSlots.Globals, name)
    {
    }

    /// <summary>
    /// Starts filling this buffer: sets every member to its shader source default, reallocating first if the
    /// layout changed size. Writes are held back until <see cref="EndFill"/> so the fill costs one upload.
    /// </summary>
    /// <param name="layout">The layout to apply, which differs from the current one only after a shader reload.</param>
    public void BeginFill(GlobalsLayout layout)
    {
        var allocate = Size != layout.Size;

        if (allocate)
        {
            bytes = new byte[layout.Size];
        }

        Size = layout.Size;
        filling = true;

        layout.DefaultBytes.CopyTo(bytes);

        if (allocate)
        {
            GL.NamedBufferData(Handle, Size, bytes, BufferUsageHint.DynamicDraw);

            dirtyStart = int.MaxValue;
            dirtyEnd = 0;
            return;
        }

        dirtyStart = 0;
        dirtyEnd = Size;
    }

    /// <summary>Ends a fill started by <see cref="BeginFill"/> and uploads it as one range.</summary>
    public void EndFill()
    {
        filling = false;

        Upload();
    }

    /// <summary>Writes a scalar member, converting to the declared component type.</summary>
    public void SetScalar(in GlobalsMember constant, float value)
    {
        Span<byte> staged = stackalloc byte[sizeof(float)];

        if (constant.IsFloat)
        {
            MemoryMarshal.Write(staged, value);
        }
        else
        {
            MemoryMarshal.Write(staged, (int)value);
        }

        Commit(constant, staged);
    }

    /// <summary>Writes a scalar member, converting to the declared component type.</summary>
    public void SetScalar(in GlobalsMember constant, long value)
    {
        Span<byte> staged = stackalloc byte[sizeof(float)];

        if (constant.IsFloat)
        {
            MemoryMarshal.Write(staged, (float)value);
        }
        else
        {
            MemoryMarshal.Write(staged, (int)value);
        }

        Commit(constant, staged);
    }

    /// <summary>Writes a vector member, using as many components as the declared type has.</summary>
    public void SetVector(in GlobalsMember constant, Vector4 value)
    {
        var componentCount = Math.Min(constant.ComponentCount, 4);

        Span<byte> staged = stackalloc byte[4 * sizeof(float)];
        staged = staged[..(componentCount * sizeof(float))];

        if (constant.IsFloat)
        {
            var floats = MemoryMarshal.Cast<byte, float>(staged);

            for (var i = 0; i < componentCount; i++)
            {
                floats[i] = value[i];
            }
        }
        else
        {
            var integers = MemoryMarshal.Cast<byte, int>(staged);

            for (var i = 0; i < componentCount; i++)
            {
                integers[i] = (int)value[i];
            }
        }

        Commit(constant, staged);
    }

    /// <summary>Writes a <see cref="GlobalsType.Mat4"/> member.</summary>
    /// <remarks>
    /// Written in <see cref="Matrix4x4"/> memory order, which puts each of its rows in a column of the GLSL
    /// matrix. That is the same transposition <c>glProgramUniformMatrix4</c> applied before these were packed.
    /// </remarks>
    public void SetMatrix(in GlobalsMember constant, Matrix4x4 value)
    {
        Span<byte> staged = stackalloc byte[64];
        MemoryMarshal.Write(staged, value);

        Commit(constant, staged);
    }

    /// <summary>Uploads any pending writes and binds this buffer to <see cref="ReservedBufferSlots.Globals"/>.</summary>
    /// <remarks>
    /// Binds unconditionally. Which buffer occupies the slot is state of the GL context, which outlives any
    /// one thread and can be swapped for another, so there is nothing safe to cache it in.
    /// </remarks>
    public void Bind()
    {
        Upload();
        BindBufferBase();
    }

    private void Commit(in GlobalsMember constant, ReadOnlySpan<byte> value)
    {
        var target = bytes.AsSpan(constant.Offset, value.Length);

        if (target.SequenceEqual(value))
        {
            return;
        }

        value.CopyTo(target);

        dirtyStart = Math.Min(dirtyStart, constant.Offset);
        dirtyEnd = Math.Max(dirtyEnd, constant.Offset + value.Length);

        if (!filling)
        {
            Upload();
        }
    }

    private void Upload()
    {
        if (dirtyStart >= dirtyEnd)
        {
            return;
        }

        GL.NamedBufferSubData(Handle, dirtyStart, dirtyEnd - dirtyStart, ref bytes[dirtyStart]);

        dirtyStart = int.MaxValue;
        dirtyEnd = 0;
    }
}
