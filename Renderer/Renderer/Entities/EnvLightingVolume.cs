using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// The baked lighting volumes: cubemaps and light probe volumes. Each registers what it baked with
/// <see cref="Scene.LightingInfo"/>, which binds them to the objects they light.
/// </summary>
/// <remarks>
/// They have to spawn before anything with a model: whether a scene has cubemaps, and which kind, is
/// compiled into every mesh's shaders as it is built.
/// <para>
/// Entity scale does not resize a volume: its baked probe grid is the unscaled box over the voxel size,
/// and objects bound to it by their precomputed handshake only fall inside it at that size.
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
        SceneLightingVolume.AddEnvironmentMap(Scene, KeyValues, RigidTransform, LayerName, isSphere);
    }

    /// <summary>Registers the light probe the volume baked.</summary>
    protected void AddLightProbe()
    {
        SceneLightingVolume.AddLightProbe(Scene, KeyValues, RigidTransform, LayerName, isSphere);
    }
}
