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
        public PrimitiveType PrimitiveType { get; set; }
        public int BaseVertex { get; set; }
        public uint VertexCount { get; set; }
        public nint StartIndex { get; set; } // pointer for GL call
        public int IndexCount { get; set; }
        //public float UvDensity { get; set; }     //TODO
        //public string Flags { get; set; }        //TODO
        public Vector4 TintColor { get; set; } = Vector4.One;

        public AABB? DrawBounds { get; set; }

        public int MeshId { get; set; }
        public int FirstMeshlet { get; set; }
        public int NumMeshlets { get; set; }
        public required RenderMaterial Material { get; set; }

        public required GPUMeshBufferCache MeshBuffers { get; init; }
        public string MeshName { get; set; } = string.Empty;
        public int VertexArrayObject { get; set; } = -1;

        public required VertexDrawBuffer[] VertexBuffers { get; init; }
        public DrawElementsType IndexType { get; set; }
        public IndexDrawBuffer IndexBuffer { get; set; }
        public int VertexIdOffset { get; set; }


        public void SetNewMaterial(RenderMaterial newMaterial)
        {
            VertexArrayObject = -1;
            Material = newMaterial;

            if (newMaterial.Shader.IsLoaded)
            {
                UpdateVertexArrayObject();
            }
        }

        public void UpdateVertexArrayObject()
        {
            Debug.Assert(Material.Shader.IsLoaded, "Shader must be loaded (more specifically the attribute locations) before creating a VAO");

            VertexArrayObject = MeshBuffers.GetVertexArrayObject(
                   MeshName,
                   VertexBuffers,
                   Material,
                   IndexBuffer.Handle);

#if DEBUG
            var vaoName = $"{MeshName}+{Material.Material.Name}";
            GL.ObjectLabel(ObjectLabelIdentifier.VertexArray, VertexArrayObject, Math.Min(GLEnvironment.MaxLabelLength, vaoName.Length), vaoName);
#endif
        }

        public void DeleteVertexArrayObject()
        {
            GL.DeleteVertexArray(VertexArrayObject);
        }
    }

    /// <summary>
    /// Index buffer binding for draw calls.
    /// </summary>
    public readonly struct IndexDrawBuffer
    {
        public int Handle { get; init; }
        public uint Offset { get; init; }
    }

    /// <summary>
    /// Vertex buffer binding with stride and attribute layout.
    /// </summary>
    public readonly struct VertexDrawBuffer
    {
        public int Handle { get; init; }
        public uint Offset { get; init; }
        public uint ElementSizeInBytes { get; init; }
        public VBIB.RenderInputLayoutField[] InputLayoutFields { get; init; }
    }
}
