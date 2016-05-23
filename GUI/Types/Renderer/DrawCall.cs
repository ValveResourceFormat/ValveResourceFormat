using OpenTK;
using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer
{
    internal struct DrawCall
    {
        public PrimitiveType PrimitiveType;
        public int Shader;
        public uint BaseVertex;
        public uint VertexCount;
        public uint StartIndex;
        public uint IndexCount;
        public uint InstanceIndex;   //TODO
        public uint InstanceCount;   //TODO
        public float UvDensity;     //TODO
        public string Flags;        //TODO
        public Vector3 TintColor;   //TODO
        public Material Material;
        public uint VertexArrayObject;
        public DrawBuffer VertexBuffer;
        public DrawElementsType IndiceType;
        public DrawBuffer IndexBuffer;
    }

    internal struct DrawBuffer
    {
        public uint Id;
        public uint Offset;
    }
}
