using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer.Buffers
{
    /// <summary>
    /// Buffer for indirect drawing commands.
    /// </summary>
    public class IndirectBuffer : Buffer
    {
        public IndirectBuffer(string name)
            : base(BufferTarget.DrawIndirectBuffer, -1, name)
        {
        }

        public void Create<T>(List<T> data) where T : struct
        {
            Create(ListAccessors<T>.GetBackingArray(data), data.Count * Unsafe.SizeOf<T>());
        }

        public void Create<T>(T[] data, int totalSizeInBytes) where T : struct
        {
            Create(data, totalSizeInBytes, BufferUsageHint.StaticDraw);
        }

        public void Create<T>(T[] data, int totalSizeInBytes, BufferUsageHint usage) where T : struct
        {
            Size = totalSizeInBytes;
            GL.NamedBufferData(Handle, totalSizeInBytes, data, usage);
        }

        public void Bind()
        {
            GL.BindBuffer(Target, Handle);
        }
    }
}
