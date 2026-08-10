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
/// callbacks 0.1s apart (<c>SpinUpMove</c> / <c>SpinDownMove</c>) that hand over to <c>RotateMove</c> once
/// the target speed is reached, and the angles themselves are integrated by the fixed tick.
/// </para>
/// <para>
/// The axis flags are named after the code's <c>m_vecMoveAng</c>, which is a QAngle: the flag Hammer labels
/// "X Axis" sets roll and the one it labels "Y Axis" sets pitch, and with neither set the brush yaws about
/// the world Z axis. That mismatch is the engine's, kept here so maps rotate the way they do in game.
/// </para>
/// <para>
/// The brush is solid and the player collides with it as it turns, unless the "Not Solid" flag is set.
/// Not simulated: the "Fan Pain" damage flag, and the pusher physics that would carry a player standing on
/// it or push one the brush rotates into.
/// </para>
/// <para>
/// The rotation sound follows the speed the way <c>RampPitchVol</c> does, except in pitch: the engine winds
/// the sample from 30% to 100% of its authored pitch as the brush comes up to speed, which the sound player
/// has no control for. Volume ramps, so a fan still fades in and out with its spin. The sound radius flags
/// pick an attenuation, which is likewise not exposed, so a small-radius fan carries as far as a large one.
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
    private Vector3 startAngles;
    private float turnedFromStart;
    private MoveDoneFunction moveDoneFunction;
    private SoundEvent? playing;

    /// <summary>
    /// Initializes a <c>func_rotating</c> from its keyvalues.
    /// </summary>
    /// <param name="system">The world this entity belongs to.</param>
    /// <param name="spawnInfo">The entity's keyvalues and spawn context.</param>
    public FuncRotating(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        // KeyValue: m_flFanFriction = atof(szValue) / 100
        FanFriction = KeyValues.GetFloatProperty("fanfriction", 20f) / 100f;

        // Prevent a divide by zero if the level designer forgot the friction
        if (FanFriction == 0f)
        {
            FanFriction = 1f;
        }

        // Set the axis of rotation
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

        // Check for reverse rotation
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
        // how far it has come round is no longer a component of the QAngle to be read back
        turnedFromStart = AngleMod(turnedFromStart + Speed * tickInterval);
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
    /// <param name="data">The input's parameter and sender, unused.</param>
    [EntityInput("Start")]
    private void InputStart(EntityInputData data)
    {
        stopAtStartPos = false;

        SetTargetSpeed(MaxSpeed);
    }

    /// <summary>Spins the brush up to <c>maxspeed</c> forwards.</summary>
    /// <remarks>
    /// Unlike every other way of starting it, this one leaves a pending <c>StopAtStartPos</c> alone, so a
    /// brush told to stop at its start angle still will. That asymmetry is the engine's.
    /// </remarks>
    /// <param name="data">The input's parameter and sender, unused.</param>
    [EntityInput("StartForward")]
    private void InputStartForward(EntityInputData data)
    {
        IsReversed = false;

        SetTargetSpeed(MaxSpeed);
    }

    /// <summary>Spins the brush up to <c>maxspeed</c> backwards.</summary>
    /// <param name="data">The input's parameter and sender, unused.</param>
    [EntityInput("StartBackward")]
    private void InputStartBackward(EntityInputData data)
    {
        stopAtStartPos = false;
        IsReversed = true;

        SetTargetSpeed(MaxSpeed);
    }

    /// <summary>Brings the brush to a stop wherever it happens to be.</summary>
    /// <param name="data">The input's parameter and sender, unused.</param>
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
    /// <param name="data">The input's parameter and sender, unused.</param>
    [EntityInput("Toggle")]
    private void InputToggle(EntityInputData data) => SetTargetSpeed(Speed > 0f ? 0f : MaxSpeed);

    /// <summary>Flips the spin direction, keeping the speed it was already turning at.</summary>
    /// <param name="data">The input's parameter and sender, unused.</param>
    [EntityInput("Reverse")]
    private void InputReverse(EntityInputData data)
    {
        stopAtStartPos = false;
        IsReversed = !IsReversed;

        SetTargetSpeed(Speed);
    }

    /// <summary>Stops the brush once it comes back around to the angle it spawned at.</summary>
    /// <param name="data">The input's parameter and sender, unused.</param>
    [EntityInput("StopAtStartPos")]
    private void InputStopAtStartPos(EntityInputData data)
    {
        stopAtStartPos = true;
        SetTargetSpeed(0f);
        SetMoveDoneTime(GetNextMoveInterval());
    }

    /// <summary>Sets the speed as a fraction of <c>maxspeed</c>; a negative fraction spins in reverse.</summary>
    /// <param name="data">Carries the fraction as its parameter.</param>
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
    /// Sets the speed to ramp towards, or jumps straight to it when the brush does not accelerate.
    /// </summary>
    /// <param name="speed">The target speed in degrees per second; the sign comes from the reverse state.</param>
    public void SetTargetSpeed(float speed)
    {
        // Make sure the sign is correct - positive for forward rotation, negative for reverse
        speed = MathF.Abs(speed);

        if (IsReversed)
        {
            speed = -speed;
        }

        TargetSpeed = speed;

        if (!HasSpawnFlags(SpawnFlag.AccelerateDecelerate))
        {
            // No acceleration, change to the new speed instantly
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
    /// A pending <c>StopAtStartPos</c> steers the last stretch, as <c>bmodels.cpp</c> does: more than 90
    /// degrees out it holds the speed it had, inside that it eases towards the angle still to go with a
    /// floor of 20 degrees per second, and once it is slow and within a degree it lands on the start.
    /// </remarks>
    /// <param name="newSpeed">The speed to apply, before clamping to <see cref="MaxSpeed"/>.</param>
    private void UpdateSpeed(float newSpeed)
    {
        var oldSpeed = Speed;
        var speed = Math.Clamp(newSpeed, -MaxSpeed, MaxSpeed);

        if (stopAtStartPos && speed < 100f)
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
    /// <param name="speed">The speed to turn at, already clamped.</param>
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
            // Changing speed, so ride the volume up or down with it
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
    private void StopAtStartAngles()
    {
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
    /// <param name="targetSpeed">The speed being shed towards, which is zero when reversing.</param>
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

    /// <summary>Signed degrees turned from the spawn orientation, in [-180, 180].</summary>
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
