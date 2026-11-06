using System.Globalization;
using ValveResourceFormat.IO.ContentFormats.ValveMap;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Source1;

/// <summary>
/// What a converted map needs on export to a Hammer map beyond the converted entity lump:
/// entities split up for the engine are written as one, keys the map compiler requires are added,
/// and the models and textures that are not game files are carried.
/// </summary>
internal static class Source1MapExport
{
    /// <summary>
    /// Marks entities that were split up for the engine and are written as a single entity covering all of them:
    /// the <c>env_light_probe_volume</c> tiles of one map become one volume, and each <c>env_cubemap</c> reaches only
    /// as far as its nearest neighbour.
    /// </summary>
    public const string RegroupKey = "vrf_export_regroup";

    private const float MinCubemapRadius = 128f;
    private const float CubemapEdgeFadeFraction = 0.25f;

    private static readonly string[] BakedTextureKeys = ["cubemaptexture", "lightprobetexture", "lightprobetexture_dli", "lightprobetexture_dls"];

    public static List<EntityLump.Entity> Regroup(List<EntityLump.Entity> entities)
    {
        var probeMin = new Vector3(float.MaxValue);
        var probeMax = new Vector3(float.MinValue);
        var voxelSize = 0f;
        EntityLump.Entity? firstProbeVolume = null;
        var cubemaps = new List<EntityLump.Entity>();

        foreach (var entity in entities)
        {
            if (!entity.TryGetValue(RegroupKey, out _))
            {
                continue;
            }

            var className = entity.GetStringProperty("classname");

            if (className == "env_light_probe_volume")
            {
                var origin = entity.GetVector3Property("origin");
                probeMin = Vector3.Min(probeMin, origin + entity.GetVector3Property("box_mins"));
                probeMax = Vector3.Max(probeMax, origin + entity.GetVector3Property("box_maxs"));
                voxelSize = MathF.Max(voxelSize, entity.GetFloatProperty("voxel_size"));
                firstProbeVolume ??= entity;
            }
            else if (className == "env_cubemap")
            {
                cubemaps.Add(entity);
            }
        }

        var probeCenter = (probeMin + probeMax) * 0.5f;
        var probeHalfSize = (probeMax - probeMin) * 0.5f;
        var regrouped = new List<EntityLump.Entity>(entities.Count);

        foreach (var entity in entities)
        {
            if (!entity.TryGetValue(RegroupKey, out _))
            {
                regrouped.Add(entity);
                continue;
            }

            var className = entity.GetStringProperty("classname");

            if (className == "env_light_probe_volume")
            {
                if (entity != firstProbeVolume)
                {
                    continue;
                }

                var volume = CopyWithoutRegroupKey(entity);
                volume["origin"] = Format(probeCenter);
                volume["box_mins"] = Format(-probeHalfSize);
                volume["box_maxs"] = Format(probeHalfSize);
                volume["voxel_size"] = Format(voxelSize);
                regrouped.Add(volume);
            }
            else
            {
                var cubemap = CopyWithoutRegroupKey(entity);

                if (className == "env_cubemap" && TryGetNearestDistance(entity, cubemaps, out var nearest))
                {
                    var radius = MathF.Max(nearest, MinCubemapRadius);
                    cubemap["influenceradius"] = Format(radius);
                    cubemap["edge_fade_dist"] = Format(radius * CubemapEdgeFadeFraction);
                }

                regrouped.Add(cubemap);
            }
        }

        return regrouped;
    }

    public static void AddRequiredProperties(string className, EditGameClassProps properties)
    {
        if (className == "env_light_probe_volume")
        {
            properties.Remove("lightprobetexture_dli");
            properties.Remove("lightprobetexture_dls");
        }

        if (className is not ("scripted_sequence" or "func_physbox"))
        {
            return;
        }

        if (!properties.ContainsKey("spawnflags"))
        {
            properties["spawnflags"] = "0";
        }

        if (className != "scripted_sequence")
        {
            return;
        }

        properties.TryGetValue("m_fmoveto", out var moveTo);
        properties.Remove("m_fmoveto");
        properties["m_fMoveTo"] = moveTo ?? "0";
    }

    /// <summary>
    /// Lists the models, textures, post processing and sky map of an entity lump, except the baked light probe and cube map textures,
    /// which Hammer bakes again.
    /// </summary>
    public static List<string> GetResources(Resource entityLump)
    {
        var resources = new List<string>();

        if (entityLump.ExternalReferences is not { } references || entityLump.DataBlock is not EntityLump lump)
        {
            return resources;
        }

        var baked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in lump.GetEntities())
        {
            foreach (var key in BakedTextureKeys)
            {
                var texture = entity.GetStringProperty(key);

                if (texture != null)
                {
                    baked.Add(texture);
                }
            }
        }

        foreach (var reference in references.ResourceRefInfoList)
        {
            var name = reference.Name;
            var isResource = name.EndsWith(".vmdl", StringComparison.Ordinal)
                || name.EndsWith(".vtex", StringComparison.Ordinal)
                || name.EndsWith(".vpost", StringComparison.Ordinal);

            if (isResource && !baked.Contains(name))
            {
                resources.Add(name);
            }
        }

        foreach (var entity in lump.GetEntities())
        {
            if (entity.GetStringProperty("classname") != "skybox_reference")
            {
                continue;
            }

            var skyMap = entity.GetStringProperty("targetmapname");

            if (!string.IsNullOrEmpty(skyMap))
            {
                resources.Add(skyMap);
            }
        }

        return resources;
    }

    private static bool TryGetNearestDistance(EntityLump.Entity cubemap, List<EntityLump.Entity> cubemaps, out float nearest)
    {
        nearest = float.MaxValue;
        var origin = cubemap.GetVector3Property("origin");

        foreach (var other in cubemaps)
        {
            if (other != cubemap)
            {
                nearest = MathF.Min(nearest, Vector3.Distance(origin, other.GetVector3Property("origin")));
            }
        }

        return nearest != float.MaxValue;
    }

    private static EntityLump.Entity CopyWithoutRegroupKey(EntityLump.Entity entity)
    {
        var copy = new EntityLump.Entity { ParentLump = entity.ParentLump };

        foreach (var (key, value) in entity.Children)
        {
            if (key != RegroupKey)
            {
                copy[key] = value;
            }
        }

        return copy;
    }

    private static string Format(Vector3 value)
        => string.Create(CultureInfo.InvariantCulture, $"{value.X} {value.Y} {value.Z}");

    private static string Format(float value)
        => string.Create(CultureInfo.InvariantCulture, $"{value}");
}
