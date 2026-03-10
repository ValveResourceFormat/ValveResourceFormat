
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Blocks;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// GPU vertex and index buffers created from VBIB mesh data.
    /// </summary>
    public class GPUMeshBuffers
    {
        /// <summary>Gets the OpenGL handles for each uploaded vertex buffer.</summary>
        public int[] VertexBuffers { get; private set; }

        /// <summary>Gets the OpenGL handles for each uploaded index buffer.</summary>
        public int[] IndexBuffers { get; private set; }

        /// <summary>Uploads all vertex and index buffers from the provided VBIB to the GPU.</summary>
        /// <param name="vbib">Source vertex and index buffer data.</param>
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

        /// <summary>Deletes all GPU vertex and index buffers.</summary>
        public void Delete()
        {
            GL.DeleteBuffers(VertexBuffers.Length, VertexBuffers);
            GL.DeleteBuffers(IndexBuffers.Length, IndexBuffers);
        }
    }
}
