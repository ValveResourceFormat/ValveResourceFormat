using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// The base of the brush entities that travel between two places, Source's <c>CBaseToggle</c>: buttons,
/// doors, and anything else that slides open and shut.
/// </summary>
/// <remarks>
/// The travel is a <c>LinearMove</c>, not an interpolation: the entity is given a constant velocity and a
/// deadline, the tick integrates it like anything else that moves, and the arrival lands on the exact
/// destination rather than wherever the last tick happened to leave it.
/// </remarks>
public abstract class BaseToggle : BaseModelEntity
{
    /// <summary>Where a moving brush is in its travel.</summary>
    protected enum ToggleState
    {
        /// <summary>At the closed end, its authored position.</summary>
        AtBottom,

        /// <summary>At the open end.</summary>
        AtTop,

        /// <summary>Travelling towards the open end.</summary>
        GoingUp,

        /// <summary>Travelling back towards the closed end.</summary>
        GoingDown,
    }

    /// <summary>Gets or sets how fast the brush travels, in units per second.</summary>
    public float Speed { get; protected set; }

    /// <summary>Gets or sets how much of the brush stays proud of its opening, the <c>lip</c> keyvalue.</summary>
    public float Lip { get; protected set; }

    /// <summary>Gets the direction the brush travels in the world, resolved from its <c>movedir</c>.</summary>
    protected Vector3 MoveDirection { get; private set; }

    /// <summary>
    /// Gets or sets where the brush is heading. A travel under way keeps its velocity when this changes, and
    /// lands here once its time is up.
    /// </summary>
    protected Vector3 FinalDestination { get; set; }

    /// <summary>Gets whether a <see cref="LinearMove"/> or <see cref="AngularMove"/> is under way.</summary>
    protected bool IsLinearMoving { get; private set; }

    private Vector3 finalAngle;
    private bool isAngularMoving;

    /// <summary>A travelling brush shoves the player rather than swallowing them.</summary>
    protected internal override bool IsPusher => true;

    /// <inheritdoc/>
    protected override bool TurnsByAngleComponents => true;

    /// <summary>Initializes a moving brush from its keyvalues.</summary>
    protected BaseToggle(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <summary>
    /// Sets off towards a destination at <see cref="Speed"/>, arriving when the move-done comes due.
    /// Source's <c>CBaseToggle::LinearMove</c>: constant velocity and a deadline, not a lerp.
    /// </summary>
    protected void LinearMove(Vector3 destination)
    {
        FinalDestination = destination;

        if (destination == Origin || Speed <= 0f)
        {
            // Nowhere to go, so the arrival is now, and the one a travel under way had scheduled is dropped
            SetMoveDoneTime(-1f);
            MoveDone();
            return;
        }

        var delta = destination - Origin;
        var travelTime = delta.Length() / Speed;

        IsLinearMoving = true;
        Velocity = delta / travelTime;

        SetMoveDoneTime(travelTime);
    }

    /// <summary>
    /// Turns towards a destination angle at <paramref name="speed"/> degrees per second, arriving when
    /// the move-done comes due. Source's <c>CBaseToggle::AngularMove</c>.
    /// </summary>
    protected void AngularMove(Vector3 destinationAngle, float speed)
    {
        finalAngle = destinationAngle;

        if (destinationAngle == Angles || speed <= 0f)
        {
            SetMoveDoneTime(-1f);
            MoveDone();
            return;
        }

        var delta = destinationAngle - Angles;

        // Source's floor on the travel, so a turn cannot be so short that the tick steps over it
        var travelTime = MathF.Max(delta.Length() / speed, 0.01f);

        isAngularMoving = true;
        AngularVelocity = delta / travelTime;

        SetMoveDoneTime(travelTime);
    }

    /// <summary>
    /// Ends a travel exactly on its destination. Call from <c>MoveDone</c> before acting on the arrival.
    /// </summary>
    /// <returns><see langword="true"/> when a travel was in progress and has now landed.</returns>
    protected bool FinishLinearMove()
    {
        if (isAngularMoving)
        {
            // Land exactly on the destination rather than wherever the last tick left off
            isAngularMoving = false;
            AngularVelocity = Vector3.Zero;
            Angles = finalAngle;

            return true;
        }

        if (!IsLinearMoving)
        {
            return false;
        }

        IsLinearMoving = false;
        Velocity = Vector3.Zero;
        Origin = FinalDestination;

        return true;
    }

    /// <summary>
    /// Reads the axis a rotating brush turns about from its spawnflags. Source's
    /// <c>CBaseToggle::AxisDir</c>, whose flag names are for the QAngle component they set, so its "roll"
    /// flag is what Hammer labels X Axis.
    /// </summary>
    protected static Vector3 GetAxisDirection(bool rollAxis, bool pitchAxis)
    {
        if (rollAxis)
        {
            return new Vector3(0, 0, 1);
        }

        return pitchAxis ? new Vector3(1, 0, 0) : new Vector3(0, 1, 0);
    }

    /// <summary>
    /// Reads the direction a button, door or mover travels, the way CS2 reads it: <c>movedir</c> is an angle
    /// in the entity's own frame, with none of Source 1's magic values for up and down, and the entity's
    /// orientation carries it into the world. A door rotated by its instance therefore still opens the way it was authored to.
    /// </summary>
    /// <returns>The direction in the entity's own frame, which is the frame its bounds are measured in.</returns>
    protected Vector3 ResolveEntitySpaceMoveDirection()
    {
        var localDirection = EntityTransformHelper.EulerAnglesToForwardDirection(KeyValues.GetVector3Property("movedir"));

        MoveDirection = Vector3.TransformNormal(localDirection, EntityTransformHelper.EulerAnglesToRotationMatrix(Angles));

        return localDirection;
    }

    /// <summary>
    /// How far the brush slides: its own length along the travel axis, less the lip that keeps it proud.
    /// </summary>
    /// <remarks>
    /// Source 1 subtracts a further 2 units because its engine hands it a brush bound that is 1 unit larger
    /// in every direction. CS2 measures the unpadded bounds, and so do the compiled collision hulls here.
    /// </remarks>
    /// <param name="localDirection">The travel direction, in the frame of the brush's own bounds.</param>
    protected float GetTravelDistance(Vector3 localDirection)
    {
        var size = Collider?.LocalBounds.Size ?? Vector3.Zero;

        return MathF.Abs(localDirection.X * size.X)
            + MathF.Abs(localDirection.Y * size.Y)
            + MathF.Abs(localDirection.Z * size.Z)
            - Lip;
    }
}
