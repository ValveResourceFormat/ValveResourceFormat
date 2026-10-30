using Box3D;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary><c>info_constraint_anchor</c>: a point constraints attach to, on the body of its move parent or else the world.</summary>
public sealed class InfoConstraintAnchor : BaseEntity
{
    /// <summary>Initializes an anchor from its keyvalues.</summary>
    public InfoConstraintAnchor(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    internal bool TryGetConstraintBody(PhysicsSimulation physics, out Body body)
    {
        if (MoveParent is BaseModelEntity model)
        {
            return model.TryGetConstraintBody(WorldOrigin, out body);
        }

        body = physics.WorldAnchor;
        return true;
    }
}
