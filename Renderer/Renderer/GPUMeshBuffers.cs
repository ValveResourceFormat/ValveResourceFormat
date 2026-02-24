
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Blocks;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// GPU vertex and index buffers created from VBIB mesh data.
    /// </summary>
    public class GPUMeshBuffers
    {
        public int[] VertexBuffers { get; private set; }
        public int[] IndexBuffers { get; private set; }

        public GPUMeshBuffers(VBIB vbib)
        {
            VertexBuffers = new int[vbib.VertexBuffers.Count];
            GL.CreateBuffers(vbib.VertexBuffers.Count, VertexBuffers);

            for (var i = 0; i < vbib.VertexBuffers.Count; i++)
            {
                GL.NamedBufferData(VertexBuffers[i], (IntPtr)vbib.VertexBuffers[i].TotalSizeInBytes, vbib.VertexBuffers[i].Data, BufferUsageHint.StaticDraw);
            }

            IndexBuffers = new int[vbib.IndexBuffers.Count];
            GL.CreateBuffers(vbib.IndexBuffers.Count, IndexBuffers);

            for (var i = 0; i < vbib.IndexBuffers.Count; i++)
            {
                GL.NamedBufferData(IndexBuffers[i], (IntPtr)vbib.IndexBuffers[i].TotalSizeInBytes, vbib.IndexBuffers[i].Data, BufferUsageHint.StaticDraw);
            }
        }

        public void Delete()
        {
            GL.DeleteBuffers(VertexBuffers.Length, VertexBuffers);
            GL.DeleteBuffers(IndexBuffers.Length, IndexBuffers);
        }
    }
}
