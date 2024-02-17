using System.IO;
using System.Linq;
using System.Text;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Blocks.ResourceEditInfoStructs;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Serialization.VfxEval;

namespace ValveResourceFormat.ResourceTypes
{
    public class Material : KeyValuesOrNTRO
    {
        public string Name { get; set; } = string.Empty;
        public string ShaderName { get; set; } = string.Empty;

        public Dictionary<string, long> IntParams { get; } = [];
        public Dictionary<string, float> FloatParams { get; } = [];
        public Dictionary<string, Vector4> VectorParams { get; } = [];
        public Dictionary<string, string> TextureParams { get; } = [];
        public Dictionary<string, long> IntAttributes { get; } = [];
        public Dictionary<string, float> FloatAttributes { get; } = [];
        public Dictionary<string, Vector4> VectorAttributes { get; } = [];
        public Dictionary<string, string> StringAttributes { get; } = [];
        public Dictionary<string, string> DynamicExpressions { get; } = [];


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

        public Dictionary<string, byte> GetShaderArguments()
        {
            var arguments = new Dictionary<string, byte>();

            foreach (var (name, value) in IntParams)
            {
                if (name.StartsWith("F_", StringComparison.OrdinalIgnoreCase))
                {
                    arguments.Add(name, (byte)value);
                }
            }

            if (Resource?.EditInfo != null)
            {
                var specialDeps = (SpecialDependencies)Resource.EditInfo.Structs[ResourceEditInfo.REDIStruct.SpecialDependencies];
                var hemiOctIsoRoughness_RG_B = specialDeps.List.Any(dependancy => dependancy.CompilerIdentifier == "CompileTexture" && dependancy.String == "Texture Compiler Version Mip HemiOctIsoRoughness_RG_B");
                if (hemiOctIsoRoughness_RG_B)
                {
                    arguments.Add("HemiOctIsoRoughness_RG_B", 1);
                }
            }

            return arguments;
        }

        public KVObject GetInputSignature()
        {
            if (Resource.ContainsBlockType(BlockType.INSG))
            {
                return ((BinaryKV3)Resource.GetBlockByType(BlockType.INSG)).Data;
            }

            // Material might not have REDI, or it might have RED2 without INSG
            if (Resource.EditInfo != null && Resource.EditInfo.Type != BlockType.REDI)
            {
                return null;
            }

            var extraStringData = (ExtraStringData)Resource.EditInfo.Structs[ResourceEditInfo.REDIStruct.ExtraStringData];
            var inputSignatureString = extraStringData.List.Where(x => x.Name == "VSInputSignature").FirstOrDefault()?.Value;

            if (inputSignatureString == null)
            {
                return null;
            }

            if (!inputSignatureString.StartsWith("<!-- kv3", StringComparison.InvariantCulture))
            {
                return null;
            }

            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(inputSignatureString));

            return KeyValues3.ParseKVFile(ms).Root;
        }

        public readonly struct InputSignatureElement
        {
            public string Name { get; }
            public string Semantic { get; }
            public string D3DSemanticName { get; }
            public int D3DSemanticIndex { get; }

            public InputSignatureElement(KVObject data)
            {
                Name = data.GetProperty<string>("m_pName");
                Semantic = data.GetProperty<string>("m_pSemantic");
                D3DSemanticName = data.GetProperty<string>("m_pD3DSemanticName");
                D3DSemanticIndex = (int)data.GetIntegerProperty("m_nD3DSemanticIndex");
            }
        }

        public static InputSignatureElement FindD3DInputSignatureElement(KVObject insg, string d3dName, int d3dIndex)
        {
            foreach (var elemData in insg.GetArray<KVObject>("m_elems"))
            {
                var elem = new InputSignatureElement(elemData);
                if (elem.D3DSemanticName == d3dName && elem.D3DSemanticIndex == d3dIndex)
                {
                    return elem;
                }
            }

            return default;
        }
    }
}
