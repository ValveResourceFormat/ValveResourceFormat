
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Blocks;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// GPU vertex and index buffers created from <see cref="VBIB"/> mesh data.
    /// </summary>
    public class GPUMeshBuffers
    {
        /// <summary>Gets the OpenGL handles for each uploaded vertex buffer.</summary>
        public int[] VertexBuffers { get; private set; }

        /// <summary>Gets the OpenGL handles for each uploaded index buffer.</summary>
        public int[] IndexBuffers { get; private set; }

        /// <summary>Uploads all vertex and index buffers from the provided <see cref="VBIB"/> to the GPU.</summary>
        /// <param name="device">Device that creates the buffer objects.</param>
        /// <param name="vbib">Source vertex and index buffer data.</param>
        /// <param name="name">Mesh name used to label the buffers.</param>
        public GPUMeshBuffers(GraphicsDevice device, VBIB vbib, string name)
        {
            VertexBuffers = new int[vbib.VertexBuffers.Count];

            for (var i = 0; i < vbib.VertexBuffers.Count; i++)
            {
                VertexBuffers[i] = device.CreateBuffer($"{name} VB {i}");
                GL.NamedBufferData(VertexBuffers[i], (IntPtr)vbib.VertexBuffers[i].TotalSizeInBytes, vbib.VertexBuffers[i].Data, BufferUsageHint.StaticDraw);
            }

            IndexBuffers = new int[vbib.IndexBuffers.Count];

            for (var i = 0; i < vbib.IndexBuffers.Count; i++)
            {
                IndexBuffers[i] = device.CreateBuffer($"{name} IB {i}");
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
