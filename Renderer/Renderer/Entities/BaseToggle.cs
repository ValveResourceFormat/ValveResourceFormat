using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// Base for brush entities that travel between two positions, such as buttons and doors.
/// </summary>
/// <remarks>
/// A travel gives the entity a constant velocity and a deadline, and the arrival snaps to the exact
/// destination instead of wherever the last tick left it.
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

    /// <summary>Gets how fast the brush travels, in units per second.</summary>
    public float Speed { get; protected set; }

    /// <summary>Gets the <c>lip</c> keyvalue: how much of the brush stays proud of its opening.</summary>
    public float Lip { get; protected set; }

    /// <summary>Gets the direction the brush travels in the world, resolved from its <c>movedir</c>.</summary>
    protected Vector3 MoveDirection { get; private set; }

    /// <summary>
    /// Gets or sets where the brush is heading. Changing it mid-travel keeps the velocity; the brush lands
    /// here once its time is up.
    /// </summary>
    protected Vector3 FinalDestination { get; set; }

    /// <summary>Gets whether a <see cref="LinearMove"/> is under way.</summary>
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
    /// Moves towards a destination at <see cref="Speed"/>, arriving when the move-done comes due.
    /// </summary>
    protected void LinearMove(Vector3 destination)
    {
        FinalDestination = destination;

        if (destination == Origin || Speed <= 0f)
        {
            // Nothing to travel: arrive now and drop any scheduled arrival
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
    /// the move-done comes due.
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

        // Minimum duration so the tick cannot step over a tiny turn
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
    /// Reads the axis a rotating brush turns about from its spawnflags. Flag names are for the QAngle
    /// component they set, so the "roll" flag is what Hammer labels X Axis.
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
    /// Reads the direction a button, door or mover travels. <c>movedir</c> is an angle in the entity's own
    /// frame with no special values for up and down, and the entity's orientation carries it into the world,
    /// so an instance-rotated door still opens as authored.
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
    /// Deliberately takes off no extra 2 units for bounds padded by 1 unit per side: the compiled collision
    /// hulls are unpadded.
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
