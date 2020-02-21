using System;
using System.Collections.Generic;
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

            foreach (var kvp in data.GetArray("m_intParams"))
            {
                IntParams[kvp.GetProperty<string>("m_name")] = kvp.GetIntegerProperty("m_nValue");
            }

            foreach (var kvp in data.GetArray("m_floatParams"))
            {
                FloatParams[kvp.GetProperty<string>("m_name")] = kvp.GetFloatProperty("m_flValue");
            }

            foreach (var kvp in data.GetArray("m_vectorParams"))
            {
                VectorParams[kvp.GetProperty<string>("m_name")] = kvp.GetSubCollection("m_value").ToVector4();
            }

            foreach (var kvp in data.GetArray("m_textureParams"))
            {
                TextureParams[kvp.GetProperty<string>("m_name")] = kvp.GetProperty<string>("m_pValue");
            }

            // TODO: These 3 parameters
            //var textureAttributes = (NTROArray)Output["m_textureAttributes"];
            //var dynamicParams = (NTROArray)Output["m_dynamicParams"];
            //var dynamicTextureParams = (NTROArray)Output["m_dynamicTextureParams"];

            foreach (var kvp in data.GetArray("m_intAttributes"))
            {
                IntAttributes[kvp.GetProperty<string>("m_name")] = kvp.GetIntegerProperty("m_nValue");
            }

            foreach (var kvp in data.GetArray("m_floatAttributes"))
            {
                FloatAttributes[kvp.GetProperty<string>("m_name")] = kvp.GetFloatProperty("m_flValue");
            }

            foreach (var kvp in data.GetArray("m_vectorAttributes"))
            {
                VectorAttributes[kvp.GetProperty<string>("m_name")] = kvp.GetSubCollection("m_value").ToVector4();
            }

            foreach (var kvp in data.GetArray("m_stringAttributes"))
            {
                StringAttributes[kvp.GetProperty<string>("m_name")] = kvp.GetProperty<string>("m_pValue");
            }
        }

        public IKeyValueCollection GetData()
        {
            var data = resource.DataBlock;
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

        public IDictionary<string, bool> GetShaderArguments()
        {
            var data = GetData();

            var arguments = new Dictionary<string, bool>();

            foreach (var intParam in data.GetArray("m_intParams"))
            {
                var name = intParam.GetProperty<string>("m_name");
                var value = intParam.GetIntegerProperty("m_nValue");

                arguments.Add(name, value != 0);
            }

            return arguments;
        }
    }
}
