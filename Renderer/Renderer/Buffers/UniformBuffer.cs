using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer.Buffers
{
    /// <summary>
    /// Uniform buffer object that stores shader-accessible structured data on the GPU.
    /// </summary>
    /// <typeparam name="T">The struct type to store in the buffer.</typeparam>
    public class UniformBuffer<T> : Buffer, IDisposable
        where T : notnull, new()
    {
        [NotNull]
        T data;
        /// <summary>Gets or sets the structured data stored in this uniform buffer, uploading to the GPU on set.</summary>
        public T Data { get => data; set { data = value; Update(); } }

        // A buffer where the structure is marshalled into, before being sent to the GPU
        readonly float[] cpuBuffer;
        readonly GCHandle cpuBufferHandle;

        /// <summary>Initializes a new uniform buffer at the given binding point index.</summary>
        /// <param name="bindingPoint">The UBO binding point index.</param>
        public UniformBuffer(int bindingPoint) : base(BufferTarget.UniformBuffer, bindingPoint, typeof(T).Name)
        {
            Size = Marshal.SizeOf<T>();
            Debug.Assert(Size % 16 == 0);
            Debug.Assert(Size <= 65536);

            cpuBuffer = new float[Size / 4];
            cpuBufferHandle = GCHandle.Alloc(cpuBuffer, GCHandleType.Pinned);

            data = new T();
            Initialize();
        }

        /// <summary>Initializes a new uniform buffer at the given reserved slot.</summary>
        /// <param name="slot">The reserved buffer slot to bind to.</param>
        public UniformBuffer(ReservedBufferSlots slot) : this((int)slot) { }

        private void WriteToCpuBuffer()
        {
            Debug.Assert(Size == Marshal.SizeOf(data));

            var cpuBufferPtr = cpuBufferHandle.AddrOfPinnedObject();

            if (typeof(T).IsValueType)
            {
                unsafe
                {
                    // avoid Marshal.StructureToPtr boxing our struct
                    Unsafe.Write(cpuBufferPtr.ToPointer(), data);
                }
            }
            else
            {
                Marshal.StructureToPtr(data, cpuBufferPtr, false);
            }
        }

        private void Initialize()
        {
            WriteToCpuBuffer();
            GL.NamedBufferData(Handle, Size, cpuBuffer, BufferUsageHint.StaticDraw);
            BindBufferBase();
        }

        /// <summary>Marshals <see cref="Data"/> into the intermediate CPU buffer and uploads it to the GPU.</summary>
        public void Update()
        {
            WriteToCpuBuffer();
            GL.NamedBufferSubData(Handle, IntPtr.Zero, Size, cpuBuffer);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>Releases the pinned CPU buffer handle held by this buffer.</summary>
        protected virtual void Dispose(bool disposing)
        {
            // make sure dispose gets called, or this will leak
            if (disposing)
            {
                cpuBufferHandle.Free();
            }
        }
    }
}
