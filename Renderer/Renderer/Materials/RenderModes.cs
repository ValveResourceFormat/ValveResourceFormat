using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading;

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
            new("Cubemaps"),
            new("Illumination"),
            new("LightmapShadows"),
            new("LightmapCharts"),
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
            new("Meshlets"),

            new("Debug", IsHeader: true),
            new("LightTiles"),
            new("EnvmapTiles"),
            new("Subgroups"),
            new("QuadOverdraw")
        ];

        // Shaders are preprocessed on several threads at once
        private static readonly Lock ShaderIdsLock = new();
        private static readonly Dictionary<string, byte> ShaderIds = new(Items.Count);

        /// <summary>Returns the shader define index of a render mode, assigning it on first use during preprocessing.</summary>
        /// <param name="renderMode">The render mode name (without the <c>renderMode_</c> prefix).</param>
        public static byte GetOrAddShaderId(string renderMode)
        {
            using var _ = ShaderIdsLock.EnterScope();

            if (ShaderIds.TryGetValue(renderMode, out var value))
            {
                return value;
            }

            var renderModeObj = new RenderMode(renderMode);
            var index = Items.IndexOf(renderModeObj);

            if (index == -1)
            {
                Debug.Assert(false); // Add to <see cref="Items"/> if this assert is hit

                Items = Items.Add(renderModeObj);
                index = Items.Count - 1;
            }

            value = (byte)index;
            ShaderIds.Add(renderMode, value);

            return value;
        }

        /// <summary>Returns the shader define index registered for the given render mode name, or 0 if none has been registered.</summary>
        /// <param name="renderMode">The render mode name (without the <c>renderMode_</c> prefix).</param>
        public static byte GetShaderId(string renderMode)
        {
            using var _ = ShaderIdsLock.EnterScope();

            return ShaderIds.TryGetValue(renderMode, out var value) ? value : (byte)0;
        }
    }
}
