using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer.UniformBuffers
{
    interface IBuffer : IDisposable
    {
    }

    public enum ReservedBufferSlots
    {
        View = 0,
        Lighting = 1,
        LightProbe = 2,
    }

    class UniformBuffer<T> : IBuffer
        where T : new()
    {
        public int Handle { get; }
        public int Size { get; }
        public int BindingPoint { get; }
        private const BufferRangeTarget Target = BufferRangeTarget.UniformBuffer;

        [NotNull]
        T data;
        public T Data { get => data; set { data = value; Update(); } }

        // A buffer where the structure is marshalled into, before being sent to the GPU
        readonly float[] cpuBuffer;
        readonly GCHandle cpuBufferHandle;

        public UniformBuffer(int bindingPoint)
        {
            GL.CreateBuffers(1, out int handle);
            Handle = handle;
            BindingPoint = bindingPoint;

            Size = Marshal.SizeOf<T>();
            Debug.Assert(Size % 16 == 0);

            cpuBuffer = new float[Size / 4];
            cpuBufferHandle = GCHandle.Alloc(cpuBuffer, GCHandleType.Pinned);

            data = new T();
            Initialize();

#if DEBUG
            var objectLabel = nameof(UniformBuffer<T>);
            GL.ObjectLabel(ObjectLabelIdentifier.Buffer, Handle, objectLabel.Length, objectLabel);
#endif
        }

        public UniformBuffer(ReservedBufferSlots slot) : this((int)slot) { }

        private void WriteToCpuBuffer()
        {
            Debug.Assert(Size == Marshal.SizeOf(data));
            Marshal.StructureToPtr(data, cpuBufferHandle.AddrOfPinnedObject(), false);
        }

        private void Initialize()
        {
            WriteToCpuBuffer();
            GL.NamedBufferData(Handle, Size, cpuBuffer, BufferUsageHint.StaticDraw);
            BindBufferBase();
        }

        public void Update()
        {
            WriteToCpuBuffer();
            GL.NamedBufferSubData(Handle, IntPtr.Zero, Size, cpuBuffer);
        }

        public void BindBufferBase()
        {
            GL.BindBufferBase(Target, BindingPoint, Handle);
        }

        public void Dispose()
        {
            // make sure dispose gets called, or this will leak
            cpuBufferHandle.Free();
            GL.DeleteBuffer(Handle);
        }
    }
}
