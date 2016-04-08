using System.Collections.Generic;
using OpenTK;
using ValveResourceFormat.Blocks;

namespace GUI.Types.Renderer
{
    internal class Material
    {
        public string Name;
        public string ShaderName;
        public Dictionary<string, int> TextureIDs;
        public Dictionary<string, int> IntParams;
        public Dictionary<string, float> FloatParams;
        public Dictionary<string, Vector4> VectorParams;
        public Dictionary<string, ResourceExtRefList.ResourceReferenceInfo> TextureParams;
        //public Dictionary<string, ????> dynamicParams;
        //public Dictionary<string, ????> dynamicTextureParams;
        public Dictionary<string, int> IntAttributes;
        public Dictionary<string, float> FloatAttributes;
        public Dictionary<string, Vector4> VectorAttributes;
        //public Dictionary<string, long> textureAttributes;
        public Dictionary<string, string> StringAttributes;
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

            TextureIDs = new Dictionary<string, int>();
        }
    }
}
