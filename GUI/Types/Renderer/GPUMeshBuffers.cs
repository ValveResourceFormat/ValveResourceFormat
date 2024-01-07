using System;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Blocks;

namespace GUI.Types.Renderer
{
    class GPUMeshBuffers
    {
        public struct Buffer
        {
            public uint Handle;
            public long Size;
        }

        public Buffer[] VertexBuffers { get; private set; }
        public Buffer[] IndexBuffers { get; private set; }

        public GPUMeshBuffers(VBIB vbib)
        {
            VertexBuffers = new Buffer[vbib.VertexBuffers.Count];
            IndexBuffers = new Buffer[vbib.IndexBuffers.Count];

            for (var i = 0; i < vbib.VertexBuffers.Count; i++)
            {
                VertexBuffers[i].Handle = (uint)GL.GenBuffer();
                GL.BindBuffer(BufferTarget.ArrayBuffer, VertexBuffers[i].Handle);
                GL.BufferData(BufferTarget.ArrayBuffer, (IntPtr)(vbib.VertexBuffers[i].ElementCount * vbib.VertexBuffers[i].ElementSizeInBytes), vbib.VertexBuffers[i].Data, BufferUsageHint.StaticDraw);

                GL.GetBufferParameter(BufferTarget.ArrayBuffer, BufferParameterName.BufferSize, out VertexBuffers[i].Size);
            }

            for (var i = 0; i < vbib.IndexBuffers.Count; i++)
            {
                IndexBuffers[i].Handle = (uint)GL.GenBuffer();
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, IndexBuffers[i].Handle);
                GL.BufferData(BufferTarget.ElementArrayBuffer, (IntPtr)(vbib.IndexBuffers[i].ElementCount * vbib.IndexBuffers[i].ElementSizeInBytes), vbib.IndexBuffers[i].Data, BufferUsageHint.StaticDraw);

                GL.GetBufferParameter(BufferTarget.ElementArrayBuffer, BufferParameterName.BufferSize, out IndexBuffers[i].Size);
            }
        }
    }
}
