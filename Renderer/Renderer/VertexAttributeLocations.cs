using System.Collections.Frozen;
using ValveResourceFormat.Renderer.Shaders;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Canonical vertex attribute locations shared by every shader that reads mesh vertex buffers.
    /// 
    /// Locations are fixed per attribute name so a vertex array object built for a mesh is valid for
    /// any shader (material, depth-only, picking, outline).
    ///
    /// This makes it so instead of having to let the driver pick a slot, then asking it what it picked and caching that,
    /// we just reference the canonical slot of the attribute (this also helps with switching away from OpenGL).
    /// 
    /// The numbers live in <c>Shaders/common/vertex_layout.slang</c>, and each attributeTable row below pairs one of its macros
    /// with the shader attribute names and buffer semantics that resolve to it.
    ///
    /// The attributeTable is built on first use, failing with the macro's name if the file no longer defines it in order to keep things matching.
    /// </summary>
    public static class VertexAttributeLocations
    {
        private static readonly FrozenDictionary<string, int> ByName;
        private static readonly FrozenDictionary<(string Semantic, int Index), int> BySemantic;

        static VertexAttributeLocations()
        {
            var macros = ShaderParser.ParseVertexLayoutMacros();

            // Macro, the shader attribute names resolving to it (material input signature names),
            // and the raw buffer semantics resolving to it (the fallback when no signature name matches).
            (string Macro, string[] Names, (string Semantic, int Index)[] Semantics)[] attributeTable =
            [
                ("ATTRIBUTE_POSITION", ["vPOSITION"], [("POSITION", 0)]),
                ("ATTRIBUTE_BLENDINDICES", ["vBLENDINDICES"], [("BLENDINDICES", 0)]),
                ("ATTRIBUTE_BLENDWEIGHT", ["vBLENDWEIGHT"], [("BLENDWEIGHT", 0), ("BLENDWEIGHTS", 0)]),
                ("ATTRIBUTE_TEXCOORD", ["vTEXCOORD"], [("TEXCOORD", 0)]),
                ("ATTRIBUTE_BLENDINDICES2", ["vBLENDINDICES2"], [("BLENDINDICES", 2)]),
                ("ATTRIBUTE_BLENDWEIGHT2", ["vBLENDWEIGHT2"], [("BLENDWEIGHT", 2)]),
                ("ATTRIBUTE_NORMAL", ["vNORMAL"], [("NORMAL", 0)]),
                ("ATTRIBUTE_TANGENT", ["vTANGENT"], [("TANGENT", 0)]),
                ("ATTRIBUTE_COLOR", ["vCOLOR"], [("COLOR", 0)]),
                ("ATTRIBUTE_TEXCOORD1", ["vTEXCOORD1"], [("TEXCOORD", 1)]),
                ("ATTRIBUTE_TEXCOORD2", ["vTEXCOORD2"], [("TEXCOORD", 2)]),
                ("ATTRIBUTE_TEXCOORD3", ["vTEXCOORD3"], [("TEXCOORD", 3)]),
                ("ATTRIBUTE_TEXCOORD4", ["vTEXCOORD4"], [("TEXCOORD", 4)]),
                ("ATTRIBUTE_TEXCOORD5", ["vTEXCOORD5"], [("TEXCOORD", 5)]),
                ("ATTRIBUTE_LIGHTMAP_UV", ["vLightmapUV", "vLightmapUVW"], []),
                ("ATTRIBUTE_COLOR1", ["vCOLOR1", "vPerVertexLighting"], [("COLOR", 1)]),
                ("ATTRIBUTE_PHASE", ["vPHASE"], [("PHASE", 0)]),
                ("ATTRIBUTE_FOLIAGE", ["vFoliageParams"], []),
            ];

            var byName = new Dictionary<string, int>(StringComparer.Ordinal);
            var bySemantic = new Dictionary<(string, int), int>();

            foreach (var (macro, names, semantics) in attributeTable)
            {
                if (!macros.TryGetValue(macro, out var location))
                {
                    throw new ShaderLoader.ShaderCompilerException($"vertex_layout.slang does not define '{macro}'.");
                }

                foreach (var name in names)
                {
                    byName.Add(name, location);
                }

                foreach (var semantic in semantics)
                {
                    bySemantic.Add(semantic, location);
                }
            }

            ByName = byName.ToFrozenDictionary(StringComparer.Ordinal);
            BySemantic = bySemantic.ToFrozenDictionary();
        }

        /// <summary>Resolves a shader attribute name from a material input signature to its canonical
        /// location, or -1 if the name is unknown.</summary>
        /// <param name="attributeName">Shader attribute name, e.g. <c>vTEXCOORD1</c> or <c>vLightmapUV</c>.</param>
        /// <returns>The canonical attribute location, or -1.</returns>
        public static int Get(string attributeName) => ByName.GetValueOrDefault(attributeName, -1);

        /// <summary>Resolves a vertex buffer semantic to its canonical location, or -1 if the semantic
        /// is unknown. This is the fallback when no material input signature name resolves.</summary>
        /// <param name="semanticName">Buffer semantic name, e.g. <c>TEXCOORD</c>.</param>
        /// <param name="semanticIndex">Buffer semantic index.</param>
        /// <returns>The canonical attribute location, or -1.</returns>
        public static int Get(string semanticName, int semanticIndex) => BySemantic.GetValueOrDefault((semanticName, semanticIndex), -1);
    }
}
