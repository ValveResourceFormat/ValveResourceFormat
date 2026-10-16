using Box3D;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>logic_collision_pair</c>: switches collision off, or back on, between the two bodies
/// <c>attach1</c> and <c>attach2</c> name. Maps use it for parts of one object that overlap by design -
/// a toilet handle sunk into its cistern - so they do not shove each other apart.
/// </summary>
public sealed class LogicCollisionPair : BaseEntity
{
    // A filter joint does nothing but stop its two bodies colliding
    private Joint filter;
    private bool hasFilter;

    /// <summary>Initializes a collision pair from its keyvalues.</summary>
    public LogicCollisionPair(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <summary>Gets whether the two bodies currently pass through each other.</summary>
    public bool CollisionsDisabled => hasFilter;

    // In Activate, as the attached entities may be authored after this one and need their bodies
    /// <inheritdoc/>
    public override void Activate()
    {
        // The key reads from the pair's point of view: disabled means the pair's collisions are
        if (KeyValues.GetBooleanProperty("startdisabled"))
        {
            DisableCollisions();
        }
    }

    private void DisableCollisions()
    {
        if (hasFilter || EntitySystem.PhysicsOrNull is not { } physics)
        {
            return;
        }

        if (!PhysConstraint.TryResolveAttachment(this, physics, "attach1", out var body1)
            || !PhysConstraint.TryResolveAttachment(this, physics, "attach2", out var body2)
            || body1.Equals(body2))
        {
            return;
        }

        var definition = FilterJointDefinition.Default with
        {
            Base = JointDefinition.Connect(body1, body2, default, default),
        };

        filter = physics.World.CreateFilterJoint(definition).AsJoint;
        hasFilter = true;
    }

    private void EnableCollisions()
    {
        if (!hasFilter)
        {
            return;
        }

        if (filter.IsValid)
        {
            filter.Destroy(wakeBodies: true);
        }

        hasFilter = false;
    }

    [EntityInput("DisableCollisions")]
    private void InputDisableCollisions(EntityInputData data) => DisableCollisions();

    [EntityInput("EnableCollisions")]
    private void InputEnableCollisions(EntityInputData data) => EnableCollisions();

    /// <inheritdoc/>
    protected override void OnRemove()
    {
        base.OnRemove();

        EnableCollisions();
    }
}
