using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer.UniformBuffers
{
    interface IBlockBindableBuffer : IDisposable
    {
        int BindingPoint { get; }
        string Name { get; }

        public void SetBlockBinding(Shader shader)
        {
            var blockIndex = shader.GetUniformBlockIndex(Name);
            if (blockIndex > -1)
            {
                GL.UniformBlockBinding(shader.Program, blockIndex, BindingPoint);
            }
        }
    }

    class UniformBuffer<T> : IBlockBindableBuffer
        where T : new()
    {
        public int Handle { get; }
        public int Size { get; }
        public int BindingPoint { get; }
        public string Name { get; }

        T data;
        public T Data { get => data; set { data = value; Update(); } }

        // A buffer where the structure is marshalled into, before being sent to the GPU
        readonly float[] cpuBuffer;
        GCHandle cpuBufferHandle;

        const BufferTarget Target = BufferTarget.UniformBuffer;

        public UniformBuffer(int bindingPoint)
        {
            Handle = GL.GenBuffer();
            BindingPoint = bindingPoint;

            Size = Marshal.SizeOf<T>();
            Debug.Assert(Size % 16 == 0);

            cpuBuffer = new float[Size / 4];
            cpuBufferHandle = GCHandle.Alloc(cpuBuffer, GCHandleType.Pinned);

            data = new T();
            Initialize();

            Name = typeof(T).Name;
#if DEBUG
            Bind();
            GL.ObjectLabel(ObjectLabelIdentifier.Buffer, Handle, Name.Length, Name);
            Unbind();
#endif
        }

        void Bind() => GL.BindBuffer(Target, Handle);
        static void Unbind() => GL.BindBuffer(Target, 0);

        private void WriteToCpuBuffer()
        {
            Debug.Assert(Size == Marshal.SizeOf(data));
            Marshal.StructureToPtr(data, cpuBufferHandle.AddrOfPinnedObject(), false);
        }

        private void Initialize()
        {
            Bind();
            WriteToCpuBuffer();
            GL.BufferData(Target, Size, cpuBuffer, BufferUsageHint.StaticDraw);
            GL.BindBufferBase(BufferRangeTarget.UniformBuffer, BindingPoint, Handle);
            Unbind();
        }

        public void Update()
        {
            Bind();
            WriteToCpuBuffer();
            GL.BufferSubData(Target, IntPtr.Zero, Size, cpuBuffer);
            Unbind();
        }

        public void Dispose()
        {
            // make sure dispose gets called, or this will leak
            cpuBufferHandle.Free();
            GL.DeleteBuffer(Handle);
        }
    }
}
