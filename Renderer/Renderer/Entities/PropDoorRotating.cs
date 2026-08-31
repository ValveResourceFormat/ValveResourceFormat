using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>prop_door_rotating</c>, Source's <c>CPropDoorRotating</c>: the swinging model door on maps like
/// de_inferno. A rotating door with its own keys and manners: it is usable by default, swings away from
/// whoever opened it unless <c>opendir</c> forces a side, and plays the authored sound overrides. Double
/// doors, breaking, and blocking are not simulated. Alyx's <c>prop_door_rotating_physics</c> is played
/// as the same thing: the hand that swings it in VR is a use press here.
/// </summary>
public class PropDoorRotating : FuncDoorRotating
{
    /// <summary>What a <c>prop_door_rotating</c>'s <c>spawnflags</c> mean.</summary>
    [Flags]
    public new enum SpawnFlag : uint
    {
        /// <summary>Spawns at its open position.</summary>
        StartsOpen = 1,

        /// <summary>Spawns locked.</summary>
        StartsLocked = 2048,

        /// <summary>Pressing use while the door is open closes it. Checked by default.</summary>
        UseCloses = 8192,

        /// <summary>Refuses player use entirely.</summary>
        IgnorePlayerUse = 32768,
    }

    /// <summary>How <c>opendir</c> constrains the swing.</summary>
    public enum OpenDirection
    {
        /// <summary>Away from whoever opens it, the standard door behavior.</summary>
        Both = 0,

        /// <summary>Forward only.</summary>
        ForwardOnly = 1,

        /// <summary>Backward only.</summary>
        BackwardOnly = 2,
    }

    /// <summary>Gets which way the door may swing.</summary>
    public OpenDirection OpenDir { get; private set; }

    private string? soundOpen;
    private string? soundClose;
    private string? soundMove;
    private string? soundLocked;

    /// <summary>Initializes a <c>prop_door_rotating</c> from its keyvalues.</summary>
    public PropDoorRotating(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <summary>Usable by default; only the ignore flag switches it off.</summary>
    public override EntityCapability ObjectCaps
        => HasSpawnFlags(SpawnFlag.IgnorePlayerUse) ? EntityCapability.None : EntityCapability.ImpulseUse;

    /// <inheritdoc/>
    public override void Spawn()
    {
        base.Spawn();

        OpenDir = (OpenDirection)KeyValues.GetInt32Property("opendir");
        IsLocked = HasSpawnFlags(SpawnFlag.StartsLocked);

        soundOpen = NonEmpty(KeyValues.GetStringProperty("soundopenoverride"));
        soundClose = NonEmpty(KeyValues.GetStringProperty("soundcloseoverride"));
        soundMove = NonEmpty(KeyValues.GetStringProperty("soundmoveoverride"));
        soundLocked = NonEmpty(KeyValues.GetStringProperty("soundlockedoverride"));

        foreach (var sound in (string?[])[soundOpen, soundClose, soundMove, soundLocked])
        {
            if (sound != null)
            {
                Sound.Cache(sound);
            }
        }
    }

    private static string? NonEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    /// <summary>A prop door swings about its hinge, which is always yaw.</summary>
    protected override Vector3 ReadMoveAngles() => new(0, 1, 0);

    /// <summary>The <c>returndelay</c> keyvalue; -1, the default, keeps the door open.</summary>
    protected override float ReadWait() => KeyValues.GetFloatProperty("returndelay", -1f);

    // Spawn positions 1 and 2 are open (forward/back); ajar is treated as closed
    /// <inheritdoc/>
    protected override bool SpawnsOpen() => KeyValues.GetInt32Property("spawnpos") is 1 or 2;

    /// <inheritdoc/>
    public override void Use(BaseEntity? activator)
    {
        // Standing open with "use closes" unchecked, the door has nothing to say to a press
        if (IsOpen && !HasSpawnFlags(SpawnFlag.UseCloses))
        {
            return;
        }

        Toggle(activator);
    }

    /// <inheritdoc/>
    protected override void StartMove(bool opening)
    {
        if (opening)
        {
            AngleOpen = AngleClosed + (MoveAngles * (Distance * PickSwingDirection()));
        }

        base.StartMove(opening);
    }

    /// <summary>
    /// Which way the swing goes: the forced side when <c>opendir</c> names one, otherwise ahead of
    /// whoever is coming through - the direction the player is looking as they press it.
    /// </summary>
    private float PickSwingDirection()
    {
        if (OpenDir != OpenDirection.Both)
        {
            return OpenDir == OpenDirection.ForwardOnly ? 1f : -1f;
        }

        var push = LastActivator switch
        {
            PlayerEntity player => player.Controller.ViewForward,
            { } activator => Origin - activator.Origin,
            _ => EntityTransformHelper.EulerAnglesToForwardDirection(AngleClosed),
        };

        // The panel's actual lever from the hinge, off the collision bounds, so nothing is assumed
        // about which authored axis the panel extends along. A positive yaw sweeps the panel along
        // Z x lever; the swing goes whichever way carries the panel with the push.
        var lever = (Collider?.WorldBounds.Center ?? Origin) - Origin;
        lever.Z = 0f;

        if (lever.LengthSquared() < 1e-4f)
        {
            return 1f;
        }

        var tangent = Vector3.Cross(Vector3.UnitZ, lever);

        return Vector3.Dot(tangent, push) >= 0f ? 1f : -1f;
    }

    /// <inheritdoc/>
    protected override void OnSetOff(bool opening) => Play(soundMove);

    /// <inheritdoc/>
    protected override void OnArrived(bool open) => Play(open ? soundOpen : soundClose);

    /// <inheritdoc/>
    protected override void OnLockedUse() => Play(soundLocked);

    private void Play(string? soundEvent)
    {
        if (soundEvent != null)
        {
            Sound.Play(soundEvent, Origin);
        }
    }
}
