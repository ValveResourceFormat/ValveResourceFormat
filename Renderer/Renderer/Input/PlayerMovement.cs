using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.Renderer.Gameplay;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Input;

/// <summary>
/// CS2-style FPS player movement controller. Simulates fixed 64 Hz ticks and draws the player between
/// the last two.
/// </summary>
public class PlayerMovement : IPlayerController
{
    // Half-extents around the hull center. Standing 32x32x72, ducked 32x32x54.
    private Vector3 StandingHullHalfExtents = new(16, 16, 36);
    private Vector3 DuckedHullHalfExtents = new(16, 16, 27);

    /// <summary>
    /// Gets or sets whether the player has the narrow hull and deep crouch of a VR player, a 16 by 16 footprint
    /// ducking to 32 units tall, rather than the 32 by 32 hull ducking to 54.
    /// </summary>
    public bool CompactHull
    {
        get;
        set
        {
            field = value;
            StandingHullHalfExtents = value ? new(8, 8, 36) : new(16, 16, 36);
            DuckedHullHalfExtents = value ? new(8, 8, 16) : new(16, 16, 27);
        }
    }

    // Movement constants from Source engine (movevars_shared.cpp)
    private const float GravityValue = 800f;              // sv_gravity
    private const float FrictionValue = 5.2f;             // sv_friction
    private const float StopSpeedValue = 80f;             // sv_stopspeed
    private const float AccelerateValue = 5.5f;           // sv_accelerate
    private const float JumpImpulseValue = 301.993377f;   // sv_jump_impulse = sqrt(2*800*57)
    private const float MaxVelocityValue = 3500f;         // sv_maxvelocity
    private const float WalkSpeedModifier = 0.52f;        // CS_PLAYER_SPEED_WALK_MODIFIER
    private const float DuckSpeedModifier = 0.34f;        // CS_PLAYER_SPEED_DUCK_MODIFIER

    private const float TickInterval = EntitySystem.TickInterval;

    private const float WalkTaperBand = 5f;               // Walk acceleration fades over this many u/s below the walk speed
    private const float WalkSpeedAllowance = 25f;         // Walking only lowers the max speed of a player within this of it

    // A hard landing lowers the max speed and the next jump, sv_bhop_time_window around it is a perfect hop
    private const float LandingPenaltyPerSpeed = 0.0005f;
    private const float LandingSpeedRecoveryRate = 1.1111894f; // Valve pulled this out of their ass, literally
    private const float LandingJumpRecoveryRate = 0.6f;
    private const double BhopWindowTicks = 0.25;

    private const float NonJumpVelocity = 140f;           // Moving up faster than this means airborne
    private const float AirMaxWishSpeed = 30f;            // Air-control wishspeed cap (AirAccelerate)

    private const float ViewHeightOffset = 8f;
    private float ViewHeightStanding => StandingHullHalfExtents.Z * 2f - ViewHeightOffset;

    private const float BunnyjumpMaxSpeedFactor = 1.1f;   // Only allow bunny jumping up to 1.1x max speed

    private const float SurfaceEpsilon = Rubikon.SurfaceEpsilon; // Keep-away margin maintained by TraceBBox
    private const float ContactNudge = SurfaceEpsilon / 4f; // Clearance kept short of a degenerate opposing surface in the margin-restore push
    private const float UntraceableDistanceSquared = Rubikon.Epsilon * Rubikon.Epsilon;
    private const float StepSize = 18f;                   // Maximum height of steps/obstacles player can climb
    private const float GroundProbeDistance = 2f;         // How far below the hull to look for ground contact
    private const float StepDownTolerance = 2f;           // A step may end at most this far below where it started
    private const float SlowStepSpeed = 64f;              // Below this speed a step onto a steep edge gets a second look

    private const float DuckSpeedIdeal = 8f;              // CS_PLAYER_DUCK_SPEED_IDEAL, duck amount per second
    private const float DuckSpeedMin = 1.5f;              // Below this a new duck cannot start, and unducking never runs slower
    private const float DuckSpamPenalty = 2f;             // Duck speed lost each time the duck key changes state
    private const float TimeBetweenDucks = 0.4f;          // sv_timebetweenducks
    private const float DuckUnflagAmount = 0.75f;         // Unducking drops the ducked flag once the amount falls to this
    private const float DuckSpeedRecoveryDistance = 64f;  // Moving this far from the last full duck speed spot recovers it faster
    private const int CrouchStuckMaxRise = 36;            // How far a freshly ducked hull may be lifted out of the floor
    private float CrouchHeightDifference => (StandingHullHalfExtents.Z - DuckedHullHalfExtents.Z) * 2f;

    /// <summary>Gets the current player velocity in world units per second.</summary>
    public Vector3 Velocity { get; private set; }
    private Vector3 TracePosition;
    private Vector3 Feet => TracePosition - new Vector3(0, 0, HullHalfExtents.Z);

    // Cap on a single frame's time, so a stall (load, breakpoint) is not owed back in ticks
    private const float MaxFrameDeltaTime = 0.1f;

    // Time since the last tick, and where that tick started the feet, to draw between the two
    private float Accumulator;
    private Vector3 PreviousFeet;
    private long TickCount;

    // A press can come and go in a frame that completes no tick, so it is held for the next one
    private bool JumpPressPending;

    // Times in ticks: the last jump press, the press a landing may still use, and the touchdown
    private double LastJumpPress;
    private double UsableJumpPress;
    private double LandedTime;
    private Vector2 LandedVelocity;
    private float LandedVelocityZ;

    /// <summary>
    /// Gets the player position at feet level (where the AABB touches the ground), as drawn this frame
    /// between the last two ticks.
    /// </summary>
    public Vector3 Position { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the player is currently on the ground.
    /// </summary>
    public bool OnGround { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the player was touching the ground in the previous tick.
    /// </summary>
    public bool WasOnGroundLastFrame { get; private set; }

    /// <summary>Upward velocity the last jump left the ground with, after the landing penalty.</summary>
    public float JumpImpulse { get; private set; }

    /// <summary>Whether a jump left the ground in this frame's ticks. Leaving it any other way does not count.</summary>
    public bool Jumped { get; private set; }

    // Last known non-overlapping position, restored when stuck
    private Vector3 LastValidPosition;
    private bool HasValidPosition;

    private float SlopeClipNormalZ = 1f;

    private readonly ViewEffects Effects = new();

    // Movement sound events (CS2, game_sounds_footsteps.vsndevts / game_sounds_player.vsndevts).
    // Footstep and land events depend on the surface underfoot, from FootstepSoundTable.
    private const string LadderStepSoundEvent = "CT_Ladder.StepLeft";
    private const string JumpSoundEvent = "Default.WalkJump";
    private const string GearSoundEvent = "Gear.JumpLand.CT";
    private const float GearVolume = 0.1f;
    private const string FallDamageSoundEvent = "Player.DamageFall";

    private static readonly string[] MovementSounds = [
        LadderStepSoundEvent,
        JumpSoundEvent,
        GearSoundEvent,
        FallDamageSoundEvent,
    ];

    private const float StepSoundVelWalk = 90f;           // Standing walk speed threshold for footsteps
    private const float StepSoundVelRun = 220f;           // Standing run speed threshold for footsteps
    private const float WalkingStepVolume = 0.8f;         // Slightly quieter steps below run speed (the authored volume is 0.9)
    private const float LadderStepVolume = 0.5f;
    private const float LadderStepSoundTime = 0.35f;
    private const float LandMinFallSpeed = 270f;          // Quieter landings are silent (a normal jump lands at ~302)
    private const float FallDamageSpeed = 580f;

    private float StepSoundTime;
    private bool StepIsLeft;
    private FootstepSoundTable.SurfaceSounds? CachedSurfaceSounds;

    private readonly GameFileLoader FileLoader;
    private FootstepSoundTable FootstepSounds = new();
    private readonly List<Audio.SoundHandle> ActiveMovementSounds = [];

    /// <summary>
    /// Gets the view punch from landings and weapon fire, as (pitch, yaw) degrees. This is additive on top
    /// of the player's aim for rendering only.
    /// </summary>
    public Vector2 ViewPunchDegrees => Effects.ViewPunchDegrees;

    /// <summary>
    /// Adds to the view punch, which then decays at <see cref="ViewPunchDecay"/>.
    /// </summary>
    /// <param name="degrees">Punch as (pitch, yaw) degrees.</param>
    public void AddViewPunch(Vector2 degrees) => Effects.AddViewPunch(degrees);

    /// <summary>Gets whether the player is being simulated - the viewer's walk mode.</summary>
    public bool IsActive => Input.WalkMode;

    private bool HoldingCtrl => Input.Holding(TrackedKeys.Control);
    private bool HoldingShift => Input.Holding(TrackedKeys.Shift);

    private UserInput Input { get; }
    private PhysicsWorld? Physics => Input.PhysicsWorld is { IsEmpty: false } physics ? physics : null;

    /// <summary>
    /// Test-only extra static collision half-spaces layered onto the traces, each a
    /// <see cref="Vector4"/> whose XYZ is the outward unit normal and W the plane offset d;
    /// the half-space n.x &lt;= d is solid. Lets headless tests build walls and overhangs without
    /// a physics world. Empty in normal use.
    /// </summary>
    public List<Vector4> DebugCollisionPlanes { get; } = [];

    /// <summary>
    /// Gets the duck amount from 0 (standing) to 1 (fully crouched). It animates the view and pose; the
    /// collision hull only switches to ducked once it reaches 1, or straight away in the air.
    /// </summary>
    public float CrouchBlend { get; private set; }

    // Ducked selects the hull; DuckingFlag is FL_DUCKING, which unducking drops early
    private bool Ducked;
    private bool Ducking;
    private bool DuckingFlag;
    private bool DesiresDuck;
    private float DuckSpeed = DuckSpeedIdeal;
    private float TimeSinceDuck = TimeBetweenDucks;
    private Vector2 LastPositionAtFullDuckSpeed;

    // Eye height relative to standing, eased toward the duck amount and away from DuckRootOffset
    private float DuckViewOffset;

    // Both offsets ease at the hull height difference per 0.2 seconds
    private float DuckOffsetSpeed => CrouchHeightDifference / 0.2f;

    /// <summary>
    /// Gets how far the rendered body sits from <see cref="Position"/>. An airborne hull switch moves
    /// the feet by half the hull height difference; this takes it back out and eases it away, so the
    /// body does not jump.
    /// </summary>
    public float DuckRootOffset { get; private set; }

    /// <summary>
    /// Gets the current eye height blended between standing and crouched positions.
    /// </summary>
    public float BlendedEyeHeight { get; private set; }

    /// <summary>The current eye position</summary>
    public Vector3 EyePosition { get; private set; }

    /// <inheritdoc/>
    public Vector3 ViewForward => Input.Camera.Forward;

    /// <inheritdoc/>
    public Vector3 ViewAngles => Input.Camera.GetQAngle();

    // Buttons seen since the entity tick last collected them, and what it collected that time. Frames
    // are folded together rather than replaced so a tap between two ticks survives to be reported.
    private TrackedKeys PendingButtons;
    private TrackedKeys HeldButtons;
    private TrackedKeys LastCollectedButtons;

    private float DuckSpeedModifierActive => float.Lerp(1f, DuckSpeedModifier, CrouchBlend);
    /// <summary>
    /// Gets the current collision hull half-extents, either the standing or the ducked hull.
    /// </summary>
    public Vector3 HullHalfExtents => Ducked ? DuckedHullHalfExtents : StandingHullHalfExtents;

    private float SurfaceFriction = 1.0f;
    private const float JumpFriction = 0.25f;
    private const float WalkableSlope = 0.7f; // ~45 degrees

    private const float MaxClimbSpeed = 200f;
    private const float LadderScaleSpeed = 0.78f;         // sv_ladder_scale_speed
    private const float LadderAngle = -0.707f;            // sv_ladder_angle
    private const float LadderDampen = 0.2f;              // sv_ladder_dampen
    private const float LadderJumpSpeed = 270f;           // Push off the face when jumping from a ladder
    private const float LadderJumpBlockTime = 0.2f;       // Jumping off is ignored this long after mounting

    /// <summary>Gets a value indicating whether the player is climbing a ladder.</summary>
    public bool OnLadder { get; private set; }

    /// <summary>Gets the normal of the ladder surface being climbed, pointing away from it.</summary>
    public Vector3 LadderNormal { get; private set; }
    private float LadderJumpBlockRemaining;

    // A ladder just walked off the top of is grabbed again, but only after a ground move, not after a jump
    private bool HasWalkMovedSinceLastJump;

    /// <summary>Gets or sets a value indicating whether the controller should reinitialize its position from the camera on the next tick.</summary>
    public bool Initialize { get; set; }
    /// <summary>Gets or sets a value indicating whether bunny-hopping is allowed by holding the jump key.</summary>
    public bool AutoBunnyHop { get; set; } = true;
    /// <summary>Gets or sets a value indicating whether the view glides up and down stair steps instead of popping.</summary>
    public bool StepSmoothingEnabled { get; set; } = true;
    /// <summary>Gets or sets the base run speed in world units per second.</summary>
    public float RunSpeed { get; set; } = 250f;

    /// <summary>sv_airaccelerate for movement maps, far above the engine default so surf and strafe maps have air control.</summary>
    public const float AirAccelerateMovementMaps = 150f;
    /// <summary>sv_airaccelerate for competitive play.</summary>
    public const float AirAccelerateCompetitive = 12f;

    /// <summary>Gets or sets sv_airaccelerate, the air-control acceleration rate.</summary>
    public float AirAccelerate { get; set; } = AirAccelerateMovementMaps;

    /// <summary>
    /// Gets or sets view_punch_decay, the exponential rate the landing punch recovers at.
    /// </summary>
    public float ViewPunchDecay { get; set; } = 18f;

    /// <summary>
    /// Initializes a new <see cref="PlayerMovement"/> bound to the given user input source.
    /// </summary>
    /// <param name="input">The user input instance used to read key state.</param>
    /// <param name="fileLoader">The loader for the footstep sounds of each surface.</param>
    public PlayerMovement(UserInput input, GameFileLoader fileLoader)
    {
        Input = input;
        FileLoader = fileLoader;
    }

    /// <summary>
    /// Reinitialize the character position from the current camera location.
    /// Call this when switching from noclip to FPS movement mode.
    /// </summary>
    public void ResetPosition(Camera camera)
    {
        TracePosition = camera.Location - Vector3.UnitZ * ViewHeightStanding + new Vector3(0, 0, StandingHullHalfExtents.Z);
        Velocity = Vector3.Zero;
        HasValidPosition = false; // Do not restore positions from before the reset
        pushViewLag = Vector3.Zero;
        pushViewLagTimeLeft = 0f;
        SlopeClipNormalZ = 1f;
        Effects.Reset();

        Accumulator = 0f;
        JumpPressPending = false;
        LastJumpPress = double.NegativeInfinity;
        UsableJumpPress = double.NegativeInfinity;
        LandedTime = double.NegativeInfinity;
        LandedVelocityZ = 0f;

        CrouchBlend = 0f;
        Ducked = false;
        Ducking = false;
        DuckingFlag = false;
        DuckSpeed = DuckSpeedIdeal;
        TimeSinceDuck = TimeBetweenDucks;
        DuckViewOffset = 0f;
        DuckRootOffset = 0f;

        CacheSounds();
    }

    /// <summary>
    /// Moves the player somewhere else outright, keeping their velocity.
    /// </summary>
    /// <param name="feetPosition">Where the feet arrive.</param>
    /// <param name="angles">View angles to adopt, or null to keep the current one.</param>
    public void Teleport(Vector3 feetPosition, Vector3? angles)
    {
        // Carry the already-placed camera along so the view does not trail by a frame
        var moved = feetPosition - Feet;
        Position += moved;
        PreviousFeet += moved;
        EyePosition += moved;
        Input.Camera.Location += moved;

        TracePosition = feetPosition + new Vector3(0, 0, HullHalfExtents.Z);
        HasValidPosition = false; // Do not restore positions from before the teleport
        pushViewLag = Vector3.Zero;
        pushViewLagTimeLeft = 0f;
        Effects.ClearStepOffset();

        if (angles is { } viewAngles)
        {
            Input.Camera.SetFromQAngle(viewAngles);
        }
    }

    /// <summary>
    /// Folds this frame's buttons into what the next entity tick will collect. Runs once per rendered
    /// frame, since the tick that collects them is slower than the frames that see them.
    /// </summary>
    private void SampleButtons()
    {
        HeldButtons = Input.Keys;
        PendingButtons |= HeldButtons;
    }

    /// <inheritdoc/>
    public PlayerButtonState ConsumeButtons()
    {
        var held = PendingButtons;
        var changed = held ^ LastCollectedButtons;

        LastCollectedButtons = held;

        // Only what is still physically down survives into the next tick, so a button tapped between two
        // ticks reports pressed once and released on the tick after, rather than sticking down
        PendingButtons = HeldButtons;

        return new PlayerButtonState(held, changed & held, changed & ~held);
    }

    /// <summary>Loads the footstep sounds and pre-decodes the sounds movement can fire, so the first step does not stall mid-frame.</summary>
    public void CacheSounds()
    {
        FootstepSounds = new FootstepSoundTable(FileLoader);

        foreach (var soundEvent in MovementSounds)
        {
            Sound.Cache(soundEvent);
        }
    }

    /// <summary>
    /// Runs the ticks this frame completes, then places the camera between the last two.
    /// </summary>
    public void ProcessMovement(Camera camera, float deltaTime)
    {
        if (Initialize)
        {
            ResetPosition(camera);
            TryUnstuck(ref TracePosition, HullHalfExtents);
            Velocity = Input.Velocity;
            Initialize = false;

            // Nothing pressed while the camera was flying was pressed at anything in the world, and a key
            // held across the switch is not a fresh press either, so start from what is down right now
            PendingButtons = Input.Keys;
            HeldButtons = Input.Keys;
            LastCollectedButtons = Input.Keys;

            // The player is drawn a tick behind the simulation, so without a tick in hand the view would
            // hold still at the camera for a tick before carrying on with the speed the camera had
            Tick(camera);
        }

        SampleButtons();

        JumpPressPending |= Input.Pressed(TrackedKeys.Space) || Input.Holding(TrackedKeys.MouseWheelDown | TrackedKeys.MouseWheelUp);
        Jumped = false;

        deltaTime = MathF.Min(deltaTime, MaxFrameDeltaTime);
        Accumulator += deltaTime;

        while (Accumulator >= TickInterval)
        {
            Accumulator -= TickInterval;
            Tick(camera);
        }

        Effects.DecayViewPunch(deltaTime, ViewPunchDecay);
        DecayPushViewLag(deltaTime);

        var horizontalSpeed = new Vector2(Velocity.X, Velocity.Y).Length();
        Effects.Update(deltaTime, horizontalSpeed, StepSmoothingEnabled);

        var feet = Vector3.Lerp(PreviousFeet, Feet, Accumulator / TickInterval);
        Position = feet - new Vector3(0, 0, Effects.StepOffset) + pushViewLag;

        DuckRootOffset = MathUtils.Approach(DuckRootOffset, 0f, DuckOffsetSpeed * deltaTime);
        DuckViewOffset = MathUtils.Approach(DuckViewOffset, -CrouchHeightDifference * CrouchBlend - DuckRootOffset, DuckOffsetSpeed * deltaTime);
        BlendedEyeHeight = ViewHeightStanding + DuckViewOffset;
        EyePosition = Position + Vector3.UnitZ * BlendedEyeHeight;
        camera.Location = EyePosition;
        camera.Roll = float.DegreesToRadians(Effects.LandingRollDegrees);

        UpdateMovementSounds();
    }

    /// <summary>
    /// Simulates one tick, with the keys held and the view angles of the frame that completes it.
    /// </summary>
    private void Tick(Camera camera)
    {
        TickCount++;
        PreviousFeet = Feet;

        var jumpPressed = JumpPressPending;
        JumpPressPending = false;

        var position = TracePosition;

        Duck(TickInterval, ref position);

        var isWalking = HoldingShift && !HoldingCtrl && !Ducking && !DuckingFlag;

        var playerHull = HullHalfExtents;

        WasOnGroundLastFrame = OnGround;

        // Impact speed to use if this tick turns out to land: ground movement zeroes the
        // vertical velocity, so it must be sampled while still falling
        var fallSpeed = MathF.Max(0f, -Velocity.Z);

        CategorizePosition(ref position, playerHull);

        var justLanded = !WasOnGroundLastFrame && OnGround;
        var wantsToJump = jumpPressed || (AutoBunnyHop && Input.Holding(TrackedKeys.Space));

        if (justLanded)
        {
            OnLanded(fallSpeed, wantsToJump, position, playerHull);
            RecordLanding(TickCount, fallSpeed);
        }

        LadderJumpBlockRemaining = MathF.Max(0f, LadderJumpBlockRemaining - TickInterval);

        if (!LadderMove(ref position, playerHull, camera, jumpPressed))
        {
            OnLadder = false;
        }

        var maxSpeed = CalculateMaxSpeed(isWalking);

        CheckJumpButton(jumpPressed, maxSpeed, position, playerHull);

        var (wishdir, wishspeed) = CalculateWishVelocity(camera.Yaw, maxSpeed);

        // Where the fall started, to solve when inside the tick a landing touched down
        var moveStartZ = position.Z;
        var moveStartVelocityZ = Velocity.Z;

        // Friction, acceleration and gravity act over the whole tick, so the move uses the velocity
        // halfway through it and the other half is added after
        var deferredVelocity = Vector3.Zero;

        if (OnLadder)
        {
            // LadderMove set the whole velocity: no gravity, friction or air acceleration on a ladder
        }
        else if (OnGround)
        {
            ZeroVerticalVelocity();

            var startVelocity = Velocity;
            WalkMove(wishdir, wishspeed, maxSpeed, isWalking);
            deferredVelocity = (Velocity - startVelocity) * 0.5f;
            Velocity -= deferredVelocity;
        }
        else
        {
            deferredVelocity = AirAccelerateHalf(wishdir, wishspeed);

            var halfGravity = new Vector3(0f, 0f, GravityValue * TickInterval * 0.5f);
            Velocity -= halfGravity;
            deferredVelocity -= halfGravity;

            // An upward push acts as an acceleration against gravity rather than a velocity
            Velocity += new Vector3(0f, 0f, BaseVelocity.Z * TickInterval);
        }

        ClampVelocity();

        // Added after friction and acceleration, and taken out again after the move
        var pushVelocity = BaseVelocity with { Z = 0f };
        Velocity += pushVelocity;

        // The freshest impact speed for a landing the move below may produce is the tick's midpoint
        fallSpeed = MathF.Max(fallSpeed, -Velocity.Z);

        if (OnLadder)
        {
            position = TryPlayerMove(position, playerHull);
        }
        else if (OnGround)
        {
            HasWalkMovedSinceLastJump = true;

            // Stop when the velocity at the end of the tick would be tiny
            if ((Velocity + deferredVelocity).LengthSquared() < 1f)
            {
                Velocity = Vector3.Zero;
            }
            else
            {
                position = GroundMove(position, playerHull);
                StayOnGround(ref position, playerHull);
            }
        }
        else
        {
            position = TryPlayerMove(position, playerHull);
        }

        // A move stopped dead also drops the rest of the tick's acceleration
        if (Velocity != Vector3.Zero)
        {
            Velocity += deferredVelocity - pushVelocity;
        }

        CategorizePosition(ref position, playerHull);

        CheckStuck(ref position, playerHull);

        // The other landing path: airborne at the start of the tick, grounded by the move
        // above. justLanded already handled the start-of-tick categorize path.
        if (!justLanded && !WasOnGroundLastFrame && OnGround)
        {
            OnLanded(fallSpeed, wantsToJump, position, playerHull);

            var (fraction, touchdownSpeed) = SolveTouchdown(position.Z - moveStartZ, moveStartVelocityZ, fallSpeed);
            RecordLanding(TickCount + fraction, touchdownSpeed);
        }

        UpdateStepSounds(position, playerHull, TickInterval);

        TracePosition = position;
    }

    /// <summary>
    /// Touchdown effects for a landing at <paramref name="fallSpeed"/>: the view punch and roll,
    /// and the landing thud, gear rattle and pain grunt.
    /// </summary>
    private void OnLanded(float fallSpeed, bool willBunnyHop, Vector3 position, Vector3 halfExtents)
    {
        Effects.OnLanded(fallSpeed, audible: fallSpeed > LandMinFallSpeed && !willBunnyHop);

        if (fallSpeed <= LandMinFallSpeed)
        {
            return;
        }

        // A landing that immediately turns into another jump (perfect bhop) skips the land thud
        if (!willBunnyHop)
        {
            PlaySound(TraceFootstepSurface(position, halfExtents).Land, position, halfExtents);
            PlaySound(GearSoundEvent, position, halfExtents, GearVolume);
        }

        // The pain grunt plays even when bhopping - a hard fall hurts either way
        if (fallSpeed >= FallDamageSpeed)
        {
            PlaySound(FallDamageSoundEvent, position, halfExtents);
        }

        SetStepSoundTime(walking: false);
    }

    /// <summary>Plays footstep sounds periodically based on ground speed.</summary>
    private void UpdateStepSounds(Vector3 position, Vector3 halfExtents, float deltaTime)
    {
        var speedSqr = Velocity.LengthSquared();
        var walkSpeed = RunSpeed * WalkSpeedModifier;

        if (speedSqr < walkSpeed * walkSpeed || (HoldingShift && !HoldingCtrl))
        {
            if (speedSqr < 10f)
            {
                // Standing still keeps the timer armed, so moving again does not step instantly
                SetStepSoundTime(walking: false);
            }

            return;
        }

        // CBasePlayer::UpdateStepSound
        StepSoundTime = Math.Max(StepSoundTime - deltaTime, 0f);
        if (StepSoundTime > 0f)
        {
            return;
        }

        var speed = MathF.Sqrt(speedSqr);

        if (speed < StepSoundVelWalk || !(OnGround || OnLadder))
        {
            return;
        }

        if (OnLadder)
        {
            StepSoundTime = LadderStepSoundTime;
            PlaySound(LadderStepSoundEvent, position, halfExtents, LadderStepVolume);
            return;
        }

        var walking = speed < StepSoundVelRun;
        SetStepSoundTime(walking);

        // Walking pace plays quiet steps, running plays at full volume
        PlayFootstep(position, halfExtents, walking ? WalkingStepVolume : null);
    }

    private void PlayFootstep(Vector3 position, Vector3 halfExtents, float? volume = null)
    {
        var sounds = TraceFootstepSurface(position, halfExtents);

        StepIsLeft = !StepIsLeft;
        PlaySound(StepIsLeft ? sounds.StepLeft : sounds.StepRight, position, halfExtents, volume);
    }

    // Unlike the movement traces this passes through player clips to reach the ground they cover
    private FootstepSoundTable.SurfaceSounds TraceFootstepSurface(Vector3 position, Vector3 halfExtents)
    {
        // Reaches a step height under the feet, as deep as the ground under a clip can be while it is stood on
        var reach = new Vector3(0, 0, StepSize);
        var sweep = Physics?.TraceAABB(position, position - reach, halfExtents, Rubikon.FootstepCollisionName) ?? new Rubikon.TraceResult();
        Input.EntitySystem?.TraceAABB(position, position - reach, halfExtents, detectStartSolid: false, ref sweep);

        // Where surfaces meet under the footprint, the one under the center wins
        var feet = position - new Vector3(0, 0, halfExtents.Z);
        var center = Physics?.TraceRay(position, feet - reach, Rubikon.FootstepCollisionName) ?? new Rubikon.TraceResult();
        Input.EntitySystem?.TraceRay(position, feet - reach, Rubikon.FootstepCollisionName, ref center);

        var restsOnCenter = center.Hit && center.HitPosition.Z >= sweep.HitPosition.Z - halfExtents.Z - GroundProbeDistance;
        var sounds = FootstepSounds.Find(restsOnCenter ? center.SurfacePropertyHash : sweep.SurfacePropertyHash);

        // Decoded once a surface is first walked on, ahead of the steps and the landing that follow
        if (sounds != CachedSurfaceSounds)
        {
            CachedSurfaceSounds = sounds;
            Sound.Cache(sounds.StepLeft);
            Sound.Cache(sounds.StepRight);
            Sound.Cache(sounds.Land);
        }

        return sounds;
    }

    /// <summary>
    /// 400ms walking, 300ms running, +100ms ducked.
    /// </summary>
    private void SetStepSoundTime(bool walking)
    {
        StepSoundTime = walking ? 0.4f : 0.3f;

        if (DuckingFlag)
        {
            StepSoundTime += 0.1f;
        }
    }

    private void PlaySound(string soundEventName, Vector3 position, Vector3 halfExtents, float? volume = null)
    {
        // Convert from hull center to feet position; the sound event applies its own position offset
        var feetPosition = position - new Vector3(0, 0, halfExtents.Z);
        var handle = Sound.Play(soundEventName, feetPosition, volume: volume);

        if (handle.IsValid)
        {
            ActiveMovementSounds.Add(handle);
        }
    }

    /// <summary>
    /// Tracks the player's position for our movement sounds ("use_entity_position_if_local_player" in the sound
    /// event data). Left at their emit position they would fall behind at run speed and get louder while receding,
    /// since the footstep distance-volume curve peaks at ~116 units.
    /// </summary>
    private void UpdateMovementSounds()
    {
        for (var i = ActiveMovementSounds.Count - 1; i >= 0; i--)
        {
            var handle = ActiveMovementSounds[i];

            if (!handle.Playing)
            {
                ActiveMovementSounds.RemoveAt(i);
                continue;
            }

            handle.Position = Position;
        }
    }

    /// <summary>
    /// Remembers the last clear position and restores it when a tick ends embedded.
    /// </summary>
    private void CheckStuck(ref Vector3 position, Vector3 halfExtents)
    {
        if (!IsStuck(position, halfExtents))
        {
            LastValidPosition = position;
            HasValidPosition = true;
            return;
        }

        // Shallow penetration - grazing a turning mover's rim - frees with a nudge that keeps the
        // player's speed; the restore below is a teleport and costs all of it
        Span<Vector3> nudges =
        [
            Vector3.UnitZ,
            Vector3.UnitX, -Vector3.UnitX,
            Vector3.UnitY, -Vector3.UnitY,
        ];

        for (var distance = 0.25f; distance <= 1f; distance *= 2f)
        {
            foreach (var direction in nudges)
            {
                var nudged = position + direction * distance;

                if (!IsStuck(nudged, halfExtents))
                {
                    position = nudged;
                    LastValidPosition = nudged;
                    HasValidPosition = true;
                    return;
                }
            }
        }

        if (HasValidPosition)
        {
            position = LastValidPosition;
            Velocity = Vector3.Zero;
            Effects.ClearStepOffset(); // deliberate teleport, nothing to glide
        }
    }

    private void ZeroVerticalVelocity()
    {
        Velocity = new Vector3(Velocity.X, Velocity.Y, 0);
        SlopeClipNormalZ = 1f; // zeroed Z has no slope provenance
    }

    /// <summary>
    /// Animates the duck amount toward the duck key and switches the hull at either end. On the
    /// ground the feet stay put; in the air the hull shrinks and grows around its center.
    /// </summary>
    private void Duck(float deltaTime, ref Vector3 position)
    {
        var duckHeld = HoldingCtrl;

        if (DesiresDuck != duckHeld)
        {
            DesiresDuck = duckHeld;
            DuckSpeed = MathF.Max(0f, DuckSpeed - DuckSpamPenalty);
        }

        TimeSinceDuck += deltaTime;

        var canStartDuck = DuckSpeed >= DuckSpeedMin && (DuckingFlag || TimeSinceDuck >= TimeBetweenDucks);
        var wantsDuck = duckHeld && canStartDuck;

        DuckSpeed = MathUtils.Approach(DuckSpeed, DuckSpeedIdeal, deltaTime * 3f);

        var horizontalPosition = new Vector2(position.X, position.Y);

        if (DuckSpeed >= DuckSpeedIdeal)
        {
            LastPositionAtFullDuckSpeed = horizontalPosition;
        }
        else if (CrouchBlend is <= 0f or >= 1f
            && Vector2.DistanceSquared(LastPositionAtFullDuckSpeed, horizontalPosition) > DuckSpeedRecoveryDistance * DuckSpeedRecoveryDistance)
        {
            DuckSpeed = MathUtils.Approach(DuckSpeed, DuckSpeedIdeal, deltaTime * 6f);
        }

        if (wantsDuck)
        {
            if (CrouchBlend < 1f)
            {
                Ducking = true;
            }

            if (Ducking)
            {
                // Ducking is a little slower than unducking
                CrouchBlend = MathUtils.Approach(CrouchBlend, 1f, DuckSpeed * 0.8f * deltaTime);

                if (CrouchBlend >= 1f || !OnGround)
                {
                    FinishDuck(ref position);
                }
            }
        }
        else
        {
            if (CrouchBlend > 0f)
            {
                Ducking = true;
            }

            if (Ducking)
            {
                if (CanUnduck(position))
                {
                    CrouchBlend = MathUtils.Approach(CrouchBlend, 0f, MathF.Max(DuckSpeedMin, DuckSpeed) * deltaTime);
                    SetDuckedHull(false, ref position);

                    if (CrouchBlend <= 0f || !OnGround)
                    {
                        FinishUnduck(ref position);
                    }

                    if (CrouchBlend <= DuckUnflagAmount)
                    {
                        DuckingFlag = false;
                    }
                }
                else
                {
                    // Stay fully ducked under the obstruction and try again once clear of it
                    CrouchBlend = 1f;
                    SetDuckedHull(true, ref position);
                    Ducking = false;
                    DuckingFlag = true;
                }
            }
        }
    }

    private void FinishDuck(ref Vector3 position)
    {
        SetDuckedHull(true, ref position);

        // Airborne, the legs tuck up instead of the head coming down
        if (!OnGround && !OnLadder)
        {
            ShiftFeet(CrouchHeightDifference / 2f, ref position);
        }

        Ducking = false;
        DuckingFlag = true;
        TimeSinceDuck = 0f;
        CrouchBlend = 1f;

        FixCrouchStuck(ref position);
    }

    private void FinishUnduck(ref Vector3 position)
    {
        SetDuckedHull(false, ref position);

        if (!OnGround && !OnLadder)
        {
            ShiftFeet(-CrouchHeightDifference / 2f, ref position);
        }

        Ducking = false;
        DuckingFlag = false;
        CrouchBlend = 0f;
    }

    /// <summary>
    /// Moves the hull for an airborne duck or unduck, leaving the body and the eyes where they were
    /// to ease after it.
    /// </summary>
    private void ShiftFeet(float shift, ref Vector3 position)
    {
        position.Z += shift;
        PreviousFeet.Z += shift;
        DuckRootOffset -= shift;
        DuckViewOffset -= shift;
    }

    /// <summary>
    /// Switches the collision hull, keeping the feet where they are.
    /// </summary>
    private void SetDuckedHull(bool ducked, ref Vector3 position)
    {
        if (Ducked == ducked)
        {
            return;
        }

        var feet = position.Z - HullHalfExtents.Z;
        Ducked = ducked;
        position.Z = feet + HullHalfExtents.Z;
    }

    /// <summary>
    /// Whether the standing hull fits: at the feet on the ground, or swept down to where an
    /// airborne unduck puts the feet.
    /// </summary>
    /// <remarks>
    /// Tested with the hull grown slightly rather than shrunk as <see cref="IsStuck"/> does. A
    /// shrunk probe lets the player stand up with a ceiling edge just inside the hull's side, and
    /// every move after that starts solid.
    /// </remarks>
    private bool CanUnduck(Vector3 position)
    {
        var standingCenter = position + new Vector3(0, 0, StandingHullHalfExtents.Z - HullHalfExtents.Z);
        var clearance = StandingHullHalfExtents + new Vector3(StuckProbeShrink);

        if (OnGround || OnLadder)
        {
            return !TraceBBox(standingCenter, standingCenter + Vector3.UnitZ, clearance, detectStartSolid: true).StartSolid;
        }

        var drop = new Vector3(0, 0, CrouchHeightDifference / 2f);
        var trace = TraceBBox(standingCenter, standingCenter - drop, clearance, detectStartSolid: true);
        return !trace.Hit && !trace.StartSolid;
    }

    /// <summary>
    /// Lifts a freshly ducked hull out of whatever it landed in, a unit at a time.
    /// </summary>
    private void FixCrouchStuck(ref Vector3 position)
    {
        var halfExtents = HullHalfExtents;

        if (!IsStuck(position, halfExtents))
        {
            return;
        }

        for (var rise = 1; rise <= CrouchStuckMaxRise; rise++)
        {
            var test = position + new Vector3(0, 0, rise);

            if (!IsStuck(test, halfExtents))
            {
                position = test;
                return;
            }
        }
    }

    /// <summary>
    /// Keeps the player glued to the ground on downward slopes/stairs.
    /// </summary>
    private void StayOnGround(ref Vector3 position, Vector3 halfExtents)
    {
        var trace = TraceBBox(position, position + new Vector3(0, 0, -StepSize), halfExtents);

        if (IsWalkableGroundHit(trace))
        {
            var preSnapZ = position.Z;

            SnapToGround(ref position, trace, snapDownOnly: true);

            // Feed downward stair snaps into the view smoothing
            SmoothStep(position.Z - preSnapZ);
        }
    }

    /// <summary>
    /// Hands a stair step to the view smoothing, which eases it in. The previous tick moves with it, so
    /// the tick interpolation does not draw the same step a second time.
    /// </summary>
    private void SmoothStep(float delta)
    {
        Effects.OnStep(delta);

        if (StepSmoothingEnabled)
        {
            PreviousFeet.Z += delta;
        }
    }

    /// <summary>
    /// Whether a trace landed on a surface flat enough to stand on.
    /// </summary>
    private static bool IsWalkableGroundHit(Rubikon.TraceResult trace)
        => trace.Hit && trace.HitNormal.Z >= WalkableSlope;

    /// <summary>
    /// Snaps the position's Z onto the ground found by <paramref name="trace"/>, keeping a
    /// SurfaceEpsilon perpendicular gap. XY is preserved so slopes do not induce sliding.
    /// </summary>
    /// <param name="position">The hull center position to adjust.</param>
    /// <param name="trace">The downward trace whose hit describes the ground.</param>
    /// <param name="snapDownOnly">When set, the snap may only lower the player (pure ground-following).</param>
    private static void SnapToGround(ref Vector3 position, Rubikon.TraceResult trace, bool snapDownOnly)
    {
        // A zero-distance hit is the trace start echoed back; nothing to snap
        if (trace.IsMinimalDistance)
        {
            return;
        }

        // The margin trace already stops with the full perpendicular SurfaceEpsilon gap
        // (a vertical standoff of SurfaceEpsilon / normal.Z on slopes), so the hit
        // position is the resting height as-is
        var groundZ = trace.HitPosition.Z;

        if (snapDownOnly)
        {
            groundZ = MathF.Min(position.Z, groundZ);
        }

        position = new Vector3(position.X, position.Y, groundZ);
    }

    /// <summary>
    /// Checks for walkable ground below, a step down for a standing player and 2 units otherwise, and
    /// snaps onto it.
    /// </summary>
    private void CategorizePosition(ref Vector3 position, Vector3 halfExtents)
    {
        // A player already standing looks a full step down, so stepping onto a steep edge above the
        // floor, like the end of a plank, still stands on the floor beside it
        var probeDistance = OnGround && !OnLadder ? StepSize : GroundProbeDistance;
        var result = TraceBBox(position, position + new Vector3(0, 0, -probeDistance), halfExtents);

        var grounded = IsWalkableGroundHit(result);

        // A steep surface can shadow walkable ground; retry with quarter-footprint hulls.
        // The quadrant probes only decide groundedness: their hit positions are quarter-hull
        // standoffs, and snapping the full hull to one embeds it into the shadowing surface.
        // Brush entity ground also needs the hull center over the entity: the probe is hull-sized
        // and catches rim slivers past a mover's edge, and snapping onto those sinks the hull into
        // the surface and welds a departing player to the mover.
        var snapToHit = grounded && HasEntityFooting(result, position, halfExtents);

        if (!grounded && result.Hit)
        {
            grounded = ProbeGroundQuadrants(position, halfExtents, probeDistance);
        }

        // NonJumpVelocity guard, on the Z velocity a plain projection would have produced (see SlopeClipNormalZ)
        OnGround = grounded && Velocity.Z * SlopeClipNormalZ < NonJumpVelocity && !(OnLadder && Velocity.Z > 0.02f);

        // Only a walkable main-probe hit assigns the ground entity: a quadrant-rescued grounding
        // stands on a steep sliver, and riding that would drag the player along an entity they left
        GroundEntity = OnGround && snapToHit ? result.HitEntity : null;

        // SurfaceFriction resets to 1 each call and is quartered while airborne and rising, but only
        // below NonJumpVelocity: faster ascents take an early-out that never reaches that line. A full
        // jump (302 u/s) therefore keeps full air control until 140 u/s, and the dead window is just
        // the last stretch up to the apex, restoring the tick it passes.
        SurfaceFriction = !OnGround && Velocity.Z > 0f && Velocity.Z <= NonJumpVelocity ? JumpFriction : 1f;

        if (OnGround && snapToHit)
        {
            SnapToGround(ref position, result, snapDownOnly: false);
        }
    }

    /// <summary>
    /// Whether a ground hit's entity really is underfoot: the world always is, a brush entity only
    /// when a ray from the hull center down reaches it.
    /// </summary>
    private bool HasEntityFooting(in Rubikon.TraceResult result, Vector3 position, Vector3 halfExtents)
    {
        if (result.HitEntity is not { } entity || entity == Input.EntitySystem?.World)
        {
            return true;
        }

        return entity.Collider?.TraceRay(
            position, position - new Vector3(0, 0, halfExtents.Z + GroundProbeDistance), Rubikon.Cs2PlayerCollisionFilter) is { Hit: true };
    }

    /// <summary>
    /// Retries the ground probe per hull corner.
    /// </summary>
    private bool ProbeGroundQuadrants(Vector3 position, Vector3 halfExtents, float probeDistance)
    {
        var quarterExtents = new Vector3(halfExtents.X * 0.5f, halfExtents.Y * 0.5f, halfExtents.Z);

        Span<Vector2> corners =
        [
            new Vector2(-1, -1),
            new Vector2(1, -1),
            new Vector2(-1, 1),
            new Vector2(1, 1),
        ];

        foreach (var corner in corners)
        {
            var center = position + new Vector3(corner.X * quarterExtents.X, corner.Y * quarterExtents.Y, 0);
            var probe = TraceBBox(center, center + new Vector3(0, 0, -probeDistance), quarterExtents);

            if (IsWalkableGroundHit(probe))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Attempts to find a valid position if stuck inside geometry.
    /// </summary>
    private bool TryUnstuck(ref Vector3 position, Vector3 halfExtents)
    {
        if (!IsStuck(position, halfExtents))
        {
            return true;
        }

        // No downward candidates: don't unstuck into a fall
        Span<Vector3> directions =
        [
            Vector3.UnitZ,        // Up
            Vector3.UnitX,        // Right
            -Vector3.UnitX,       // Left
            Vector3.UnitY,        // Forward
            -Vector3.UnitY,       // Back
            Vector3.Normalize(new Vector3(1, 1, 0)),   // Diagonal
            Vector3.Normalize(new Vector3(-1, 1, 0)),
            Vector3.Normalize(new Vector3(1, -1, 0)),
            Vector3.Normalize(new Vector3(-1, -1, 0)),
        ];

        for (var distance = 1f; distance < 100f; distance += 10f)
        {
            foreach (var dir in directions)
            {
                var testPos = position + dir * distance;

                if (!IsStuck(testPos, halfExtents))
                {
                    position = testPos;
                    Velocity = Vector3.Zero;
                    return true;
                }
            }
        }

        return false;
    }

    // Overlap probes shrink the hull by this, so a hull resting its SurfaceEpsilon gap away is not inside
    private const float StuckProbeShrink = SurfaceEpsilon / 2f;

    /// <summary>
    /// Check whether the hull overlaps solid geometry at the given position.
    /// </summary>
    private bool IsStuck(Vector3 position, Vector3 halfExtents)
    {
        var probe = TraceBBox(position, position + new Vector3(0, 0, 1f), halfExtents - new Vector3(StuckProbeShrink), detectStartSolid: true);
        return probe.StartSolid;
    }

    /// <summary>
    /// Slides the hull at a constant <see cref="Velocity"/> for the rest of the tick, clipping the
    /// velocity against every surface it meets.
    /// </summary>
    private Vector3 TryPlayerMove(Vector3 start, Vector3 halfExtents)
    {
        const int MaxBumps = 6;

        var position = start;
        var timeLeft = TickInterval;

        // Stop dead if clipping ever turns the velocity against the entry velocity (corner ping-pong)
        var entryVelocity = Velocity;

        // Planes hit without making progress; movement must be clipped to parallel all of them
        Span<Vector3> planes = stackalloc Vector3[MaxBumps];
        var planeCount = 0;

        for (var bump = 0; bump < MaxBumps && timeLeft > 0f; bump++)
        {
            var delta = Velocity * timeLeft;

            if (delta.LengthSquared() < UntraceableDistanceSquared)
            {
                break;
            }

            var result = TraceBBox(position, position + delta, halfExtents);

            if (!result.Hit)
            {
                return position + delta;
            }

            NoteImpact(result);

            // Advance to the hit point (already margin-adjusted by TraceBBox)
            var fraction = result.Distance / delta.Length();

            position = result.HitPosition;
            timeLeft -= fraction * timeLeft;

            // Progress invalidates the accumulated planes
            if (fraction > 0)
            {
                planeCount = 0;
            }

            // 2024-11-07 CS2 update
            if (!OnGround && Velocity.Z < 0f && result.HitNormal.Z >= WalkableSlope
                && new Vector2(Velocity.X, Velocity.Y).LengthSquared() < 1f)
            {
                Velocity = Vector3.Zero;
                return position;
            }

            // Re-contacting a surface already in the set teaches the clip nothing, so it must not
            // consume a slot. Matches the GroundMove guard.
            var newPlane = !ContainsPlane(planes[..planeCount], result.HitNormal);

            if (newPlane)
            {
                planes[planeCount++] = result.HitNormal;
            }

            // ClipToPlanes also clips a delta; this one is spent, so it takes a scratch copy
            var clippedDelta = delta;

            // The reversal guard only applies to a genuinely moving player, and only to a real
            // reversal. A corner clip commonly leaves the velocity perpendicular to the way the
            // tick entered, where an equality test fires and zeroes everything - including the Z
            // that the jump is riding on, so the player hangs at the apex. Matches the GroundMove guard.
            if (!ClipToPlanes(planes[..planeCount], ref clippedDelta, out var velocity, out var clipNormalZ)
                || (entryVelocity.LengthSquared() > 1f && Vector3.Dot(velocity, entryVelocity) < 0f))
            {
                // Trapped by three or more planes, or clipping reversed the move
                Velocity = Vector3.Zero;
                return position;
            }

            Velocity = velocity;
            SlopeClipNormalZ = clipNormalZ;

            // A repeated flush contact neither advanced nor learned anything, and repeating it
            // would only burn the bump budget: the move is as constrained as it is going to get
            if (!newPlane && fraction <= 0f)
            {
                break;
            }
        }

        return position;
    }

    /// <summary>
    /// Clips the movement to parallel every accumulated plane.
    /// Returns false when no direction satisfies all planes.
    /// </summary>
    private bool ClipToPlanes(ReadOnlySpan<Vector3> planes, ref Vector3 delta, out Vector3 velocity, out float clipNormalZ)
    {
        velocity = Velocity;
        return TryClipToPlanes(planes, ref delta, ref velocity, OnGround, out clipNormalZ);
    }

    /// <summary>
    /// Shared plane resolution: prefers a single-plane clip that does not drive into any other
    /// plane, falls back to the two-plane crease, and fails (zeroing both vectors) when no
    /// direction satisfies every plane.
    /// </summary>
    private static bool TryClipToPlanes(ReadOnlySpan<Vector3> planes, ref Vector3 delta, ref Vector3 velocity, bool onGround, out float clipNormalZ)
    {
        for (var i = 0; i < planes.Length; i++)
        {
            var candidateVelocity = velocity;
            var candidateDelta = delta;
            var candidateNormalZ = ClipVelocity(ref candidateDelta, ref candidateVelocity, planes[i], onGround);

            var valid = true;
            for (var j = 0; j < planes.Length; j++)
            {
                if (j != i && Vector3.Dot(candidateVelocity, planes[j]) < 0f)
                {
                    valid = false;
                    break;
                }
            }

            if (valid)
            {
                delta = candidateDelta;
                velocity = candidateVelocity;
                clipNormalZ = candidateNormalZ;
                return true;
            }
        }

        clipNormalZ = 1f;

        // Two planes form a crease to slide along
        if (planes.Length == 2)
        {
            ClipToCrease(ref delta, ref velocity, planes[0], planes[1]);
            return true;
        }

        velocity = Vector3.Zero;
        delta = Vector3.Zero;
        return false;
    }

    /// <summary>
    /// Whether <paramref name="normal"/> is already among <paramref name="planes"/>, so a repeated
    /// flush contact against the same surface does not consume a bump slot.
    /// </summary>
    private static bool ContainsPlane(ReadOnlySpan<Vector3> planes, Vector3 normal)
    {
        foreach (var plane in planes)
        {
            if (Vector3.Dot(plane, normal) > 0.999f)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Constrains movement to the crease line between two planes.
    /// </summary>
    private static void ClipToCrease(ref Vector3 delta, ref Vector3 velocity, Vector3 plane1, Vector3 plane2)
    {
        var crease = Vector3.Cross(plane1, plane2);
        var creaseLengthSquared = crease.LengthSquared();

        // Near-parallel planes have no crease direction; stop dead
        if (creaseLengthSquared < 1e-12f)
        {
            velocity = Vector3.Zero;
            delta = Vector3.Zero;
            return;
        }

        crease /= MathF.Sqrt(creaseLengthSquared);
        velocity = Vector3.Dot(crease, velocity) * crease;
        delta = Vector3.Dot(crease, delta) * crease;
    }

    /// <summary>
    /// Ground counterpart of <see cref="TryPlayerMove"/>: slides at a constant <see cref="Velocity"/>
    /// with step (stairs) support. The tick's walls are accumulated and the velocity is clipped to
    /// parallel all of them, so a later bump slides along instead of re-entering one.
    /// </summary>
    private Vector3 GroundMove(Vector3 position, Vector3 halfExtents)
    {
        const int MaxBumps = 8;

        var remaining = TickInterval;
        var entryVelocity = Velocity;

        // Walls hit this tick; the velocity is clipped to parallel all of them
        Span<Vector3> planes = stackalloc Vector3[MaxBumps];
        var planeCount = 0;

        for (var bump = 0; bump < MaxBumps && remaining > 1e-6f; bump++)
        {
            var delta = Velocity * remaining;

            if (delta.LengthSquared() < UntraceableDistanceSquared)
            {
                break;
            }

            var (moved, fraction, hitNormal, hit) = StepSweep(position, delta, halfExtents);
            position = moved;

            if (!hit)
            {
                break;
            }

            remaining -= fraction * remaining;

            // Re-contacting a surface already in the set teaches the clip nothing, so it must not
            // consume a slot. Combined with a zero fraction it also means the bump neither
            // advanced nor learned anything, and repeating it would only burn the remaining
            // budget: the move is as constrained as it is going to get, so stop after the reclip.
            var newPlane = !ContainsPlane(planes[..planeCount], hitNormal);

            if (newPlane)
            {
                planes[planeCount++] = hitNormal;
            }

            // A genuinely trapped move (three or more planes with no valid direction) stops
            // dead. The reversal guard only applies to a moving player: when clipping flips
            // the velocity against the way the tick entered. At rest (or crawling) the entry
            // velocity is ~0, so it must not fire - otherwise the first wall contact zeroes
            // the acceleration every tick and the player can never slide out along a wall.
            // ClipToPlanes also clips a delta; this one is spent, so it takes a scratch copy
            var clippedDelta = delta;

            if (!ClipToPlanes(planes[..planeCount], ref clippedDelta, out var velocity, out var clipNormalZ)
                || (entryVelocity.LengthSquared() > 1f && Vector3.Dot(velocity, entryVelocity) < 0f))
            {
                Velocity = Vector3.Zero;
                break;
            }

            Velocity = velocity;
            SlopeClipNormalZ = clipNormalZ;

            if (!newPlane && fraction <= 0f)
            {
                break;
            }
        }

        return position;
    }

    /// <summary>
    /// One swept ground step with step (stairs) support: try the direct move, and also a stepped-up
    /// move that settles back down, and keep whichever travelled farther laterally. Reports the
    /// lateral fraction achieved, the surface normal the move stopped against, and whether it was
    /// blocked at all (a fully cleared stepped move reports no hit).
    /// </summary>
    private (Vector3 position, float fraction, Vector3 hitNormal, bool hit) StepSweep(Vector3 start, Vector3 delta, Vector3 halfExtents)
    {
        var direct = TraceBBox(start, start + delta, halfExtents);

        if (!direct.Hit)
        {
            return (start + delta, 1f, Vector3.UnitZ, false);
        }

        NoteImpact(direct);

        // Direct branch: stop at the wall
        var directPosition = direct.HitPosition;
        var directLateral = LateralProgress(start, directPosition, delta);

        // Stepped branch: step up as far as headroom allows, sweep, then settle back down
        var stepUpEnd = start + new Vector3(0, 0, StepSize);
        var upTrace = TraceBBox(start, stepUpEnd, halfExtents);
        var steppedStart = upTrace.Hit ? upTrace.HitPosition : stepUpEnd;

        var steppedSweep = TraceBBox(steppedStart, steppedStart + delta, halfExtents);

        if (steppedSweep.Hit)
        {
            NoteImpact(steppedSweep);
        }

        // Blocked at once by a wall the direct sweep missed: it's a wall we're sliding along, so
        // report it without moving and let the caller retry the step along it
        if (steppedSweep.Hit && steppedSweep.IsMinimalDistance
            && !ContainsPlane([direct.HitNormal], steppedSweep.HitNormal))
        {
            return (start, 0f, steppedSweep.HitNormal, true);
        }

        var steppedSlide = steppedSweep.Hit ? steppedSweep.HitPosition : steppedStart + delta;

        var downEnd = steppedSlide + new Vector3(0, 0, -(StepSize + GroundProbeDistance));
        var downTrace = TraceBBox(steppedSlide, downEnd, halfExtents);

        // Reject unwalkable or embedded landings; the down tolerance keeps a low ceiling
        // from turning a step into a ledge drop
        var landingInvalid = !downTrace.Hit
            || downTrace.IsMinimalDistance
            || (downTrace.HitNormal.Z < WalkableSlope && !SlowStepFindsFooting(steppedSlide, halfExtents))
            || downTrace.HitPosition.Z - start.Z < -StepDownTolerance;

        if (!landingInvalid)
        {
            var steppedPosition = downTrace.HitPosition;
            var steppedLateral = LateralProgress(start, steppedPosition, delta);

            if (steppedLateral >= directLateral)
            {
                var stepDist = steppedPosition.Z - directPosition.Z;
                if (stepDist > 0f)
                {
                    SmoothStep(stepDist);
                }

                // The stepped move is blocked only if its lateral sweep hit a wall
                return (steppedPosition, LateralFraction(start, steppedPosition, delta), steppedSweep.HitNormal, steppedSweep.Hit);
            }
        }

        return (directPosition, LateralFraction(start, directPosition, delta), direct.HitNormal, true);
    }

    /// <summary>
    /// Second chance for a slow player whose step landed on a steep surface, such as a bevelled curb
    /// edge: the step still counts when a slightly narrower hull finds walkable ground below, and so
    /// does the full hull a unit further along the velocity. The player then lands on the steep
    /// part and slides up onto the top.
    /// </summary>
    private bool SlowStepFindsFooting(Vector3 slide, Vector3 halfExtents)
    {
        if (Velocity.LengthSquared() >= SlowStepSpeed * SlowStepSpeed)
        {
            return false;
        }

        var stepDown = new Vector3(0, 0, -(StepSize + GroundProbeDistance));
        var narrowExtents = halfExtents - new Vector3(2f, 2f, 0f);
        var narrow = TraceBBox(slide, slide + stepDown * 1.5f, narrowExtents);

        if (!narrow.Hit || narrow.HitNormal.Z < WalkableSlope)
        {
            return false;
        }

        var ahead = TraceBBox(slide, slide + Vector3.Normalize(Velocity), halfExtents);

        if (ahead.Hit && ahead.IsMinimalDistance)
        {
            return false;
        }

        var aheadPosition = ahead.Hit ? ahead.HitPosition : slide + Vector3.Normalize(Velocity);
        var below = TraceBBox(aheadPosition, aheadPosition + stepDown, halfExtents);
        return below.Hit && below.HitNormal.Z >= WalkableSlope;
    }

    /// <summary>
    /// How far <paramref name="position"/> got from <paramref name="start"/> along the lateral
    /// (XY) direction <paramref name="delta"/> meant to go, signed: a move ending up behind
    /// where it started reports a negative distance. Zero for negligible lateral intent.
    /// </summary>
    private static float LateralProgress(Vector3 start, Vector3 position, Vector3 delta)
    {
        var intended = new Vector2(delta.X, delta.Y);
        var intendedLength = intended.Length();

        if (intendedLength < 1e-4f)
        {
            return 0f;
        }

        var achieved = new Vector2(position.X - start.X, position.Y - start.Y);
        return Vector2.Dot(achieved, intended / intendedLength);
    }

    /// <summary>
    /// Fraction of a move's intended lateral (XY) distance that <paramref name="position"/>
    /// reached from <paramref name="start"/>. Returns 1 for negligible lateral intent so the
    /// caller treats the move as unobstructed.
    /// </summary>
    private static float LateralFraction(Vector3 start, Vector3 position, Vector3 delta)
    {
        var intended = new Vector2(delta.X, delta.Y).Length();

        if (intended < 1e-4f)
        {
            return 1f;
        }

        return MathUtils.Saturate(LateralProgress(start, position, delta) / intended);
    }

    /// <summary>
    /// Clips velocity against a surface normal. Returns the normal.Z of a walkable-slope clip
    /// (1 otherwise) so callers can recover the plain-projection vertical velocity.
    /// </summary>
    private static float ClipVelocity(ref Vector3 delta, ref Vector3 velocity, Vector3 normal, bool onGround)
    {
        // Walkable slopes on ground maintain horizontal speed
        if (onGround && normal.Z >= WalkableSlope)
        {
            var horizontalVel = new Vector3(velocity.X, velocity.Y, 0);
            var horizontalSpeed = horizontalVel.Length();

            if (horizontalSpeed > 0.001f)
            {
                var horizontalDir = horizontalVel / horizontalSpeed;
                var projectedDir = MathUtils.ProjectOntoPlane(horizontalDir, normal);

                if (projectedDir.LengthSquared() > 0.001f)
                {
                    velocity = Vector3.Normalize(projectedDir) * horizontalSpeed;
                }
            }

            var horizontalDelta = new Vector3(delta.X, delta.Y, 0);
            var deltaLength = horizontalDelta.Length();

            if (deltaLength > 0.001f)
            {
                var deltaDir = horizontalDelta / deltaLength;
                var projectedDeltaDir = MathUtils.ProjectOntoPlane(deltaDir, normal);

                if (projectedDeltaDir.LengthSquared() > 0.001f)
                {
                    delta = Vector3.Normalize(projectedDeltaDir) * deltaLength;
                }
            }

            return normal.Z;
        }
        else
        {
            delta = MathUtils.ProjectOntoPlane(delta, normal);
            velocity = MathUtils.ProjectOntoPlane(velocity, normal);
            return 1f;
        }
    }

    /// <summary>
    /// Prevent excessive speed gain from bunnyhopping
    /// Ported from cs_gamemovement.cpp PreventBunnyJumping()
    /// </summary>
    private void PreventBunnyJumping()
    {
        var maxscaledspeed = BunnyjumpMaxSpeedFactor * RunSpeed;
        var spd = Velocity.Length();

        if (spd <= maxscaledspeed)
        {
            return;
        }

        Velocity *= maxscaledspeed / spd;
    }

    /// <summary>
    /// Jumps on a press, on a held key with auto bunny hopping, or on a press held over from just
    /// before landing. Presses arrive at tick starts, and every time here is in ticks.
    /// </summary>
    private void CheckJumpButton(bool pressed, float maxSpeed, Vector3 position, Vector3 halfExtents)
    {
        if (pressed)
        {
            var sinceLastPress = TickCount - LastJumpPress;
            LastJumpPress = TickCount;

            // sv_jump_spam_penalty_time: a press within a tick of the previous one is ignored
            if (sinceLastPress <= 1)
            {
                pressed = false;
            }
            else if (!InBhopWindow(UsableJumpPress))
            {
                // An earlier press still inside the landing window is kept, else this one is used on landing
                UsableJumpPress = TickCount;
            }
        }

        if (pressed || (AutoBunnyHop && Input.Holding(TrackedKeys.Space)))
        {
            Jump(maxSpeed, position, halfExtents);
            return;
        }

        // A press buffered from just before landing, or held back by the landing tick, still jumps
        if (UsableJumpPress >= LandedTime - BhopWindowTicks && UsableJumpPress < LandedTime + 1)
        {
            Jump(maxSpeed, position, halfExtents);
            return;
        }

        UsableJumpPress = double.NegativeInfinity;
    }

    private bool InBhopWindow(double time) => time >= LandedTime - BhopWindowTicks && time < LandedTime + BhopWindowTicks;

    /// <summary>
    /// Leaves the ground with the jump impulse, lowered by a recent hard landing. A press timed on the
    /// landing keeps the speed landed with; any other jump has to wait out the landing tick.
    /// </summary>
    private void Jump(float maxSpeed, Vector3 position, Vector3 halfExtents)
    {
        // The usable press is kept while airborne: the player may still land inside the window
        if (!OnGround || OnLadder)
        {
            return;
        }

        var jumpTime = (double)TickCount;
        var useLandingVelocity = false;

        if (InBhopWindow(UsableJumpPress) || (AutoBunnyHop && TickCount <= LandedTime + 1))
        {
            // A perfect bunny hop keeps the speed it landed with, if that was above the max speed
            useLandingVelocity = AutoBunnyHop || LandedVelocity.LengthSquared() > maxSpeed * maxSpeed;
        }

        if (useLandingVelocity)
        {
            jumpTime = LandedTime;
            Velocity = new Vector3(LandedVelocity, Velocity.Z);
        }
        else if (TickCount < LandedTime + 1)
        {
            return;
        }

        UsableJumpPress = double.NegativeInfinity;

        if (!AutoBunnyHop)
        {
            PreventBunnyJumping();
        }

        // Sampled before the jump impulse, which is what makes the takeoff loud
        var loudTakeoffFootstep = Velocity.Length() > WalkSpeedModifier * RunSpeed;

        OnGround = false;
        Jumped = true;
        HasWalkMovedSinceLastJump = false;

        // A standing jump loses the first half tick of gravity
        var impulse = Ducking || DuckingFlag ? JumpImpulseValue : JumpImpulseValue - (GravityValue * 0.5f * (TickInterval * 0.5f));

        JumpImpulse = impulse * JumpVelocityScale(jumpTime);
        Velocity = new Vector3(Velocity.X, Velocity.Y, JumpImpulse);
        SlopeClipNormalZ = 1f; // jump impulse is genuine vertical velocity

        // Jumping off a mover keeps its motion, the ground base velocity inheritance
        Velocity += RideVelocity;

        if (loudTakeoffFootstep)
        {
            PlayFootstep(position, halfExtents);
        }

        PlaySound(JumpSoundEvent, position, halfExtents);
        PlaySound(GearSoundEvent, position, halfExtents, GearVolume);
    }

    private void RecordLanding(double time, float fallSpeed)
    {
        LandedTime = time;
        LandedVelocity = new Vector2(Velocity.X, Velocity.Y);
        LandedVelocityZ = -fallSpeed;
    }

    /// <summary>
    /// When inside the tick a fall touched down, as a fraction of the tick, and the speed it touched
    /// down with, solved from z0 + v0 t - g t^2 / 2. Falls back to the end of the tick.
    /// </summary>
    private static (float Fraction, float FallSpeed) SolveTouchdown(float dropZ, float startVelocityZ, float fallSpeed)
    {
        var discriminant = (startVelocityZ * startVelocityZ) - (2f * GravityValue * dropZ);

        if (dropZ >= 0f || startVelocityZ >= 0f || discriminant < 0f)
        {
            return (1f, fallSpeed);
        }

        var time = Math.Clamp((startVelocityZ + MathF.Sqrt(discriminant)) / GravityValue, 0f, TickInterval);
        return (time / TickInterval, (GravityValue * time) - startVelocityZ);
    }

    private float LandingPenalty => Math.Clamp(1f + (LandedVelocityZ * LandingPenaltyPerSpeed), 0.2f, 1f);

    /// <summary>A hard landing caps the max speed, recovering with every whole tick since the landing.</summary>
    private float LandingSpeedScale()
    {
        var ticksSinceLanding = (float)Math.Floor(TickCount - LandedTime);
        return MathF.Min((ticksSinceLanding * TickInterval * LandingSpeedRecoveryRate) + (LandingPenalty * LandingPenalty), 1f);
    }

    /// <summary>A hard landing also lowers the next jump, recovering over time.</summary>
    private float JumpVelocityScale(double jumpTime)
    {
        var secondsSinceLanding = (float)((jumpTime - LandedTime + 1) * TickInterval);
        return MathF.Min(LandingPenalty + (secondsSinceLanding * LandingJumpRecoveryRate), 1f);
    }

    /// <summary>
    /// Gets the entity the player stands on - a brush entity, or the world entity on the map itself -
    /// and null in the air. Set from the ground probe every tick.
    /// </summary>
    public Entities.BaseEntity? GroundEntity { get; private set; }

    /// <summary>
    /// Gets the velocity of the mover currently carrying the player, zero on still ground. Riding is
    /// positional, so <see cref="Velocity"/> never contains it.
    /// </summary>
    public Vector3 RideVelocity => GroundEntity?.GetSurfaceVelocity(TracePosition) ?? Vector3.Zero;

    /// <inheritdoc/>
    public void Push(Vector3 delta)
    {
        if (delta == Vector3.Zero)
        {
            return;
        }

        TracePosition += delta;
        PreviousFeet += delta;

        // The view stays where it was drawn and catches up by the next tick, as the pusher is drawn
        // moving there over the same interval
        pushViewLag -= delta;
        pushViewLagTimeLeft = TickInterval;
    }

    private Vector3 BaseVelocity;

    /// <inheritdoc/>
    public void SetBaseVelocity(Vector3 velocity)
    {
        if (velocity == Vector3.Zero)
        {
            // The push carries on as momentum, its upward part is in the velocity already
            Velocity += BaseVelocity with { Z = 0f };
            BaseVelocity = Vector3.Zero;
            return;
        }

        // Lifted a unit off the ground, or the next ground probe would hold the player down
        if (velocity.Z > 0f && OnGround)
        {
            OnGround = false;
            GroundEntity = null;
            Push(Vector3.UnitZ);
        }

        BaseVelocity = velocity;
    }

    // How far the view still trails the hull the entity tick pushed, and how much of the tick interval
    // remains to close it
    private Vector3 pushViewLag;
    private float pushViewLagTimeLeft;

    /// <inheritdoc/>
    public Vector3 HullCenter => TracePosition;

    /// <inheritdoc/>
    public Rubikon.TraceResult TraceHull(Vector3 startCenter, Vector3 endCenter) => TraceBBox(startCenter, endCenter, HullHalfExtents);

    /// <inheritdoc/>
    public bool IsHullStuck(Vector3 center)
    {
        // In place, unlike IsStuck, whose sweep lets a hull shallowly inside an entity count as free
        // because it could step out
        var probe = HullHalfExtents - new Vector3(StuckProbeShrink);

        return TraceBBoxRaw(center, center + Vector3.UnitZ, probe, detectStartSolid: true).StartSolid
            || Input.EntitySystem?.OverlapsSolidEntity(center, probe) == true;
    }

    /// <summary>
    /// Closes a slice of the view's lag behind a push, at the constant rate that finishes it by the
    /// next tick.
    /// </summary>
    private void DecayPushViewLag(float deltaTime)
    {
        if (pushViewLagTimeLeft <= deltaTime)
        {
            pushViewLag = Vector3.Zero;
            pushViewLagTimeLeft = 0f;
            return;
        }

        pushViewLag -= pushViewLag * (deltaTime / pushViewLagTimeLeft);
        pushViewLagTimeLeft -= deltaTime;
    }

    private bool LadderMove(ref Vector3 position, Vector3 halfExtents, Camera camera, bool jumpPressed)
    {
        if (Physics == null)
        {
            return false;
        }

        // View basis with pitch, unlike ground movement: on a ladder the player goes where they look
        var basis = EntityTransformHelper.EulerAnglesToRotationMatrixRadians(new Vector3(camera.Pitch, camera.Yaw, 0f));
        var forward = new Vector3(basis.M11, basis.M12, basis.M13);
        var left = new Vector3(basis.M21, basis.M22, basis.M23);

        var forwardMove = (Input.Holding(TrackedKeys.W) ? 1f : 0f) - (Input.Holding(TrackedKeys.S) ? 1f : 0f);
        var leftMove = (Input.Holding(TrackedKeys.A) ? 1f : 0f) - (Input.Holding(TrackedKeys.D) ? 1f : 0f);

        // Already on a ladder, keep pressing into it; otherwise look for one where the player is heading
        Vector3 wishdir;
        if (OnLadder)
        {
            wishdir = -LadderNormal;
        }
        else
        {
            if (forwardMove == 0f && leftMove == 0f)
            {
                return false;
            }

            wishdir = Vector3.Normalize((leftMove * left) + (forwardMove * forward));
        }

        var distance = OnLadder ? 10f : 2f;
        var pm = Physics.TraceAABB(position, position + (wishdir * distance), halfExtents, Rubikon.LadderCollisionName);

        if (!IsLadderTrace(pm))
        {
            // Not pressing into a ladder: grab one just walked off the top of
            if (!HasWalkMovedSinceLastJump || OnLadder || OnGround)
            {
                return false;
            }

            if (!(Velocity.Z <= 0f) || !(Velocity.Z > -50f) || !(MathF.Abs(Velocity.X) > 0f) || !(MathF.Abs(Velocity.Y) > 0f))
            {
                return false;
            }

            var start = position + new Vector3(0f, 0f, -6f);
            var back = position - (Vector3.Normalize(Velocity) * 24f);

            pm = Physics.TraceAABB(start, back, halfExtents, Rubikon.LadderCollisionName, detectStartSolid: true);

            if (!IsLadderTrace(pm) || pm.StartSolid)
            {
                return false;
            }

            OnLadder = true;
            LadderNormal = pm.HitNormal;
            Velocity = Vector3.Zero;

            // Snap back onto the ladder if there's room
            var solid = TraceBBox(start, back, halfExtents, detectStartSolid: true);
            var snapped = solid.Hit ? solid.HitPosition : back;

            if (!solid.StartSolid && !IsStuck(snapped, halfExtents))
            {
                position = snapped;
            }
        }

        if (!OnLadder)
        {
            LadderJumpBlockRemaining = LadderJumpBlockTime;
        }

        OnLadder = true;
        LadderNormal = pm.HitNormal;
        SlopeClipNormalZ = 1f;

        var onFloor = OnGround || IsWalkableGroundHit(TraceBBox(position, position - Vector3.UnitZ, halfExtents));

        var climbSpeed = HoldingCtrl || HoldingShift ? DuckSpeedModifier * MaxClimbSpeed : MaxClimbSpeed;
        var forwardSpeed = forwardMove * climbSpeed;
        var leftSpeed = leftMove * climbSpeed;

        if (jumpPressed || Input.Holding(TrackedKeys.Space))
        {
            if (LadderJumpBlockRemaining <= 0f)
            {
                OnLadder = false;
                Velocity = pm.HitNormal * LadderJumpSpeed;
            }
        }
        else if (forwardSpeed == 0f && leftSpeed == 0f)
        {
            Velocity = Vector3.Zero;
        }
        else
        {
            var n = pm.HitNormal;
            var velocity = (forwardSpeed * forward) + (leftSpeed * left);

            // Horizontal tangent of the ladder face
            var perp = Vector3.Normalize(new Vector3(-n.Y, n.X, 0f));

            // Split the wish into the part pressing into the face and the part along it
            var normal = Vector3.Dot(n, velocity);
            var cross = n * normal;
            var lateral = velocity - cross;

            // Up the face, in its plane
            var tmp = Vector3.Cross(n, perp);

            var tmpDist = Vector3.Dot(tmp, lateral);
            var perpDist = Vector3.Dot(perp, lateral);

            // A wish mostly sideways across the face is damped
            var angleVec = Vector3.Normalize((perp * perpDist) + cross);
            if (LadderAngle > Vector3.Dot(n, angleVec))
            {
                lateral = (perp * LadderDampen * perpDist) + (tmp * tmpDist);
            }

            // Pressing into the face climbs it
            Velocity = (lateral - (tmp * normal)) * LadderScaleSpeed;

            if (onFloor && normal > 0f)
            {
                // On the ground and moving away from the ladder
                Velocity += n * MaxClimbSpeed;
            }
        }

        return true;
    }

    private static bool IsLadderTrace(in Rubikon.TraceResult trace)
        => trace.Hit && trace.HitNormal.Z != 1f;

    /// <summary>
    /// The speed the player may reach this tick: the run speed, lowered by walking, by a recent hard
    /// landing and by how far ducked.
    /// </summary>
    private float CalculateMaxSpeed(bool isWalking)
    {
        var maxSpeed = RunSpeed;
        var walkSpeed = RunSpeed * WalkSpeedModifier;

        // Walking only takes over close to the walk speed, so it never brakes a run
        if (isWalking && walkSpeed + WalkSpeedAllowance > Velocity.Length())
        {
            maxSpeed = walkSpeed;
        }

        if (OnGround)
        {
            maxSpeed *= LandingSpeedScale();
        }

        return maxSpeed * DuckSpeedModifierActive;
    }

    /// <summary>
    /// Calculate desired movement direction and speed from input
    /// </summary>
    private (Vector3 wishdir, float wishspeed) CalculateWishVelocity(float yaw, float maxSpeed)
    {
        var (sinYaw, cosYaw) = MathF.SinCos(yaw);
        var forward = new Vector3(cosYaw, sinYaw, 0);
        var right = new Vector3(sinYaw, -cosYaw, 0);

        float forwardMove = 0, sideMove = 0;

        if (Input.Holding(TrackedKeys.W))
        {
            forwardMove += maxSpeed;
        }

        if (Input.Holding(TrackedKeys.S))
        {
            forwardMove -= maxSpeed;
        }

        if (Input.Holding(TrackedKeys.D))
        {
            sideMove += maxSpeed;
        }

        if (Input.Holding(TrackedKeys.A))
        {
            sideMove -= maxSpeed;
        }

        var wishvel = forward * forwardMove + right * sideMove;

        var wishspeed = wishvel.Length();
        var wishdir = wishspeed > 0 ? Vector3.Normalize(wishvel) : Vector3.Zero;

        return (wishdir, MathF.Min(wishspeed, maxSpeed));
    }

    /// <summary>
    /// Ground friction for one tick. Below the stop speed it takes the stop speed's worth.
    /// </summary>
    /// <returns>How much more friction there was than speed left to take, which comes off the acceleration.</returns>
    private float Friction()
    {
        var speed = new Vector2(Velocity.X, Velocity.Y).Length();

        if (speed < 0.1f)
        {
            return 0f;
        }

        var drop = MathF.Max(speed, StopSpeedValue) * FrictionValue * SurfaceFriction * TickInterval;
        Velocity *= MathF.Max(speed - drop, 0f) / speed;

        return MathF.Max(drop - speed, 0f);
    }

    /// <summary>
    /// Ground acceleration for one tick. Slower weapons, ducking and walking all accelerate slower, and
    /// walking fades out near the walk speed so the player settles on it instead of overshooting.
    /// </summary>
    private void Accelerate(Vector3 wishdir, float wishspeed, bool isWalking, float frictionLeft)
    {
        var currentspeed = Vector3.Dot(Velocity, wishdir);
        var addspeed = wishspeed - currentspeed;

        if (addspeed <= 0)
        {
            return;
        }

        currentspeed = MathF.Max(0f, currentspeed);

        var isDucking = HoldingCtrl || Ducking || DuckingFlag;

        // sv_accelerate_use_weapon_speed
        var weaponScale = MathF.Min(1f, RunSpeed / 250f);

        var accelScale = MathF.Max(250f, wishspeed);
        var goalSpeed = accelScale * weaponScale;
        var accelScaleMultiplier = isDucking || isWalking ? 1f : weaponScale;
        var accel = AccelerateValue;

        if (isDucking)
        {
            goalSpeed *= DuckSpeedModifier;
            accelScaleMultiplier = MathF.Min(DuckSpeedModifier, accelScaleMultiplier);
        }

        accelScale *= accelScaleMultiplier;

        if (isWalking)
        {
            accelScale *= WalkSpeedModifier;
            goalSpeed *= WalkSpeedModifier;

            if (currentspeed > goalSpeed - WalkTaperBand)
            {
                accel *= MathUtils.Saturate(1f - ((currentspeed - (goalSpeed - WalkTaperBand)) / WalkTaperBand));
            }
        }

        var rate = (accelScale * accel * SurfaceFriction) - (frictionLeft / TickInterval);

        if (rate > 0f)
        {
            Velocity += MathF.Min(rate * TickInterval, addspeed) * wishdir;
        }
    }

    /// <summary>
    /// Ground friction and acceleration for one tick, then the cap on ground speed.
    /// </summary>
    private void WalkMove(Vector3 wishdir, float wishspeed, float maxSpeed, bool isWalking)
    {
        var frictionLeft = Friction();
        Accelerate(wishdir, wishspeed, isWalking, frictionLeft);

        // Also what stops a hard landing dead, as the landing penalty shrinks the max speed
        var speed = Velocity.Length();

        if (speed > maxSpeed)
        {
            Velocity *= maxSpeed / speed;
        }
    }

    /// <summary>
    /// Air acceleration for one tick. Half of it is applied before the move, and the returned half is
    /// for after it, like the rest of the tick's acceleration.
    /// </summary>
    private Vector3 AirAccelerateHalf(Vector3 wishdir, float wishspeed)
    {
        var addspeed = MathF.Min(wishspeed, AirMaxWishSpeed) - Vector3.Dot(Velocity, wishdir);

        if (addspeed <= 0f)
        {
            return Vector3.Zero;
        }

        // Note: the accel rate uses the original wishspeed, NOT the capped value
        var fullAcceleration = AirAccelerate * wishspeed * SurfaceFriction * TickInterval;
        var halfAcceleration = fullAcceleration * 0.5f;

        if (halfAcceleration > addspeed)
        {
            Velocity += addspeed * wishdir;
            return Vector3.Zero;
        }

        Velocity += halfAcceleration * wishdir;
        return (fullAcceleration > addspeed ? addspeed - halfAcceleration : halfAcceleration) * wishdir;
    }

    /// <summary>
    /// Clamps each velocity component to sv_maxvelocity.
    /// </summary>
    private void ClampVelocity()
    {
        Velocity = Vector3.Clamp(Velocity, new Vector3(-MaxVelocityValue), new Vector3(MaxVelocityValue));
    }

    // How far past the sweep end the raw trace looks so that approaching surfaces are
    // seen before the hull enters their margin. Grazing surfaces (perpendicular gap
    // closing slower than SurfaceEpsilon per this many units) escape the lookahead and
    // are instead handled by the margin-restore push below.
    private const float MarginLookahead = 4f;

    // Relative slop, in float epsilons, within which a perpendicular gap counts as flush against
    // the surface. Scaled by the hull position's magnitude, since that is what the ULP is of.
    private const float FlushContactTolerance = 4f * 1.1920929e-7f;

    /// <summary>
    /// Forward-looking keep-away margin: the raw trace extends a little past the sweep end
    /// and the move stops where the perpendicular gap to the hit surface equals
    /// SurfaceEpsilon. Resting against a surface then yields a stable zero-distance hit
    /// every frame (velocity gets clipped, position holds), instead of a
    /// creep-into-the-margin-then-snap-back cycle that would make position and speed visibly
    /// oscillate when pressing against a wall.
    /// </summary>
    private Rubikon.TraceResult TraceBBox(Vector3 from, Vector3 to, Vector3 halfExtents, bool detectStartSolid = false)
    {
        var length = Vector3.Distance(from, to);

        if (length * length < UntraceableDistanceSquared)
        {
            return new Rubikon.TraceResult();
        }

        var direction = (to - from) / length;
        var raw = TraceBBoxRaw(from, to + direction * MarginLookahead, halfExtents, detectStartSolid);

        if (!raw.Hit || raw.StartSolid)
        {
            return raw;
        }

        // How fast the sweep closes the perpendicular gap; positive because the SAT only
        // reports surfaces facing the sweep
        var approach = Vector3.Dot(-direction, raw.HitNormal);

        if (approach <= Rubikon.Epsilon)
        {
            return new Rubikon.TraceResult();
        }

        // Perpendicular gap still to be given up before the hull sits at its standoff. Kept in
        // perpendicular space rather than as the equivalent raw.Distance - SurfaceEpsilon/approach:
        // that form divides by approach, and at a grazing angle the division amplifies a single
        // ULP of the hull position into a large apparent distance along the sweep. A hull resting
        // against a wall and sliding along it holds its gap to exactly one ULP, so the along-sweep
        // form hands back a spurious fraction of travel every frame - one that grows as the slide
        // angle, and with it approach, shrinks with the frame time.
        var perpendicularSlack = (raw.Distance * approach) - SurfaceEpsilon;

        // Within this the hull counts as already flush: a resting contact, credited no travel
        var positionScale = Vector3.Abs(from).MaxComponent();
        var flushTolerance = MathF.Min(SurfaceEpsilon / 16f, FlushContactTolerance * MathF.Max(1f, positionScale));

        var allowed = MathF.Abs(perpendicularSlack) <= flushTolerance ? 0f : perpendicularSlack / approach;

        if (allowed >= length)
        {
            return new Rubikon.TraceResult(); // surface too far away to constrain this move
        }

        if (allowed >= 0f)
        {
            raw.Distance = allowed;
            raw.HitPosition = from + direction * allowed;
            return raw;
        }

        // Already inside the margin (a grazing surface past the lookahead): report a
        // zero-distance hit, staying put along the sweep, and restore the perpendicular
        // gap along the normal. The push is validated so it cannot embed into an opposing
        // surface; an opposing corridor is split so both sides keep equal gaps.
        var deficit = SurfaceEpsilon - raw.Distance * approach;
        var push = TraceBBoxRaw(from, from + raw.HitNormal * deficit, halfExtents, detectStartSolid: false);

        float pushDistance;

        if (!push.Hit)
        {
            pushDistance = deficit;
        }
        else
        {
            var approachB = Vector3.Dot(-raw.HitNormal, push.HitNormal);
            pushDistance = approachB > 0f
                ? MathF.Min(deficit, push.Distance * approachB / (1f + approachB))
                : MathF.Min(deficit, MathF.Max(push.Distance - ContactNudge, 0f));
        }

        raw.Distance = 0f;
        raw.HitPosition = from + raw.HitNormal * pushDistance;
        return raw;
    }

    private void NoteImpact(in Rubikon.TraceResult result) => Input.EntitySystem?.NotePlayerImpact(result);

    private Rubikon.TraceResult TraceBBoxRaw(Vector3 from, Vector3 to, Vector3 halfExtents, bool detectStartSolid)
    {
        var result = Physics != null
            ? Physics.TraceAABB(from, to, halfExtents, "player", detectStartSolid)
            : TraceInfiniteGroundPlane(from, to, halfExtents, detectStartSolid);

        foreach (var plane in DebugCollisionPlanes)
        {
            var normal = plane.AsVector3();
            result.MinimizeWith(PhysicsWorld.TracePlane(from, to, halfExtents, normal, plane.W, detectStartSolid));
        }

        // The static world is the world entity, so standing on the map gives a ground entity like
        // standing on anything else
        if (result.Hit)
        {
            result.HitEntity = Input.EntitySystem?.World;
        }

        // Brush entities are not part of the world's physics, so they get swept separately
        Input.EntitySystem?.TraceAABB(from, to, halfExtents, detectStartSolid, ref result);

        return result;
    }

    /// <summary>
    /// Trace against an infinite ground plane at Z=0.
    /// </summary>
    private static Rubikon.TraceResult TraceInfiniteGroundPlane(Vector3 from, Vector3 to, Vector3 halfExtents, bool detectStartSolid)
        => PhysicsWorld.TracePlane(from, to, halfExtents, Vector3.UnitZ, 0f, detectStartSolid);

    /// <summary>
    /// Cosmetic view-feel state driven by the simulation: the landing dip/roll punch, and stair-step
    /// view smoothing.
    /// </summary>
    private sealed class ViewEffects
    {
        private const float StepSmoothingSlope = 0.6f;    // decay per unit of horizontal speed (~stair rise/run)
        private const float StepSmoothingMinSpeed = 100f; // decay floor

        /// <summary>View punch as (pitch, yaw) degrees.</summary>
        public Vector2 ViewPunchDegrees { get; private set; }

        /// <summary>Camera roll from a hard landing, in degrees.</summary>
        public float LandingRollDegrees { get; private set; }

        // Stair-step view smoothing: pending signed view offset
        // from hull step jumps, decays toward zero
        public float StepOffset { get; private set; }

        private const float PlayerFatalFallSpeed = 1024f;

        public void Reset()
        {
            ViewPunchDegrees = Vector2.Zero;
            LandingRollDegrees = 0f;
            StepOffset = 0f;
        }

        /// <summary>
        /// Touchdown effects, applied once on the tick the player lands: punch the view down. Falls
        /// past the safe threshold also kick the camera roll.
        /// </summary>
        /// <param name="impactSpeed">Downward speed at the moment of impact, in units per second.</param>
        /// <param name="audible">Whether this landing made a noise; a silent one does not punch the view.</param>
        public void OnLanded(float impactSpeed, bool audible)
        {
            LandingRollDegrees = Math.Clamp((impactSpeed - 300f) * 0.013f, 0f, 10f);

            if (audible)
            {
                ViewPunchDegrees = ViewPunchDegrees with { X = MathF.Max(MathF.Min(impactSpeed, PlayerFatalFallSpeed) * 0.001f, 0.75f) };
            }
        }

        public void AddViewPunch(Vector2 degrees)
        {
            ViewPunchDegrees += degrees;
        }

        /// <summary>
        /// Exponentially decays the view punch toward zero.
        /// </summary>
        public void DecayViewPunch(float deltaTime, float decayRate)
        {
            ViewPunchDegrees *= MathF.Exp(-decayRate * deltaTime);
        }

        /// <summary>
        /// Accumulates a vertical step displacement (positive = up) into the view-smoothing offset.
        /// </summary>
        public void OnStep(float delta)
        {
            StepOffset = Math.Clamp(StepOffset + delta, -StepSize, StepSize);
        }

        public void ClearStepOffset()
        {
            StepOffset = 0f;
        }

        public void Update(float deltaTime, float horizontalSpeed, bool stepSmoothingEnabled)
        {
            if (stepSmoothingEnabled)
            {
                // Glide the view along recent stair steps; decay tracks horizontal speed
                var stepDecay = MathF.Max(StepSmoothingMinSpeed, horizontalSpeed * StepSmoothingSlope) * deltaTime;
                StepOffset = StepOffset >= 0f
                    ? MathF.Max(0f, StepOffset - stepDecay)
                    : MathF.Min(0f, StepOffset + stepDecay);
            }
            else
            {
                StepOffset = 0f;
            }

            // The landing roll snaps on at impact; recovering it is a fast exponential ease-out
            var landingRecovery = MathF.Exp(-12f * deltaTime);
            LandingRollDegrees = LandingRollDegrees > 0.01f ? LandingRollDegrees * landingRecovery : 0f;
        }
    }
}
