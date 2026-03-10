using System.Collections.Immutable;

namespace ValveResourceFormat.Renderer.Materials
{
    /// <summary>
    /// Available render mode options for debug visualization.
    /// </summary>
    public static class RenderModes
    {
        /// <summary>
        /// Render mode configuration with optional header flag.
        /// </summary>
        public record struct RenderMode(string Name, bool IsHeader = false);

        /// <summary>Gets or sets the ordered list of all render modes available for selection in the viewer UI.</summary>
        public static ImmutableList<RenderMode> Items { get; set; } =
        [
            new("Default"),

            new("Lighting", IsHeader: true),
            new("FullBright"),
            new("Diffuse"),
            new("Specular"),
            new("Irradiance"),
            new("IrradianceDebug"),
            new("Illumination"),
            new("LightmapShadows"),
            new("Cubemaps"),
            new("RimLight"),

            new("Material", IsHeader: true),
            new("Color"),
            new("Tint"),
            new("Occlusion"),
            new("Roughness"),
            new("Metalness"),
            new("Height"),
            new("Mask1"),
            new("Mask2"),
            new("ExtraParams"),

            new("Normals", IsHeader: true),
            new("Normals"),
            new("Tangents"),
            new("BumpMap"),
            new("BumpNormals"),

            new("Vertex Attributes", IsHeader: true),
            new("TerrainBlend"),
            new("FoliageParams"),
            new("VertexColor"),

            new ("Texture Coordinates", IsHeader: true),
            new ("UvDensity"),
            new ("LightmapUvDensity"),
            new ("MipmapUsage"),

            new("Identification", IsHeader: true),
            new("ObjectId"),
            new("MeshId"),
            new("ShaderId"),
            new("ShaderProgramId"),
            new("Meshlets")
        ];

        private readonly static Dictionary<string, byte> ShaderIds = new(Items.Count);

        /// <summary>Registers the shader define index assigned to a render mode name during preprocessing.</summary>
        /// <param name="renderMode">The render mode name (without the <c>renderMode_</c> prefix).</param>
        /// <param name="value">The byte index assigned to this render mode in the shader define.</param>
        public static void AddShaderId(string renderMode, byte value) => ShaderIds.Add(renderMode, value);

        /// <summary>Returns the shader define index registered for the given render mode name, or 0 if none has been registered.</summary>
        /// <param name="renderMode">The render mode name (without the <c>renderMode_</c> prefix).</param>
        public static byte GetShaderId(string renderMode) => ShaderIds.TryGetValue(renderMode, out var value) ? value : (byte)0;
    }
}
