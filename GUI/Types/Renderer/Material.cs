using System.Collections.Generic;
using OpenTK;
using ValveResourceFormat.Blocks;

namespace GUI.Types.Renderer
{
    internal class Material
    {
        public string Name { get; set; }
        public string ShaderName { get; set; }

        public Dictionary<string, int> IntParams { get; }
        public Dictionary<string, float> FloatParams { get; }
        public Dictionary<string, Vector4> VectorParams { get; }
        public Dictionary<string, int> Textures { get; }
        public Dictionary<string, ResourceExtRefList.ResourceReferenceInfo> TextureParams { get; }
        //public Dictionary<string, ????> dynamicParams;
        //public Dictionary<string, ????> dynamicTextureParams;
        public Dictionary<string, int> IntAttributes { get; }
        public Dictionary<string, float> FloatAttributes { get; }
        public Dictionary<string, Vector4> VectorAttributes { get; }
        //public Dictionary<string, long> textureAttributes;
        public Dictionary<string, string> StringAttributes { get; }
        //public string[] renderAttributesUsed; // ?

        public Material()
        {
            IntParams = new Dictionary<string, int>();
            FloatParams = new Dictionary<string, float>();
            VectorParams = new Dictionary<string, Vector4>();
            TextureParams = new Dictionary<string, ResourceExtRefList.ResourceReferenceInfo>();

            IntAttributes = new Dictionary<string, int>();
            FloatAttributes = new Dictionary<string, float>();
            VectorAttributes = new Dictionary<string, Vector4>();
            StringAttributes = new Dictionary<string, string>();

            Textures = new Dictionary<string, int>();
        }
    }
}
