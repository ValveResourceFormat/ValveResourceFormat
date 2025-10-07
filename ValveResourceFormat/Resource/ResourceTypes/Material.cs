using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Serialization.VfxEval;

#nullable disable

namespace ValveResourceFormat.ResourceTypes
{
    /// <summary>
    /// Represents a material resource containing shader parameters and texture references.
    /// </summary>
    public class Material : KeyValuesOrNTRO
    {
        /// <summary>
        /// Gets or sets the material name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the shader name used by this material.
        /// </summary>
        public string ShaderName { get; set; } = string.Empty;

        /// <summary>
        /// Gets the integer shader parameters.
        /// </summary>
        public Dictionary<string, long> IntParams { get; } = [];

        /// <summary>
        /// Gets the floating-point shader parameters.
        /// </summary>
        public Dictionary<string, float> FloatParams { get; } = [];

        /// <summary>
        /// Gets the vector shader parameters.
        /// </summary>
        public Dictionary<string, Vector4> VectorParams { get; } = [];

        /// <summary>
        /// Gets the texture shader parameters.
        /// </summary>
        public Dictionary<string, string> TextureParams { get; } = [];

        /// <summary>
        /// Gets the integer material attributes.
        /// </summary>
        public Dictionary<string, long> IntAttributes { get; } = [];

        /// <summary>
        /// Gets the floating-point material attributes.
        /// </summary>
        public Dictionary<string, float> FloatAttributes { get; } = [];

        /// <summary>
        /// Gets the vector material attributes.
        /// </summary>
        public Dictionary<string, Vector4> VectorAttributes { get; } = [];

        /// <summary>
        /// Gets the string material attributes.
        /// </summary>
        public Dictionary<string, string> StringAttributes { get; } = [];

        /// <summary>
        /// Gets the evaluated dynamic expressions for dynamic scalar and texture parameters.
        /// </summary>
        public Dictionary<string, string> DynamicExpressions { get; } = [];

        private VsInputSignature? inputSignature;

        /// <summary>
        /// Gets the vertex shader input signature defining vertex attributes.
        /// </summary>
        public VsInputSignature InputSignature
        {
            get
            {
                if (!inputSignature.HasValue)
                {
                    var inputSignatureObject = GetInputSignatureObject();
                    inputSignature = inputSignatureObject != null ? new(inputSignatureObject) : VsInputSignature.Empty;
                }

                return inputSignature.Value;
            }
        }

        /// <inheritdoc/>
        public override void Read(BinaryReader reader)
        {
            base.Read(reader);

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

        /// <summary>
        /// Gets the shader arguments from integer parameters starting with "F_".
        /// </summary>
        /// <returns>Dictionary of shader argument names and values.</returns>
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

            return arguments;
        }

        private KVObject GetInputSignatureObject()
        {
            if (Resource is null)
            {
                return null;
            }

            if (Resource.ContainsBlockType(BlockType.INSG))
            {
                return ((BinaryKV3)Resource.GetBlockByType(BlockType.INSG)).Data;
            }

            // Material might not have REDI, or it might have RED2 without INSG
            if (Resource.EditInfo != null && Resource.EditInfo.Type != BlockType.REDI)
            {
                return null;
            }

            if (Resource.EditInfo.SearchableUserData.FirstOrDefault(x => x.Key == "VSInputSignature").Value is not string inputSignatureString)
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

        /// <summary>
        /// Represents the vertex shader input signature containing vertex attribute elements.
        /// </summary>
        public readonly struct VsInputSignature
        {
            /// <summary>
            /// An empty input signature with no elements.
            /// </summary>
            public static readonly VsInputSignature Empty = new();

            /// <summary>
            /// Gets the array of input signature elements.
            /// </summary>
            public InputSignatureElement[] Elements { get; }

            /// <summary>
            /// Initializes a new instance of the <see cref="VsInputSignature"/> struct with no elements.
            /// </summary>
            public VsInputSignature()
            {
                Elements = [];
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="VsInputSignature"/> struct from data.
            /// </summary>
            /// <param name="data">The key-value data containing element definitions.</param>
            public VsInputSignature(KVObject data)
            {
                Elements = [.. data.GetArray("m_elems").Select(x => new InputSignatureElement(x))];
            }
        }

        /// <summary>
        /// Represents a single element in the vertex shader input signature.
        /// </summary>
        [DebuggerDisplay("{Name,nq} ({Semantic,nq})")]
        public readonly struct InputSignatureElement
        {
            /// <summary>
            /// Gets the element name.
            /// </summary>
            public string Name { get; }

            /// <summary>
            /// Gets the semantic name.
            /// </summary>
            public string Semantic { get; }

            /// <summary>
            /// Gets the Direct3D semantic name.
            /// </summary>
            public string D3DSemanticName { get; }

            /// <summary>
            /// Gets the Direct3D semantic index.
            /// </summary>
            public int D3DSemanticIndex { get; }

            /// <summary>
            /// Initializes a new instance of the <see cref="InputSignatureElement"/> struct from data.
            /// </summary>
            /// <param name="data">The key-value data containing element definition.</param>
            public InputSignatureElement(KVObject data)
            {
                Name = data.GetProperty<string>("m_pName");
                Semantic = data.GetProperty<string>("m_pSemantic");
                D3DSemanticName = data.GetProperty<string>("m_pD3DSemanticName");
                D3DSemanticIndex = (int)data.GetIntegerProperty("m_nD3DSemanticIndex");
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="InputSignatureElement"/> struct with specified values.
            /// </summary>
            /// <param name="name">The element name.</param>
            /// <param name="semantic">The semantic name.</param>
            /// <param name="d3dSemanticName">The Direct3D semantic name.</param>
            /// <param name="d3dSemanticIndex">The Direct3D semantic index.</param>
            public InputSignatureElement(string name, string semantic, string d3dSemanticName, int d3dSemanticIndex)
            {
                Name = name;
                Semantic = semantic;
                D3DSemanticName = d3dSemanticName;
                D3DSemanticIndex = d3dSemanticIndex;
            }
        }

        /// <summary>
        /// Finds an input signature element by Direct3D semantic name and index.
        /// </summary>
        /// <param name="insg">The input signature to search.</param>
        /// <param name="d3dName">The Direct3D semantic name.</param>
        /// <param name="d3dIndex">The Direct3D semantic index.</param>
        /// <returns>The matching element, or default if not found.</returns>
        public static InputSignatureElement FindD3DInputSignatureElement(VsInputSignature insg, string d3dName, int d3dIndex)
        {
            foreach (var element in insg.Elements)
            {
                if (element.D3DSemanticName == d3dName && element.D3DSemanticIndex == d3dIndex)
                {
                    return element;
                }
            }

            return default;
        }
    }
}
