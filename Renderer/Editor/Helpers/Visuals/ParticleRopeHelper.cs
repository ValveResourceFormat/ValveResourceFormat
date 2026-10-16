using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.SceneNodes;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The rope of a <c>path_particle_rope</c>, hung between the nodes of its path, which stops moving once it
/// has simulated for as long as the entity says.
/// </summary>
public sealed class ParticleRopeHelper : EntityHelper
{
    /// <summary>Initializes the rope of an entity.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    public ParticleRopeHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
    {
    }

    /// <inheritdoc/>
    public override void AddToScene()
    {
        if (CableSceneNode.TryCreate(Owner.Scene, Owner.KeyValues, out var cable))
        {
            // The path nodes are in the space of the entity, which control point 0 follows
            cable.LayerName = Scene.ParticlesLayerName;
            cable.Transform = Owner.Transform;

            // The rope settles for as long as its key says and then holds still
            cable.MaxSimulationTime = GetFloatKey("max_simulation_time");
            Owner.AddNode(cable, followsEntity: true);
        }
    }
}
