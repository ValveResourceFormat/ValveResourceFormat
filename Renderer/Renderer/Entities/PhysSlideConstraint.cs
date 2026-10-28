using Box3D;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>phys_slideconstraint</c>: lets a body slide without turning along the line from the origin to <c>slideaxis</c>.
/// A press on the sliding body runs it to the far end of the line, as a drawer opens or shuts.
/// </summary>
public sealed class PhysSlideConstraint : PhysConstraint
{
    [Flags]
    private enum SlideSpawnFlags : uint
    {
        LimitEndpoints = 2,
    }

    // How hard a press pushes the body along, in units per second squared
    private const float DriveAcceleration = 2000f;

    private PrismaticJoint slider;
    private bool hasSlider;
    private Body slidingBody;
    private float closedTranslation;
    private float openTranslation;
    private float frictionForce;

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

        var line = PrismaticJointDefinition.Slider(connection.BodyA, connection.BodyB, Origin, slide / length);

        // Friction 1 about holds the slider against gravity
        var friction = KeyValues.GetFloatProperty("slidefriction");

        var definition = line with
        {
            Base = line.Base with { CollideConnected = connection.CollideConnected },
            LimitsEnabled = HasSpawnFlags(SlideSpawnFlags.LimitEndpoints),
            LowerTranslation = slidesFirst ? travel - length : -travel,
            UpperTranslation = slidesFirst ? travel : length - travel,
            MotorEnabled = friction > 0f,
            MotorSpeed = 0f,
            MaxMotorForce = friction * slidingBody.Mass * PhysicsSimulation.GravityValue,
        };

        slider = world.CreatePrismaticJoint(definition);
        hasSlider = true;
        this.slidingBody = slidingBody;
        closedTranslation = slidesFirst ? travel : -travel;
        openTranslation = slidesFirst ? travel - length : length - travel;
        frictionForce = definition.MaxMotorForce;

        return slider.AsJoint;
    }

    /// <inheritdoc/>
    protected override DriveTuning Tuning => new(Speed: 60f, ApproachRate: 8f, ArriveTolerance: 0.25f, StallProgress: 0.1f);

    /// <inheritdoc/>
    protected override bool TryGetTravel(Body body, out float position, out float firstEnd, out float secondEnd)
    {
        position = firstEnd = secondEnd = 0f;

        if (!hasSlider || !slidingBody.Equals(body) || body.Type != BodyType.Dynamic)
        {
            return false;
        }

        position = slider.Translation;
        firstEnd = closedTranslation;
        secondEnd = openTranslation;
        return true;
    }

    /// <inheritdoc/>
    protected override void SetDriveSpeed(float? speed)
    {
        if (speed is not { } driveSpeed)
        {
            slider.MotorSpeed = 0f;
            slider.MaxMotorForce = frictionForce;
            slider.MotorEnabled = frictionForce > 0f;
            return;
        }

        // Kept awake, or a body slowing near its goal falls asleep short of it
        slider.MotorEnabled = true;
        slider.MaxMotorForce = slidingBody.Mass * DriveAcceleration;
        slider.MotorSpeed = driveSpeed;
        slidingBody.IsAwake = true;
    }
}
