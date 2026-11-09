using System.Collections.Frozen;
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

    // Ropes are written as the models the converter bakes them into, in every game
    private static readonly HashSet<string> RopeClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "keyframe_rope",
        "move_rope",
    };

    private static readonly string[] MaterialKeys =
    [
        "material",
        "overlaymaterial",
        "detailmaterial",
    ];

    private static readonly HashSet<string> TonemapClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "env_tonemap_controller",
        "env_tonemap_controller_ghost",
        "env_tonemap_controller_infected",
    };

    private const string FogClass = "env_fog_controller";

    // Inputs the tonemap controller of Source 2 has under other names
    private static readonly FrozenDictionary<string, string> TonemapInputRenames = new Dictionary<string, string>
    {
        ["SetAutoExposureMin"] = "SetMinExposure",
        ["SetAutoExposureMax"] = "SetMaxExposure",
        ["UseDefaultAutoExposure"] = "Disable",
        ["SetTonemapPercentBrightPixels"] = "SetPercentBrightPixels",
        ["SetTonemapPercentTarget"] = "SetPercentTarget",
        ["SetTonemapMinAvgLum"] = "SetMinAverageLuminosity",
        ["SetTonemapRate"] = "SetRate",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    // Inputs the tonemap controller of Source 2 has no counterpart for
    private static readonly HashSet<string> TonemapRemovedInputs = new(StringComparer.OrdinalIgnoreCase)
    {
        "SetBloomExponent",
        "BlendTonemapScale",
    };

    private static readonly FrozenDictionary<string, string> FogInputRenames = new Dictionary<string, string>
    {
        ["SetStartDist"] = "SetFogStartDistance",
        ["SetEndDist"] = "SetFogEndDistance",
        ["SetColor"] = "SetFogColor",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

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
        var hasTonemap = false;

        foreach (var entity in entities)
        {
            var className = entity.GetStringProperty("classname") ?? string.Empty;
            var isDropped = RopeClasses.Contains(className) || (targetGame == Game.Csgo && ExcludedClasses.Contains(className));

            // A dynamic light with no radius is never drawn and lights nothing
            if (!isDropped && IsPointLight(className) && entity.ContainsKey("range") && entity.GetFloatProperty("range") <= 0f)
            {
                isDropped = true;
            }

            // Only one light_environment lights a map
            if (!isDropped && className == "light_environment")
            {
                isDropped = hasEnvironmentLight;
                hasEnvironmentLight = true;
            }

            // The first tonemap controller is the master one
            if (!isDropped && className == "env_tonemap_controller")
            {
                isDropped = hasTonemap;
                hasTonemap = true;
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

        ConvertInputs(converted, GetClassesByName(entities));
        RemoveConnectionsToDropped(converted, droppedNames);

        return converted;
    }

    private static Dictionary<string, string> GetClassesByName(List<EntityLump.Entity> entities)
    {
        var classesByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in entities)
        {
            var name = entity.GetStringProperty("targetname");
            var className = entity.GetStringProperty("classname");

            if (!string.IsNullOrEmpty(name) && className != null)
            {
                classesByName.TryAdd(name, className);
            }
        }

        return classesByName;
    }

    // The inputs of a controller are named by the class of the entity they target
    private static void ConvertInputs(List<EntityLump.Entity> entities, Dictionary<string, string> classesByName)
    {
        foreach (var entity in entities)
        {
            var connections = entity.Connections;

            if (connections == null)
            {
                continue;
            }

            for (var i = connections.Count - 1; i >= 0; i--)
            {
                var connection = connections[i];

                if (!classesByName.TryGetValue(connection.TargetName, out var targetClass))
                {
                    continue;
                }

                if (TonemapClasses.Contains(targetClass) && TonemapRemovedInputs.Contains(connection.InputName))
                {
                    connections.RemoveAt(i);
                    continue;
                }

                var renames = GetInputRenames(targetClass);

                if (renames != null && renames.TryGetValue(connection.InputName, out var inputName))
                {
                    connections[i] = WithInputName(connection, inputName);
                }
            }
        }
    }

    private static FrozenDictionary<string, string>? GetInputRenames(string className)
    {
        if (TonemapClasses.Contains(className))
        {
            return TonemapInputRenames;
        }

        if (className.Equals(FogClass, StringComparison.OrdinalIgnoreCase))
        {
            return FogInputRenames;
        }

        return null;
    }

    private static EntityLump.Connection WithInputName(EntityLump.Connection connection, string inputName)
    {
        return new EntityLump.Connection
        {
            SourceEntity = connection.SourceEntity,
            OutputName = connection.OutputName,
            InputName = inputName,
            TargetName = connection.TargetName,
            OverrideParam = connection.OverrideParam,
            Delay = connection.Delay,
            TimesToFire = connection.TimesToFire,
            TargetType = connection.TargetType,
            ParamMap = connection.ParamMap,
        };
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

        // Physics props and sprites are written by their own export
        var isSpriteOrPhysicsProp = className == "env_sprite" || entity.ContainsKey(Source1PhysicsPropExport.ClassNameKey);

        if (!isSpriteOrPhysicsProp)
        {
            ConvertMaterialKeys(entity);
            ConvertRenderMode(entity);
            RemoveUnmatchedRenderFx(entity);
        }

        if (targetGame == Game.Csgo)
        {
            RemoveLeftoverKeys(entity, className);
        }

        switch (className)
        {
            case "func_brush" or "func_door" when targetGame == Game.Csgo:
                entity["lightmapstatic"] = 1L;
                break;
            case "env_tonemap_controller":
                entity["master"] = 1L;
                entity.Remove("spawnflags");
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

    // Baked lights are the only point lights that have a range, and it is above zero for all of them
    private static bool IsPointLight(string className)
        => className is "light_omni" or "light_spot";

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

    // The breakable brushes use the same key for the kind of debris they break into, which is a number
    private static void ConvertMaterialKeys(EntityLump.Entity entity)
    {
        foreach (var key in MaterialKeys)
        {
            var material = entity.GetStringProperty(key);

            if (string.IsNullOrWhiteSpace(material) || material.EndsWith(".vmat", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            entity[key] = Source1Paths.ToMaterialPath(material);
        }
    }

    // The numbers of the two engines differ, and Source 2 only has normal, alpha blended and hidden entities. The modes
    // that blend by the alpha of the entity become alpha blended, and any other mode draws normally.
    private static void ConvertRenderMode(EntityLump.Entity entity)
    {
        if (!entity.ContainsKey("rendermode"))
        {
            return;
        }

        var name = (RenderMode)entity.GetIntegerProperty("rendermode") switch
        {
            RenderMode.Normal => "kRenderNormal",
            RenderMode.TransColor or RenderMode.TransTexture or RenderMode.TransAlpha => "kRenderTransAlpha",
            _ => null,
        };

        if (name == null)
        {
            entity.Remove("rendermode");
            return;
        }

        entity["rendermode"] = name;
    }

    // Effects up to this number mean the same in both engines, and later ones are other effects or do not exist
    private static void RemoveUnmatchedRenderFx(EntityLump.Entity entity)
    {
        const long LastMatchingRenderFx = 13;

        if (entity.ContainsKey("renderfx") && entity.GetIntegerProperty("renderfx") > LastMatchingRenderFx)
        {
            entity.Remove("renderfx");
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
