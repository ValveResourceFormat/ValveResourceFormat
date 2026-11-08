using System.Globalization;
using System.Linq;
using ValveResourceFormat.IO;
using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Source1.Entities;

/// <summary>
/// Rewrites the entities of a converted map for the entity classes of the Source 2 game it is exported for. The viewer
/// reads the converted entities as they are, so these changes are made only when the entities are written to a Hammer map.
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
        "info_lighting",
        "info_no_dynamic_shadow",
        "point_spotlight",
        "postprocess_controller",
        "shadow_control",
    };

    // Ropes are written as the models the converter bakes them into, in every game
    private static readonly HashSet<string> RopeClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "keyframe_rope",
        "move_rope",
    };

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
        var hasEnvironmentLight = false;

        foreach (var entity in entities)
        {
            var className = entity.GetStringProperty("classname") ?? string.Empty;
            var isDropped = RopeClasses.Contains(className) || (targetGame == Game.Csgo && ExcludedClasses.Contains(className));

            // Only one light_environment lights a map
            if (!isDropped && className == "light_environment")
            {
                isDropped = hasEnvironmentLight;
                hasEnvironmentLight = true;
            }

            if (isDropped)
            {
                if (entity.GetStringProperty("targetname") is { Length: > 0 } droppedName)
                {
                    droppedNames.Add(droppedName);
                }

                continue;
            }

            converted.Add(Convert(entity, className, targetGame, targetClasses));
        }

        RemoveConnectionsToDropped(converted, droppedNames);

        return converted;
    }

    // A name that a kept entity also has still has a target
    private static void RemoveConnectionsToDropped(List<EntityLump.Entity> kept, HashSet<string> droppedNames)
    {
        if (droppedNames.Count == 0)
        {
            return;
        }

        foreach (var entity in kept)
        {
            if (entity.GetStringProperty("targetname") is { Length: > 0 } keptName)
            {
                droppedNames.Remove(keptName);
            }
        }

        foreach (var entity in kept)
        {
            entity.Connections?.RemoveAll(connection => droppedNames.Contains(connection.TargetName));
        }
    }

    private static EntityLump.Entity Convert(EntityLump.Entity source, string className, Game targetGame, EntityClassTable targetClasses)
    {
        var entity = Source1EntityCopy.Copy(source);

        entity.Remove("drawnbyworld");
        ConvertDefaultAnimation(entity, targetClasses.FindClass(className));

        switch (className)
        {
            case "env_sun" when targetGame == Game.Csgo:
                ConvertSun(entity);
                break;
            case "env_soundscape" when targetGame == Game.Csgo:
                entity["enablesoundevent"] = 1L;
                entity["soundevent"] = source.GetStringProperty("soundscape") ?? string.Empty;
                break;
            case "info_particle_system":
                if (!entity.ContainsKey("start_active"))
                {
                    entity["start_active"] = 0L;
                }

                break;
            case "point_worldtext":
                ConvertWorldText(entity);
                break;
        }

        return entity;
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

    // Source 2 text faces along its up axis and is sized by font_size
    private static void ConvertWorldText(EntityLump.Entity entity)
    {
        var angles = entity.GetVector3Property("angles");
        var roll = angles.Z;
        var yaw = angles.Y - 90f;
        var pitch = angles.X - 90f;

        entity["angles"] = string.Create(CultureInfo.InvariantCulture, $"{roll} {yaw} {pitch}");
        entity["font_size"] = entity.GetFloatProperty("textsize", 20f);
        entity.Remove("textsize");
        entity.Remove("spawnflags");
    }
}
