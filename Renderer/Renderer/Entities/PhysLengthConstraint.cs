using Box3D;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary><c>phys_lengthconstraint</c>: keeps the origin on one body and <c>attachpoint</c> on the other at most their spawn distance apart, or exactly with Keep Rigid.</summary>
public sealed class PhysLengthConstraint : PhysConstraint
{
    [Flags]
    private enum LengthSpawnFlags : uint
    {
        KeepRigid = 2,
    }

    /// <summary>Initializes a length constraint from its keyvalues.</summary>
    public PhysLengthConstraint(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    protected override Joint? CreateJoint(Box3D.PhysicsWorld world, JointDefinition connection)
    {
        var end = EntityTransformHelper.TryParseVector3(KeyValues.GetStringProperty("attachpoint") ?? string.Empty, out var point) ? point : Origin;
        var length = MathF.Max(0f, Vector3.Distance(Origin, end) + KeyValues.GetFloatProperty("addlength"));
        var isRigid = HasSpawnFlags(LengthSpawnFlags.KeepRigid);

        var definition = DistanceJointDefinition.Between(connection.BodyA, connection.BodyB, Origin, end);

        return world.CreateDistanceJoint(definition with
        {
            Base = definition.Base with { CollideConnected = connection.CollideConnected },
            Length = length,

            // A spring with no stiffness leaves the rope slack between its limits
            SpringEnabled = !isRigid,
            Hertz = 0f,
            LimitsEnabled = !isRigid,
            MinLength = MathF.Min(KeyValues.GetFloatProperty("minlength"), length),
            MaxLength = length,
        }).AsJoint;
    }
}
