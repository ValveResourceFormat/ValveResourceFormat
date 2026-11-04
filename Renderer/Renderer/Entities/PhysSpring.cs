using Box3D;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary><c>phys_spring</c>: a damped spring from the origin on one body to the <c>springaxis</c> point on the other.</summary>
public sealed class PhysSpring : PhysConstraint
{
    [Flags]
    private enum SpringSpawnFlags : uint
    {
        OnlyStretch = 1,
        EnableCollision = 2,
    }

    /// <summary>Initializes a spring from its keyvalues.</summary>
    public PhysSpring(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    protected override bool CollidesConnected => HasSpawnFlags(SpringSpawnFlags.EnableCollision);

    // A spring is not a constraint to the world body, the bodies it holds keep colliding with it
    /// <inheritdoc/>
    protected override bool FreesFromWorldGeometry => false;

    /// <inheritdoc/>
    protected override Joint? CreateJoint(Box3D.PhysicsWorld world, JointDefinition connection)
    {
        var end = EntityTransformHelper.TryParseVector3(KeyValues.GetStringProperty("springaxis") ?? string.Empty, out var point) ? point : Origin;
        var length = KeyValues.GetFloatProperty("length");

        // Zero means the length of the helper
        if (length <= 0f)
        {
            length = Vector3.Distance(Origin, end);
        }

        var definition = DistanceJointDefinition.Between(connection.BodyA, connection.BodyB, Origin, end);

        return world.CreateDistanceJoint(definition with
        {
            Base = definition.Base with { CollideConnected = connection.CollideConnected },
            Length = length * KeyValues.GetFloatProperty("lengthscale", 1f),
            SpringEnabled = true,
            Hertz = KeyValues.GetFloatProperty("frequency", 5f),
            DampingRatio = KeyValues.GetFloatProperty("damping", 0.7f),
            UpperSpringForce = HasSpawnFlags(SpringSpawnFlags.OnlyStretch) ? 0f : definition.UpperSpringForce,
        }).AsJoint;
    }
}
