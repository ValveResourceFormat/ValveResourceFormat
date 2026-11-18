using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace ValveResourceFormat.IO;

public sealed partial class MapExtract
{
    // Keys the map compiler writes into entities from light baking and compiling, which no FGD declares
    private static readonly HashSet<string> CompilerOutputKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "compile_source_id",

        "bakedshadowindex",
        "light_map_uniqueid",
        "light_path_uniqueid",
        "desiredstationary",
        "bakelightindex",
        "bakelightindexscale",
        "precomputedboundsmins",
        "precomputedboundsmaxs",
        "precomputedobborigin",
        "precomputedobbangles",
        "precomputedobbextent",
        "precomputedsubfrusta",
        "precomputed_vis_clusters",
        "precomputedfieldsvalid",
        "precomputedminrange",
        "precomputedmaxrange",
        "precomputeup",
        "precomputedirection",
        "precomputeposition",

        "_template_lump_ent_index",
        "worldname",
    };

    // Light probe and cubemap keys of the baked result, which no FGD declares
    private static readonly HashSet<string> CompilerOutputProbeKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "handshake",
        "array_index",
        "light_probe_size_x",
        "light_probe_size_y",
        "light_probe_size_z",
        "light_probe_atlas_x",
        "light_probe_atlas_y",
        "light_probe_atlas_z",
    };

    // The compiler fills in every unit from the one brightness_units selects, by unit index
    private static readonly string[] BrightnessUnitKeys = ["brightness", "brightness_lumens", "brightness_nits", "brightness_legacy", "brightness_candelas"];

    private static bool IsProbeClass(string className)
        => className is "env_combined_light_probe_volume" or "env_light_probe_volume" or "env_cubemap" or "env_cubemap_box" or "env_cubemap_fog";

    /// <summary>
    /// Gets whether a key holds what the map compiler wrote into the entity, rather than what was authored in Hammer.
    /// </summary>
    private static bool IsCompilerOutputProperty(string className, string key, KVObject value, Entity compiledEntity)
    {
        // Numbered per light frustum, such as precomputedobborigin0
        if (CompilerOutputKeys.GetAlternateLookup<ReadOnlySpan<char>>().Contains(key.AsSpan().TrimEnd("0123456789")))
        {
            return true;
        }

        // Gridnav bounds Dota writes on worldspawn
        if (key.StartsWith("_dotatilegrid_", StringComparison.Ordinal))
        {
            return true;
        }

        // Older Hammer builds wrote it as 0 on every entity, which is also the default where a class declares it
        if (key == "uselocaloffset" && value.ToInt32OrDefault() == 0)
        {
            return true;
        }

        if (IsProbeClass(className))
        {
            if (CompilerOutputProbeKeys.Contains(key))
            {
                return true;
            }

            // Baked textures are written to the lightmaps or cubemaps folder of the map, or left empty for unused
            // ones, a texture set in Hammer is kept
            if (key == "cubemaptexture" || key.StartsWith("lightprobetexture", StringComparison.Ordinal))
            {
                var path = (ToEditString(value) ?? string.Empty).Replace('\\', '/');
                return path.Length == 0 || (path.StartsWith("maps/", StringComparison.OrdinalIgnoreCase)
                    && (path.Contains("/lightmaps/", StringComparison.OrdinalIgnoreCase) || path.Contains("/cubemaps/", StringComparison.OrdinalIgnoreCase)));
            }
        }

        if (className.StartsWith("light_", StringComparison.Ordinal) && key.StartsWith("brightness_", StringComparison.Ordinal) && key != "brightness_units")
        {
            var unit = compiledEntity.GetInt32Property("brightness_units");
            var authoredKey = unit >= 0 && unit < BrightnessUnitKeys.Length ? BrightnessUnitKeys[unit] : null;
            return Array.IndexOf(BrightnessUnitKeys, key) > 0 && key != authoredKey;
        }

        return false;
    }
}
