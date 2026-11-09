using System.Linq;
using ValveResourceFormat.IO;
using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Source1.Entities;

/// <summary>
/// Rewrites the entities of a converted map for the entity classes of the Source 2 game it is exported for. The viewer
/// reads the converted entities as they are, so the changes that depend on the game are made only when the entities are
/// written to a Hammer map. The changes every game needs are made by <see cref="Source1EntityFinalizer"/>.
/// </summary>
internal static class Source1EntityExport
{
    private static readonly string[] SunRemovedKeys =
    [
        "glowdistancescale",
        "hdrcolorscale",
        "material",
        "overlaymaterial",
        "overlaysize",
        "overlaycolor",
        "rendercolor",
        "size",
        "use_angles",
        "target",
    ];

    // Classes the map importer of the game does not carry over: the Source 2 game defines no such entity, hides it from
    // Hammer, or the converter already turned it into something else
    private static readonly HashSet<string> ExcludedClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "color_correction",
        "cs_bot_patrol_route_respawn",
        "cs_bot_patrol_route_waypoint",
        "env_detail_controller",
        "env_lightglow",
        "env_tonemap_controller",
        "fog_volume",
        "func_areaportal",
        "func_areaportalwindow",
        "func_dustmotes",
        "func_occluder",
        "func_precipitation",
        "func_viscluster",
        "infodecal",
        "info_lighting",
        "info_no_dynamic_shadow",
        "point_spotlight",
        "postprocess_controller",
        "shadow_control",
    };

    // Keys of Source 1 that no entity class of the Source 2 game declares
    private static readonly string[] LeftoverKeys =
    [
        "uniformscale",
        "shadowdepth",
        "shadowdepthnocache",
        "preventpropcombine",
        "enablelightbounce",
        "mingpulevel",
        "mincpulevel",
        "maxgpulevel",
        "maxcpulevel",
        "ignorenormals",
        "drawinfastreflection",
        "disablex360",
        "disablevertexlighting",
        "disableshadowdepth",
        "disableflashlight",
        "disableselfshadowing",
        "cubemapsize",
    ];

    // Keys the map compiler of Source 1 reads from a light, which the lights of the Source 2 game do not declare
    private static readonly string[] LightCompileKeys =
    [
        "_light",
        "_lighthdr",
        "_lightscalehdr",
        "_distance",
        "_cone",
        "_cone2",
        "_inner_cone",
        "_exponent",
        "_hardfalloff",
        "_constant_attn",
        "_linear_attn",
        "_quadratic_attn",
        "_fifty_percent_distance",
        "_zero_percent_distance",
    ];

    private const string ClientsideRopeClass = "path_particle_rope_clientside";
    private const string RopeClass = "path_particle_rope";

    /// <summary>
    /// Gets the entities as they are written to a Hammer map for a game.
    /// </summary>
    /// <param name="entities">The converted entities, which are not changed.</param>
    /// <param name="targetGame">The Source 2 game the map is exported for.</param>
    /// <returns>The entities to write, which are copies.</returns>
    public static List<EntityLump.Entity> Convert(List<EntityLump.Entity> entities, Game targetGame)
    {
        var converted = new List<EntityLump.Entity>(entities.Count);
        var targetClasses = EntityClassCatalog.GetTable(targetGame);
        var droppedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in entities)
        {
            var className = entity.GetStringProperty("classname") ?? string.Empty;
            if (targetGame == Game.Csgo && ExcludedClasses.Contains(className))
            {
                if (entity.GetStringProperty("targetname") is { Length: > 0 } droppedName)
                {
                    droppedNames.Add(droppedName);
                }

                continue;
            }

            converted.Add(Convert(entity, className, targetGame, targetClasses));
        }

        Source1EntityFinalizer.RemoveConnectionsToDropped(converted, droppedNames);

        return converted;
    }

    private static EntityLump.Entity Convert(EntityLump.Entity source, string className, Game targetGame, EntityClassTable targetClasses)
    {
        var entity = Source1EntityCopy.Copy(source);

        entity.Remove("drawnbyworld");
        ConvertDefaultAnimation(entity, targetClasses.FindClass(className));

        if (targetGame == Game.Csgo)
        {
            RemoveLeftoverKeys(entity, className);
        }

        // A game without the clientside rope has the rope that runs on the server
        if (className == ClientsideRopeClass && targetClasses.FindClass(className) == null && targetClasses.FindClass(RopeClass) != null)
        {
            entity["classname"] = RopeClass;
        }

        switch (className)
        {
            case "func_brush" or "func_door" when targetGame == Game.Csgo:
                entity["lightmapstatic"] = 1L;
                break;
            case "env_sun" when targetGame == Game.Csgo:
                ConvertSun(entity);
                break;
            case "env_soundscape" when targetGame == Game.Csgo:
                entity["enablesoundevent"] = 1L;
                entity["soundevent"] = source.GetStringProperty("soundscape") ?? string.Empty;
                break;
            case "light_omni":
                entity["lightsourceradius"] = 2.0;
                break;
        }

        return entity;
    }

    private static void RemoveLeftoverKeys(EntityLump.Entity entity, string className)
    {
        foreach (var key in LeftoverKeys)
        {
            entity.Remove(key);
        }

        if (!className.StartsWith("light", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (var key in LightCompileKeys)
        {
            entity.Remove(key);
        }
    }

    private static void ConvertDefaultAnimation(EntityLump.Entity entity, EntityClass? targetClass)
    {
        var defaultAnimation = entity.GetStringProperty("defaultanim");

        if (defaultAnimation == null)
        {
            return;
        }

        entity.Remove("defaultanim");

        var hasIdleAnimation = targetClass?.PreviewAnimationKeys.Contains("IdleAnim", StringComparer.OrdinalIgnoreCase) == true;

        if (!hasIdleAnimation)
        {
            entity.Remove("idleanimationloopmode");
            return;
        }

        if (entity.ContainsKey("idleanim") || entity.ContainsKey("startinganim"))
        {
            return;
        }

        entity["idleanim"] = defaultAnimation;
    }

    // The sun of Source 2 pitches upwards where the one of Source 1 pitches downwards, and its sprite has other keys
    private static void ConvertSun(EntityLump.Entity entity)
    {
        var angles = entity.GetVector3Property("angles");
        entity["angles"] = Source1EntityConverter.FormatVector(new Vector3(-angles.X, angles.Y, angles.Z));
        entity["colortint"] = entity.GetStringProperty("rendercolor") ?? string.Empty;
        entity["scale"] = entity.GetFloatProperty("size") * 0.25f;
        entity["rotation"] = 0f;
        entity["alphascale"] = 1f;
        entity["alphahdr"] = 1f;
        entity["alphahaze"] = 1f;
        entity["selfillumscale"] = 5f;
        entity["farzscale"] = 0.95f;
        entity["mapfarz"] = 0L;

        foreach (var key in SunRemovedKeys)
        {
            entity.Remove(key);
        }
    }
}
