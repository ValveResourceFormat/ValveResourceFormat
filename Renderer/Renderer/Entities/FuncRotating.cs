using ValveResourceFormat.Renderer.Audio;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>func_rotating</c>. A brush that spins about one axis at up to <c>maxspeed</c> degrees per second,
/// either snapping to speed or ramping up and down against <c>fanfriction</c>.
/// </summary>
/// <remarks>
/// <para>
/// Ported from Source's <c>CFuncRotating</c>, structure included: the speed ramp is a chain of move-done
/// callbacks 0.1s apart (<c>SpinUpMove</c> / <c>SpinDownMove</c>) that hand over to <c>RotateMove</c> at
/// the target speed, and the fixed tick integrates the angles.
/// </para>
/// <para>
/// The axis flags are named after the code's <c>m_vecMoveAng</c>, a QAngle, so Hammer's "X Axis" sets roll
/// and its "Y Axis" sets pitch, and with neither set the brush yaws about world Z. The mismatch is the
/// engine's, kept so maps rotate the way they do in game.
/// </para>
/// <para>
/// The brush is solid and the player collides with it as it turns, unless the "Not Solid" flag is set. The
/// "Fan Pain" damage flag is not simulated, nor is the pusher physics that would carry a player standing
/// on it or shove one it turns into.
/// </para>
/// <para>
/// The sound follows the speed like <c>RampPitchVol</c>, except in pitch: the engine winds the sample from
/// 30% to 100% pitch during spin-up, which the sound player cannot do. Volume still ramps, so a fan fades
/// in and out with its spin. The sound radius flags pick an attenuation, also not exposed, so a
/// small-radius fan carries as far as a large one.
/// </para>
/// </remarks>
public sealed class FuncRotating : BaseModelEntity
{
    /// <summary>
    /// What a <c>func_rotating</c>'s <c>spawnflags</c> mean. The axis flags are named for what they do;
    /// the engine's own constants name them for the QAngle component they set, which is why its
    /// <c>Z_AXIS</c> rolls and its <c>X_AXIS</c> pitches.
    /// </summary>
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
    }

    /// <summary>How the ramp progresses; Source picks between these with <c>SetMoveDone</c>.</summary>
    private enum MoveDoneFunction
    {
        None,
        SpinUp,
        SpinDown,
        Reverse,
        Rotate,
    }

    /// <summary>Gets the axis to rotate about, as a QAngle direction. Source's <c>m_vecMoveAng</c>.</summary>
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

    /// <summary>
    /// Gets the volume that sound reaches at full speed, 0 to 1. Authored as <c>volume</c>, 0 to 10.
    /// </summary>
    public float Volume { get; private set; } = 1f;

    private bool stopAtStartPos;
    private bool acceleratesAndDecelerates;
    private Vector3 startAngles;
    private float turnedFromStart;
    private MoveDoneFunction moveDoneFunction;
    private SoundEvent? playing;

    /// <summary>
    /// Initializes a <c>func_rotating</c> from its keyvalues.
    /// </summary>
    public FuncRotating(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        // KeyValue: m_flFanFriction = atof(szValue) / 100. Absent means zero rather than the FGD's 20,
        // as it does in the engine, so the guard below is what a map that omits it actually gets.
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
        Volume = Math.Clamp(KeyValues.GetFloatProperty("volume") / 10f, 0f, 1f);

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

        if (HasSpawnFlags(SpawnFlag.StartOn))
        {
            // Leave a magic delay for the client to start up, then toggle ourselves on
            SetNextThink(EntitySystem.CurrentTime + 0.2f);
        }
    }

    /// <summary>
    /// Starts a brush that spawned switched on. That deferred toggle is the only think this entity
    /// schedules, so there is nothing to dispatch on.
    /// </summary>
    public override void Think() => Toggle();

    /// <inheritdoc/>
    protected override void PhysicsSimulate(float tickInterval)
    {
        base.PhysicsSimulate(tickInterval);

        // Tracked as it turns, because once the body has turned off the axes the map authored it on,
        // how far it has come round is no longer a component of the QAngle to be read back.
        // Wrapped rather than run through AngleMod: that quantizes to 1/65536 of a turn and truncates
        // towards zero, so accumulating through it drops the same fraction of a step every tick and the
        // tracker walks away from the truth without bound. The engine never accumulates at all - it takes
        // the difference from the live angles and applies anglemod once, at the point of reading it.
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

    /// <summary>Spins the brush up to <c>maxspeed</c>, whichever way it was already set to turn.</summary>
    [EntityInput("Start")]
    private void InputStart(EntityInputData data)
    {
        stopAtStartPos = false;

        SetTargetSpeed(MaxSpeed);
    }

    /// <summary>Spins the brush up to <c>maxspeed</c> forwards.</summary>
    /// <remarks>
    /// Unlike the other ways of starting it, this one leaves a pending <c>StopAtStartPos</c> alone, so a
    /// brush told to stop at its start angle still does. The asymmetry is the engine's.
    /// </remarks>
    [EntityInput("StartForward")]
    private void InputStartForward(EntityInputData data)
    {
        IsReversed = false;

        SetTargetSpeed(MaxSpeed);
    }

    /// <summary>Spins the brush up to <c>maxspeed</c> backwards.</summary>
    [EntityInput("StartBackward")]
    private void InputStartBackward(EntityInputData data)
    {
        stopAtStartPos = false;
        IsReversed = true;

        SetTargetSpeed(MaxSpeed);
    }

    /// <summary>Brings the brush to a stop wherever it happens to be.</summary>
    [EntityInput("Stop")]
    private void InputStop(EntityInputData data)
    {
        stopAtStartPos = false;

        SetTargetSpeed(0f);
    }

    /// <summary>
    /// Starts the brush if it is stopped, stops it if it is spinning. Tests the speed rather than the
    /// angular velocity, as the input handler does; a brush that is running backwards starts again.
    /// </summary>
    [EntityInput("Toggle")]
    private void InputToggle(EntityInputData data) => SetTargetSpeed(Speed > 0f ? 0f : MaxSpeed);

    /// <summary>Flips the spin direction, keeping the speed it was already turning at.</summary>
    [EntityInput("Reverse")]
    private void InputReverse(EntityInputData data)
    {
        stopAtStartPos = false;
        IsReversed = !IsReversed;

        SetTargetSpeed(Speed);
    }

    /// <summary>Stops the brush once it comes back around to the angle it spawned at.</summary>
    [EntityInput("StopAtStartPos")]
    private void InputStopAtStartPos(EntityInputData data)
    {
        stopAtStartPos = true;
        SetTargetSpeed(0f);
        SetMoveDoneTime(GetNextMoveInterval());
    }

    /// <summary>
    /// Puts the brush back on its start angle and stops it there, without waiting to come round.
    /// </summary>
    /// <remarks>
    /// <c>OnReachedStart</c> is not fired, because it belongs to a pending <c>StopAtStartPos</c> running
    /// its course and a snap cancels that stop rather than completing it. Stopping still reports through
    /// <c>OnStopped</c>, like any other stop.
    /// </remarks>
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

    /// <summary>
    /// Changes the angle the brush treats as its start, the one it snaps and stops back to. The parameter
    /// is the new start angles as a QAngle.
    /// </summary>
    [EntityInput("SetStartPos")]
    private void InputSetStartPos(EntityInputData data)
    {
        startAngles = data.Vector(startAngles);

        // Re-based rather than zeroed: how far it has come round is measured from the new start, which is
        // what the engine reads off the live angles every time it asks
        turnedFromStart = AngleMod(GetAxisAngle(Angles) - GetAxisAngle(startAngles));
    }

    /// <summary>Makes the brush ramp up and down instead of snapping to speed.</summary>
    [EntityInput("EnableAccelDecel")]
    private void InputEnableAccelDecel(EntityInputData data) => acceleratesAndDecelerates = true;

    /// <summary>Makes the brush start and stop instantly.</summary>
    [EntityInput("DisableAccelDecel")]
    private void InputDisableAccelDecel(EntityInputData data) => acceleratesAndDecelerates = false;

    /// <summary>
    /// Sets the speed to the parameter's fraction of <c>maxspeed</c>; a negative fraction spins in reverse.
    /// </summary>
    [EntityInput("SetSpeed")]
    private void InputSetSpeed(EntityInputData data)
    {
        var fraction = data.Float();

        stopAtStartPos = false;
        IsReversed = fraction < 0f;
        SetTargetSpeed(Math.Clamp(MathF.Abs(fraction) * MaxSpeed, 0f, MaxSpeed));
    }

    /// <summary>
    /// Starts the brush if it is stopped, stops it if it is spinning. Source's <c>RotatingUse</c>, which is
    /// what a player pressing use, or the <c>Toggle</c> input, ends up calling.
    /// </summary>
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

    /// <summary>
    /// Applies a new rotation speed, and lands exactly on the start angle when the brush was asked to stop
    /// there. Source's <c>UpdateSpeed</c>, minus the sound pitch and volume ramp.
    /// </summary>
    /// <remarks>
    /// A pending <c>StopAtStartPos</c> steers the last stretch, like <c>bmodels.cpp</c>: over 90 degrees
    /// out it keeps its speed, inside that it eases towards the remaining angle but never below 20 degrees
    /// per second, and once slow and within a degree it lands on the start.
    /// </remarks>
    private void UpdateSpeed(float newSpeed)
    {
        var oldSpeed = Speed;
        var speed = Math.Clamp(newSpeed, -MaxSpeed, MaxSpeed);

        // The requested speed, not the clamped one: bmodels.cpp:911 tests flNewSpeed, which for a slow
        // brush with high friction can be the larger of the two and decide the approach a step earlier
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

    /// <summary>
    /// Applies the speed and reports the brush starting or stopping. Those are the two edges the engine
    /// starts and stops the rotation sound on, which is what <c>OnStarted</c> and <c>OnStopped</c> name.
    /// </summary>
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

    /// <summary>Starts the rotation sound, at the volume the speed it is starting from calls for.</summary>
    private void StartSound()
    {
        if (string.IsNullOrEmpty(SoundName))
        {
            return;
        }

        StopSound();

        playing = Sound.Play(SoundName, Transform.Translation, volume: GetRampedVolume());
    }

    /// <summary>Stops the rotation sound, if one is playing.</summary>
    private void StopSound()
    {
        playing?.Stop();
        playing = null;
    }

    /// <summary>
    /// Follows the volume up and down with the speed, Source's <c>RampPitchVol</c> without the pitch.
    /// </summary>
    private void RampVolume()
    {
        if (playing != null)
        {
            // FIXME: sound system does not allow this rn
            playing.VolumeOverride = GetRampedVolume();
        }
    }

    /// <summary>The volume for the current speed: full volume at <see cref="MaxSpeed"/>, silence stopped.</summary>
    private float GetRampedVolume() => Math.Clamp(Volume * (MathF.Abs(Speed) / MaxSpeed), 0f, 1f);

    /// <inheritdoc/>
    protected override void OnRemove()
    {
        StopSound();

        base.OnRemove();
    }

    /// <summary>
    /// Lands the brush on the angle it spawned at and forgets the pending stop. A snap, not movement, so
    /// the interpolation history goes with it.
    /// </summary>
    /// <remarks>
    /// The landing happens once, and the pending stop records whether it still has to. Both callers go
    /// through <see cref="SetTargetSpeed"/>, which for a brush that does not accelerate lands the stop
    /// before its caller can. The engine has the same two paths and does not mind, because there it is two
    /// assignments rather than an output a map can count.
    /// </remarks>
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

            // A brush still working its way back to the start angle keeps ramping, so that the approach
            // in UpdateSpeed goes on steering it
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

    /// <summary>
    /// Bleeds off a little speed, slower than it spins up.
    /// </summary>
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

    /// <summary>Comes to a stop before turning the other way.</summary>
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

    /// <summary>
    /// The at-speed state. Nothing to do while it just spins, so it wakes rarely; only a pending
    /// <c>StopAtStartPos</c> needs to watch the angle every tick.
    /// </summary>
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

    /// <summary>
    /// The component of a QAngle that lies on the axis this brush turns about, the engine's
    /// <c>checkAxis</c> (<c>bmodels.cpp:1121-1131</c>).
    /// </summary>
    private float GetAxisAngle(Vector3 angles)
    {
        if (MoveAngles.X != 0f)
        {
            return angles.X;
        }

        return MoveAngles.Y != 0f ? angles.Y : angles.Z;
    }

    /// <summary>
    /// Signed degrees turned from the spawn orientation, in [-180, 180]. The one place the quantization
    /// belongs, so it costs a fixed 1/65536 of a turn rather than compounding.
    /// </summary>
    private float GetAngleDeltaFromStart()
    {
        var delta = AngleMod(turnedFromStart);

        return delta > 180f ? delta - 360f : delta;
    }

    /// <summary>
    /// How long until the next move step. Stopping at the start position needs tick resolution to land on
    /// the angle; everything else ramps in tenths of a second.
    /// </summary>
    private float GetNextMoveInterval() => stopAtStartPos ? EntitySystem.TickInterval : 0.1f;
}
