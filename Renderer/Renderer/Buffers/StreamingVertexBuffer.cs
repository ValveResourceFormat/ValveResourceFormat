using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer.Buffers
{
    internal sealed class StreamingVertexBuffer
    {
        public int Handle { get; private set; }

        private readonly string label;
        private int vertexArray;
        private int bindingIndex;
        private int stride;
        private int capacityInBytes;

        public StreamingVertexBuffer(string label)
        {
            this.label = label;
            Handle = GraphicsDevice.CreateBuffer(label);
        }

        // The vertex array is built from this buffer, so it is created after it and attached here
        public void AttachTo(int vao, int vertexStride, int binding = 0)
        {
            vertexArray = vao;
            stride = vertexStride;
            bindingIndex = binding;
        }

        public unsafe void Upload(ReadOnlySpan<byte> data)
        {
            if (data.IsEmpty)
            {
                return;
            }

            fixed (byte* source = data)
            {
                if (data.Length > capacityInBytes)
                {
                    GL.DeleteBuffer(Handle);
                    Handle = GraphicsDevice.CreateBuffer(label);
                    GL.NamedBufferStorage(Handle, data.Length, (nint)source, BufferStorageFlags.DynamicStorageBit);
                    GL.VertexArrayVertexBuffer(vertexArray, bindingIndex, Handle, 0, stride);

                    capacityInBytes = data.Length;
                    return;
                }

                GL.NamedBufferSubData(Handle, 0, data.Length, (nint)source);
            }
        }

        public void Delete()
        {
            GL.DeleteBuffer(Handle);
            Handle = 0;
            capacityInBytes = 0;
        }
    }
}
