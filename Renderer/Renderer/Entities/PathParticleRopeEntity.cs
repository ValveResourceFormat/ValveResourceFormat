using Microsoft.Extensions.Logging;
using ValveResourceFormat.Renderer.SceneNodes;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>path_particle_rope</c>, <c>path_particle_rope_clientside</c> and Deadlock's <c>citadel_zipline_path</c>:
/// a cable hung along the entity's path nodes, which follows the entity.
/// </summary>
public sealed class PathParticleRopeEntity : BaseEntity
{
    /// <summary>Initializes a rope from its keyvalues.</summary>
    public PathParticleRopeEntity(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <summary>Builds the cable. A rope that has none draws nothing, not even an editor marker.</summary>
    /// <returns>The cable, or <see langword="null"/>.</returns>
    protected override SceneNode? CreateRootNode()
    {
        try
        {
            if (!CableSceneNode.TryCreate(Scene, KeyValues, out var cable))
            {
                EntitySystem.Logger.LogWarning("Skipped degenerate {Classname} '{Target}' at ({Origin})", Classname, TargetName, Origin);
                return null;
            }

            cable.LayerName = Scene.ParticlesLayerName;

            return cable;
        }
        catch (Exception e)
        {
            EntitySystem.Logger.LogError(e, "Failed to setup {Classname} '{Target}'", Classname, TargetName);
            return null;
        }
    }
}
