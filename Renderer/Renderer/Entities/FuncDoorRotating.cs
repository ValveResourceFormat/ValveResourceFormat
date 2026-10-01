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

        /// <summary>Always swings the same way, rather than away from whoever opens it.</summary>
        OneWay = 16,

        /// <summary>Swings about the world X axis (roll). Hammer "X Axis".</summary>
        RollAxis = 64,

        /// <summary>Swings about the world Y axis (pitch). Hammer "Y Axis".</summary>
        PitchAxis = 128,
    }

    /// <summary>Gets the angle the door rests at when closed.</summary>
    public Vector3 AngleClosed { get; private set; }

    /// <summary>Gets the angle the door rests at when open.</summary>
    public Vector3 AngleOpen { get; private set; }

    /// <summary>Gets how far the door swings, in degrees.</summary>
    public float Distance { get; private set; }

    // The QAngle axis the swing turns about, which spawning open the old way reverses
    private Vector3 moveAngles;

    /// <summary>Initializes a <c>func_door_rotating</c> from its keyvalues.</summary>
    public FuncDoorRotating(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    protected override void SetUpSpawnPosition()
    {
        moveAngles = GetAxisDirection(
            HasSpawnFlags(RotatingSpawnFlag.RollAxis),
            HasSpawnFlags(RotatingSpawnFlag.PitchAxis));

        if (HasSpawnFlags(RotatingSpawnFlag.Backwards))
        {
            moveAngles = -moveAngles;
        }

        Distance = KeyValues.GetFloatProperty("distance", 90f);

        AngleClosed = Angles;
        AngleOpen = AngleClosed + moveAngles * Distance;

        // The old start-open flag authors the door open and treats that as closed: the ends swap, and the
        // swing reverses with them
        if (HasSpawnFlags(SpawnFlag.StartsOpen))
        {
            (AngleClosed, AngleOpen) = (AngleOpen, AngleClosed);
            moveAngles = -moveAngles;

            Teleport(Origin, AngleClosed);
            State = ToggleState.AtBottom;
            return;
        }

        if (KeyValues.GetInt32Property("spawnpos") == 1)
        {
            Teleport(Origin, AngleOpen);
            State = ToggleState.AtTop;
            return;
        }

        State = ToggleState.AtBottom;
    }

    /// <inheritdoc/>
    protected override void StartMove(bool opening)
    {
        if (!opening)
        {
            AngularMove(AngleClosed, Speed);
            return;
        }

        // The engine scales the whole open angle rather than the swing, so a door not closed at zero turns
        // somewhere other than its mirrored open angle when it opens the other way
        AngularMove(AngleOpen * PickSwingSign(), Speed);
    }

    /// <inheritdoc/>
    protected override void JumpToEnd(bool atOpenEnd) => Teleport(Origin, atOpenEnd ? AngleOpen : AngleClosed);

    /// <summary>
    /// Which way a yawing door opens: away from whoever opened it, judged by the side of the hinge their
    /// nearest point on the door lies. Source's <c>CBaseDoor::DoorGoUp</c>.
    /// </summary>
    private float PickSwingSign()
    {
        if (LastActivator is not { } activator || HasSpawnFlags(RotatingSpawnFlag.OneWay) || moveAngles.Y == 0f)
        {
            return 1f;
        }

        var hinge = Origin;
        var standing = activator.Origin;

        var toNearest = Flatten(NearestPointOnDoor(standing) - standing);
        var toHinge = Flatten(hinge - standing);
        var nearestToHinge = Vector3.Distance(toHinge, toNearest);

        toNearest = MathUtils.SafeNormalize(toNearest);
        toHinge = MathUtils.SafeNormalize(toHinge);

        // Looking straight down the door with the nearest point on the hinge, the sides cannot be told
        // apart, so the hinge is pulled out away from the door's center to break the tie
        if (nearestToHinge < 5f && Vector3.Dot(toHinge, toNearest) > 0.99f)
        {
            hinge += hinge - (Collider?.WorldBounds.Center ?? hinge);
            toHinge = MathUtils.SafeNormalize(Flatten(hinge - standing));
        }

        return Vector3.Cross(toHinge, toNearest).Z > 0f ? -1f : 1f;
    }

    private Vector3 NearestPointOnDoor(Vector3 point)
    {
        if (Collider is not { IsEmpty: false } collider || !Matrix4x4.Invert(collider.Transform, out var toLocal))
        {
            return Origin;
        }

        var bounds = collider.LocalBounds;
        var local = Vector3.Clamp(Vector3.Transform(point, toLocal), bounds.Min, bounds.Max);

        return Vector3.Transform(local, collider.Transform);
    }

    private static Vector3 Flatten(Vector3 value) => value with { Z = 0f };
}
