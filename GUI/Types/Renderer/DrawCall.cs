using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Blocks;

namespace GUI.Types.Renderer
{
    class DrawCall
    {
        public PrimitiveType PrimitiveType { get; set; }
        public int BaseVertex { get; set; }
        //public uint VertexCount { get; set; }
        public nint StartIndex { get; set; } // pointer for GL call
        public int IndexCount { get; set; }
        //public float UvDensity { get; set; }     //TODO
        //public string Flags { get; set; }        //TODO
        public Vector4 TintColor { get; set; } = Vector4.One;

        public AABB? DrawBounds { get; set; }

        public int MeshId { get; set; }
        public int FirstMeshlet { get; set; }
        public int NumMeshlets { get; set; }
        public RenderMaterial Material { get; set; }
        public int VertexArrayObject { get; set; }
        public VertexDrawBuffer[] VertexBuffers { get; set; }
        public DrawElementsType IndexType { get; set; }
        public IndexDrawBuffer IndexBuffer { get; set; }
        public int VertexIdOffset { get; set; }
    }

    internal struct IndexDrawBuffer
    {
        public uint Id;
        public uint Offset;
    }

    internal struct VertexDrawBuffer
    {
        public uint Id;
        public uint Offset;
        public uint ElementSizeInBytes;
        public VBIB.RenderInputLayoutField[] InputLayoutFields;
    }
}
