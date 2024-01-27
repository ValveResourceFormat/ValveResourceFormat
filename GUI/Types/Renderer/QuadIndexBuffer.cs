using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer
{
    class QuadIndexBuffer
    {
        public int GLHandle { get; }

        public QuadIndexBuffer(int size)
        {
            GL.CreateBuffers(1, out int handle);
            GLHandle = handle;

#if DEBUG
            var bufferLabel = nameof(QuadIndexBuffer);
            GL.ObjectLabel(ObjectLabelIdentifier.Buffer, handle, bufferLabel.Length, bufferLabel);
#endif

            var indices = new ushort[size];
            for (var i = 0; i < size / 6; ++i)
            {
                indices[(i * 6) + 0] = (ushort)((i * 4) + 0);
                indices[(i * 6) + 1] = (ushort)((i * 4) + 1);
                indices[(i * 6) + 2] = (ushort)((i * 4) + 2);
                indices[(i * 6) + 3] = (ushort)((i * 4) + 0);
                indices[(i * 6) + 4] = (ushort)((i * 4) + 2);
                indices[(i * 6) + 5] = (ushort)((i * 4) + 3);
            }

            GL.NamedBufferData(handle, size * sizeof(ushort), indices, BufferUsageHint.StaticDraw);
        }
    }
}
