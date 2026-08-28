using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>func_door_rotating</c>. A door that swings about one axis instead of sliding, Source's
/// <c>CRotDoor</c>. Everything but the travel is <see cref="FuncDoor"/>'s, because in the engine they
/// are the same class; only where the two ends are, and how it gets between them, changes.
/// </summary>
public class FuncDoorRotating : FuncDoor
{
    /// <summary>What a <c>func_door_rotating</c>'s <c>spawnflags</c> mean beyond the door's own.</summary>
    [Flags]
    public enum RotatingSpawnFlag : uint
    {
        /// <summary>Swings the other way.</summary>
        Backwards = 2,

        /// <summary>Swings about the world X axis (roll). Hammer "X Axis".</summary>
        RollAxis = 64,

        /// <summary>Swings about the world Y axis (pitch). Hammer "Y Axis".</summary>
        PitchAxis = 128,
    }

    /// <summary>Gets the angle the door rests at when closed.</summary>
    public Vector3 AngleClosed { get; protected set; }

    /// <summary>Gets the angle the door rests at when open.</summary>
    public Vector3 AngleOpen { get; protected set; }

    /// <summary>Gets how far the door swings, in degrees.</summary>
    public float Distance { get; protected set; }

    /// <summary>Gets or sets the QAngle axis the swing turns about, scaled by nothing.</summary>
    protected Vector3 MoveAngles { get; set; }

    /// <summary>Initializes a <c>func_door_rotating</c> from its keyvalues.</summary>
    public FuncDoorRotating(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <summary>Reads the axis the swing turns about. A prop door only ever turns about yaw.</summary>
    protected virtual Vector3 ReadMoveAngles()
    {
        var moveAngles = GetAxisDirection(
            HasSpawnFlags(RotatingSpawnFlag.RollAxis),
            HasSpawnFlags(RotatingSpawnFlag.PitchAxis));

        return HasSpawnFlags(RotatingSpawnFlag.Backwards) ? -moveAngles : moveAngles;
    }

    /// <inheritdoc/>
    protected override void SetUpTravel()
    {
        MoveAngles = ReadMoveAngles();

        Distance = KeyValues.GetFloatProperty("distance", 90f);

        if (Distance == 0f)
        {
            Distance = 90f;
        }

        AngleClosed = Angles;
        AngleOpen = AngleClosed + (MoveAngles * Distance);

        // A swinging door does not slide, so the positions the base class worked out mean nothing to it
        PositionOpen = Origin;
        PositionClosed = Origin;
    }

    /// <inheritdoc/>
    protected override void SwapEnds()
    {
        (AngleClosed, AngleOpen) = (AngleOpen, AngleClosed);

        MoveAngles = -MoveAngles;
        Angles = AngleClosed;

        SnapInterpolation();
    }

    /// <inheritdoc/>
    protected override void StartMove(bool opening) => AngularMove(opening ? AngleOpen : AngleClosed, Speed);
}
