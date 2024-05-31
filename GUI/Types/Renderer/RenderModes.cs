using System.Collections.Immutable;

namespace GUI.Types.Renderer
{
    static class RenderModes
    {
        public record struct RenderMode(bool IsHeader, string Name);

        public static ImmutableList<RenderMode> Items =
        [
            new(false, "Default"),

            new(true, "Lighting"),
            new (false, "FullBright"),
            new(false, "Diffuse"),
            new(false, "Specular"),
            new(false, "Irradiance"),
            new(false, "Illumination"),
            new(false, "Cubemaps"),
            new(false, "RimLight"),

            new(true, "Material"),
            new(false, "Color"),
            new(false, "Tint"),
            new(false, "Occlusion"),
            new(false, "Roughness"),
            new(false, "Metalness"),
            new(false, "Height"),
            new(false, "Mask1"),
            new(false, "Mask2"),
            new(false, "ExtraParams"),
            new(false, "SpriteEffects"),

            new(true, "Normals"),
            new(false, "Normals"),
            new(false, "Tangents"),
            new(false, "BumpMap"),
            new(false, "BumpNormals"),

            new(true, "Vertex Attributes"),
            new(false, "TerrainBlend"),
            new(false, "FoliageParams"),
            new(false, "VertexColor"),

            new(true, "Identification"),
            new(false, "ObjectId"),
            new(false, "MeshId"),
            new(false, "ShaderId"),
            new(false, "ShaderProgramId"),
        ];

        private readonly static Dictionary<string, byte> ShaderIds = new(Items.Count);

        public static void AddShaderId(string renderMode, byte value) => ShaderIds.Add(renderMode, value);
        public static byte GetShaderId(string renderMode) => ShaderIds.TryGetValue(renderMode, out var value) ? value : (byte)0;
    }
}
