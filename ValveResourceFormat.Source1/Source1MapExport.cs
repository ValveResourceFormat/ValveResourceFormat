using System.Globalization;
using ValveResourceFormat.IO.ContentFormats.ValveMap;
using ValveResourceFormat.Source1.Bsp;
using ValveResourceFormat.Source1.Entities;
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

    private const string CombinedVolumeClass = "env_combined_light_probe_volume";
    private const float MinCubemapRadius = 128f;
    private const float CubemapEdgeFadeFraction = 0.25f;

    private static readonly string[] BakedTextureKeys = ["cubemaptexture", "lightprobetexture", "lightprobetexture_dli", "lightprobetexture_dls"];

    /// <summary>Regroups the entities that were split up for the engine.</summary>
    /// <param name="entities">The converted entities.</param>
    /// <param name="volumeMap">
    /// The map, when the target game has <c>env_combined_light_probe_volume</c>: the cube map samples then become volumes fitted
    /// to their rooms, below one volume that covers the world. Without it the cube maps stay <c>env_cubemap</c>.
    /// </param>
    public static List<EntityLump.Entity> Regroup(List<EntityLump.Entity> entities, BspFile? volumeMap = null)
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

        if (volumeMap != null)
        {
            return RegroupAsVolumes(entities, volumeMap, cubemaps, voxelSize);
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

                var volume = Source1EntityCopy.Copy(entity, RegroupKey);
                volume["origin"] = Format(probeCenter);
                volume["box_mins"] = Format(-probeHalfSize);
                volume["box_maxs"] = Format(probeHalfSize);
                volume["voxel_size"] = Format(voxelSize);
                regrouped.Add(volume);
            }
            else
            {
                var cubemap = Source1EntityCopy.Copy(entity, RegroupKey);

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

    // The light probe tiles are dropped, the volume below all others covers their part. A map without samples
    // reflects the default cube map everywhere, which the covering volume then carries.
    private static List<EntityLump.Entity> RegroupAsVolumes(
        List<EntityLump.Entity> entities,
        BspFile bsp,
        List<EntityLump.Entity> cubemaps,
        float voxelSize)
    {
        var hasSamples = bsp.Cubemaps.Length > 0;
        var origins = new List<Vector3>();

        if (hasSamples)
        {
            foreach (var cubemap in cubemaps)
            {
                origins.Add(cubemap.GetVector3Property("origin"));
            }
        }

        var volumes = Source1CubemapVolumes.Fit(bsp, origins);
        var regrouped = new List<EntityLump.Entity>(entities.Count);
        var volumeIndex = 0;

        foreach (var entity in entities)
        {
            if (!entity.TryGetValue(RegroupKey, out _))
            {
                regrouped.Add(entity);
                continue;
            }

            var className = entity.GetStringProperty("classname");

            if (className == "env_cubemap" && volumeIndex < volumes.Count)
            {
                regrouped.Add(CreateVolume(entity, volumes[volumeIndex], voxelSize));
                volumeIndex++;
            }
            else if (className is not ("env_cubemap" or "env_light_probe_volume"))
            {
                regrouped.Add(Source1EntityCopy.Copy(entity, RegroupKey));
            }
        }

        if (bsp.Models.Length > 0 && entities.Count > 0)
        {
            EntityLump.Entity? defaultCubemap = null;

            if (!hasSamples && cubemaps.Count > 0)
            {
                defaultCubemap = cubemaps[0];
            }

            regrouped.Add(CreateWorldVolume(entities[0].ParentLump, bsp.Models[0], defaultCubemap, voxelSize));
        }

        return regrouped;
    }

    private static EntityLump.Entity CreateVolume(EntityLump.Entity cubemap, Source1CubemapVolumes.Volume volume, float voxelSize)
    {
        var entity = new EntityLump.Entity { ParentLump = cubemap.ParentLump };
        entity["classname"] = CombinedVolumeClass;
        entity["origin"] = Format(volume.Origin);
        entity["box_mins"] = Format(volume.Mins - volume.Origin);
        entity["box_maxs"] = Format(volume.Maxs - volume.Origin);
        entity["edge_fade_dists"] = Format(volume.EdgeFade);
        entity["indoor_outdoor_level"] = (long)volume.Priority;
        SetVoxelSize(entity, voxelSize);

        if (cubemap.TryGetValue("cubemaptexture", out var texture))
        {
            entity["cubemaptexture"] = texture;
        }

        return entity;
    }

    private static EntityLump.Entity CreateWorldVolume(EntityLump lump, BspModel world, EntityLump.Entity? defaultCubemap, float voxelSize)
    {
        var center = (world.Min + world.Max) * 0.5f;
        var halfSize = (world.Max - world.Min) * 0.5f;

        var entity = new EntityLump.Entity { ParentLump = lump };
        entity["classname"] = CombinedVolumeClass;
        entity["origin"] = Format(center);
        entity["box_mins"] = Format(-halfSize);
        entity["box_maxs"] = Format(halfSize);
        entity["indoor_outdoor_level"] = (long)Source1CubemapVolumes.FallbackPriority;
        entity["flood_fill"] = 0L;
        entity["voxelize"] = 0L;
        SetVoxelSize(entity, voxelSize);

        if (defaultCubemap != null && defaultCubemap.TryGetValue("cubemaptexture", out var texture))
        {
            entity["cubemaptexture"] = texture;
        }

        return entity;
    }

    private static void SetVoxelSize(EntityLump.Entity entity, float voxelSize)
    {
        if (voxelSize > 0f)
        {
            entity["voxel_size"] = Format(voxelSize);
        }
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

    private static string Format(Vector3 value)
        => string.Create(CultureInfo.InvariantCulture, $"{value.X} {value.Y} {value.Z}");

    private static string Format(float value)
        => string.Create(CultureInfo.InvariantCulture, $"{value}");
}
