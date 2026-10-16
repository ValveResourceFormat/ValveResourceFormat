using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.SceneNodes;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The rope of a <c>path_particle_rope</c>, hung between the nodes of its path.
/// </summary>
public sealed class ParticleRopeHelper : EntityHelper
{
    /// <inheritdoc/>
    public override bool IsVisual => true;

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
            cable.LayerName = Scene.ParticlesLayerName;
            Owner.AddNode(cable);
        }
    }
}
