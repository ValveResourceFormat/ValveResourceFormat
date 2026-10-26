using Box3D;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary><c>phys_constraint</c>: holds two bodies in their spawn pose, optionally springy or free to turn.</summary>
public sealed class PhysFixedConstraint : PhysConstraint
{
    /// <summary>Initializes a fixed constraint from its keyvalues.</summary>
    public PhysFixedConstraint(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    protected override Joint? CreateJoint(Box3D.PhysicsWorld world, JointDefinition connection)
    {
        var linear = KeyValues.GetBooleanProperty("enablelinearconstraint", true);
        var angular = KeyValues.GetBooleanProperty("enableangularconstraint", true);

        if (linear && angular)
        {
            var definition = WeldJointDefinition.Default with
            {
                Base = connection,
                LinearHertz = KeyValues.GetFloatProperty("linearfrequency"),
                LinearDampingRatio = KeyValues.GetFloatProperty("lineardampingratio"),
                AngularHertz = KeyValues.GetFloatProperty("angularfrequency"),
                AngularDampingRatio = KeyValues.GetFloatProperty("angulardampingratio"),
            };

            return world.CreateWeldJoint(definition).AsJoint;
        }

        if (linear)
        {
            return world.CreateSphericalJoint(SphericalJointDefinition.Default with { Base = connection }).AsJoint;
        }

        EntitySystem.Logger.LogDebug("{Classname} '{TargetName}' locks rotation alone, which has no joint to map to", Classname, TargetName);
        return null;
    }
}
