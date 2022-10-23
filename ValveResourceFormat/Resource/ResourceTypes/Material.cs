using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Blocks.ResourceEditInfoStructs;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.VfxEval;

namespace ValveResourceFormat.ResourceTypes
{
    public class Material : KeyValuesOrNTRO
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
        public Dictionary<string, string> DynamicExpressions { get; } = new Dictionary<string, string>();


        public override void Read(BinaryReader reader, Resource resource)
        {
            base.Read(reader, resource);

            Name = Data.GetProperty<string>("m_materialName");
            ShaderName = Data.GetProperty<string>("m_shaderName");

            foreach (var kvp in Data.GetArray("m_intParams"))
            {
                IntParams[kvp.GetProperty<string>("m_name")] = kvp.GetIntegerProperty("m_nValue");
            }

            foreach (var kvp in Data.GetArray("m_floatParams"))
            {
                FloatParams[kvp.GetProperty<string>("m_name")] = kvp.GetFloatProperty("m_flValue");
            }

            foreach (var kvp in Data.GetArray("m_vectorParams"))
            {
                VectorParams[kvp.GetProperty<string>("m_name")] = kvp.GetSubCollection("m_value").ToVector4();
            }

            foreach (var kvp in Data.GetArray("m_textureParams"))
            {
                TextureParams[kvp.GetProperty<string>("m_name")] = kvp.GetProperty<string>("m_pValue");
            }

            foreach (var kvp in Data.GetArray("m_intAttributes"))
            {
                IntAttributes[kvp.GetProperty<string>("m_name")] = kvp.GetIntegerProperty("m_nValue");
            }

            foreach (var kvp in Data.GetArray("m_floatAttributes"))
            {
                FloatAttributes[kvp.GetProperty<string>("m_name")] = kvp.GetFloatProperty("m_flValue");
            }

            foreach (var kvp in Data.GetArray("m_vectorAttributes"))
            {
                VectorAttributes[kvp.GetProperty<string>("m_name")] = kvp.GetSubCollection("m_value").ToVector4();
            }

            foreach (var kvp in Data.GetArray("m_stringAttributes"))
            {
                StringAttributes[kvp.GetProperty<string>("m_name")] = kvp.GetProperty<string>("m_value");
            }

            // This is zero-length for all vmat files in Dota2 and HL archives
            var textureAttributes = Data.GetArray<string>("m_textureAttributes");
            if (textureAttributes.Length > 0)
            {
                Console.WriteLine("unexpected textureAttributes length");
            }

            var renderAttributesUsed = Data.GetArray<string>("m_renderAttributesUsed");

            foreach (var kvp in Data.GetArray("m_dynamicParams"))
            {
                var dynamicParamName = kvp.GetProperty<string>("m_name");
                var dynamicParamBytes = kvp.GetProperty<byte[]>("m_value");
                var vfxEval = new VfxEval(dynamicParamBytes, renderAttributesUsed);
                DynamicExpressions.Add(dynamicParamName, vfxEval.DynamicExpressionResult.Replace("\n", "\\n", StringComparison.Ordinal));
            }

            foreach (var kvp in Data.GetArray("m_dynamicTextureParams"))
            {
                var dynamicTextureParamName = kvp.GetProperty<string>("m_name");
                var dynamicTextureParamBytes = kvp.GetProperty<byte[]>("m_value");
                var vfxEval = new VfxEval(dynamicTextureParamBytes, renderAttributesUsed);
                DynamicExpressions.Add(dynamicTextureParamName, vfxEval.DynamicExpressionResult.Replace("\n", "\\n", StringComparison.Ordinal));
            }
        }

        public IDictionary<string, bool> GetShaderArguments()
        {
            var arguments = new Dictionary<string, bool>();

            if (Data == null)
            {
                return arguments;
            }

            foreach (var intParam in Data.GetArray("m_intParams"))
            {
                var name = intParam.GetProperty<string>("m_name");
                var value = intParam.GetIntegerProperty("m_nValue");

                arguments.Add(name, value != 0);
            }

            var specialDeps = (SpecialDependencies)Resource.EditInfo.Structs[ResourceEditInfo.REDIStruct.SpecialDependencies];
            bool hemiOctIsoRoughness_RG_B = specialDeps.List.Any(dependancy => dependancy.CompilerIdentifier == "CompileTexture" && dependancy.String == "Texture Compiler Version Mip HemiOctIsoRoughness_RG_B");
            bool invert = specialDeps.List.Any(dependancy => dependancy.CompilerIdentifier == "CompileTexture" && dependancy.String == "Texture Compiler Version LegacySource1InvertNormals");
            if (hemiOctIsoRoughness_RG_B)
            {
                arguments.Add("HemiOctIsoRoughness_RG_B", true);
            }

            if (invert)
            {
                arguments.Add("LegacySource1InvertNormals", true);
            }

            return arguments;
        }
    }
}
