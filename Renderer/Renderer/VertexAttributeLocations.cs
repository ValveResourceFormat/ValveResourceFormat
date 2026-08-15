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

        /// <summary>Layer blend weights, or per vertex subsurface curvature on skin.</summary>
        [VertexAttributeName("vTEXCOORD2", "flSSSCurvature", Semantic = "TEXCOORD", SemanticIndex = 2)]
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
    /// Resolves shader input names and buffer semantics to their <see cref="VertexSlot"/>.
    /// </summary>
    public static class VertexAttributeLocations
    {
        private static readonly FrozenDictionary<string, int> SlotByName = BuildSlotByName();
        private static readonly FrozenDictionary<(string Semantic, int Index), int> SlotBySemantic = BuildSlotBySemantic();

        private static readonly FrozenDictionary<int, string> NameBySlot = SlotByName
            .GroupBy(entry => entry.Value, entry => entry.Key)
            .ToFrozenDictionary(group => group.Key, group => group.First());

        private static readonly FrozenDictionary<int, (string Name, int Index)> SemanticBySlot = SlotBySemantic
            .GroupBy(entry => entry.Value, entry => entry.Key)
            .ToFrozenDictionary(group => group.Key, group => group.First());

        private static IEnumerable<(VertexAttributeNameAttribute Attribute, int Slot)> EnumerateSlots()
        {
            foreach (var field in typeof(VertexSlot).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.GetCustomAttribute<VertexAttributeNameAttribute>() is { } attribute)
                {
                    yield return (attribute, (int)field.GetRawConstantValue()!);
                }
            }
        }

        /// <summary>
        /// Gives every attribute of one piece of geometry its location. A mesh attribute keeps its canonical
        /// slot. Any other name takes the lowest slot this set leaves free, in name order, so that the shader
        /// and the vertex struct declaring the same set both arrive at the same numbers.
        ///
        /// A name in <paramref name="pinned"/> keeps the location it was given, and the rest allocate around
        /// it. That is how a shader that places an attribute itself, with an explicit layout qualifier, stays
        /// consistent with the geometry that feeds it.
        /// </summary>
        public static FrozenDictionary<string, int> Allocate(IEnumerable<string> attributeNames, IReadOnlyDictionary<string, int>? pinned = null)
        {
            var locations = new Dictionary<string, int>(StringComparer.Ordinal);
            var custom = new List<string>();
            var used = 0;

            foreach (var name in attributeNames)
            {
                var slot = pinned != null && pinned.TryGetValue(name, out var pinnedSlot) ? pinnedSlot : Get(name);

                if (slot == -1)
                {
                    custom.Add(name);
                    continue;
                }

                locations[name] = slot;
                used |= 1 << slot;
            }

            custom.Sort(StringComparer.Ordinal);

            var free = 0;

            foreach (var name in custom)
            {
                while ((used & (1 << free)) != 0)
                {
                    free++;
                }

                locations[name] = free;
                used |= 1 << free;
            }

            return locations.ToFrozenDictionary(StringComparer.Ordinal);
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

        /// <summary>The name of a mesh attribute, as its shaders declare it.</summary>
        public static string GetName(VertexSlot slot) => NameBySlot[(int)slot];

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

            if (location == -1)
            {
                location = Get(attribute.SemanticName, attribute.SemanticIndex);
            }

            // Handbuilt geometry names its shader input directly, and a custom attribute has no semantic
            return location == -1 && attribute.ShaderSemantic is { Length: > 0 } shaderInput ? Get(shaderInput) : location;
        }

        /// <summary>The buffer semantic a slot is filled from, for describing handbuilt geometry as a layout.</summary>
        public static (string Name, int Index) GetSemantic(int slot)
            => SemanticBySlot.GetValueOrDefault(slot, (string.Empty, 0));
    }
}
