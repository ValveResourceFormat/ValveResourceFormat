using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using ValveKeyValue;
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

        public string ToValveMaterial()
        {
            var root = new KVObject("Layer0", new List<KVObject>());

            root.Add(new KVObject("shader", ShaderName));

            foreach (var (key, value) in IntParams)
            {
                root.Add(new KVObject(key, value));
            }

            foreach (var (key, value) in FloatParams)
            {
                root.Add(new KVObject(key, value));
            }

            foreach (var (key, value) in VectorParams)
            {
                root.Add(new KVObject(key, $"[{value.X:N6} {value.Y:N6} {value.Z:N6} {value.W:N6}]"));
            }

            foreach (var (key, value) in TextureParams)
            {
                root.Add(new KVObject(key, value));
            }

            if (DynamicExpressions.Count > 0)
            {
                var dynamicExpressionsNode = new KVObject("DynamicParams", new List<KVObject>());
                root.Add(dynamicExpressionsNode);
                foreach (var (key, value) in DynamicExpressions)
                {
                    dynamicExpressionsNode.Add(new KVObject(key, value));
                }
            }

            var attributes = new List<KVObject>();

            foreach (var (key, value) in IntAttributes)
            {
                // not defined by user, so skip it
                if (key.Equals("representativetexturewidth", StringComparison.OrdinalIgnoreCase)
                || key.Equals("representativetextureheight", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                attributes.Add(new KVObject(key, value));
            }

            foreach (var (key, value) in FloatAttributes)
            {
                // Skip `int` definition if there is a `float` definition
                attributes = attributes.Where(existing_key => existing_key.Name != key).ToList();
                attributes.Add(new KVObject(key, value));
            }

            foreach (var (key, value) in VectorAttributes)
            {
                attributes.Add(new KVObject(key, $"[{value.X:N6} {value.Y:N6} {value.Z:N6} {value.W:N6}]"));
            }

            foreach (var (key, value) in StringAttributes)
            {
                attributes.Add(new KVObject(key, value ?? string.Empty));
            }

            var toSystem = new HashSet<string>
            {
                "physicssurfaceproperties",
                "worldmappingwidth",
                "worldmappingheight"
            };

            if (attributes.Any())
            {
                // Some attributes are actually SystemAttributes
                var systemattributes = new List<KVObject>();
                var isSystemAttribute = attributes.ToLookup(attribute => toSystem.Contains(attribute.Name.ToLower()));

                if (isSystemAttribute[false].Any())
                {
                    root.Add(new KVObject("Attributes", isSystemAttribute[false]));
                }

                if (isSystemAttribute[true].Any())
                {
                    root.Add(new KVObject("SystemAttributes", isSystemAttribute[true]));
                }
            }

            var extraStringData = (ExtraStringData)Resource.EditInfo.Structs[ResourceEditInfo.REDIStruct.ExtraStringData];
            var subrect = extraStringData.List.Where(x => x.Name.ToLower() == "subrectdefinition").FirstOrDefault();

            if (subrect != null)
            {
                var toolattributes = new List<KVObject>()
                {
                    new KVObject("SubrectDefinition", subrect.Value)
                };

                root.Add(new KVObject("ToolAttributes", toolattributes));
            }

            using var ms = new MemoryStream();
            KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Serialize(ms, root);
            return Encoding.UTF8.GetString(ms.ToArray());
        }
    }
}
