using System.Collections.Immutable;

namespace GUI.Types.Renderer
{
    static class RenderModes
    {
        public record struct RenderMode(string Name, bool IsHeader = false);

        public static ImmutableList<RenderMode> Items =
        [
            new("Default"),

            new("Lighting", IsHeader: true),
            new("FullBright"),
            new("Diffuse"),
            new("Specular"),
            new("Irradiance"),
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
        ];

        private readonly static Dictionary<string, byte> ShaderIds = new(Items.Count);

        public static void AddShaderId(string renderMode, byte value) => ShaderIds.Add(renderMode, value);
        public static byte GetShaderId(string renderMode) => ShaderIds.TryGetValue(renderMode, out var value) ? value : (byte)0;
    }
}
