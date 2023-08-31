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
        where T : struct
    {
        public int Handle { get; }
        public int Size { get; }
        public int BindingPoint { get; }
        public string Name { get; }

        public bool IsCreated { get; private set; }

        T data = new();
        public T Data
        {
            get => data;
            set
            {
                data = value;

                if (!IsCreated)
                {
                    Initialize();
                }
                else
                {
                    Update();
                }
            }
        }

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

            Name = typeof(T).Name;
#if DEBUG
            using (BindingContext())
            {
                GL.ObjectLabel(ObjectLabelIdentifier.Buffer, Handle, Name.Length, Name);
            }
#endif
        }

        void Bind() => GL.BindBuffer(Target, Handle);
        static void Unbind() => GL.BindBuffer(Target, 0);
        BindingContext BindingContext() => new(Bind, Unbind);

        private void WriteToCpuBuffer()
        {
            Marshal.StructureToPtr(data, cpuBufferHandle.AddrOfPinnedObject(), false);
        }

        public void Initialize()
        {
            using (BindingContext())
            {
                WriteToCpuBuffer();
                GL.BufferData(Target, Size, cpuBuffer, BufferUsageHint.StaticDraw);
                GL.BindBufferBase(BufferRangeTarget.UniformBuffer, BindingPoint, Handle);
                IsCreated = true;
            }
        }

        public void Update()
        {
            Debug.Assert(IsCreated);

            using (BindingContext())
            {
                WriteToCpuBuffer();
                GL.BufferSubData(Target, IntPtr.Zero, Size, cpuBuffer);
            }
        }

        public void UpdateWith(params Func<T, T>[] updaters)
        {
            var data = Data;
            foreach (var updater in updaters)
            {
                data = updater(data);
            }

            Data = data;
        }

        public void Dispose()
        {
            // make sure dispose gets called, or this will leak
            cpuBufferHandle.Free();
            GL.DeleteBuffer(Handle);
        }
    }
}
