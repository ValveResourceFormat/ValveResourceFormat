using Box3D;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary><c>logic_collision_pair</c>: toggles collision between the <c>attach1</c> and <c>attach2</c> bodies.</summary>
public sealed class LogicCollisionPair : BaseEntity
{
    private Joint filter;
    private bool hasFilter;

    /// <summary>Initializes a collision pair from its keyvalues.</summary>
    public LogicCollisionPair(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <summary>Gets whether the two bodies currently pass through each other.</summary>
    public bool CollisionsDisabled => hasFilter;

    // Not in Spawn, as the attached entities may be authored after this one
    /// <inheritdoc/>
    public override void Activate()
    {
        // Disabling the entity means disabling collisions
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
