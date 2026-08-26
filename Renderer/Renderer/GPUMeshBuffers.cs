
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
        /// <param name="vbib">Source vertex and index buffer data.</param>
        /// <param name="name">Mesh name used to label the buffers.</param>
        public GPUMeshBuffers(VBIB vbib, string name)
        {
            VertexBuffers = new int[vbib.VertexBuffers.Count];

            for (var i = 0; i < vbib.VertexBuffers.Count; i++)
            {
                var buffer = vbib.VertexBuffers[i];
                VertexBuffers[i] = GraphicsDevice.CreateBuffer($"{name} VB {i}", buffer.Data.AsSpan(0, (int)buffer.TotalSizeInBytes), BufferUsage.Static);
            }

            IndexBuffers = new int[vbib.IndexBuffers.Count];

            for (var i = 0; i < vbib.IndexBuffers.Count; i++)
            {
                var buffer = vbib.IndexBuffers[i];

                // A meshlet-encoded index buffer (MSLT) keeps its raw meshopt bytes, which are shorter than the
                // decoded TotalSizeInBytes. The classic draw path never binds it, so uploading the raw bytes is
                // enough; clamp so it does not overrun.
                var length = Math.Min((int)buffer.TotalSizeInBytes, buffer.Data.Length);
                IndexBuffers[i] = GraphicsDevice.CreateBuffer($"{name} IB {i}", buffer.Data.AsSpan(0, length), BufferUsage.Static);
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
