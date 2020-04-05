using OpenTK;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Serialization;

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

        public static bool IsCompressedNormalTangent(IKeyValueCollection drawCall)
        {
            if (drawCall.ContainsKey("m_bUseCompressedNormalTangent"))
            {
                return drawCall.GetProperty<bool>("m_bUseCompressedNormalTangent");
            }

            if (drawCall.ContainsKey("m_nFlags"))
            {
                var flags = drawCall.GetProperty<object>("m_nFlags");

                switch (flags)
                {
                    case string flagsString:
                        return flagsString.Contains("MESH_DRAW_FLAGS_USE_COMPRESSED_NORMAL_TANGENT");
                    case long flagsLong:
                        // TODO: enum
                        return (flagsLong & 2) == 2;
                }
            }

            return false;
        }
    }

    internal struct DrawBuffer
    {
        public uint Id;
        public uint Offset;
    }
}
