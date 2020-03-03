using OpenTK;
using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer
{
    internal class DrawCall
    {
        public PrimitiveType PrimitiveType { get; set; }
        public Shader Shader { get; set; }
        //public uint BaseVertex { get; set; }
        //public uint VertexCount { get; set; }
        public uint StartIndex { get; set; }
        public int IndexCount { get; set; }
        //public uint InstanceIndex { get; set; }   //TODO
        //public uint InstanceCount { get; set; }   //TODO
        //public float UvDensity { get; set; }     //TODO
        //public string Flags { get; set; }        //TODO
        public Vector3 TintColor { get; set; } = Vector3.One;
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
