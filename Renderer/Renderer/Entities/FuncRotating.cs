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
/// Not simulated: the fan sounds, the "Fan Pain" damage flag, and the pusher physics that would carry a
/// player standing on it or push one the brush rotates into.
/// </para>
/// </remarks>
public sealed class FuncRotating : BaseEntity
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
    }

    /// <summary>How the ramp progresses; Source picks between these with <c>SetMoveDone</c>.</summary>
    private enum MoveDoneFunction
    {
        None,
        SpinUp,
        SpinDown,
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

    private bool stopAtStartPos;
    private Vector3 startAngles;
    private MoveDoneFunction moveDoneFunction;

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

        startAngles = Angles;

        SetModel();

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

            case MoveDoneFunction.Rotate:
                RotateMove();
                break;
        }
    }

    /// <summary>Spins the brush up to <c>maxspeed</c>.</summary>
    /// <param name="data">The input's parameter and sender, unused.</param>
    [EntityInput("Start")]
    private void InputStart(EntityInputData data) => SetTargetSpeed(MaxSpeed);

    /// <summary>Brings the brush to a stop wherever it happens to be.</summary>
    /// <param name="data">The input's parameter and sender, unused.</param>
    [EntityInput("Stop")]
    private void InputStop(EntityInputData data) => SetTargetSpeed(0f);

    /// <summary>Starts the brush if it is stopped, stops it if it is spinning.</summary>
    /// <param name="data">The input's parameter and sender, unused.</param>
    [EntityInput("Toggle")]
    private void InputToggle(EntityInputData data) => Toggle();

    /// <summary>Flips the spin direction and runs back up to speed.</summary>
    /// <param name="data">The input's parameter and sender, unused.</param>
    [EntityInput("Reverse")]
    private void InputReverse(EntityInputData data)
    {
        IsReversed = !IsReversed;
        SetTargetSpeed(MaxSpeed);
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
        moveDoneFunction = MathF.Abs(TargetSpeed) > MathF.Abs(Speed)
            ? MoveDoneFunction.SpinUp
            : MoveDoneFunction.SpinDown;

        SetMoveDoneTime(GetNextMoveInterval());
    }

    /// <summary>
    /// Applies a new rotation speed, and lands exactly on the start angle when the brush was asked to stop
    /// there. Source's <c>UpdateSpeed</c>, minus the sound pitch and volume ramp.
    /// </summary>
    /// <remarks>
    /// A pending <c>StopAtStartPos</c> overrides a stop that would leave the brush short of its start
    /// angle: below 100 degrees per second it keeps crawling at 25 until the angle comes back around,
    /// then snaps onto it. Same shape as the engine's ramp, which also holds a slow speed until arrival.
    /// </remarks>
    /// <param name="newSpeed">The speed to apply, before clamping to <see cref="MaxSpeed"/>.</param>
    private void UpdateSpeed(float newSpeed)
    {
        var speed = Math.Clamp(newSpeed, -MaxSpeed, MaxSpeed);

        if (stopAtStartPos && MathF.Abs(speed) < 100f)
        {
            if (MathF.Abs(speed) <= 25f && MathF.Abs(GetAngleDeltaFromStart()) < 1f)
            {
                StopAtStartAngles();
                speed = 0f;
            }
            else
            {
                speed = SpinDirection(speed) * MathF.Max(MathF.Abs(speed), 25f);
            }
        }

        Speed = speed;
        AngularVelocity = MoveAngles * Speed;
    }

    /// <summary>
    /// Lands the brush on the angle it spawned at and forgets the pending stop. A snap, not movement, so
    /// the interpolation history goes with it.
    /// </summary>
    private void StopAtStartAngles()
    {
        TargetSpeed = 0f;
        stopAtStartPos = false;
        Angles = startAngles;
        SnapInterpolation();
    }

    /// <summary>
    /// Which way the brush is turning: from the speed offered, else the speed it already had, else the
    /// direction its reverse state implies. A brush crawling to a stop must not lose its direction.
    /// </summary>
    /// <param name="speed">The speed about to be applied.</param>
    private float SpinDirection(float speed)
    {
        if (speed != 0f)
        {
            return MathF.Sign(speed);
        }

        return Speed != 0f ? MathF.Sign(Speed) : (IsReversed ? -1f : 1f);
    }

    private void SpinUpMove()
    {
        var newSpeed = MathF.Abs(Speed) + 0.2f * MaxSpeed * FanFriction;
        var spinUpDone = newSpeed >= MathF.Abs(TargetSpeed);

        if (spinUpDone)
        {
            newSpeed = MathF.Abs(TargetSpeed);
        }

        UpdateSpeed(TargetSpeed < 0f ? -newSpeed : newSpeed);

        if (spinUpDone)
        {
            moveDoneFunction = MoveDoneFunction.Rotate;
            RotateMove();
        }
        else
        {
            SetMoveDoneTime(GetNextMoveInterval());
        }
    }

    private void SpinDownMove()
    {
        // Spins down slower than it spins up
        var newSpeed = MathF.Abs(Speed) - 0.1f * MaxSpeed * FanFriction;

        if (newSpeed < 0f)
        {
            newSpeed = 0f;
        }

        var spinDownDone = newSpeed <= MathF.Abs(TargetSpeed);

        if (spinDownDone)
        {
            newSpeed = MathF.Abs(TargetSpeed);
        }

        UpdateSpeed(TargetSpeed < 0f ? -newSpeed : newSpeed);

        if (spinDownDone)
        {
            moveDoneFunction = MoveDoneFunction.Rotate;
            RotateMove();
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
        var anglesPerTick = GetSpinAxisComponent(AngularVelocity) * EntitySystem.TickInterval;

        // Close enough that the next tick would overshoot the start angle: stop on it exactly
        if (MathF.Abs(angleDelta) < MathF.Abs(anglesPerTick))
        {
            SetTargetSpeed(0f);
            StopAtStartAngles();
        }
    }

    /// <summary>
    /// Reads the one QAngle component that is actually turning. <see cref="MoveAngles"/> is a signed unit
    /// basis vector, so projecting onto its absolute value picks that component out.
    /// </summary>
    /// <param name="angles">The angles, or angular rate, to read.</param>
    private float GetSpinAxisComponent(Vector3 angles) => Vector3.Dot(angles, Vector3.Abs(MoveAngles));

    /// <summary>Signed degrees from the spawn orientation, in [-180, 180].</summary>
    private float GetAngleDeltaFromStart()
    {
        var delta = AngleMod(GetSpinAxisComponent(Angles - startAngles));

        return delta > 180f ? delta - 360f : delta;
    }

    /// <summary>
    /// How long until the next move step. Stopping at the start position needs tick resolution to land on
    /// the angle; everything else ramps in tenths of a second.
    /// </summary>
    private float GetNextMoveInterval() => stopAtStartPos ? EntitySystem.TickInterval : 0.1f;
}
