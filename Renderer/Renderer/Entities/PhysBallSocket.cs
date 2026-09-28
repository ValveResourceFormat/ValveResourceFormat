using Box3D;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>phys_ballsocket</c>: pins the two bodies together at this entity's origin and leaves them free to
/// turn every way about it, resisted only by the authored <c>friction</c>.
/// </summary>
public sealed class PhysBallSocket : PhysConstraint
{
    /// <summary>Initializes a ball socket from its keyvalues.</summary>
    public PhysBallSocket(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    protected override Joint? CreateJoint(PhysicsWorld world, JointDefinition connection)
    {
        var friction = KeyValues.GetFloatProperty("friction");

        // Friction as a motor that wants no motion and gives up past the friction torque
        var definition = SphericalJointDefinition.Default with
        {
            Base = connection,
            MotorEnabled = true,
            MotorVelocity = Vector3.Zero,
            MaxMotorTorque = FrictionTorque(friction, connection),
        };

        return world.CreateSphericalJoint(definition).AsJoint;
    }
}
