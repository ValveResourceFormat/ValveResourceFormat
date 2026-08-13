using System.Collections.Frozen;
using System.Linq;
using System.Reflection;

namespace ValveResourceFormat.Renderer
{
    /// <summary>Names the shader inputs bound to a <see cref="VertexAttributeSlot"/>, and optionally the
    /// vertex buffer semantics that resolve to it. Several names may share a slot when no shader variant
    /// can declare both, the same way <see cref="Materials.ReservedTextureSlots"/> shares texture units.</summary>
    /// <param name="names">Shader input names bound to this slot, e.g. <c>vTEXCOORD1</c>.</param>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class VertexAttributeNameAttribute(params string[] names) : Attribute
    {
        /// <summary>Gets the shader input names bound to this slot.</summary>
        public IReadOnlyList<string> Names { get; } = names;

        /// <summary>Gets the vertex buffer semantic names that resolve to this slot, e.g. <c>TEXCOORD</c>.
        /// Empty for attributes that only exist under a shader name, such as <c>vLightmapUV</c>.</summary>
        public string[] Semantics { get; init; } = [];

        /// <summary>Gets the semantic index <see cref="Semantics"/> resolve at.</summary>
        public int SemanticIndex { get; init; }
    }

    /// <summary>
    /// Canonical vertex attribute locations, shared by every shader.
    ///
    /// A slot is fixed per attribute name, so a vertex array object built for a mesh is valid for any shader
    /// that draws it (material, depth-only, picking, outline), and the renderer never has to ask the driver
    /// which slot it picked. <see cref="Shaders.ShaderParser"/> stamps these numbers onto the shader's
    /// <c>in</c> declarations while preprocessing, so the shaders cannot drift from this table.
    ///
    /// OpenGL only guarantees 16 slots, and the mesh attributes below take all of them. Anything else shares
    /// a slot with an attribute it can never be declared next to, which the GLSL compiler enforces per
    /// variant as a duplicate location error. One-off attributes belonging to a single renderer are always
    /// aliases, never slots of their own.
    /// </summary>
    public enum VertexAttributeSlot
    {
        /// <summary>Vertex position. Depth-only and picking read slots 0-3 and nothing else, so the
        /// attributes they need come first.</summary>
        [VertexAttributeName("vPOSITION", Semantics = ["POSITION"])]
        Position = 0,

        /// <summary>Skinning bone indices.</summary>
        [VertexAttributeName("vBLENDINDICES", Semantics = ["BLENDINDICES"])]
        BlendIndices,

        /// <summary>Skinning bone weights.</summary>
        [VertexAttributeName("vBLENDWEIGHT", Semantics = ["BLENDWEIGHT", "BLENDWEIGHTS"])]
        BlendWeight,

        /// <summary>Primary texture coordinates, read by alpha tested depth.</summary>
        [VertexAttributeName("vTEXCOORD", Semantics = ["TEXCOORD"])]
        TexCoord,

        /// <summary>Second skinning stream bone indices, for eight bone skinning.</summary>
        [VertexAttributeName("vBLENDINDICES2", Semantics = ["BLENDINDICES"], SemanticIndex = 2)]
        BlendIndices2,

        /// <summary>Second skinning stream bone weights.</summary>
        [VertexAttributeName("vBLENDWEIGHT2", Semantics = ["BLENDWEIGHT"], SemanticIndex = 2)]
        BlendWeight2,

        /// <summary>Normal, or a compressed tangent frame.</summary>
        [VertexAttributeName("vNORMAL", Semantics = ["NORMAL"])]
        Normal,

        /// <summary>Tangent, when it is not packed into the normal.</summary>
        [VertexAttributeName("vTANGENT", Semantics = ["TANGENT"])]
        Tangent,

        /// <summary>Vertex color.</summary>
        [VertexAttributeName("vCOLOR", Semantics = ["COLOR"])]
        Color,

        /// <summary>Secondary texture coordinates.</summary>
        [VertexAttributeName("vTEXCOORD1", Semantics = ["TEXCOORD"], SemanticIndex = 1)]
        TexCoord1,

        /// <summary>Blend weights for the layered material shaders.</summary>
        [VertexAttributeName("vTEXCOORD2", Semantics = ["TEXCOORD"], SemanticIndex = 2)]
        TexCoord2,

        /// <summary>Layer parameters, or foliage sway parameters under their engine name.</summary>
        [VertexAttributeName("vTEXCOORD3", "vFoliageParams", Semantics = ["TEXCOORD"], SemanticIndex = 3)]
        TexCoord3,

        /// <summary>Layer blend color.</summary>
        [VertexAttributeName("vTEXCOORD4", Semantics = ["TEXCOORD"], SemanticIndex = 4)]
        TexCoord4,

        /// <summary>Layer blend alpha, or overlay projection direction.</summary>
        [VertexAttributeName("vTEXCOORD5", Semantics = ["TEXCOORD"], SemanticIndex = 5)]
        TexCoord5,

        /// <summary>Lightmap coordinates. Baked at map compile time, so it carries no buffer semantic and
        /// only ever resolves through the material input signature.</summary>
        [VertexAttributeName("vLightmapUV", "vLightmapUVW")]
        LightmapUV,

        /// <summary>Baked per vertex lighting, the second color stream under its engine name.</summary>
        [VertexAttributeName("vCOLOR1", "vPerVertexLighting", Semantics = ["COLOR"], SemanticIndex = 1)]
        Color1,

        ///////// Aliases /////////
        // Attributes belonging to one renderer, sharing the slot of a mesh attribute that renderer never declares.

        /// <summary>Bomb damage quad phase. Those quads are never skinned.</summary>
        [VertexAttributeName("vPHASE", Semantics = ["PHASE"])]
        Phase = BlendIndices2,

        /// <summary>Text depth. Text is never skinned.</summary>
        [VertexAttributeName("vDEPTH")]
        TextDepth = BlendIndices,

        /// <summary>Particle sprite animation frame blend. Particles are never skinned.</summary>
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

        /// <summary>Morph composite rect position and weights. The composite pass draws rects, not meshes.</summary>
        [VertexAttributeName("vPositionWeights")]
        MorphPositionWeights = Position,

        /// <summary>Morph composite rect texture coordinates.</summary>
        [VertexAttributeName("vTexCoords")]
        MorphTexCoords = TexCoord,

        /// <summary>Morph composite rect offsets.</summary>
        [VertexAttributeName("vOffsetsPositionSpeed")]
        MorphOffsets = BlendIndices,

        /// <summary>Morph composite rect ranges.</summary>
        [VertexAttributeName("vRangesPositionSpeed")]
        MorphRanges = BlendWeight,
    }

    /// <summary>
    /// Resolves shader input names and vertex buffer semantics to the <see cref="VertexAttributeSlot"/> they
    /// are bound to.
    /// </summary>
    public static class VertexAttributeLocations
    {
        private static readonly FrozenDictionary<string, int> SlotByName = BuildSlotByName();
        private static readonly FrozenDictionary<(string Semantic, int Index), int> SlotBySemantic = BuildSlotBySemantic();
        private static readonly FrozenDictionary<int, string> NamesBySlot = SlotByName
            .GroupBy(entry => entry.Value, entry => entry.Key)
            .ToFrozenDictionary(group => group.Key, group => string.Join('/', group));

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
                foreach (var semantic in attribute.Semantics)
                {
                    slotBySemantic.Add((semantic, attribute.SemanticIndex), slot);
                }
            }

            return slotBySemantic.ToFrozenDictionary();
        }

        /// <summary>Resolves a shader input name, such as one from a material input signature, to its
        /// canonical location.</summary>
        /// <param name="attributeName">Shader input name, e.g. <c>vTEXCOORD1</c> or <c>vLightmapUV</c>.</param>
        /// <returns>The canonical attribute location, or -1 if the name is unknown.</returns>
        public static int Get(string attributeName) => SlotByName.GetValueOrDefault(attributeName, -1);

        /// <summary>Resolves a vertex buffer semantic to its canonical location. This is the fallback when
        /// no material input signature name resolves.</summary>
        /// <param name="semanticName">Buffer semantic name, e.g. <c>TEXCOORD</c>.</param>
        /// <param name="semanticIndex">Buffer semantic index.</param>
        /// <returns>The canonical attribute location, or -1 if the semantic is unknown.</returns>
        public static int Get(string semanticName, int semanticIndex) => SlotBySemantic.GetValueOrDefault((semanticName, semanticIndex), -1);

        /// <summary>Names the attributes in a location bitmask, for diagnostics. Slots shared by several
        /// names list all of them, since a bitmask cannot say which one was meant.</summary>
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
