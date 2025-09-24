using System.Buffers;
using System.Runtime.InteropServices;
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

            System.Diagnostics.Debug.Assert(size % 6 == 0);
#endif

            var sizeInBytes = size * sizeof(ushort);
            var indicesBytes = ArrayPool<byte>.Shared.Rent(sizeInBytes);

            try
            {
                var indices = MemoryMarshal.Cast<byte, ushort>(indicesBytes.AsSpan());
                for (var i = 0; i < size / 6; ++i)
                {
                    indices[(i * 6) + 0] = (ushort)((i * 4) + 0);
                    indices[(i * 6) + 1] = (ushort)((i * 4) + 1);
                    indices[(i * 6) + 2] = (ushort)((i * 4) + 2);
                    indices[(i * 6) + 3] = (ushort)((i * 4) + 0);
                    indices[(i * 6) + 4] = (ushort)((i * 4) + 2);
                    indices[(i * 6) + 5] = (ushort)((i * 4) + 3);
                }

                GL.NamedBufferData(handle, sizeInBytes, indicesBytes, BufferUsageHint.StaticDraw);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(indicesBytes);
            }
        }
    }
}
