using System.Diagnostics;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Blocks;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Single GPU draw operation with geometry, material, and render state.
    /// </summary>
    public class DrawCall
    {
        /// <summary>Gets or sets the OpenGL primitive type for this draw call.</summary>
        public PrimitiveType PrimitiveType { get; set; }

        /// <summary>Gets or sets the base vertex offset applied to all indices.</summary>
        public int BaseVertex { get; set; }

        /// <summary>Gets or sets the number of vertices in the draw call.</summary>
        public uint VertexCount { get; set; }

        /// <summary>Gets or sets the byte offset into the index buffer where drawing begins.</summary>
        public nint StartIndex { get; set; } // pointer for GL call

        /// <summary>Gets or sets the number of indices to draw.</summary>
        public int IndexCount { get; set; }

        //public float UvDensity { get; set; }     //TODO
        //public string Flags { get; set; }        //TODO

        /// <summary>Gets or sets the per-draw-call tint color multiplier.</summary>
        public Vector4 TintColor { get; set; } = Vector4.One;

        /// <summary>Gets or sets the optional bounding box used for draw-level culling.</summary>
        public AABB? DrawBounds { get; set; }

        /// <summary>Gets or sets the mesh identifier used for picking.</summary>
        public int MeshId { get; set; }

        /// <summary>Gets or sets the index of the first meshlet for this draw call.</summary>
        public int FirstMeshlet { get; set; }

        /// <summary>Gets or sets the number of meshlets in this draw call.</summary>
        public int NumMeshlets { get; set; }

        /// <summary>Gets or sets the render material applied to this draw call.</summary>
        public required RenderMaterial Material { get; set; }

        /// <summary>Gets the GPU mesh buffer cache that owns the buffers for this draw call.</summary>
        public required GPUMeshBufferCache MeshBuffers { get; init; }

        /// <summary>Gets or sets the name of the mesh this draw call belongs to.</summary>
        public string MeshName { get; set; } = string.Empty;

        /// <summary>Vertex array state for this draw call's geometry. Created lazily; owned by <see cref="MeshBuffers"/>.</summary>
        private RenderVao? vao;

        /// <summary>Gets the vertex buffer bindings used by this draw call.</summary>
        public required VertexDrawBuffer[] VertexBuffers { get; init; }

        /// <summary>Gets or sets the data type of each element in the index buffer.</summary>
        public DrawElementsType IndexType { get; set; }

        /// <summary>Gets or sets the index buffer binding for this draw call.</summary>
        public IndexDrawBuffer IndexBuffer { get; set; }

        /// <summary>Gets or sets the vertex ID offset used for morph target lookup.</summary>
        public int VertexIdOffset { get; set; }

        /// <summary>Gets the size in bytes of a single index element.</summary>
        public int IndexSizeInBytes => IndexType switch
        {
            DrawElementsType.UnsignedByte => 1,
            DrawElementsType.UnsignedShort => 2,
            DrawElementsType.UnsignedInt => 4,
            _ => throw new UnreachableException(nameof(IndexType))
        };


        /// <summary>Replaces the material and rebuilds the vertex array state.</summary>
        /// <param name="newMaterial">The new material to assign.</param>
        public void SetNewMaterial(RenderMaterial newMaterial)
        {
            Material = newMaterial;
            UpdateVertexArrayObject();
        }

        /// <summary>Returns the VAO matching the shader this draw call is about to be rendered with,
        /// creating it if necessary. Replacement shaders (depth only, outline, picking) get their own
        /// VAOs since their attribute locations differ from the material shader's.</summary>
        /// <param name="shader">The shader the draw call will be rendered with.</param>
        /// <returns>The OpenGL VAO handle.</returns>
        public int GetVertexArrayObject(Shader shader)
        {
            vao ??= new RenderVao(MeshBuffers, VertexBuffers, IndexBuffer.Handle, Material.Material.InputSignature, MeshName);
            return vao.Get(shader);
        }

        /// <summary>Resets the vertex array state and recreates the material shader VAO if the shader is ready.</summary>
        public void UpdateVertexArrayObject()
        {
            vao = null;

            if (Material.Shader.IsLoaded)
            {
                GetVertexArrayObject(Material.Shader);
            }
        }
    }

    /// <summary>
    /// Index buffer binding for draw calls.
    /// </summary>
    public readonly struct IndexDrawBuffer
    {
        /// <summary>Gets the OpenGL buffer object handle.</summary>
        public int Handle { get; init; }

        /// <summary>Gets the byte offset within the buffer.</summary>
        public uint Offset { get; init; }
    }

    /// <summary>
    /// Vertex buffer binding with stride and attribute layout.
    /// </summary>
    public readonly struct VertexDrawBuffer
    {
        /// <summary>Gets the OpenGL buffer object handle.</summary>
        public int Handle { get; init; }

        /// <summary>Gets the index of this buffer within the mesh's vertex buffer list (0 for single-buffer geometry).</summary>
        public int BufferIndex { get; init; }

        /// <summary>Gets the byte offset within the buffer.</summary>
        public uint Offset { get; init; }

        /// <summary>Gets the size in bytes of a single vertex element.</summary>
        public uint ElementSizeInBytes { get; init; }

        /// <summary>Gets the input layout fields describing the vertex attribute layout.</summary>
        public VBIB.RenderInputLayoutField[] InputLayoutFields { get; init; }
    }
}
