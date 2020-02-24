using System;
using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer
{
    public class QuadIndexBuffer
    {
        public int GLHandle { get; private set; }

        public QuadIndexBuffer(int size)
        {
            GLHandle = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, GLHandle);

            var indices = new ushort[size];
            for (int i = 0; i < size / 6; ++i)
            {
                indices[(i * 6) + 0] = (ushort)((i * 4) + 0);
                indices[(i * 6) + 1] = (ushort)((i * 4) + 1);
                indices[(i * 6) + 2] = (ushort)((i * 4) + 2);
                indices[(i * 6) + 3] = (ushort)((i * 4) + 0);
                indices[(i * 6) + 4] = (ushort)((i * 4) + 2);
                indices[(i * 6) + 5] = (ushort)((i * 4) + 3);
            }

            GL.BufferData(BufferTarget.ElementArrayBuffer, size * sizeof(ushort), indices, BufferUsageHint.StaticDraw);
        }
    }
}
