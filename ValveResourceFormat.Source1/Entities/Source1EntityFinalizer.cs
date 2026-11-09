using System.Collections.Frozen;
using System.Globalization;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Source1.Entities;

/// <summary>
/// Rewrites the entities of an entity lump into what Source 2 reads: the names of its classes, inputs and keys, and
/// the entities it has no use for. Both the viewer and the Hammer export read the lump this way.
/// </summary>
internal static class Source1EntityFinalizer
{
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
    /// Rewrites the entities of one lump in place and removes the ones Source 2 does not need.
    /// </summary>
    /// <param name="entities">The entities of a lump.</param>
    public static void Apply(List<EntityLump.Entity> entities)
    {
        var classesByName = GetClassesByName(entities);
        var kept = new List<EntityLump.Entity>(entities.Count);
        var droppedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasEnvironmentLight = false;
        var hasTonemap = false;

        foreach (var entity in entities)
        {
            var className = entity.GetStringProperty("classname") ?? string.Empty;
            var isDropped = false;

            // A dynamic light with no radius is never drawn and lights nothing
            if (IsPointLight(className) && entity.ContainsKey("range") && entity.GetFloatProperty("range") <= 0f)
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

            Convert(entity, className);
            kept.Add(entity);
        }

        ConvertInputs(kept, classesByName);
        RemoveConnectionsToDropped(kept, droppedNames);

        entities.Clear();
        entities.AddRange(kept);
    }

    /// <summary>
    /// Removes the connections that target the name of a dropped entity. A name that a kept entity also has still has a target.
    /// </summary>
    /// <param name="kept">The entities that stay.</param>
    /// <param name="droppedNames">The names of the entities that were dropped, which is changed.</param>
    public static void RemoveConnectionsToDropped(List<EntityLump.Entity> kept, HashSet<string> droppedNames)
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

    // Baked lights are the only point lights that have a range, and it is above zero for all of them
    private static bool IsPointLight(string className)
        => className is "light_omni" or "light_spot";

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

    private static void Convert(EntityLump.Entity entity, string className)
    {
        // Physics props and sprites are written by their own export
        var isSpriteOrPhysicsProp = className == "env_sprite" || entity.ContainsKey(Source1PhysicsPropExport.ClassNameKey);

        if (!isSpriteOrPhysicsProp)
        {
            ConvertMaterialKeys(entity);
            ConvertRenderMode(entity);
            RemoveUnmatchedRenderFx(entity);
        }

        switch (className)
        {
            case "env_tonemap_controller":
                entity["master"] = 1L;
                entity.Remove("spawnflags");
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
