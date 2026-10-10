using ValveResourceFormat.Renderer.Audio;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>func_rotating</c>. A brush that spins about one axis at up to <c>maxspeed</c> degrees per second,
/// either snapping to speed or ramping up and down against <c>fanfriction</c>.
/// </summary>
/// <remarks>
/// <para>
/// The speed ramp is a chain of move-done callbacks 0.1s apart (<c>SpinUpMove</c> / <c>SpinDownMove</c>)
/// that hand over to <c>RotateMove</c> at the target speed.
/// </para>
/// <para>
/// Hammer's "X Axis" sets roll and its "Y Axis" sets pitch, and with neither set the brush yaws about
/// world Z. The mismatch is deliberate, so maps rotate the way they do in game.
/// </para>
/// <para>
/// The brush is solid unless "Not Solid" is set. The "Fan Pain" damage flag is not simulated.
/// </para>
/// <para>
/// Sound volume follows the speed, but the pitch stays fixed: in game the sample is wound from 30% to
/// 100% pitch during spin-up, which the sound player cannot do.
/// </para>
/// </remarks>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CFuncRotating">CFuncRotating</seealso>
public sealed class FuncRotating : BaseModelEntity
{
    /// <summary>A turning brush shoves the player rather than swallowing them.</summary>
    protected internal override bool IsPusher => true;

    /// <inheritdoc/>
    protected override bool IsUnblockableByPlayer => HasSpawnFlags(SpawnFlag.UnblockableByPlayer);

    /// <summary>The <c>spawnflags</c> of a <c>func_rotating</c>.</summary>
    [Flags]
    public enum SpawnFlag : uint
    {
        /// <summary>Spawns already spinning at <c>maxspeed</c>.</summary>
        StartOn = 1,

        /// <summary>Spins the other way.</summary>
        Backwards = 2,

        /// <summary>Rotates about the world X axis (roll). Hammer "X Axis".</summary>
        RollAxis = 4,

        /// <summary>Rotates about the world Y axis (pitch). Hammer "Y Axis".</summary>
        PitchAxis = 8,

        /// <summary>Ramps up to speed and back down instead of snapping.</summary>
        AccelerateDecelerate = 16,

        /// <summary>Hurts whatever it touches, scaled by rotation speed. Hammer "Fan Pain". Not simulated.</summary>
        Hurt = 32,

        /// <summary>Never solid, for things like fake volumetric light cones.</summary>
        NotSolid = 64,

        /// <summary>Rotation sound is heard from close by. Attenuation is not simulated.</summary>
        SmallSoundRadius = 128,

        /// <summary>Rotation sound carries a middling distance. Attenuation is not simulated.</summary>
        MediumSoundRadius = 256,

        /// <summary>Rotation sound carries a long way, the Hammer default. Attenuation is not simulated.</summary>
        LargeSoundRadius = 512,

        /// <summary>The player cannot stall it: they are moved the whole way, through the world if need be.</summary>
        UnblockableByPlayer = 2048,
    }

    private enum MoveDoneFunction
    {
        None,
        SpinUp,
        SpinDown,
        Reverse,
        Rotate,
    }

    /// <summary>Gets the axis to rotate about, as a QAngle direction.</summary>
    public Vector3 MoveAngles { get; private set; }

    /// <summary>Gets the top rotation speed in degrees per second.</summary>
    public float MaxSpeed { get; private set; }

    /// <summary>Gets the ramp friction, <c>fanfriction</c> as a fraction.</summary>
    public float FanFriction { get; private set; }

    /// <summary>Gets the current rotation speed in degrees per second; negative spins in reverse.</summary>
    public float Speed { get; private set; }

    /// <summary>Gets the speed being ramped towards.</summary>
    public float TargetSpeed { get; private set; }

    /// <summary>Gets whether the <c>Reverse</c> input flipped the spin direction.</summary>
    public bool IsReversed { get; private set; }

    /// <summary>Gets the sound played while the brush turns, the <c>message</c> keyvalue.</summary>
    public string? SoundName { get; private set; }

    /// <summary>Gets the volume the sound reaches at full speed, 0 to 1. Authored as <c>volume</c>, 0 to 10.</summary>
    public float Volume { get; private set; } = 1f;

    private bool stopAtStartPos;
    private bool acceleratesAndDecelerates;
    private Vector3 startAngles;
    private float turnedFromStart;
    private MoveDoneFunction moveDoneFunction;
    private SoundHandle playing;

    /// <summary>
    /// Initializes a <c>func_rotating</c> from its keyvalues.
    /// </summary>
    public FuncRotating(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        // Absent means zero rather than the FGD's 20, so the guard below is what a map that omits it gets.
        FanFriction = KeyValues.GetFloatProperty("fanfriction") / 100f;

        // Prevent a divide by zero if the level designer forgot the friction
        if (FanFriction == 0f)
        {
            FanFriction = 1f;
        }

        if (HasSpawnFlags(SpawnFlag.RollAxis))
        {
            MoveAngles = new Vector3(0, 0, 1); // roll
        }
        else if (HasSpawnFlags(SpawnFlag.PitchAxis))
        {
            MoveAngles = new Vector3(1, 0, 0); // pitch
        }
        else
        {
            MoveAngles = new Vector3(0, 1, 0); // yaw
        }

        if (HasSpawnFlags(SpawnFlag.Backwards))
        {
            MoveAngles = -MoveAngles;
        }

        // Did the level designer forget to assign a maximum speed? Prevent a divide
        // by zero in the sound ramp as well as silliness with the rotation
        MaxSpeed = MathF.Abs(KeyValues.GetFloatProperty("maxspeed"));

        if (MaxSpeed == 0f)
        {
            MaxSpeed = 100f;
        }

        SoundName = KeyValues.GetStringProperty("message");

        // Authored 0 to 10, and emitted as a fraction of full volume. A map that leaves it at zero means
        // the default rather than silence, as it did before the keyvalue existed.
        Volume = MathUtils.Saturate(KeyValues.GetFloatProperty("volume") / 10f);

        if (Volume == 0f)
        {
            Volume = 1f;
        }

        if (!string.IsNullOrEmpty(SoundName))
        {
            Sound.Cache(SoundName);
        }

        startAngles = Angles;
        acceleratesAndDecelerates = HasSpawnFlags(SpawnFlag.AccelerateDecelerate);

        // Some rotating objects, like fake volumetric lights, are never solid
        IsSolid = !HasSpawnFlags(SpawnFlag.NotSolid);
        MovesWithoutPushing = KeyValues.GetBooleanProperty("movewithoutpushingblockers");

        if (HasSpawnFlags(SpawnFlag.StartOn))
        {
            // Leave a magic delay for the client to start up, then toggle ourselves on
            SetNextThink(EntitySystem.CurrentTime + 0.2f);
        }
    }

    /// <summary>Toggles on a brush that spawned with <c>StartOn</c>; the only think this entity schedules.</summary>
    public override void Think() => Toggle();

    /// <inheritdoc/>
    protected override void PhysicsSimulate(float tickInterval)
    {
        base.PhysicsSimulate(tickInterval);

        // Tracked here because once the body has turned off its authored axes, progress is no longer a
        // QAngle component to read back.
        // Wrapped rather than run through AngleMod: that quantizes to 1/65536 of a turn and truncates
        // towards zero, so accumulating through it drops a fraction of a step every tick and drifts
        // without bound. AngleMod is applied once, when reading.
        turnedFromStart = (turnedFromStart + Speed * tickInterval) % 360f;
    }

    /// <inheritdoc/>
    public override void MoveDone()
    {
        switch (moveDoneFunction)
        {
            case MoveDoneFunction.SpinUp:
                SpinUpMove();
                break;

            case MoveDoneFunction.SpinDown:
                SpinDownMove();
                break;

            case MoveDoneFunction.Reverse:
                ReverseMove();
                break;

            case MoveDoneFunction.Rotate:
                RotateMove();
                break;
        }
    }

    [EntityInput("Start")]
    private void InputStart(EntityInputData data)
    {
        stopAtStartPos = false;

        SetTargetSpeed(MaxSpeed);
    }

    // Unlike the other starts, deliberately leaves a pending StopAtStartPos alone, so it still stops there.
    [EntityInput("StartForward")]
    private void InputStartForward(EntityInputData data)
    {
        IsReversed = false;

        SetTargetSpeed(MaxSpeed);
    }

    [EntityInput("StartBackward")]
    private void InputStartBackward(EntityInputData data)
    {
        stopAtStartPos = false;
        IsReversed = true;

        SetTargetSpeed(MaxSpeed);
    }

    [EntityInput("Stop")]
    private void InputStop(EntityInputData data)
    {
        stopAtStartPos = false;

        SetTargetSpeed(0f);
    }

    // Tests the speed rather than the angular velocity, so a brush running backwards starts again.
    [EntityInput("Toggle")]
    private void InputToggle(EntityInputData data) => SetTargetSpeed(Speed > 0f ? 0f : MaxSpeed);

    [EntityInput("Reverse")]
    private void InputReverse(EntityInputData data)
    {
        stopAtStartPos = false;
        IsReversed = !IsReversed;

        SetTargetSpeed(Speed);
    }

    [EntityInput("StopAtStartPos")]
    private void InputStopAtStartPos(EntityInputData data)
    {
        stopAtStartPos = true;
        SetTargetSpeed(0f);
        SetMoveDoneTime(GetNextMoveInterval());
    }

    // Does not fire OnReachedStart: a snap cancels a pending StopAtStartPos rather than completing it.
    // OnStopped still fires, as for any other stop.
    [EntityInput("SnapToStartPos")]
    private void InputSnapToStartPos(EntityInputData data)
    {
        stopAtStartPos = false;
        TargetSpeed = 0f;
        moveDoneFunction = MoveDoneFunction.None;

        ApplySpeed(0f);
        SetMoveDoneTime(-1f);

        turnedFromStart = 0f;
        Angles = startAngles;

        SnapInterpolation();
    }

    // The parameter is the new start angles as a QAngle.
    [EntityInput("SetStartPos")]
    private void InputSetStartPos(EntityInputData data)
    {
        startAngles = data.Vector(startAngles);

        // Re-based rather than zeroed: how far it has come round is measured from the new start.
        turnedFromStart = AngleMod(GetAxisAngle(Angles) - GetAxisAngle(startAngles));
    }

    [EntityInput("EnableAccelDecel")]
    private void InputEnableAccelDecel(EntityInputData data) => acceleratesAndDecelerates = true;

    [EntityInput("DisableAccelDecel")]
    private void InputDisableAccelDecel(EntityInputData data) => acceleratesAndDecelerates = false;

    // The parameter is a fraction of maxspeed; a negative fraction spins in reverse.
    [EntityInput("SetSpeed")]
    private void InputSetSpeed(EntityInputData data)
    {
        var fraction = data.Float();

        stopAtStartPos = false;
        IsReversed = fraction < 0f;
        SetTargetSpeed(Math.Clamp(MathF.Abs(fraction) * MaxSpeed, 0f, MaxSpeed));
    }

    /// <summary>Starts the brush if it is stopped, stops it if it is spinning.</summary>
    public void Toggle() => SetTargetSpeed(AngularVelocity != Vector3.Zero ? 0f : MaxSpeed);

    /// <summary>
    /// Sets the speed in degrees per second to ramp towards, or jumps straight to it when the brush does
    /// not accelerate. The sign comes from the reverse state, not from the speed passed in.
    /// </summary>
    public void SetTargetSpeed(float speed)
    {
        speed = MathF.Abs(speed);

        if (IsReversed)
        {
            speed = -speed;
        }

        TargetSpeed = speed;

        if (!acceleratesAndDecelerates)
        {
            UpdateSpeed(TargetSpeed);

            if (stopAtStartPos)
            {
                // Still has to watch for the start angle coming back around
                moveDoneFunction = MoveDoneFunction.Rotate;
                SetMoveDoneTime(GetNextMoveInterval());
            }
            else
            {
                moveDoneFunction = MoveDoneFunction.None;
                SetMoveDoneTime(-1f);
            }

            return;
        }

        // Otherwise ramp towards it, a tenth of a second at a time
        if ((Speed > 0f && TargetSpeed < 0f) || (Speed < 0f && TargetSpeed > 0f))
        {
            // Turning the other way means coming to a stop first
            moveDoneFunction = MoveDoneFunction.Reverse;
        }
        else if (MathF.Abs(Speed) < MathF.Abs(TargetSpeed))
        {
            moveDoneFunction = MoveDoneFunction.SpinUp;
        }
        else if (MathF.Abs(Speed) > MathF.Abs(TargetSpeed))
        {
            moveDoneFunction = MoveDoneFunction.SpinDown;
        }
        else
        {
            // Already there, so just keep turning
            moveDoneFunction = MoveDoneFunction.Rotate;
        }

        SetMoveDoneTime(GetNextMoveInterval());
    }

    // A pending StopAtStartPos steers the last stretch: over 90 degrees out it keeps its speed, inside that
    // it eases towards the remaining angle but never below 20 degrees per second, and once slow and within
    // a degree it lands on the start.
    private void UpdateSpeed(float newSpeed)
    {
        var oldSpeed = Speed;
        var speed = Math.Clamp(newSpeed, -MaxSpeed, MaxSpeed);

        // The requested speed, not the clamped one, is tested: for a slow brush with high friction it can be
        // the larger of the two and decide the approach a step earlier
        if (stopAtStartPos && newSpeed < 100f)
        {
            var angleDelta = GetAngleDeltaFromStart();

            if (speed <= 25f && MathF.Abs(angleDelta) < 1f)
            {
                ApplySpeed(0f);
                StopAtStartAngles();

                return;
            }

            if (MathF.Abs(angleDelta) > 90f)
            {
                // Still most of a turn from home, so keep the speed it had
                speed = oldSpeed;
            }
            else
            {
                var minSpeed = MathF.Max(MathF.Abs(angleDelta), 20f);

                speed = oldSpeed > 0f ? minSpeed : -minSpeed;
            }
        }

        ApplySpeed(speed);
    }

    // Starting and stopping also start and stop the rotation sound, and fire OnStarted and OnStopped.
    private void ApplySpeed(float speed)
    {
        var wasTurning = Speed != 0f;

        Speed = speed;
        AngularVelocity = MoveAngles * Speed;

        if (!wasTurning && speed != 0f)
        {
            StartSound();

            EntitySystem.TriggerOutput(this, "OnStarted");
        }
        else if (wasTurning && speed == 0f)
        {
            StopSound();

            EntitySystem.TriggerOutput(this, "OnStopped");
        }
        else
        {
            RampVolume();
        }
    }

    private void StartSound()
    {
        if (string.IsNullOrEmpty(SoundName))
        {
            return;
        }

        StopSound();

        playing = Sound.Play(SoundName, Transform.Translation, volume: Volume);
        RampVolume();
    }

    private void StopSound()
    {
        playing.Stop();
        playing = default;
    }

    private void RampVolume()
    {
        playing.Volume = MathUtils.Saturate(MathF.Abs(Speed) / MaxSpeed);
    }

    /// <inheritdoc/>
    protected override void OnRemove()
    {
        StopSound();

        base.OnRemove();
    }

    // Lands on the spawn angles and clears the pending stop; a snap, so interpolation is reset.
    // The pending stop also guards against landing twice: for a brush that does not accelerate,
    // SetTargetSpeed has already landed it before the caller gets here.
    private void StopAtStartAngles()
    {
        if (!stopAtStartPos)
        {
            return;
        }

        TargetSpeed = 0f;
        stopAtStartPos = false;
        turnedFromStart = 0f;

        Angles = startAngles;
        SnapInterpolation();

        EntitySystem.TriggerOutput(this, "OnReachedStart");
    }

    private void SpinUpMove()
    {
        var newSpeed = MathF.Abs(Speed) + 0.2f * MaxSpeed * FanFriction;
        var spinUpDone = false;

        if (newSpeed >= MathF.Abs(TargetSpeed))
        {
            newSpeed = TargetSpeed;

            // A brush still heading back to its start angle keeps ramping, so UpdateSpeed goes on steering it
            spinUpDone = !stopAtStartPos;
        }
        else if (TargetSpeed < 0f)
        {
            newSpeed = -newSpeed;
        }

        UpdateSpeed(newSpeed);

        if (spinUpDone)
        {
            moveDoneFunction = MoveDoneFunction.Rotate;
            RotateMove();
        }

        SetMoveDoneTime(GetNextMoveInterval());
    }

    /// <summary>Bleeds off speed, slower than it spins up.</summary>
    /// <returns><see langword="true"/> once it has arrived and the ramp is over.</returns>
    private bool SpinDown(float targetSpeed)
    {
        var newSpeed = MathF.Abs(Speed) - 0.1f * MaxSpeed * FanFriction;
        var spinDownDone = false;

        if (newSpeed < 0f)
        {
            newSpeed = 0f;
        }

        if (newSpeed <= MathF.Abs(targetSpeed))
        {
            newSpeed = targetSpeed;
            spinDownDone = !stopAtStartPos;
        }
        else if (Speed < 0f)
        {
            // Shedding speed must not flip the direction it is already turning
            newSpeed = -newSpeed;
        }

        UpdateSpeed(newSpeed);

        return spinDownDone;
    }

    private void SpinDownMove()
    {
        if (SpinDown(TargetSpeed))
        {
            moveDoneFunction = MoveDoneFunction.Rotate;
            RotateMove();
        }
        else
        {
            SetMoveDoneTime(GetNextMoveInterval());
        }
    }

    private void ReverseMove()
    {
        if (SpinDown(0f))
        {
            // Stopped, so now spin back up the other way
            SetTargetSpeed(TargetSpeed);
        }
        else
        {
            SetMoveDoneTime(GetNextMoveInterval());
        }
    }

    // The at-speed state. Wakes rarely, as only a pending StopAtStartPos needs to watch
    // the angle every tick.
    private void RotateMove()
    {
        SetMoveDoneTime(10f);

        if (!stopAtStartPos)
        {
            return;
        }

        SetMoveDoneTime(GetNextMoveInterval());

        var angleDelta = GetAngleDeltaFromStart();
        var anglesPerTick = Speed * EntitySystem.TickInterval;

        // Close enough that the next tick would overshoot the start angle: stop on it exactly
        if (MathF.Abs(angleDelta) < MathF.Abs(anglesPerTick))
        {
            SetTargetSpeed(0f);
            StopAtStartAngles();
        }
    }

    // The QAngle component on the axis this brush turns about.
    private float GetAxisAngle(Vector3 angles)
    {
        if (MoveAngles.X != 0f)
        {
            return angles.X;
        }

        return MoveAngles.Y != 0f ? angles.Y : angles.Z;
    }

    // Signed degrees turned from the start, in [-180, 180]. Quantization is applied only here, so its error
    // stays a fixed 1/65536 of a turn rather than compounding.
    private float GetAngleDeltaFromStart()
    {
        var delta = AngleMod(turnedFromStart);

        return delta > 180f ? delta - 360f : delta;
    }

    // Stopping at the start position needs tick resolution to land on the angle;
    // the ramp steps in tenths of a second.
    private float GetNextMoveInterval() => stopAtStartPos ? EntitySystem.TickInterval : 0.1f;
}
