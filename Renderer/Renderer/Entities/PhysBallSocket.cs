using Box3D;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary><c>phys_ballsocket</c>: pins two bodies at a point and lets them turn freely about it.</summary>
public sealed class PhysBallSocket : PhysConstraint
{
    /// <summary>Initializes a ball socket from its keyvalues.</summary>
    public PhysBallSocket(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    protected override Joint? CreateJoint(Box3D.PhysicsWorld world, JointDefinition connection)
    {
        var friction = KeyValues.GetFloatProperty("friction");

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
