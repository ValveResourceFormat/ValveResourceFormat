using ValveResourceFormat.Renderer.SceneEnvironment;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// The baked lighting volumes: cubemaps and light probe volumes. Each registers what it baked with
/// <see cref="Scene.LightingInfo"/>, which binds them to the objects they light.
/// </summary>
/// <remarks>
/// They have to spawn before anything with a model: whether a scene has cubemaps, and which kind, is
/// compiled into every mesh's shaders as it is built.
/// <para>
/// Entity scale does not resize a volume, see <see cref="SceneLightingVolume"/>.
/// </para>
/// </remarks>
public abstract class EnvLightingVolume : BaseEntity
{
    private readonly bool isSphere;

    /// <summary>Initializes a lighting volume from its keyvalues.</summary>
    /// <param name="system">The entity system the volume belongs to.</param>
    /// <param name="spawnInfo">The volume's keyvalues and placement.</param>
    /// <param name="isSphere">Whether the volume is a sphere of <c>influenceradius</c> rather than a box.</param>
    protected EnvLightingVolume(EntitySystem system, EntitySpawnInfo spawnInfo, bool isSphere) : base(system, spawnInfo)
    {
        this.isSphere = isSphere;
    }

    /// <summary>Registers the cubemap the volume baked, if it has one and it is not a custom texture.</summary>
    protected void AddEnvironmentMap()
    {
        if (SceneLightingVolume.AddEnvironmentMap(Scene, KeyValues, RigidTransform, LayerName, isSphere) is { } envMap)
        {
            envMap.EntityData = Data;
        }
    }

    /// <summary>Registers the light probe the volume baked.</summary>
    /// <param name="fadesAtEdges">Whether the probe fades out over the edge fade distances, otherwise it ends at its box.</param>
    protected void AddLightProbe(bool fadesAtEdges)
    {
        SceneLightingVolume.AddLightProbe(Scene, KeyValues, RigidTransform, LayerName, isSphere, fadesAtEdges).EntityData = Data;
    }
}
