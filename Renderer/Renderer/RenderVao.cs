using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Vertex array state for geometry that is unique per pipeline.
    /// Bind sites probe this with the shader about to be used. The VAOs themselves are created and owned by
    /// <see cref="GPUMeshBufferCache"/>, keyed by the shader plus the actual GPU buffer handles involved; this
    /// only memoizes the most recent lookups so repeat draws with the same shader skip the dictionary lookup.
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

        private int primaryProgram = -1;
        private int primaryVao = -1;
        private int replacementProgram = -1;
        private int replacementVao = -1;

        /// <summary>Returns the VAO matching the given shader, creating it through the cache on first use.</summary>
        /// <param name="shader">The shader the geometry is about to be rendered with.</param>
        /// <returns>The OpenGL VAO handle.</returns>
        public int Get(Shader shader)
        {
            if (shader.Program == primaryProgram)
            {
                return primaryVao;
            }

            if (shader.Program == replacementProgram)
            {
                return replacementVao;
            }

            shader.EnsureLoaded();
            var vao = meshBuffers.GetVertexArrayObject(vertexBuffers, shader, inputSignature, indexBuffer, debugLabel);

            if (primaryProgram == -1)
            {
                primaryProgram = shader.Program;
                primaryVao = vao;
            }
            else
            {
                replacementProgram = shader.Program;
                replacementVao = vao;
            }

            return vao;
        }

        /// <summary>Deletes the cached VAOs built from this state's buffers. Call before deleting a buffer
        /// that is not tracked by <see cref="GPUMeshBufferCache"/>, so no VAO is left referencing it.</summary>
        public void Delete()
        {
            meshBuffers.InvalidateVertexArrayObjectsForFreedBuffers([.. Array.ConvertAll(vertexBuffers, vb => vb.Handle), indexBuffer]);

            primaryProgram = -1;
            primaryVao = -1;
            replacementProgram = -1;
            replacementVao = -1;
        }
    }
}
