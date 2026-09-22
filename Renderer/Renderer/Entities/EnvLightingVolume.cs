using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// The baked lighting volumes: cubemaps and light probe volumes. Each registers what it baked with its
/// scene's <see cref="Scene.LightingInfo"/>, which binds them to the objects they light.
/// </summary>
/// <remarks>
/// They have to spawn before anything with a model: whether a scene has cubemaps, and of which kind, is
/// compiled into every mesh's shaders as it is built.
/// </remarks>
public abstract class EnvLightingVolume : BaseEntity
{
    /// <summary>Gets the handshake baked objects name this volume by, 0 when it has none.</summary>
    public int HandShake { get; private set; }

    /// <summary>Gets the volume's bounds in its own space.</summary>
    public AABB Bounds { get; private set; }

    /// <summary>Gets the indoor/outdoor level, which decides between overlapping volumes.</summary>
    public int IndoorOutdoorLevel { get; private set; }

    /// <summary>
    /// Gets the volume's placement. The entity scale does not shrink it: its baked probe grid is the
    /// unscaled box over the voxel size, and the objects bound to it by their precomputed handshake only
    /// fall inside it while it keeps that size.
    /// </summary>
    protected Matrix4x4 VolumeTransform => EntityTransformHelper.ToRigidTransformationMatrix(Angles, Origin) * ParentTransform;

    /// <summary>Initializes a lighting volume from its keyvalues.</summary>
    protected EnvLightingVolume(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        var handShakeString = KeyValues.GetStringProperty("handshake");

        if (!int.TryParse(handShakeString, out var handShake))
        {
            handShake = KeyValues.GetInt32Property("handshake");
        }

        HandShake = handShake;
        IndoorOutdoorLevel = KeyValues.GetInt32Property("indoor_outdoor_level");

        if (Classname.Equals("env_cubemap", StringComparison.OrdinalIgnoreCase))
        {
            var radius = KeyValues.GetFloatProperty("influenceradius");
            Bounds = new AABB(-radius, -radius, -radius, radius, radius, radius);
        }
        else
        {
            Bounds = new AABB(KeyValues.GetVector3Property("box_mins"), KeyValues.GetVector3Property("box_maxs"));
        }
    }

    /// <summary>Registers the cubemap the volume baked, if it has one and it is not a custom texture.</summary>
    protected void AddEnvironmentMap()
    {
        var cubemapTextureName = KeyValues.GetStringProperty("cubemaptexture");

        if (cubemapTextureName == null)
        {
            return;
        }

        var envMapTexture = Scene.RendererContext.MaterialLoader.GetTexture(cubemapTextureName, true);
        var arrayIndex = KeyValues.GetInt32Property("array_index");

        var envMap = new SceneEnvMap(Scene, Bounds)
        {
            LayerName = LayerName,
            Transform = VolumeTransform,
            EntityData = Data,
            HandShake = HandShake,
            ArrayIndex = arrayIndex,
            IndoorOutdoorLevel = IndoorOutdoorLevel,
            EdgeFadeDists = KeyValues.GetVector3Property("edge_fade_dists"), // TODO: Not available on all entities
            ProjectionMode = Classname.Equals("env_cubemap", StringComparison.OrdinalIgnoreCase) ? 0 : 1,
            EnvMapTexture = envMapTexture,
            NormalizationSH = SceneEnvMap.CalculateNormalizationSH(envMapTexture.RadianceCoefficients, arrayIndex),
        };

        if (KeyValues.GetStringProperty("customcubemaptexture") == null)
        {
            Scene.LightingInfo.AddEnvironmentMap(envMap);
        }
    }

    /// <summary>Registers the light probe the volume baked.</summary>
    protected void AddLightProbe()
    {
        var materialLoader = Scene.RendererContext.MaterialLoader;
        var lightProbeTextureName = KeyValues.GetStringProperty("lightprobetexture");

        var lightProbe = new SceneLightProbe(Scene, Bounds)
        {
            LayerName = LayerName,
            Transform = VolumeTransform,
            EntityData = Data,
            HandShake = HandShake,
            Irradiance = lightProbeTextureName != null ? materialLoader.GetTexture(lightProbeTextureName, srgbRead: true) : null,
            IndoorOutdoorLevel = IndoorOutdoorLevel,
            VoxelSize = KeyValues.GetFloatProperty("voxel_size"),
        };

        var dliName = KeyValues.GetStringProperty("lightprobetexture_dli");
        var dlsName = KeyValues.GetStringProperty("lightprobetexture_dls");
        var dlsdName = KeyValues.GetStringProperty("lightprobetexture_dlshd");

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

        Scene.LightingInfo.LightProbeType = KeyValues.ContainsKey("light_probe_atlas_x")
            ? LightProbeType.ProbeAtlas
            : LightProbeType.IndividualProbes;

        if (dlsdName != null)
        {
            lightProbe.DirectLightShadows = materialLoader.GetTexture(dlsdName);
            lightProbe.DirectLightShadows.SetWrapMode(RsTextureAddressMode.Clamp);

            lightProbe.AtlasSize = new Vector3(
                KeyValues.GetFloatProperty("light_probe_size_x"),
                KeyValues.GetFloatProperty("light_probe_size_y"),
                KeyValues.GetFloatProperty("light_probe_size_z")
            );

            lightProbe.AtlasOffset = new Vector3(
                KeyValues.GetFloatProperty("light_probe_atlas_x"),
                KeyValues.GetFloatProperty("light_probe_atlas_y"),
                KeyValues.GetFloatProperty("light_probe_atlas_z")
            );
        }

        Scene.LightingInfo.AddProbe(lightProbe);
    }
}
