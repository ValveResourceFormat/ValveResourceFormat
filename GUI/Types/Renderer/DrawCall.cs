using OpenTK;
using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer
{
    class DrawCall
    {
        public PrimitiveType PrimitiveType { get; set; }
        public uint BaseVertex { get; set; }
        //public uint VertexCount { get; set; }
        public uint StartIndex { get; set; }
        public int IndexCount { get; set; }
        //public float UvDensity { get; set; }     //TODO
        //public string Flags { get; set; }        //TODO
        public Vector3 TintColor { get; set; } = Vector3.One;

        public AABB? DrawBounds { get; set; }

        public int MeshId { get; set; }
        public int FirstMeshlet { get; set; }
        public int NumMeshlets { get; set; }
        public RenderMaterial Material { get; set; }
        public uint VertexArrayObject { get; set; }
        public DrawBuffer VertexBuffer { get; set; }
        public DrawElementsType IndexType { get; set; }
        public DrawBuffer IndexBuffer { get; set; }
    }

    internal struct DrawBuffer
    {
        public uint Id;
        public uint Offset;
    }
}
