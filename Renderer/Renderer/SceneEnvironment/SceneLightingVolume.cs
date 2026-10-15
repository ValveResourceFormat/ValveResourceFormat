using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.SceneEnvironment;

/// <summary>
/// Adds the baked lighting of an <c>env_cubemap</c> or <c>env_light_probe_volume</c> to a scene, from the
/// entity's keyvalues: its environment map, and its light probe. Compiled maps and source maps both use it.
/// </summary>
public static class SceneLightingVolume
{
    private readonly record struct Volume(int HandShake, int IndoorOutdoorLevel, AABB Bounds, Vector3 EdgeFadeDists);

    private static Volume ReadVolume(EntityLump.Entity entity, bool isSphere)
    {
        // The handshake baked objects name this volume by, 0 when it has none
        if (!int.TryParse(entity.GetStringProperty("handshake"), out var handShake))
        {
            handShake = entity.GetInt32Property("handshake");
        }

        AABB bounds;

        if (isSphere)
        {
            var radius = entity.GetFloatProperty("influenceradius");
            bounds = new AABB(-radius, -radius, -radius, radius, radius, radius);
        }
        else
        {
            bounds = new AABB(entity.GetVector3Property("box_mins"), entity.GetVector3Property("box_maxs"));
        }

        // TODO: Not available on all entities
        var edgeFadeDists = entity.GetVector3Property("edge_fade_dists");

        return new Volume(handShake, entity.GetInt32Property("indoor_outdoor_level"), bounds, edgeFadeDists);
    }

    /// <summary>Adds the environment map a lighting volume entity names to the lighting of a scene.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="entity">The keyvalues of the entity.</param>
    /// <param name="transform">Where the entity is placed, without its scale.</param>
    /// <param name="layerName">Visibility layer of the entity.</param>
    /// <param name="isSphere">Whether the volume is a sphere of <c>influenceradius</c> rather than a box.</param>
    /// <returns>The environment map, or <see langword="null"/> when the entity names no texture.</returns>
    public static SceneEnvMap? AddEnvironmentMap(Scene scene, EntityLump.Entity entity, in Matrix4x4 transform, string? layerName, bool isSphere)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(entity);

        var volume = ReadVolume(entity, isSphere);

        var cubemapTextureName = entity.GetStringProperty("cubemaptexture");

        if (cubemapTextureName == null)
        {
            return null;
        }

        var envMapTexture = scene.RendererContext.MaterialLoader.GetTexture(cubemapTextureName, true);
        var arrayIndex = entity.GetInt32Property("array_index");

        var envMap = new SceneEnvMap(scene, volume.Bounds)
        {
            LayerName = layerName,
            Transform = transform,
            EntityData = entity,
            HandShake = volume.HandShake,
            ArrayIndex = arrayIndex,
            IndoorOutdoorLevel = volume.IndoorOutdoorLevel,
            EdgeFadeDists = volume.EdgeFadeDists,
            ProjectionMode = isSphere ? 0 : 1,
            EnvMapTexture = envMapTexture,
            NormalizationSH = SceneEnvMap.CalculateNormalizationSH(envMapTexture.RadianceCoefficients, arrayIndex),
        };

        if (entity.GetStringProperty("customcubemaptexture") == null)
        {
            scene.LightingInfo.AddEnvironmentMap(envMap);
        }

        return envMap;
    }

    /// <summary>Adds the light probe a lighting volume entity names to the lighting of a scene.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="entity">The keyvalues of the entity.</param>
    /// <param name="transform">Where the entity is placed, without its scale.</param>
    /// <param name="layerName">Visibility layer of the entity.</param>
    /// <param name="isSphere">Whether the volume is a sphere of <c>influenceradius</c> rather than a box.</param>
    /// <returns>The light probe.</returns>
    public static SceneLightProbe AddLightProbe(Scene scene, EntityLump.Entity entity, in Matrix4x4 transform, string? layerName, bool isSphere)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(entity);

        var volume = ReadVolume(entity, isSphere);

        var materialLoader = scene.RendererContext.MaterialLoader;
        var lightProbeTextureName = entity.GetStringProperty("lightprobetexture");

        var lightProbe = new SceneLightProbe(scene, volume.Bounds)
        {
            LayerName = layerName,
            Transform = transform,
            EntityData = entity,
            HandShake = volume.HandShake,
            Irradiance = lightProbeTextureName != null ? materialLoader.GetTexture(lightProbeTextureName, srgbRead: true) : null,
            IndoorOutdoorLevel = volume.IndoorOutdoorLevel,
            EdgeFadeDists = volume.EdgeFadeDists,
            VoxelSize = entity.GetFloatProperty("voxel_size"),
        };

        var dliName = entity.GetStringProperty("lightprobetexture_dli");
        var dlsName = entity.GetStringProperty("lightprobetexture_dls");
        var dlsdName = entity.GetStringProperty("lightprobetexture_dlshd");

        if (dlsName != null)
        {
            lightProbe.DirectLightScalars = materialLoader.GetTexture(dlsName);
            lightProbe.DirectLightScalars.SetWrapMode(RsTextureAddressMode.Clamp);
        }

        if (dliName != null)
        {
            lightProbe.DirectLightIndices = materialLoader.GetTexture(dliName);
            lightProbe.DirectLightIndices.SetFiltering(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            lightProbe.DirectLightIndices.SetWrapMode(RsTextureAddressMode.Clamp);
        }

        scene.LightingInfo.LightProbeType = entity.ContainsKey("light_probe_atlas_x")
            ? LightProbeType.ProbeAtlas
            : LightProbeType.IndividualProbes;

        if (dlsdName != null)
        {
            lightProbe.DirectLightShadows = materialLoader.GetTexture(dlsdName);
            lightProbe.DirectLightShadows.SetWrapMode(RsTextureAddressMode.Clamp);

            lightProbe.AtlasSize = new Vector3(
                entity.GetFloatProperty("light_probe_size_x"),
                entity.GetFloatProperty("light_probe_size_y"),
                entity.GetFloatProperty("light_probe_size_z")
            );

            lightProbe.AtlasOffset = new Vector3(
                entity.GetFloatProperty("light_probe_atlas_x"),
                entity.GetFloatProperty("light_probe_atlas_y"),
                entity.GetFloatProperty("light_probe_atlas_z")
            );
        }

        scene.LightingInfo.AddProbe(lightProbe);

        return lightProbe;
    }
}
