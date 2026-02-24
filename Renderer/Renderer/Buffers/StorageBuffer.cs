using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer.Buffers
{
    /// <summary>
    /// Shader storage buffer object for large read-write data arrays on the GPU.
    /// </summary>
    public class StorageBuffer : Buffer
    {
        private IntPtr PersistentPtr;

        public StorageBuffer(ReservedBufferSlots bindingPoint)
            : base(BufferTarget.ShaderStorageBuffer, (int)bindingPoint, bindingPoint.ToString())
        {
        }

        /// <remarks>
        /// BufferUsageHint.DynamicRead creates a mapped buffer
        ///  </remarks>
        public static StorageBuffer Allocate<T>(ReservedBufferSlots bindingPoint, int elements, BufferUsageHint usage)
        {
            var buffer = new StorageBuffer(bindingPoint) { Size = elements * Unsafe.SizeOf<T>() };
            if (usage == BufferUsageHint.DynamicRead)
            {
                GL.NamedBufferStorage(buffer.Handle, buffer.Size, IntPtr.Zero, BufferStorageFlags.MapPersistentBit | BufferStorageFlags.MapReadBit | BufferStorageFlags.MapCoherentBit);
                buffer.PersistentPtr = GL.MapNamedBuffer(buffer.Handle, BufferAccess.ReadOnly);
            }
            else
            {
                GL.NamedBufferData(buffer.Handle, buffer.Size, IntPtr.Zero, usage);
            }
            return buffer;
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

        public void Create<T>(ReadOnlySpan<T> data, BufferUsageHint usageHint) where T : struct
        {
            Size = data.Length * Unsafe.SizeOf<T>();
            GL.NamedBufferData(Handle, Size, ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(data)), usageHint);
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

            if (PersistentPtr != IntPtr.Zero)
            {
                Debug.Assert(offset + size <= Size);
                unsafe
                {
                    var dest = (void*)(PersistentPtr + offset);
                    Unsafe.CopyBlock(dest, Unsafe.AsPointer(ref data[0]), (uint)size);
                }
                return;
            }


            GL.NamedBufferSubData(Handle, offset, size, data);
        }

        public unsafe void Clear()
        {
            if (PersistentPtr != IntPtr.Zero)
            {
                // For mapped buffers, write directly to mapped memory
                Unsafe.InitBlock((void*)PersistentPtr, 0, (uint)Size);
                return;
            }

            GL.ClearNamedBufferData(Handle, PixelInternalFormat.R32ui, PixelFormat.RedInteger, PixelType.UnsignedInt, IntPtr.Zero);
        }

        public unsafe void Read<T>(ref T output) where T : struct
        {
            Debug.Assert(Size <= Unsafe.SizeOf<T>());

            if (PersistentPtr != IntPtr.Zero)
            {
                output = Unsafe.Read<T>((void*)PersistentPtr);
                return;
            }

            GL.GetNamedBufferSubData(Handle, IntPtr.Zero, Size, ref output);
        }

        public override void Delete()
        {
            if (PersistentPtr != IntPtr.Zero)
            {
                GL.UnmapNamedBuffer(Handle);
                PersistentPtr = IntPtr.Zero;
            }

            base.Delete();
        }
    }
}
