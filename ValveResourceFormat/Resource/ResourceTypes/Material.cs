using System.Collections.Generic;
using System.IO;
using System.Numerics;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.NTRO;

namespace ValveResourceFormat.ResourceTypes
{
    public class Material : NTRO
    {
        public string Name { get; set; }
        public string ShaderName { get; set; }

        public Dictionary<string, int> IntParams { get; } = new Dictionary<string, int>();
        public Dictionary<string, float> FloatParams { get; } = new Dictionary<string, float>();
        public Dictionary<string, Vector4> VectorParams { get; } = new Dictionary<string, Vector4>();
        public Dictionary<string, string> TextureParams { get; } = new Dictionary<string, string>();
        public Dictionary<string, int> IntAttributes { get; } = new Dictionary<string, int>();
        public Dictionary<string, float> FloatAttributes { get; } = new Dictionary<string, float>();
        public Dictionary<string, Vector4> VectorAttributes { get; } = new Dictionary<string, Vector4>();
        public Dictionary<string, string> StringAttributes { get; } = new Dictionary<string, string>();

        public override void Read(BinaryReader reader, Resource resource)
        {
            base.Read(reader, resource);

            Name = ((NTROValue<string>)Output["m_materialName"]).Value;
            ShaderName = ((NTROValue<string>)Output["m_shaderName"]).Value;

            // TODO: Is this a string array?
            //RenderAttributesUsed = ((ValveResourceFormat.ResourceTypes.NTROSerialization.NTROValue<string>)Output["m_renderAttributesUsed"]).Value;

            var intParams = (NTROArray)Output["m_intParams"];
            foreach (var t in intParams)
            {
                var subStruct = ((NTROValue<NTROStruct>)t).Value;
                IntParams[((NTROValue<string>)subStruct["m_name"]).Value] = ((NTROValue<int>)subStruct["m_nValue"]).Value;
            }

            var floatParams = (NTROArray)Output["m_floatParams"];
            foreach (var t in floatParams)
            {
                var subStruct = ((NTROValue<NTROStruct>)t).Value;
                FloatParams[((NTROValue<string>)subStruct["m_name"]).Value] = ((NTROValue<float>)subStruct["m_flValue"]).Value;
            }

            var vectorParams = (NTROArray)Output["m_vectorParams"];
            foreach (var t in vectorParams)
            {
                var subStruct = ((NTROValue<NTROStruct>)t).Value;
                VectorParams[subStruct.GetProperty<string>("m_name")] = subStruct.GetSubCollection("m_value").ToVector4();
            }

            var textureParams = (NTROArray)Output["m_textureParams"];
            foreach (var t in textureParams)
            {
                var subStruct = ((NTROValue<NTROStruct>)t).Value;
                TextureParams[subStruct.GetProperty<string>("m_name")] = subStruct.GetProperty<string>("m_pValue");
            }

            // TODO: These 3 parameters
            var textureAttributes = (NTROArray)Output["m_textureAttributes"];
            var dynamicParams = (NTROArray)Output["m_dynamicParams"];
            var dynamicTextureParams = (NTROArray)Output["m_dynamicTextureParams"];

            var intAttributes = (NTROArray)Output["m_intAttributes"];
            foreach (var t in intAttributes)
            {
                var subStruct = ((NTROValue<NTROStruct>)t).Value;
                IntAttributes[((NTROValue<string>)subStruct["m_name"]).Value] = ((NTROValue<int>)subStruct["m_nValue"]).Value;
            }

            var floatAttributes = (NTROArray)Output["m_floatAttributes"];
            foreach (var t in floatAttributes)
            {
                var subStruct = ((NTROValue<NTROStruct>)t).Value;
                FloatAttributes[((NTROValue<string>)subStruct["m_name"]).Value] = ((NTROValue<float>)subStruct["m_flValue"]).Value;
            }

            var vectorAttributes = (NTROArray)Output["m_vectorAttributes"];
            foreach (var t in vectorAttributes)
            {
                var subStruct = ((NTROValue<NTROStruct>)t).Value;
                VectorAttributes[((NTROValue<string>)subStruct["m_name"]).Value] = ((NTROValue<Vector4>)subStruct["m_value"]).Value;
            }

            var stringAttributes = (NTROArray)Output["m_stringAttributes"];
            foreach (var t in stringAttributes)
            {
                var subStruct = ((NTROValue<NTROStruct>)t).Value;
                StringAttributes[((NTROValue<string>)subStruct["m_name"]).Value] = ((NTROValue<string>)subStruct["m_value"]).Value;
            }
        }
    }
}
