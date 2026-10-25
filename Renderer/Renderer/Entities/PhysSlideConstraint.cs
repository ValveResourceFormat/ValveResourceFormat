using Box3D;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>phys_slideconstraint</c>: lets one body slide along the line from this entity's origin toward the
/// <c>slideaxis</c> point without turning, such as a drawer in its cabinet. With the endpoints limited it
/// travels from the origin to that point and no further.
/// </summary>
public sealed class PhysSlideConstraint : PhysConstraint
{
    [Flags]
    private enum SlideSpawnFlags : uint
    {
        LimitEndpoints = 2,
    }

    /// <summary>Initializes a slide constraint from its keyvalues.</summary>
    public PhysSlideConstraint(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    private Vector3 SlideVector => EntityTransformHelper.TryParseVector3(KeyValues.GetStringProperty("slideaxis") ?? string.Empty, out var end)
        ? end - Origin
        : Vector3.Zero;

    /// <inheritdoc/>
    protected override Joint? CreateJoint(Box3D.PhysicsWorld world, JointDefinition connection)
    {
        var slide = SlideVector;
        var length = slide.Length();

        if (length < 1e-4f)
        {
            return null;
        }

        var slider = PrismaticJointDefinition.Slider(connection.BodyA, connection.BodyB, Origin, slide / length);

        // The body that slides toward the endpoint is the moving one: attach2 relative to attach1,
        // unless attach2 is the world and attach1 slides against it
        var slidesFirst = connection.BodyA.Type == BodyType.Dynamic && connection.BodyB.Type != BodyType.Dynamic;
        var slidingBody = slidesFirst ? connection.BodyA : connection.BodyB;

        // Friction 1 is about enough to hold the slider's weight
        var friction = KeyValues.GetFloatProperty("slidefriction");

        var definition = slider with
        {
            Base = slider.Base with { CollideConnected = connection.CollideConnected },
            LimitsEnabled = HasSpawnFlags(SlideSpawnFlags.LimitEndpoints),
            LowerTranslation = slidesFirst ? -length : 0f,
            UpperTranslation = slidesFirst ? 0f : length,
            MotorEnabled = friction > 0f,
            MotorSpeed = 0f,
            MaxMotorForce = friction * slidingBody.Mass * PhysicsSimulation.GravityValue,
        };

        return world.CreatePrismaticJoint(definition).AsJoint;
    }
}
