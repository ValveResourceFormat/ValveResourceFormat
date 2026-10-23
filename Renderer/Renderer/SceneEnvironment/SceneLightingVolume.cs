using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.SceneEnvironment;

/// <summary>
/// Adds the baked lighting of a cubemap or light probe volume entity to a scene, from the entity's keyvalues: its
/// environment map, and its light probe. Compiled maps and source maps both use it, and each sets the
/// <see cref="SceneNode.EntityData"/> of the nodes to the entity they belong to.
/// </summary>
/// <remarks>
/// Entity scale does not resize a volume: its baked probe grid is the unscaled box over the voxel size,
/// and objects bound to it by their precomputed handshake only fall inside it at that size.
/// </remarks>
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
        Vector3 edgeFadeDists;

        if (isSphere)
        {
            var radius = entity.GetFloatProperty("influenceradius");
            bounds = new AABB(-radius, -radius, -radius, radius, radius, radius);
            edgeFadeDists = new Vector3(entity.GetFloatProperty("edge_fade_dist"));
        }
        else
        {
            bounds = new AABB(entity.GetVector3Property("box_mins"), entity.GetVector3Property("box_maxs"));
            edgeFadeDists = entity.GetVector3Property("edge_fade_dists");
        }

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

        if (entity.GetStringProperty("cubemaptexture") is not { Length: > 0 } cubemapTextureName)
        {
            return null;
        }

        var volume = ReadVolume(entity, isSphere);
        var envMapTexture = scene.RendererContext.MaterialLoader.GetTexture(cubemapTextureName, true);
        var arrayIndex = entity.GetInt32Property("array_index");

        var envMap = new SceneEnvMap(scene, volume.Bounds)
        {
            LayerName = layerName,
            Transform = transform,
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
    /// <param name="fadesAtEdges">Whether the probe fades out over the edge fade distances, otherwise it ends at its box.</param>
    /// <returns>The light probe.</returns>
    public static SceneLightProbe AddLightProbe(Scene scene, EntityLump.Entity entity, in Matrix4x4 transform, string? layerName, bool isSphere, bool fadesAtEdges)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(entity);

        var volume = ReadVolume(entity, isSphere);
        var materialLoader = scene.RendererContext.MaterialLoader;

        RenderTexture? LoadTexture(string key, bool srgbRead = false)
            => entity.GetStringProperty(key) is { Length: > 0 } name ? materialLoader.GetTexture(name, srgbRead) : null;

        var lightProbe = new SceneLightProbe(scene, volume.Bounds)
        {
            LayerName = layerName,
            Transform = transform,
            HandShake = volume.HandShake,
            Irradiance = LoadTexture("lightprobetexture", srgbRead: true),
            IndoorOutdoorLevel = volume.IndoorOutdoorLevel,
            EdgeFadeDists = fadesAtEdges ? volume.EdgeFadeDists : Vector3.Zero,
            VoxelSize = entity.GetFloatProperty("voxel_size"),

            AtlasSize = new Vector3(
                entity.GetFloatProperty("light_probe_size_x"),
                entity.GetFloatProperty("light_probe_size_y"),
                entity.GetFloatProperty("light_probe_size_z")),
            AtlasOffset = new Vector3(
                entity.GetFloatProperty("light_probe_atlas_x"),
                entity.GetFloatProperty("light_probe_atlas_y"),
                entity.GetFloatProperty("light_probe_atlas_z")),
        };

        // A texture the volume was baked without reads as no direct light and no shadow
        if (LoadTexture("lightprobetexture_dls") is { } scalars)
        {
            lightProbe.DirectLightScalars = scalars;
            scalars.SetWrapMode(RsTextureAddressMode.Clamp);
        }
        else
        {
            lightProbe.DirectLightScalars = materialLoader.GetDefaultBlackVolume();
        }

        if (LoadTexture("lightprobetexture_dli") is { } indices)
        {
            lightProbe.DirectLightIndices = indices;
            indices.SetFiltering(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            indices.SetWrapMode(RsTextureAddressMode.Clamp);
        }
        else
        {
            lightProbe.DirectLightIndices = materialLoader.GetDefaultBlackVolume();
        }

        if (LoadTexture("lightprobetexture_dlshd") is { } shadows)
        {
            lightProbe.DirectLightShadows = shadows;
            shadows.SetWrapMode(RsTextureAddressMode.Clamp);
        }
        else
        {
            lightProbe.DirectLightShadows = materialLoader.GetDefaultBlackVolume();
        }

        scene.LightingInfo.AddProbe(lightProbe);

        return lightProbe;
    }
}
