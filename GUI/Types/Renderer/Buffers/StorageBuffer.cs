using System.Runtime.CompilerServices;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer.Buffers
{
    class StorageBuffer : Buffer
    {
        public StorageBuffer(ReservedBufferSlots bindingPoint)
            : base(BufferTarget.ShaderStorageBuffer, (int)bindingPoint, bindingPoint.ToString())
        {
        }

        public void Create<T>(List<T> data) where T : struct
        {
            Create(ListAccessors<T>.GetBackingArray(data), data.Count * Unsafe.SizeOf<T>());
        }

        public void Create<T>(T[] data, int totalSizeInBytes) where T : struct
        {
            Size = totalSizeInBytes;
            GL.NamedBufferData(Handle, totalSizeInBytes, data, BufferUsageHint.StreamDraw);
        }

        public void Update<T>(T[] data, int offset, int size) where T : struct
        {
            if (Size == 0)
            {
                if (offset == 0)
                {
                    Create(data, size);
                    return;
                }

                throw new InvalidOperationException("Trying to update an unitialized buffer.");
            }

            GL.NamedBufferSubData(Handle, offset, size, data);
        }
    }
}
