using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Blocks;

namespace GUI.Types.Renderer
{
    class GPUMeshBuffers
    {
        public struct Buffer
        {
            public int Handle;
            public long Size;
        }

        public Buffer[] VertexBuffers { get; private set; }
        public Buffer[] IndexBuffers { get; private set; }

        public GPUMeshBuffers(VBIB vbib)
        {
            VertexBuffers = new Buffer[vbib.VertexBuffers.Count];
            IndexBuffers = new Buffer[vbib.IndexBuffers.Count];

            var vertexHandles = new int[vbib.VertexBuffers.Count];
            GL.CreateBuffers(vbib.VertexBuffers.Count, vertexHandles);

            var indexHandles = new int[vbib.IndexBuffers.Count];
            GL.CreateBuffers(vbib.IndexBuffers.Count, indexHandles);

            for (var i = 0; i < vbib.VertexBuffers.Count; i++)
            {
                VertexBuffers[i].Handle = vertexHandles[i];
                GL.NamedBufferData(VertexBuffers[i].Handle, (IntPtr)(vbib.VertexBuffers[i].ElementCount * vbib.VertexBuffers[i].ElementSizeInBytes), vbib.VertexBuffers[i].Data, BufferUsageHint.StaticDraw);
                GL.GetNamedBufferParameter(VertexBuffers[i].Handle, BufferParameterName.BufferSize, out VertexBuffers[i].Size);
            }

            for (var i = 0; i < vbib.IndexBuffers.Count; i++)
            {
                IndexBuffers[i].Handle = indexHandles[i];
                GL.NamedBufferData(IndexBuffers[i].Handle, (IntPtr)(vbib.IndexBuffers[i].ElementCount * vbib.IndexBuffers[i].ElementSizeInBytes), vbib.IndexBuffers[i].Data, BufferUsageHint.StaticDraw);
                GL.GetNamedBufferParameter(IndexBuffers[i].Handle, BufferParameterName.BufferSize, out IndexBuffers[i].Size);
            }
        }
    }
}
