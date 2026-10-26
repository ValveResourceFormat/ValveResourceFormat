using Box3D;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary><c>phys_slideconstraint</c>: lets a body slide without turning along the line from the origin to <c>slideaxis</c>.</summary>
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

        // attach2 slides relative to attach1, unless only attach1 is dynamic
        var slidesFirst = connection.BodyA.Type == BodyType.Dynamic && connection.BodyB.Type != BodyType.Dynamic;
        var slidingBody = slidesFirst ? connection.BodyA : connection.BodyB;

        // Placed at the start of the line, a positive initial offset spawns it that fraction of the way along
        var travel = Math.Clamp(KeyValues.GetFloatProperty("initialoffset"), 0f, 1f) * length;

        if (travel > 0f && slidingBody.Type == BodyType.Dynamic)
        {
            slidingBody.SetTransform(slidingBody.Position + slide / length * travel, slidingBody.Rotation);
        }

        var slider = PrismaticJointDefinition.Slider(connection.BodyA, connection.BodyB, Origin, slide / length);

        // Friction 1 about holds the slider against gravity
        var friction = KeyValues.GetFloatProperty("slidefriction");

        var definition = slider with
        {
            Base = slider.Base with { CollideConnected = connection.CollideConnected },
            LimitsEnabled = HasSpawnFlags(SlideSpawnFlags.LimitEndpoints),
            LowerTranslation = slidesFirst ? travel - length : -travel,
            UpperTranslation = slidesFirst ? travel : length - travel,
            MotorEnabled = friction > 0f,
            MotorSpeed = 0f,
            MaxMotorForce = friction * slidingBody.Mass * PhysicsSimulation.GravityValue,
        };

        return world.CreatePrismaticJoint(definition).AsJoint;
    }
}
