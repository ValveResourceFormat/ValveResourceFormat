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

        /// <summary>Initializes a new storage buffer bound to the given reserved slot.</summary>
        public StorageBuffer(ReservedBufferSlots bindingPoint)
            : base(BufferTarget.ShaderStorageBuffer, (int)bindingPoint, bindingPoint.ToString())
        {
        }

        /// <summary>Allocates a new storage buffer sized for the given number of elements.</summary>
        /// <remarks>
        /// BufferUsageHint.DynamicRead creates a mapped buffer
        ///  </remarks>
        /// <typeparam name="T">The element type used to compute the total byte size.</typeparam>
        /// <param name="bindingPoint">The reserved slot to bind the buffer to.</param>
        /// <param name="elements">Number of elements to allocate space for.</param>
        /// <param name="usage">The intended usage hint for the buffer.</param>
        /// <returns>The newly allocated <see cref="StorageBuffer"/>.</returns>
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

        /// <summary>Uploads the contents of a list to this buffer, replacing any existing data.</summary>
        public void Create<T>(List<T> data) where T : struct
        {
            Create(ListAccessors<T>.GetBackingArray(data), data.Count * Unsafe.SizeOf<T>());
        }

        /// <summary>Uploads a typed array to this buffer with the given total byte size.</summary>
        /// <param name="data">The source array to upload.</param>
        /// <param name="totalSizeInBytes">Total number of bytes to upload from <paramref name="data"/>.</param>
        public void Create<T>(T[] data, int totalSizeInBytes) where T : struct
        {
            Size = totalSizeInBytes;
            GL.NamedBufferData(Handle, totalSizeInBytes, data, BufferUsageHint.StreamDraw);
        }

        /// <summary>Uploads a read-only span to this buffer using the specified usage hint.</summary>
        /// <param name="data">The source span to upload.</param>
        /// <param name="usageHint">The intended usage pattern for the buffer.</param>
        public void Create<T>(ReadOnlySpan<T> data, BufferUsageHint usageHint) where T : struct
        {
            Size = data.Length * Unsafe.SizeOf<T>();
            GL.NamedBufferData(Handle, Size, ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(data)), usageHint);
        }

        /// <summary>Updates a region of this buffer with new data, allocating it first if empty.</summary>
        /// <param name="data">The source array containing new data.</param>
        /// <param name="offset">Byte offset into the buffer at which to begin writing.</param>
        /// <param name="size">Number of bytes to write.</param>
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

        /// <summary>Zeroes the entire contents of this buffer.</summary>
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

        /// <summary>Reads the buffer's contents back from the GPU into the given struct.</summary>
        /// <param name="output">The struct to populate with buffer data.</param>
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

        /// <inheritdoc/>
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
