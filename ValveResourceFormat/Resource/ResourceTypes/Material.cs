using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ValveResourceFormat.Serialization;

namespace ValveResourceFormat.ResourceTypes
{
    public class Material
    {
        public string Name { get; set; }
        public string ShaderName { get; set; }

        public Dictionary<string, long> IntParams { get; } = new Dictionary<string, long>();
        public Dictionary<string, float> FloatParams { get; } = new Dictionary<string, float>();
        public Dictionary<string, Vector4> VectorParams { get; } = new Dictionary<string, Vector4>();
        public Dictionary<string, string> TextureParams { get; } = new Dictionary<string, string>();
        public Dictionary<string, long> IntAttributes { get; } = new Dictionary<string, long>();
        public Dictionary<string, float> FloatAttributes { get; } = new Dictionary<string, float>();
        public Dictionary<string, Vector4> VectorAttributes { get; } = new Dictionary<string, Vector4>();
        public Dictionary<string, string> StringAttributes { get; } = new Dictionary<string, string>();

        private readonly Resource resource;

        public Material()
        {
        }

        public Material(Resource resource)
        {
            this.resource = resource;

            var data = GetData();

            Name = data.GetProperty<string>("m_materialName");
            ShaderName = data.GetProperty<string>("m_shaderName");

            // TODO: Is this a string array?
            //RenderAttributesUsed = ((ValveResourceFormat.ResourceTypes.NTROSerialization.NTROValue<string>)Output["m_renderAttributesUsed"]).Value;

            IntParams = data.GetArray("m_intParams").ToDictionary(
                kvp => kvp.GetProperty<string>("m_name"),
                kvp => kvp.GetIntegerProperty("m_nValue"));

            FloatParams = data.GetArray("m_floatParams").ToDictionary(
                kvp => kvp.GetProperty<string>("m_name"),
                kvp => kvp.GetFloatProperty("m_flValue"));

            VectorParams = data.GetArray("m_vectorParams").ToDictionary(
                kvp => kvp.GetProperty<string>("m_name"),
                kvp => kvp.GetSubCollection("m_value").ToVector4());

            TextureParams = data.GetArray("m_textureParams").ToDictionary(
                kvp => kvp.GetProperty<string>("m_name"),
                kvp => kvp.GetProperty<string>("m_pValue"));

            // TODO: These 3 parameters
            //var textureAttributes = (NTROArray)Output["m_textureAttributes"];
            //var dynamicParams = (NTROArray)Output["m_dynamicParams"];
            //var dynamicTextureParams = (NTROArray)Output["m_dynamicTextureParams"];

            IntAttributes = data.GetArray("m_intAttributes").ToDictionary(
                kvp => kvp.GetProperty<string>("m_name"),
                kvp => kvp.GetIntegerProperty("m_nValue"));

            FloatAttributes = data.GetArray("m_floatAttributes").ToDictionary(
                kvp => kvp.GetProperty<string>("m_name"),
                kvp => kvp.GetFloatProperty("m_flValue"));

            VectorAttributes = data.GetArray("m_vectorAttributes").ToDictionary(
                kvp => kvp.GetProperty<string>("m_name"),
                kvp => kvp.GetSubCollection("m_value").ToVector4());

            StringAttributes = data.GetArray("m_stringAttributes").ToDictionary(
                kvp => kvp.GetProperty<string>("m_name"),
                kvp => kvp.GetProperty<string>("m_pValue"));
        }

        public IKeyValueCollection GetData()
        {
            var data = resource.Blocks[BlockType.DATA];
            if (data is NTRO ntro)
            {
                return ntro.Output;
            }
            else if (data is BinaryKV3 kv)
            {
                return kv.Data;
            }

            throw new InvalidOperationException($"Unknown material data type {data.GetType().Name}");
        }
    }
}
