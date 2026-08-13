using System.Collections.Frozen;
using System.Linq;
using System.Reflection;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer
{
    /// <summary>Names the shader inputs bound to a <see cref="VertexAttributeSlot"/>, and the buffer
    /// semantics that resolve to it.</summary>
    /// <param name="names">Shader input names, e.g. <c>vTEXCOORD1</c>.</param>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class VertexAttributeNameAttribute(params string[] names) : Attribute
    {
        /// <summary>Gets the shader input names.</summary>
        public IReadOnlyList<string> Names { get; } = names;

        /// <summary>Gets the buffer semantic resolving to this slot, if any.</summary>
        public string? Semantic { get; init; }

        /// <summary>Gets the semantic index <see cref="Semantic"/> resolves at.</summary>
        public int SemanticIndex { get; init; }
    }

    /// <summary>
    /// Canonical vertex attribute locations, fixed per attribute name so that one vertex array object is
    /// valid for every shader drawing the geometry, and the renderer never has to ask the driver which slot
    /// it picked. <see cref="ShaderParser"/> stamps these onto the shader's <c>in</c> declarations.
    ///
    /// OpenGL only guarantees 16 slots and these take all of them, so geometry with an attribute of its own
    /// (text depth, particle frame blend) names the slot of a mesh attribute it never declares, rather than
    /// reserving one. Declaring both in one shader is a duplicate location error.
    /// </summary>
    public enum VertexAttributeSlot
    {
        /// <summary>Vertex position. Depth-only and picking read nothing above slot 3.</summary>
        [VertexAttributeName("vPOSITION", Semantic = "POSITION")]
        Position = 0,

        /// <summary>Skinning bone indices.</summary>
        [VertexAttributeName("vBLENDINDICES", Semantic = "BLENDINDICES")]
        BlendIndices,

        /// <summary>Skinning bone weights.</summary>
        [VertexAttributeName("vBLENDWEIGHT", Semantic = "BLENDWEIGHT")]
        BlendWeight,

        /// <summary>Primary texture coordinates, read by alpha tested depth.</summary>
        [VertexAttributeName("vTEXCOORD", Semantic = "TEXCOORD")]
        TexCoord,

        /// <summary>Second skinning stream bone indices, for eight bone skinning.</summary>
        [VertexAttributeName("vBLENDINDICES2", Semantic = "BLENDINDICES", SemanticIndex = 2)]
        BlendIndices2,

        /// <summary>Second skinning stream bone weights.</summary>
        [VertexAttributeName("vBLENDWEIGHT2", Semantic = "BLENDWEIGHT", SemanticIndex = 2)]
        BlendWeight2,

        /// <summary>Normal, or a compressed tangent frame.</summary>
        [VertexAttributeName("vNORMAL", Semantic = "NORMAL")]
        Normal,

        /// <summary>Tangent, when it is not packed into the normal.</summary>
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

        /// <summary>Layer parameters, or foliage sway parameters under their engine name.</summary>
        [VertexAttributeName("vTEXCOORD3", "vFoliageParams", Semantic = "TEXCOORD", SemanticIndex = 3)]
        TexCoord3,

        /// <summary>Layer blend color.</summary>
        [VertexAttributeName("vTEXCOORD4", Semantic = "TEXCOORD", SemanticIndex = 4)]
        TexCoord4,

        /// <summary>Layer blend alpha, or overlay projection direction.</summary>
        [VertexAttributeName("vTEXCOORD5", Semantic = "TEXCOORD", SemanticIndex = 5)]
        TexCoord5,

        /// <summary>Lightmap coordinates. Baked at map compile time, so it has no buffer semantic and only
        /// ever resolves through the material input signature.</summary>
        [VertexAttributeName("vLightmapUV", "vLightmapUVW")]
        LightmapUV,

        /// <summary>Baked per vertex lighting, the second color stream under its engine name.</summary>
        [VertexAttributeName("vCOLOR1", "vPerVertexLighting", Semantic = "COLOR", SemanticIndex = 1)]
        Color1,

        /// <summary>The spelling some meshes use for the weight stream.</summary>
        [VertexAttributeName(Semantic = "BLENDWEIGHTS")]
        BlendWeightsSpelling = BlendWeight,

        // Attributes a single renderer's own geometry carries. Reserving them here is what makes their
        // location deterministic, the driver's own assignment is implementation defined. They take the slot
        // of a mesh attribute that geometry never has, rather than one of their own.

        /// <summary>Bomb damage quad phase. Those quads are never skinned.</summary>
        [VertexAttributeName("vPHASE", Semantic = "PHASE")]
        Phase = BlendIndices2,

        /// <summary>Text depth. Text is never skinned.</summary>
        [VertexAttributeName("vDEPTH")]
        TextDepth = BlendIndices,

        /// <summary>Particle sprite sheet frame blend. Particles are never skinned.</summary>
        [VertexAttributeName("vFrameBlend")]
        FrameBlend = BlendWeight,

        /// <summary>Particle sprite sheet layer coordinates.</summary>
        [VertexAttributeName("vLayerUv0")]
        LayerUv0 = BlendIndices,

        /// <summary>Particle sprite sheet layer coordinates.</summary>
        [VertexAttributeName("vLayerUv1")]
        LayerUv1 = BlendIndices2,

        /// <summary>Particle sprite sheet layer coordinates.</summary>
        [VertexAttributeName("vLayerUv2")]
        LayerUv2 = BlendWeight2,

        /// <summary>Particle sprite sheet layer coordinates.</summary>
        [VertexAttributeName("vLayerUv3")]
        LayerUv3 = Normal,
    }

    /// <summary>
    /// Resolves shader input names and buffer semantics to their <see cref="VertexAttributeSlot"/>.
    /// </summary>
    public static class VertexAttributeLocations
    {
        private static readonly FrozenDictionary<string, int> SlotByName = BuildSlotByName();
        private static readonly FrozenDictionary<(string Semantic, int Index), int> SlotBySemantic = BuildSlotBySemantic();
        private static readonly FrozenDictionary<int, string> NamesBySlot = SlotByName
            .GroupBy(entry => entry.Value, entry => entry.Key)
            .ToFrozenDictionary(group => group.Key, group => string.Join('/', group));

        private static readonly FrozenDictionary<int, (string Name, int Index)> SemanticBySlot = SlotBySemantic
            .GroupBy(entry => entry.Value, entry => entry.Key)
            .ToFrozenDictionary(group => group.Key, group => group.First());

        private static IEnumerable<(VertexAttributeNameAttribute Attribute, int Slot)> EnumerateSlots()
        {
            foreach (var field in typeof(VertexAttributeSlot).GetFields(BindingFlags.Public | BindingFlags.Static))
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
                    // Add, not assign: two slots claiming one attribute name is a mistake worth failing on.
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

        /// <summary>Resolves a shader input name to its location, or -1 if unknown.</summary>
        /// <param name="attributeName">Shader input name, e.g. <c>vTEXCOORD1</c>.</param>
        /// <returns>The attribute location, or -1.</returns>
        public static int Get(string attributeName) => SlotByName.GetValueOrDefault(attributeName, -1);

        /// <summary>Resolves a buffer semantic to its location, or -1 if unknown. The fallback when no
        /// material input signature name resolves.</summary>
        /// <param name="semanticName">Buffer semantic name, e.g. <c>TEXCOORD</c>.</param>
        /// <param name="semanticIndex">Buffer semantic index.</param>
        /// <returns>The attribute location, or -1.</returns>
        public static int Get(string semanticName, int semanticIndex) => SlotBySemantic.GetValueOrDefault((semanticName, semanticIndex), -1);

        /// <summary>Resolves one vertex buffer attribute to its location: the material input signature's name
        /// for it wins, falling back to the attribute's own buffer semantic when the signature is absent or
        /// names something unknown.</summary>
        /// <param name="inputSignature">Material input signature naming the buffer semantics.</param>
        /// <param name="attribute">The vertex buffer attribute.</param>
        /// <param name="signatureName">The name the input signature gave it, empty if none did.</param>
        /// <returns>The attribute location, or -1 if neither the name nor the semantic is known.</returns>
        public static int Resolve(Material.VsInputSignature inputSignature, VBIB.RenderInputLayoutField attribute, out string signatureName)
        {
            signatureName = inputSignature.Elements is { Length: > 0 }
                ? Material.FindD3DInputSignatureElement(inputSignature, attribute.SemanticName, attribute.SemanticIndex).Name ?? string.Empty
                : string.Empty;

            var location = signatureName.Length > 0 ? Get(signatureName) : -1;

            return location != -1 ? location : Get(attribute.SemanticName, attribute.SemanticIndex);
        }

        /// <summary>Returns the buffer semantic a location is filled from, for describing handbuilt geometry
        /// as a vertex buffer layout.</summary>
        /// <param name="slot">The attribute.</param>
        /// <returns>The semantic name and index.</returns>
        public static (string Name, int Index) GetSemantic(VertexAttributeSlot slot)
            => SemanticBySlot.TryGetValue((int)slot, out var semantic)
                ? semantic
                : throw new ArgumentException($"'{slot}' has no buffer semantic.", nameof(slot));

        /// <summary>Names the attributes in a location bitmask, for diagnostics.</summary>
        /// <param name="locationMask">Bitmask of attribute locations.</param>
        /// <returns>A readable list of attribute names.</returns>
        public static string DescribeMask(int locationMask)
        {
            var names = new List<string>();

            for (var location = 0; locationMask >> location != 0; location++)
            {
                if ((locationMask & (1 << location)) != 0)
                {
                    names.Add(NamesBySlot.GetValueOrDefault(location, $"location {location}"));
                }
            }

            return names.Count > 0 ? string.Join(", ", names) : "no attributes";
        }
    }
}
