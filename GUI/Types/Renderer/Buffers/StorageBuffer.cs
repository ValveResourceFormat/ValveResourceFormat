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
            BindBufferBase();
        }

        public void Create<T>(List<T> data) where T : struct
        {
            Create(ListAccessors<T>.GetBackingArray(data), Unsafe.SizeOf<T>());
        }

        public void Create<T>(T[] data, int sizeOfData) where T : struct
        {
            GL.NamedBufferData(Handle, data.Length * sizeOfData, data, BufferUsageHint.StaticRead);
        }

        public void Update<T>(T[] data, int sizeOfData) where T : struct
        {
            GL.NamedBufferSubData(Handle, IntPtr.Zero, data.Length * sizeOfData, data);
        }
    }
}
