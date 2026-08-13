using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer
{
    /// <summary>Gives the shader input names of a <see cref="VertexSlot"/>, and the buffer semantic
    /// that gets that slot.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class VertexAttributeNameAttribute(params string[] names) : Attribute
    {
        /// <summary>Gets the shader input names.</summary>
        public IReadOnlyList<string> Names { get; } = names;

        /// <summary>Gets the buffer semantic of this slot, if the slot has one.</summary>
        public string? Semantic { get; init; }

        /// <summary>Gets the index of the <see cref="Semantic"/>.</summary>
        public int SemanticIndex { get; init; }
    }

    /// <summary>
    /// Canonical vertex attribute locations, fixed per name so one VAO serves every shader that draws the
    /// geometry. <see cref="ShaderParser"/> stamps them onto the <c>in</c> declarations.
    /// </summary>
    public enum VertexSlot
    {
        // Depth only and picking read nothing above slot 3

        /// <summary>Vertex position.</summary>
        [VertexAttributeName("vPOSITION", Semantic = "POSITION")]
        Position = 0,

        /// <summary>Skinning bone indices.</summary>
        [VertexAttributeName("vBLENDINDICES", Semantic = "BLENDINDICES")]
        BlendIndices,

        /// <summary>Skinning bone weights.</summary>
        [VertexAttributeName("vBLENDWEIGHT", Semantic = "BLENDWEIGHT")]
        BlendWeight,

        /// <summary>Texture coordinates.</summary>
        [VertexAttributeName("vTEXCOORD", Semantic = "TEXCOORD")]
        TexCoord,

        /// <summary>Bone indices of the second skinning stream.</summary>
        [VertexAttributeName("vBLENDINDICES2", Semantic = "BLENDINDICES", SemanticIndex = 2)]
        BlendIndices2,

        /// <summary>Bone weights of the second skinning stream.</summary>
        [VertexAttributeName("vBLENDWEIGHT2", Semantic = "BLENDWEIGHT", SemanticIndex = 2)]
        BlendWeight2,

        /// <summary>Normal, or a compressed tangent frame.</summary>
        [VertexAttributeName("vNORMAL", Semantic = "NORMAL")]
        Normal,

        /// <summary>Tangent, when the normal does not pack it.</summary>
        [VertexAttributeName("vTANGENT", Semantic = "TANGENT")]
        Tangent,

        /// <summary>Vertex color.</summary>
        [VertexAttributeName("vCOLOR", Semantic = "COLOR")]
        Color,

        /// <summary>Secondary texture coordinates.</summary>
        [VertexAttributeName("vTEXCOORD1", Semantic = "TEXCOORD", SemanticIndex = 1)]
        TexCoord1,

        /// <summary>Layer blend weights.</summary>
        [VertexAttributeName("vTEXCOORD2", Semantic = "TEXCOORD", SemanticIndex = 2)]
        TexCoord2,

        /// <summary>Layer parameters, or foliage sway parameters.</summary>
        [VertexAttributeName("vTEXCOORD3", "vFoliageParams", Semantic = "TEXCOORD", SemanticIndex = 3)]
        TexCoord3,

        /// <summary>Layer blend color.</summary>
        [VertexAttributeName("vTEXCOORD4", Semantic = "TEXCOORD", SemanticIndex = 4)]
        TexCoord4,

        /// <summary>Layer blend alpha, or overlay projection direction.</summary>
        [VertexAttributeName("vTEXCOORD5", Semantic = "TEXCOORD", SemanticIndex = 5)]
        TexCoord5,

        /// <summary>Lightmap coordinates. Map compiled, so only the input signature names them.</summary>
        [VertexAttributeName("vLightmapUV", "vLightmapUVW")]
        LightmapUV,

        /// <summary>Baked per vertex lighting, the second color stream.</summary>
        [VertexAttributeName("vCOLOR1", "vPerVertexLighting", Semantic = "COLOR", SemanticIndex = 1)]
        Color1,

        /// <summary>Spelling some meshes use for the weight stream.</summary>
        [VertexAttributeName(Semantic = "BLENDWEIGHTS")]
        BlendWeightsAlias = BlendWeight,

    }

    /// <summary>
    /// Attributes of one renderer's own geometry. Each takes the slot of a mesh attribute that geometry
    /// cannot have, since the 16 guaranteed slots are already spoken for. Declaring both in one shader is a
    /// duplicate location error.
    /// </summary>
#pragma warning disable CA1069, CA1027 // Aliasing is the point: two renderers can take the same mesh slot
    public enum CustomVertexSlot
    {
        /// <summary>Bomb damage quad phase.</summary>
        [VertexAttributeName("vPHASE", Semantic = "PHASE")]
        Phase = VertexSlot.BlendIndices2,

        /// <summary>Text depth.</summary>
        [VertexAttributeName("vDEPTH")]
        TextDepth = VertexSlot.BlendIndices,

        /// <summary>Particle sheet frame blend.</summary>
        [VertexAttributeName("vFrameBlend")]
        FrameBlend = VertexSlot.BlendWeight,

        /// <summary>Particle sheet layer coordinates.</summary>
        [VertexAttributeName("vLayerUv0")]
        LayerUv0 = VertexSlot.BlendIndices,

        /// <summary>Particle sheet layer coordinates.</summary>
        [VertexAttributeName("vLayerUv1")]
        LayerUv1 = VertexSlot.BlendIndices2,

        /// <summary>Particle sheet layer coordinates.</summary>
        [VertexAttributeName("vLayerUv2")]
        LayerUv2 = VertexSlot.BlendWeight2,

        /// <summary>Particle sheet layer coordinates.</summary>
        [VertexAttributeName("vLayerUv3")]
        LayerUv3 = VertexSlot.Normal,
    }
#pragma warning restore CA1069, CA1027

    /// <summary>
    /// Resolves shader input names and buffer semantics to their <see cref="VertexSlot"/>.
    /// </summary>
    public static class VertexAttributeLocations
    {
        private static readonly FrozenDictionary<string, int> SlotByName = BuildSlotByName();
        private static readonly FrozenDictionary<(string Semantic, int Index), int> SlotBySemantic = BuildSlotBySemantic();

        private static readonly FrozenDictionary<int, (string Name, int Index)> SemanticBySlot = SlotBySemantic
            .GroupBy(entry => entry.Value, entry => entry.Key)
            .ToFrozenDictionary(group => group.Key, group => group.First());

        private static IEnumerable<(VertexAttributeNameAttribute Attribute, int Slot)> EnumerateSlots()
            => EnumerateSlots<VertexSlot>().Concat(EnumerateSlots<CustomVertexSlot>());

        private static IEnumerable<(VertexAttributeNameAttribute Attribute, int Slot)> EnumerateSlots<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] TSlot>() where TSlot : struct, Enum
        {
            foreach (var field in typeof(TSlot).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.GetCustomAttribute<VertexAttributeNameAttribute>() is { } attribute)
                {
                    yield return (attribute, (int)field.GetRawConstantValue()!);
                }
            }
        }

        private static FrozenDictionary<string, int> BuildSlotByName()
        {
            var slotByName = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var (attribute, slot) in EnumerateSlots())
            {
                foreach (var name in attribute.Names)
                {
                    // Add, not assign: two slots claiming one name is worth failing on
                    slotByName.Add(name, slot);
                }
            }

            return slotByName.ToFrozenDictionary(StringComparer.Ordinal);
        }

        private static FrozenDictionary<(string, int), int> BuildSlotBySemantic()
        {
            var slotBySemantic = new Dictionary<(string, int), int>();

            foreach (var (attribute, slot) in EnumerateSlots())
            {
                if (attribute.Semantic != null)
                {
                    slotBySemantic.Add((attribute.Semantic, attribute.SemanticIndex), slot);
                }
            }

            return slotBySemantic.ToFrozenDictionary();
        }

        /// <summary>Resolves a shader input name, or -1 if unknown.</summary>
        public static int Get(string attributeName) => SlotByName.GetValueOrDefault(attributeName, -1);

        /// <summary>Resolves a buffer semantic, or -1 if unknown. The fallback when no signature name does.</summary>
        public static int Get(string semanticName, int semanticIndex) => SlotBySemantic.GetValueOrDefault((semanticName, semanticIndex), -1);

        /// <summary>Resolves one buffer attribute, or -1 if unknown. The input signature name wins over the
        /// attribute's own semantic.</summary>
        public static int Resolve(Material.VsInputSignature inputSignature, VBIB.RenderInputLayoutField attribute, out string signatureName)
        {
            signatureName = inputSignature.Elements is { Length: > 0 }
                ? Material.FindD3DInputSignatureElement(inputSignature, attribute.SemanticName, attribute.SemanticIndex).Name ?? string.Empty
                : string.Empty;

            var location = signatureName.Length > 0 ? Get(signatureName) : -1;

            return location != -1 ? location : Get(attribute.SemanticName, attribute.SemanticIndex);
        }

        /// <summary>The buffer semantic a slot is filled from, for describing handbuilt geometry as a layout.</summary>
        public static (string Name, int Index) GetSemantic(VertexSlot slot)
            => SemanticBySlot.TryGetValue((int)slot, out var semantic)
                ? semantic
                : throw new ArgumentException($"'{slot}' has no buffer semantic.", nameof(slot));
    }
}
