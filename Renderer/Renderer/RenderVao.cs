using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Vertex array state for a piece of geometry. Attribute locations are canonical
    /// (<see cref="VertexAttributeLocations"/>), so one VAO serves every shader that draws the
    /// geometry. The VAO itself is created and owned by <see cref="GPUMeshBufferCache"/>, keyed by
    /// the resolved attribute bindings plus the actual GPU buffer handles involved; this only
    /// memoizes the lookup.
    /// </summary>
    /// <param name="meshBuffers">The cache that creates and owns the VAOs.</param>
    /// <param name="vertexBuffers">Vertex buffer bindings describing the geometry layout.</param>
    /// <param name="indexBuffer">OpenGL handle of the index buffer, or 0 for non-indexed geometry.</param>
    /// <param name="inputSignature">Material input signature mapping buffer semantics to shader attribute names.</param>
    /// <param name="debugLabel">Optional label applied to newly created VAOs in debug builds.</param>
    public class RenderVao(GPUMeshBufferCache meshBuffers, VertexDrawBuffer[] vertexBuffers, int indexBuffer, Material.VsInputSignature inputSignature, string? debugLabel = null)
    {
        /// <summary>Initializes vertex array state for geometry in a single vertex buffer.</summary>
        /// <param name="meshBuffers">The cache that creates and owns the VAOs.</param>
        /// <param name="debugLabel">Optional label applied to newly created VAOs in debug builds.</param>
        /// <param name="vertexBuffer">OpenGL handle of the vertex buffer.</param>
        /// <param name="stride">Size in bytes of a single vertex.</param>
        /// <param name="inputLayoutFields">Input layout describing the vertex attributes.</param>
        /// <param name="indexBuffer">OpenGL handle of the index buffer, or 0 for non-indexed geometry.</param>
        /// <param name="inputSignature">Optional material input signature mapping buffer semantics to shader attribute names.</param>
        public RenderVao(GPUMeshBufferCache meshBuffers, string? debugLabel, int vertexBuffer, int stride, VBIB.RenderInputLayoutField[] inputLayoutFields,
            int indexBuffer = 0, Material.VsInputSignature inputSignature = default)
            : this(meshBuffers,
            [
                new VertexDrawBuffer
                {
                    Handle = vertexBuffer,
                    ElementSizeInBytes = (uint)stride,
                    InputLayoutFields = inputLayoutFields,
                },
            ], indexBuffer, inputSignature, debugLabel)
        {
        }

        private int vao = -1;

        /// <summary>Returns the VAO for this geometry, creating it through the cache on first use.</summary>
        /// <returns>The OpenGL VAO handle.</returns>
        public int Get()
        {
            if (vao == -1)
            {
                vao = meshBuffers.GetVertexArrayObject(vertexBuffers, inputSignature, indexBuffer, debugLabel);
            }

            return vao;
        }

        /// <summary>Deletes the cached VAOs built from this state's buffers. Call before deleting a buffer
        /// that is not tracked by <see cref="GPUMeshBufferCache"/>, so no VAO is left referencing it.</summary>
        public void Delete()
        {
            meshBuffers.InvalidateVertexArrayObjectsForFreedBuffers([.. Array.ConvertAll(vertexBuffers, vb => vb.Handle), indexBuffer]);

            vao = -1;
        }
    }
}
