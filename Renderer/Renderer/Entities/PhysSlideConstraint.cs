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

    // A press slides the body at this many units per second, slowing over the last stretch at this many units
    // per second per unit left so it does not slam into the end and rebound
    private const float DriveSpeed = 60f;
    private const float ApproachRate = 8f;
    private const float DriveAcceleration = 2000f;
    private const float ArriveTolerance = 0.25f;

    // A slide that gains less than this over the stall time is blocked, and ends where it is
    private const float StallProgress = 0.1f;
    private const float StallTime = 0.3f;

    private PrismaticJoint slider;
    private bool hasSlider;
    private Body slidingBody;
    private float closedTranslation;
    private float openTranslation;
    private float frictionForce;

    private bool isDriving;
    private float goalTranslation;
    private float stallError;
    private float stallTimeLeft;

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

    /// <summary>Finds the active slide constraint that <paramref name="body"/> slides on.</summary>
    internal static PhysSlideConstraint? FindSliding(EntitySystem system, Body body)
    {
        foreach (var entity in system.Entities)
        {
            if (entity is PhysSlideConstraint { IsActive: true, hasSlider: true } slide && slide.slidingBody.Equals(body))
            {
                return slide;
            }
        }

        return null;
    }

    /// <summary>Slides the body to whichever end of the line it is further from.</summary>
    internal void Press()
    {
        if (!slider.AsJoint.IsValid || slidingBody.Type != BodyType.Dynamic)
        {
            return;
        }

        var translation = slider.Translation;

        goalTranslation = MathF.Abs(translation - closedTranslation) < MathF.Abs(translation - openTranslation)
            ? openTranslation
            : closedTranslation;
        isDriving = true;
        stallError = MathF.Abs(goalTranslation - translation);
        stallTimeLeft = StallTime;
        slider.MotorEnabled = true;
        slider.MaxMotorForce = slidingBody.Mass * DriveAcceleration;
        Drive(goalTranslation - translation);
    }

    /// <summary>Stops a slide a press started, leaving the body to whatever holds it.</summary>
    internal void StopDrive()
    {
        if (!isDriving)
        {
            return;
        }

        isDriving = false;

        if (slider.AsJoint.IsValid)
        {
            slider.MotorSpeed = 0f;
            slider.MaxMotorForce = frictionForce;
            slider.MotorEnabled = frictionForce > 0f;
        }
    }

    // Kept awake, or a body slowing near its goal falls asleep short of it
    private void Drive(float error)
    {
        slider.MotorSpeed = MathF.CopySign(MathF.Min(DriveSpeed, MathF.Abs(error) * ApproachRate), error);
        slidingBody.IsAwake = true;
    }

    /// <inheritdoc/>
    protected override void PhysicsSimulate(float tickInterval)
    {
        base.PhysicsSimulate(tickInterval);

        if (!isDriving)
        {
            return;
        }

        if (!IsActive || !slider.AsJoint.IsValid)
        {
            isDriving = false;
            return;
        }

        var error = goalTranslation - slider.Translation;

        if (MathF.Abs(error) <= ArriveTolerance)
        {
            StopDrive();
            return;
        }

        if (stallError - MathF.Abs(error) > StallProgress)
        {
            stallError = MathF.Abs(error);
            stallTimeLeft = StallTime;
        }
        else if ((stallTimeLeft -= tickInterval) <= 0f)
        {
            StopDrive();
            return;
        }

        Drive(error);
    }
}
