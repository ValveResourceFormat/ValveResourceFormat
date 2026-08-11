namespace ValveResourceFormat.Renderer.Input;

/// <summary>
/// Source engine-style FPS player movement controller.
/// </summary>
public partial class PlayerMovement
{
    // Half-extents around the hull center. Standing 32x32x72, ducked 32x32x48.
    private static readonly Vector3 StandingHullHalfExtents = new(16, 16, 36);
    private static readonly Vector3 DuckedHullHalfExtents = new(16, 16, 24);

    // Movement constants from Source engine (movevars_shared.cpp)
    private const float GravityValue = 800f;              // sv_gravity
    private const float FrictionValue = 5.2f;             // sv_friction
    private const float StopSpeedValue = 80f;             // sv_stopspeed
    private const float AccelerateValue = 5.5f;           // sv_accelerate
    private const float JumpImpulseValue = 301.993377f;   // sv_jump_impulse = sqrt(2*800*57)
    private const float MaxVelocityValue = 3500f;         // sv_maxvelocity
    private const float WalkSpeedModifier = 0.52f;        // CS_PLAYER_SPEED_WALK_MODIFIER
    private const float DuckSpeedModifier = 0.34f;        // CS_PLAYER_SPEED_DUCK_MODIFIER

    private float TickInterval => 1f / EmulatedTickRate;  // Reference tick; the low-speed accel kick reaches its floor within one

    private const float NonJumpVelocity = 140f;           // Moving up faster than this means airborne (NON_JUMP_VELOCITY)
    private const float AirMaxWishSpeed = 30f;            // Air-control wishspeed cap (AirAccelerate)

    private const float ViewHeightOffset = 8f;
    private static readonly float ViewHeightStanding = StandingHullHalfExtents.Z * 2f - ViewHeightOffset;
    private const float ViewHeightDucked = 46f;

    private const float BunnyjumpMaxSpeedFactor = 1.1f;   // Only allow bunny jumping up to 1.1x max speed

    private const float SurfaceEpsilon = 0.03125f;        // Keep-away margin (1/32 unit) maintained by TraceBBox
    private const float NegligibleMoveDistance = 1e-4f;   // Slide iteration stops once this little move remains
    private const float ContactNudge = SurfaceEpsilon / 4f; // Clearance kept short of a degenerate opposing surface in the margin-restore push
    private const float UntraceableDistanceSquared = Rubikon.Epsilon * Rubikon.Epsilon;
    private const float StepSize = 18f;                   // Maximum height of steps/obstacles player can climb
    private const float GroundProbeDistance = 2f;         // How far below the hull to look for ground contact
    private const float StepDownTolerance = 2f;           // A step may end at most this far below where it started

    private const float CrouchBlendTime = 0.2f;           // Time to complete crouch/uncrouch animation (seconds)
    private static readonly float CrouchHeightDifference = (StandingHullHalfExtents.Z - DuckedHullHalfExtents.Z) * 2f;

    /// <summary>Gets the current player velocity in world units per second.</summary>
    public Vector3 Velocity { get; private set; }
    private Vector3 TracePosition;
    private Vector3 TracePositionSmooth;

    // Cap on a single frame's simulation step (load stalls, breakpoints)
    private const float MaxFrameDeltaTime = 0.1f;

    /// <summary>
    /// Gets the current player position at feet level (where the AABB touches the ground).
    /// </summary>
    public Vector3 Position => TracePositionSmooth - new Vector3(0, 0, HullHalfExtents.Z);

    /// <summary>
    /// Gets a value indicating whether the player is currently on the ground.
    /// </summary>
    public bool OnGround { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the player was touching the ground in the previous frame.
    /// </summary>
    public bool WasOnGroundLastFrame { get; private set; }

    // Last known non-overlapping position, restored when stuck
    private Vector3 LastValidPosition;
    private bool HasValidPosition;

    private float SlopeClipNormalZ = 1f;

    // Analytically integrated displacement of the current ground segment (set by
    // Friction/Accelerate/WalkMove); ground moves use it instead of Velocity * dt so
    // distance traveled is framerate-independent while speed is changing
    private Vector3 GroundMoveDelta;

    // Yaw at the previous simulated frame, for the frame's view rotation (tickless air strafe)
    private float PreviousYaw;
    private bool HasPreviousYaw;

    private readonly ViewEffects Effects = new();

    // Movement sound events (CS2, game_sounds_footsteps.vsndevts / game_sounds_player.vsndevts).
    // Footstep and land events are per-material (CT_<Material>.StepLeft / Land_<Material>.StepLeft);
    // physics traces do not return surface materials yet, so default to concrete.
    private const string FootstepSoundEvent = "CT_Concrete.StepLeft";
    private const string JumpSoundEvent = "Default.WalkJump";
    private const string LandSoundEvent = "Land_Concrete.StepLeft";
    private const string GearSoundEvent = "Gear.JumpLand.CT";
    private const float GearVolume = 0.1f;
    private const string FallDamageSoundEvent = "Player.DamageFall";

    private static readonly string[] MovementSounds = [
        FootstepSoundEvent,
        JumpSoundEvent,
        LandSoundEvent,
        GearSoundEvent,
        FallDamageSoundEvent,
    ];

    private const float StepSoundVelWalk = 90f;           // GetStepSoundVelocities velwalk (standing)
    private const float StepSoundVelRun = 220f;           // GetStepSoundVelocities velrun (standing)
    private const float WalkingStepVolume = 0.8f;         // Slightly quieter steps below run speed (the authored volume is 0.9)
    private const float LandMinFallSpeed = 270f;          // CCSPlayer::OnLand - quieter landings are silent (a normal jump lands at ~302)
    private const float FallDamageSpeed = 580f;           // PLAYER_MAX_SAFE_FALL_SPEED

    private float StepSoundTime;
    private readonly List<Audio.SoundEvent> ActiveMovementSounds = [];

    /// <summary>
    /// Gets the current jump stamina, from 0 to 1. Landing drains it, it recovers over roughly
    /// a second, and jump impulses scale by it, so spammed hops get progressively lower (like
    /// CS's sv_stamina behavior).
    /// </summary>
    public float Stamina => Effects.Stamina;

    /// <summary>
    /// Gets the downward view punch from the last landing, in degrees. This is additive on top of
    /// the player's aim for rendering only, exactly like Source's m_viewPunchAngle.
    /// </summary>
    public float ViewPunchPitchDegrees => Effects.ViewPunchPitchDegrees;

    private bool HoldingCtrl => Input.Holding(TrackedKeys.Control);
    private bool HoldingShift => Input.Holding(TrackedKeys.Shift);

    private UserInput Input { get; }
    private Rubikon? Physics => Input.PhysicsWorld;

    /// <summary>
    /// Gets or sets a value indicating whether traces also collide with
    /// an infinite ground plane at Z=0.
    /// </summary>
    public bool GridPlaneCollisionEnabled { get; set; }

    /// <summary>
    /// Test-only extra static collision half-spaces layered onto the traces, each a
    /// <see cref="Vector4"/> whose XYZ is the outward unit normal and W the plane offset d;
    /// the half-space n·x ≤ d is solid. Lets headless tests build walls and overhangs without
    /// a physics world. Empty in normal use.
    /// </summary>
    public List<Vector4> DebugCollisionPlanes { get; } = [];

    /// <summary>
    /// Linear value from 0 to 1 representing how much the player is crouched.  0 = standing, 1 = fully crouched.
    /// </summary>
    public float CrouchBlend { get; private set; }

    /// <summary>
    /// Gets the current eye height blended between standing and crouched positions.
    /// </summary>
    public float BlendedEyeHeight { get; private set; }

    /// <summary>The current eye position</summary>
    public Vector3 EyePosition { get; private set; }

    private float DuckSpeedModifierActive => float.Lerp(1f, DuckSpeedModifier, CrouchBlend);
    private Vector3 SnappedHullHalfExtents => HoldingCtrl ? DuckedHullHalfExtents : StandingHullHalfExtents;

    /// <summary>
    /// Gets the current collision hull half-extents, with height blended between the standing
    /// and ducked hulls by <see cref="CrouchBlend"/>.
    /// </summary>
    public Vector3 HullHalfExtents => new(
        StandingHullHalfExtents.X,
        StandingHullHalfExtents.Y,
        float.Lerp(StandingHullHalfExtents.Z, DuckedHullHalfExtents.Z, CrouchBlend));

    private float SurfaceFriction = 1.0f;
    private const float JumpFriction = 0.25f;
    private const float WalkableSlope = 0.7f; // ~45 degrees

    /// <summary>Gets or sets a value indicating whether the controller should reinitialize its position from the camera on the next tick.</summary>
    public bool Initialize { get; set; }
    /// <summary>Gets or sets a value indicating whether bunny-hopping is allowed by holding the jump key.</summary>
    public bool AutoBunnyHop { get; set; } = true;
    /// <summary>Gets or sets a value indicating whether ground pre-strafe is allowed.</summary>
    public bool PrestrafeEnabled { get; set; }
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
    /// Gets or sets the tick rate whose per-tick acceleration step the tickless integrators
    /// reproduce. A discrete engine tops the wishdir velocity component up by at most the whole
    /// remaining addspeed once per tick, which is a bound on speed gained per unit time; the
    /// continuous forms reimpose that bound so they converge to the same trajectory instead of
    /// out-accelerating the reference engine as the frame time shrinks.
    /// </summary>
    public float EmulatedTickRate { get; set; } = 64f;

    /// <summary>
    /// Initializes a new <see cref="PlayerMovement"/> bound to the given user input source.
    /// </summary>
    /// <param name="input">The user input instance used to read key state.</param>
    public PlayerMovement(UserInput input)
    {
        Input = input;
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
        SlopeClipNormalZ = 1f;
        HasPreviousYaw = false;
        Effects.Reset();

        CacheSounds();
    }

    /// <summary>
    /// Moves the player somewhere else outright, keeping their velocity.
    /// </summary>
    /// <param name="feetPosition">Where the feet arrive.</param>
    /// <param name="angles">View angles to adopt, or null to keep the current one.</param>
    public void Teleport(Vector3 feetPosition, Vector3? angles)
    {
        var position = feetPosition + new Vector3(0, 0, HullHalfExtents.Z);

        // Carry the already-placed camera along so the view does not trail by a frame
        var moved = position - TracePositionSmooth;
        EyePosition += moved;
        Input.Camera.Location += moved;

        TracePosition = position;
        TracePositionSmooth = position;
        HasValidPosition = false; // Do not restore positions from before the teleport
        Effects.ClearStepOffset();

        if (angles is { } viewAngles)
        {
            Input.Camera.SetFromQAngle(viewAngles);

            // A snapped yaw is not a turn; restart the strafe yaw tracking
            HasPreviousYaw = false;
        }
    }

    /// <summary>Pre-decodes the sounds movement can fire, so the first step does not decode mid-frame.</summary>
    public static void CacheSounds()
    {
        foreach (var soundEvent in MovementSounds)
        {
            Sound.Cache(soundEvent);
        }
    }

    /// <summary>
    /// Runs the movement simulation once per rendered frame, mirroring Source's frame order.
    /// </summary>
    public void ProcessMovement(Camera camera, float deltaTime)
    {
        if (Initialize)
        {
            ResetPosition(camera);
            TryUnstuck(ref TracePosition, SnappedHullHalfExtents);
            Velocity = Input.Velocity;
            Initialize = false;
        }

        var position = TracePosition;
        var yaw = camera.Yaw;

        // Signed view rotation over this frame, wrapped to (-pi, pi]
        var yawDelta = HasPreviousYaw ? MathF.IEEERemainder(yaw - PreviousYaw, MathF.Tau) : 0f;
        PreviousYaw = yaw;
        HasPreviousYaw = true;

        deltaTime = MathF.Min(deltaTime, MaxFrameDeltaTime);

        Effects.DecayViewPunch(deltaTime, ViewPunchDecay);

        var isDucking = HoldingCtrl;
        var isWalking = !HoldingCtrl && HoldingShift;

        BlendDuckedHull(deltaTime, ref position, isDucking);

        var playerHull = HullHalfExtents;

        WasOnGroundLastFrame = OnGround;

        // Impact speed to use if this frame turns out to land: ground movement zeroes the
        // vertical velocity, so it must be sampled while still falling
        var fallSpeed = MathF.Max(0f, -Velocity.Z);

        CategorizePosition(ref position, playerHull);

        var justLanded = !WasOnGroundLastFrame && OnGround;

        var wantsToJump = AutoBunnyHop ? Input.Holding(TrackedKeys.Space) : Input.Pressed(TrackedKeys.Space);
        wantsToJump = wantsToJump || Input.Holding(TrackedKeys.MouseWheelDown) || Input.Holding(TrackedKeys.MouseWheelUp);

        if (justLanded)
        {
            OnLanded(fallSpeed, wantsToJump, position, playerHull);
        }

        if (wantsToJump && OnGround)
        {
            if (!AutoBunnyHop)
            {
                PreventBunnyJumping();
            }

            // Sampled before the jump impulse, which is what makes the takeoff loud
            var loudTakeoffFootstep = Velocity.Length() > WalkSpeedModifier * RunSpeed;

            CheckJump();

            if (loudTakeoffFootstep)
            {
                PlaySound(FootstepSoundEvent, position, playerHull);
            }

            PlaySound(JumpSoundEvent, position, playerHull);
            PlaySound(GearSoundEvent, position, playerHull, GearVolume);
        }

        var (wishdir, wishspeed) = CalculateWishVelocity(yaw, isWalking);
        var airMoveDelta = Vector3.Zero;
        var airVelocityDelta = Vector3.Zero;

        if (OnGround)
        {
            // Ground friction+acceleration is integrated per-bump inside GroundMove,
            // coupled to the collision sweep, so nothing is accelerated here
            ZeroVerticalVelocity();
        }
        else
        {
            // AirMove produces the frame's whole strafe gain, but it is taken back out of
            // Velocity and carried as a delta: Velocity stays the velocity at the frame's start,
            // so every accelerating term - strafe and gravity alike - is handed to the mover the
            // same way, and the mover always holds the true velocity at the instant it is at.
            var preAirVelocity = Velocity;
            AirMove(wishdir, wishspeed, deltaTime, yawDelta);
            var strafeGain = Velocity - preAirVelocity;
            Velocity = preAirVelocity;

            airVelocityDelta = strafeGain - new Vector3(0f, 0f, GravityValue * deltaTime);

            // The frame's displacement if nothing is in the way: (v + dv/2) * dt.
            //
            // Z is exact — gravity is constant, so the midpoint velocity is the frame's average —
            // but XY is not: the strafe velocity follows TicklessAirStrafe's regime machine, not a
            // straight line in time, so the trapezoid carries a second-order error; an exact form
            // would mean integrating that state machine across its regime transitions.
            airMoveDelta = TrapezoidDisplacement(Velocity, Velocity + airVelocityDelta, deltaTime);
        }

        ClampVelocity();

        // Gravity is applied inside the move, so the freshest impact speed for a landing the
        // move below may produce is the frame's midpoint - the average the trapezoid sweeps
        fallSpeed = MathF.Max(fallSpeed, -(Velocity.Z + (airVelocityDelta.Z * 0.5f)));

        // Ground movement integrates and sweeps together with step support; in the air there
        // is nothing to step off, so the constant-velocity slide runs on the precomputed delta
        var moveStart = position;

        position = OnGround
            ? GroundMove(position, wishdir, wishspeed, deltaTime, DuckSpeedModifierActive, isWalking, playerHull)
            : TryPlayerMove(position, airVelocityDelta, deltaTime, playerHull, wishdir, wishspeed);

        if (OnGround)
        {
            StayOnGround(ref position, playerHull);
        }
        else
        {
            RewindToGroundBandEntry(ref position, moveStart, airMoveDelta, playerHull);
        }

        CategorizePosition(ref position, playerHull);

        CheckStuck(ref position, playerHull);

        // The other landing path: airborne at the start of the frame, grounded by the move
        // above. justLanded already handled the start-of-frame categorize path.
        if (!justLanded && !WasOnGroundLastFrame && OnGround)
        {
            OnLanded(fallSpeed, wantsToJump, position, playerHull);
        }

        UpdateStepSounds(position, playerHull, deltaTime);

        TracePosition = position;

        var horizontalSpeed = new Vector2(Velocity.X, Velocity.Y).Length();
        Effects.Update(deltaTime, horizontalSpeed, StepSmoothingEnabled);

        TracePositionSmooth = TracePosition - new Vector3(0, 0, Effects.StepOffset);

        BlendedEyeHeight = ViewHeightStanding + (ViewHeightDucked - ViewHeightStanding) * CrouchBlend;
        EyePosition = Position + Vector3.UnitZ * BlendedEyeHeight;
        camera.Location = EyePosition;
        camera.Roll = float.DegreesToRadians(Effects.LandingRollDegrees);

        UpdateMovementSounds();
    }

    /// <summary>
    /// Touchdown effects for a landing at <paramref name="fallSpeed"/>: the view punch and roll,
    /// stamina drain, and the landing thud, gear rattle and pain grunt.
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
            PlaySound(LandSoundEvent, position, halfExtents);
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
        var walkSpeed = RunSpeed * WalkSpeedModifier; // CS_PLAYER_SPEED_RUN * CS_PLAYER_SPEED_WALK_MODIFIER

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

        if (speed < StepSoundVelWalk || !OnGround)
        {
            return;
        }

        var walking = speed < StepSoundVelRun;
        SetStepSoundTime(walking);

        // fvol from the original: walking pace plays quiet steps, running full
        PlaySound(FootstepSoundEvent, position, halfExtents, walking ? WalkingStepVolume : null);
    }

    /// <summary>
    /// 400ms walking, 300ms running, +100ms ducked.
    /// </summary>
    private void SetStepSoundTime(bool walking)
    {
        StepSoundTime = walking ? 0.4f : 0.3f;

        if (HoldingCtrl)
        {
            StepSoundTime += 0.1f;
        }
    }

    private void PlaySound(string soundEventName, Vector3 position, Vector3 halfExtents, float? volume = null)
    {
        // Convert from hull center to feet position; the sound event applies its own position offset
        var feetPosition = position - new Vector3(0, 0, halfExtents.Z);
        var handle = Sound.Play(soundEventName, feetPosition, volume: volume);

        if (handle != null)
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
        }
        else if (HasValidPosition)
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

    private void BlendDuckedHull(float deltaTime, ref Vector3 position, bool isDucking)
    {
        var crouchDelta = isDucking ? 1f : -1f;
        crouchDelta = crouchDelta / CrouchBlendTime * deltaTime;

        var goalCrouch = MathUtils.Saturate(CrouchBlend + crouchDelta);
        crouchDelta = goalCrouch - CrouchBlend;

        // On ground the feet stay anchored as the hull resizes; in air the head does
        var feetAnchored = OnGround;

        if (crouchDelta != 0f)
        {
            // Uncrouching needs clearance in the direction the hull grows
            if (crouchDelta < 0f)
            {
                if (UncrouchBlocked(position, feetAnchored))
                {
                    // Blocked head-anchored? Try anchoring the feet so the player can still stand up
                    if (!feetAnchored && !UncrouchBlocked(position, feetAnchored: true))
                    {
                        feetAnchored = true;
                    }
                    else
                    {
                        crouchDelta = 0f;
                    }
                }
            }

            // Move the hull center so the anchored end stays in place
            var bboxPositionDelta = CrouchHeightDifference * crouchDelta / 2f;
            bboxPositionDelta *= feetAnchored ? -1f : 1f;
            position += new Vector3(0, 0, bboxPositionDelta);
        }

        CrouchBlend = MathUtils.Saturate(CrouchBlend + crouchDelta);
    }

    /// <summary>
    /// Whether a full uncrouch is obstructed, tested by sweeping the ducked hull toward the growth direction.
    /// </summary>
    private bool UncrouchBlocked(Vector3 position, bool feetAnchored)
    {
        var growth = new Vector3(0, 0, feetAnchored ? CrouchHeightDifference : -CrouchHeightDifference);
        return TraceBBox(position, position + growth, DuckedHullHalfExtents).Hit;
    }

    /// <summary>
    /// Keeps the player glued to the ground on downward slopes/stairs (Source StayOnGround).
    /// </summary>
    private void StayOnGround(ref Vector3 position, Vector3 halfExtents)
    {
        var trace = TraceBBox(position, position + new Vector3(0, 0, -StepSize), halfExtents);

        if (IsWalkableGroundHit(trace))
        {
            var preSnapZ = position.Z;

            SnapToGround(ref position, trace, snapDownOnly: true);

            // Feed downward stair snaps into the view smoothing
            Effects.OnStep(position.Z - preSnapZ);
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
    /// Checks for walkable ground within the 2-unit probe and snaps onto it.
    /// </summary>
    private void CategorizePosition(ref Vector3 position, Vector3 halfExtents)
    {
        var result = TraceBBox(position, position + new Vector3(0, 0, -GroundProbeDistance), halfExtents);

        var grounded = IsWalkableGroundHit(result);

        // A steep surface can shadow walkable ground; retry with quarter-footprint hulls.
        // The quadrant probes only decide groundedness: their hit positions are quarter-hull
        // standoffs, and snapping the full hull to one embeds it into the shadowing surface
        var snapToHit = grounded;

        if (!grounded && result.Hit)
        {
            grounded = ProbeGroundQuadrants(position, halfExtents);
        }

        // NON_JUMP_VELOCITY guard, on the Z velocity a plain projection would have produced (see SlopeClipNormalZ)
        OnGround = grounded && Velocity.Z * SlopeClipNormalZ < NonJumpVelocity;

        // CGameMovement::CategorizePosition resets m_surfaceFriction to 1 each call and quarters
        // it while airborne and rising - but only below NON_JUMP_VELOCITY: faster ascents take
        // the bMovingUpRapidly early-out, which never reaches the friction line. A full jump
        // (302 u/s) therefore keeps full air control until 140 u/s, and the dead window is just
        // the last stretch up to the apex, restoring the frame it passes.
        SurfaceFriction = !OnGround && Velocity.Z > 0f && Velocity.Z <= NonJumpVelocity ? JumpFriction : 1f;

        if (OnGround && snapToHit)
        {
            SnapToGround(ref position, result, snapDownOnly: false);
        }
    }

    /// <summary>
    /// Groundedness is decided by a probe <see cref="GroundProbeDistance"/> below the hull, so the
    /// rule is really "once the hull is within that band, snap down". Applied at the end of a
    /// frame that lands inside the band, the snap credits the whole frame's horizontal travel
    /// however far past the band entry it went, which makes the landing point depend on where a
    /// frame boundary happened to fall. Rewind the move to the instant it crossed into the band —
    /// found by sweeping the same displacement from a start lowered by the band width — so the
    /// landing is fixed by the trajectory instead of by the timestep. The caller's
    /// <see cref="CategorizePosition"/> then does the snap from there.
    /// </summary>
    private void RewindToGroundBandEntry(ref Vector3 position, Vector3 moveStart, Vector3 delta, Vector3 halfExtents)
    {
        // Only a descending move can enter the band from above
        if (delta.Z >= 0f || delta.LengthSquared() < UntraceableDistanceSquared)
        {
            return;
        }

        // Nothing to rewind unless the move actually ended inside the band over walkable ground
        var probe = TraceBBox(position, position + new Vector3(0, 0, -GroundProbeDistance), halfExtents);

        if (!IsWalkableGroundHit(probe))
        {
            return;
        }

        // Where the same sweep, lowered by the band width, first meets the ground: the moment the
        // real path crossed into the band. A start already inside it has nothing to rewind to.
        var loweredStart = moveStart - new Vector3(0, 0, GroundProbeDistance);
        var crossing = TraceBBox(loweredStart, loweredStart + delta, halfExtents);

        if (!crossing.Hit || crossing.StartSolid)
        {
            return;
        }

        var fraction = crossing.Distance / delta.Length();

        if (fraction is <= 0f or >= 1f || !float.IsFinite(fraction))
        {
            return;
        }

        var entry = moveStart + (delta * fraction);

        // The rewind lands the hull exactly a band width above the floor, where the caller's probe
        // is a borderline no-hit: leaving it there would keep the player airborne for another
        // frame and hand back more horizontal travel than the snap was meant to save. Commit to
        // the snap here instead, tracing with enough slack to clear the standoff the crossing
        // trace already left, and only accept it if it really lands on walkable ground.
        var reach = GroundProbeDistance + (SurfaceEpsilon * 4f);
        var landing = TraceBBox(entry, entry + new Vector3(0, 0, -reach), halfExtents);

        if (!IsWalkableGroundHit(landing))
        {
            return;
        }

        position = entry;
        SnapToGround(ref position, landing, snapDownOnly: true);
    }

    /// <summary>
    /// Retries the ground probe per hull corner, like Source's TryTouchGroundInQuadrants.
    /// </summary>
    private bool ProbeGroundQuadrants(Vector3 position, Vector3 halfExtents)
    {
        var quarterExtents = new Vector3(halfExtents.X * 0.5f, halfExtents.Y * 0.5f, halfExtents.Z);

        Span<Vector2> corners = stackalloc[]
        {
            new Vector2(-1, -1),
            new Vector2(1, -1),
            new Vector2(-1, 1),
            new Vector2(1, 1),
        };

        foreach (var corner in corners)
        {
            var center = position + new Vector3(corner.X * quarterExtents.X, corner.Y * quarterExtents.Y, 0);
            var probe = TraceBBox(center, center + new Vector3(0, 0, -GroundProbeDistance), quarterExtents);

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
        Span<Vector3> directions = stackalloc[]
        {
            Vector3.UnitZ,        // Up
            Vector3.UnitX,        // Right
            -Vector3.UnitX,       // Left
            Vector3.UnitY,        // Forward
            -Vector3.UnitY,       // Back
            Vector3.Normalize(new Vector3(1, 1, 0)),   // Diagonal
            Vector3.Normalize(new Vector3(-1, 1, 0)),
            Vector3.Normalize(new Vector3(1, -1, 0)),
            Vector3.Normalize(new Vector3(-1, -1, 0)),
        };

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

    /// <summary>
    /// Check whether the hull overlaps solid geometry at the given position.
    /// </summary>
    private bool IsStuck(Vector3 position, Vector3 halfExtents)
    {
        const float StuckProbeShrink = SurfaceEpsilon / 2f;
        var probe = TraceBBox(position, position + new Vector3(0, 0, 1f), halfExtents - new Vector3(StuckProbeShrink), detectStartSolid: true);
        return probe.StartSolid;
    }

    /// <summary>
    /// Perform swept AABB collision detection for player movement with multi-bounce sliding.
    ///
    /// The frame is given as a base velocity — <see cref="Velocity"/>, the velocity at the frame's
    /// start — plus <paramref name="velocityDelta"/>, the total velocity change the frame's
    /// acceleration produces (gravity, and the air strafe gain the caller took back out of
    /// Velocity). Each segment sweeps (v + dv/2) * t, the displacement of a velocity that is
    /// linear in time, which is exactly the model <see cref="DistanceToTimeFraction"/> inverts.
    ///
    /// Because <see cref="Velocity"/> is advanced by the acceleration as time is consumed, it is
    /// always the true velocity at the instant the loop is at: an impact clips the velocity the
    /// player actually hit with, and nothing has to be un-applied or restored around the clip.
    /// </summary>
    private Vector3 TryPlayerMove(Vector3 start, Vector3 velocityDelta, float deltaTime, Vector3 halfExtents,
        Vector3 wishdir, float wishspeed)
    {
        const int MaxBumps = 6;

        var position = start;
        var timeLeft = deltaTime;

        // Stop dead if clipping ever turns the velocity against the entry velocity (corner ping-pong)
        var entryVelocity = Velocity;

        // Planes hit without making progress; movement must be clipped to parallel all of them
        Span<Vector3> planes = stackalloc Vector3[MaxBumps];
        var planeCount = 0;

        for (var bump = 0; bump < MaxBumps && timeLeft > 0f; bump++)
        {
            var delta = TrapezoidDisplacement(Velocity, Velocity + velocityDelta, timeLeft);

            if (delta.LengthSquared() < UntraceableDistanceSquared)
            {
                break;
            }

            var distance = delta.Length();

            var result = TraceBBox(position, position + delta, halfExtents);

            if (!result.Hit)
            {
                // The sweep proved the whole remaining segment is free, so its acceleration acted
                // in full, whatever surfaces were met earlier in the frame
                Velocity += velocityDelta;
                return position + delta;
            }

            // Advance to the hit point (already margin-adjusted by TraceBBox)
            var fraction = result.Distance / distance;

            // The chord runs at constant speed, but the real trajectory accelerates along its own
            // direction, so the hit distance was covered in more or less time than the chord
            // implies. Recover the time fraction before it is used to place the impact.
            var direction = delta / distance;
            var speedAlongSweep = distance / timeLeft;
            var timeFraction = speedAlongSweep > NegligibleMoveDistance
                ? DistanceToTimeFraction(fraction, Vector3.Dot(velocityDelta, direction), speedAlongSweep)
                : fraction;

            var elapsed = timeFraction * timeLeft;

            position = result.HitPosition;

            // Carry the velocity forward to the moment of impact
            Velocity += velocityDelta * timeFraction;
            timeLeft -= elapsed;

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
            // frame entered, where an equality test fires and zeroes everything - including the Z
            // that the jump is riding on, so the player hangs at the apex and twitches as the
            // next frame re-accelerates into the same trap. Matches the GroundMove guard.
            if (!ClipToPlanes(planes[..planeCount], ref clippedDelta, out var velocity, out var clipNormalZ)
                || (entryVelocity.LengthSquared() > 1f && Vector3.Dot(velocity, entryVelocity) < 0f))
            {
                // Trapped by three or more planes, or clipping reversed the move
                Velocity = Vector3.Zero;
                return position;
            }

            Velocity = velocity;
            SlopeClipNormalZ = clipNormalZ;

            // Rebuild the acceleration for the rest of the frame against the surfaces now known.
            // The strafe half is re-aimed along them, so none of it is spent pushing into a ramp
            // only for the next sweep to strip it again. Recomputed rather than accumulated: each
            // contact supersedes the last, so this always describes what is left after the most
            // recent one.
            if (timeLeft > 0f && planeCount > 0)
            {
                velocityDelta = ClipAcceleration(new Vector3(0f, 0f, -GravityValue * timeLeft), planes[..planeCount]);

                if (wishspeed > 0f)
                {
                    velocityDelta += AirAccelerateClipped(Velocity, wishdir, wishspeed, timeLeft, planes[..planeCount]);
                }
            }

            // A repeated flush contact neither advanced nor learned anything, and repeating it
            // would only burn the bump budget: the move is as constrained as it is going to get
            if (!newPlane && timeFraction <= 0f)
            {
                break;
            }
        }

        // Time the sweep never got to spend - a flush contact, an exhausted bump budget, a
        // segment too short to trace. Its acceleration is already clipped to the surfaces being
        // ridden, so applying it here does not drive into them.
        Velocity += velocityDelta;

        return position;
    }

    /// <summary>
    /// The part of an acceleration a player in contact with <paramref name="planes"/> can actually
    /// take on. Only a surface the acceleration drives into can oppose it: <see cref="ClipToPlanes"/>
    /// clips unconditionally, which is right for a velocity that was just driven into those planes,
    /// but an overhang the player is bouncing away from must not have gravity's component stripped
    /// or they drift along its underside. An unresolved clip (no direction satisfies every plane)
    /// leaves the term alone rather than cancelling it, which would leave the player hovering; a
    /// genuinely trapped move is stopped dead by the caller instead.
    /// </summary>
    private static Vector3 ClipAcceleration(Vector3 acceleration, ReadOnlySpan<Vector3> planes)
    {
        foreach (var plane in planes)
        {
            if (Vector3.Dot(acceleration, plane) < 0f)
            {
                var clipped = ClipWishVelocity(acceleration, planes, onGround: false, out var resolved);
                return resolved ? clipped : acceleration;
            }
        }

        return acceleration;
    }

    /// <summary>
    /// Converts a swept distance fraction into the time fraction that produced it.
    ///
    /// The sweep is a straight chord covered at one constant speed, but the real trajectory
    /// changes speed along it, so "half the distance" is not "half the time". Everything an
    /// impact needs — the velocity to clip, the time budget to deduct — is a function of time,
    /// so the distance fraction the trace reports has to be turned back into a time fraction.
    ///
    /// Projected on the sweep direction, the distance covered by a given time fraction is a
    /// quadratic in that fraction (the trapezoid displacement is the linear-velocity model, so
    /// this is exact for the delta being swept, not an approximation of it). Dividing through by
    /// the full chord length leaves a quadratic whose only parameter is how far the segment is
    /// from constant speed, and inverting it recovers the time fraction. A segment that holds its
    /// speed returns the distance fraction unchanged.
    /// </summary>
    /// <param name="distanceFraction">How much of the chord's length the sweep covered.</param>
    /// <param name="velocityChangeAlong">Speed gained or lost over the segment, projected on the sweep direction.</param>
    /// <param name="speedAlong">The chord's average speed along that direction.</param>
    private static float DistanceToTimeFraction(float distanceFraction, float velocityChangeAlong, float speedAlong)
    {
        // How far this segment is from constant speed: the speed it gained or lost, over twice its
        // average speed. Zero means the chord is the trajectory and the time fraction equals the
        // distance fraction; it is the only thing that bends one into the other.
        var speedChangeRatio = velocityChangeAlong / (2f * speedAlong);

        // Coefficients of speedChangeRatio*t^2 + linearCoefficient*t - distanceFraction = 0
        var linearCoefficient = 1f - speedChangeRatio;
        var discriminant = (linearCoefficient * linearCoefficient) + (4f * speedChangeRatio * distanceFraction);

        if (discriminant <= 0f)
        {
            return distanceFraction;
        }

        // Written as 2c / (-b + sqrt(disc)) rather than the textbook (-b + sqrt(disc)) / 2a. The
        // textbook form subtracts two near-equal numbers and then divides by a small one: for a
        // flush contact (distanceFraction 0) the numerator is exactly zero algebraically, but
        // sqrt(x*x) is not x in single precision, so it lands around 1e-8 and dividing by a
        // speedChangeRatio of ~1e-4 inflates that into ~1e-4 of fictitious elapsed time on every
        // contact. This form adds two positives instead and returns exactly zero for a zero
        // distance fraction, with no special case needed for a small ratio.
        var timeFraction = 2f * distanceFraction / (linearCoefficient + MathF.Sqrt(discriminant));

        return float.IsFinite(timeFraction) ? Math.Clamp(timeFraction, 0f, 1f) : distanceFraction;
    }

    /// <summary>
    /// Clips the movement to parallel every accumulated plane, as in Source's TryPlayerMove.
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
    /// Resolves the wish input against the surfaces already met this frame: clips the wish
    /// velocity to them and splits the result back into a direction and a speed, plus the scale
    /// factor the clip cost. The scale is what the acceleration magnitude has to be multiplied by
    /// so that clipping first and accelerating along the surface delivers the same tangential
    /// acceleration as accelerating along the raw wishdir and projecting afterwards.
    /// </summary>
    private static (Vector3 Wishdir, float Wishspeed, float WishScale) ClipWish(Vector3 wishdir, float wishspeed, ReadOnlySpan<Vector3> planes)
    {
        if (planes.Length == 0 || wishspeed <= 0f)
        {
            return (wishdir, wishspeed, 1f);
        }

        var clipped = ClipWishVelocity(wishdir * wishspeed, planes, onGround: true, out var resolved);
        var clippedSpeed = resolved ? clipped.Length() : 0f;

        // Wish points entirely into the surfaces: no direction is left to accelerate along, and
        // the segment runs on friction alone
        if (clippedSpeed <= NegligibleMoveDistance)
        {
            return (Vector3.Zero, 0f, 0f);
        }

        return (clipped / clippedSpeed, clippedSpeed, clippedSpeed / wishspeed);
    }

    /// <summary>
    /// Clips a wish velocity so it does not drive into any of the accumulated planes, using the
    /// same candidate-plane/crease resolution as <see cref="ClipToPlanes"/>. The magnitude of the
    /// result is meaningful: it is the part of the wish the player can actually pursue, and
    /// callers derive both the reduced wishspeed and the reduced acceleration magnitude from it.
    ///
    /// The out flag is false when no direction satisfies every plane, in which case the returned
    /// zero is a fallback rather than an answer. Callers must not read a zero on its own as "this
    /// clips to nothing": for a wish that reading is correct, but for gravity it would mean
    /// cancelling it outright and leaving the player hovering.
    ///
    /// Like <see cref="ClipToPlanes"/> this clips unconditionally, including when the wish already
    /// points away from a plane - <see cref="ClipVelocity"/> subtracts the normal component
    /// whatever its sign, so the result is always parallel to the surface. Skipping the clip in
    /// that case leaves an away-from-surface component in the acceleration direction, which lifts
    /// the player off a ramp they should be riding. Assumes a non-empty plane set, as the callers
    /// all guarantee; an empty one reports unresolved.
    /// </summary>
    private static Vector3 ClipWishVelocity(Vector3 wishVelocity, ReadOnlySpan<Vector3> planes, bool onGround, out bool resolved)
    {
        var delta = wishVelocity;
        resolved = TryClipToPlanes(planes, ref delta, ref wishVelocity, onGround, out _);
        return wishVelocity;
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
    /// Unified ground move: integrate friction+acceleration and sweep in one loop. Each bump
    /// re-integrates the combined velocity change over the time still left in the frame,
    /// sweeps that displacement (with step support), and — if it hits a surface partway —
    /// undoes the share of the acceleration it never earned (linear in the swept fraction),
    /// clips the result to the surface, and lets the next bump re-accelerate along it. The
    /// frame's walls are accumulated, and both the wish input and the segment they produce
    /// are clipped to parallel
    /// all of them, so a regenerated segment slides along instead of re-entering. Clipping the
    /// wish is what stops a player pressing into a wall from re-aiming every bump at the surface
    /// it is already flush with, spending the whole bump budget on zero-fraction contacts. The
    /// segment horizon reported by <see cref="WalkMove"/> (e.g. the sub-stopspeed kick kink)
    /// also ends a bump early, so the boosted and unboosted stretches sweep as separate traces.
    /// </summary>
    private Vector3 GroundMove(Vector3 position, Vector3 wishdir, float wishspeed, float deltaTime, float duckModifier, bool isWalking, Vector3 halfExtents)
    {
        const int MaxBumps = 8;

        var remaining = deltaTime;
        var entryVelocity = Velocity;

        // Walls hit this frame; the regenerated segment is clipped to parallel all of them
        Span<Vector3> planes = stackalloc Vector3[MaxBumps];
        var planeCount = 0;

        var startPos = position;

        for (var bump = 0; bump < MaxBumps && remaining > 1e-6f; bump++)
        {
            var segStartVelocity = Velocity;

            // Clip the wish itself, not just what it produced. Re-integrating along the raw
            // wishdir would aim the segment back into a wall already met this frame, the sweep
            // would report a flush contact at fraction zero, and the bump would repeat having
            // spent no time and gone nowhere. Clipping the wish *velocity* keeps the reduction
            // in one place: its length is the wishspeed the player can still pursue along the
            // surface, and wishScale carries the same cosine into the acceleration magnitude
            // (and so into the sub-stopspeed kick, which is defined off wishspeed).
            var (segWishdir, segWishspeed, wishScale) = ClipWish(wishdir, wishspeed, planes[..planeCount]);

            var segDt = WalkMove(segWishdir, segWishspeed, remaining, duckModifier, isWalking, wishScale);
            var freeVelocity = Velocity;
            var delta = GroundMoveDelta;

            // What friction and acceleration alone did over this segment. Kept before the clip
            // below, which replaces freeVelocity with a deflected one: that jump is a
            // discontinuity, not acceleration, and must not enter the distance/time conversion.
            var segVelocityChange = freeVelocity - segStartVelocity;

            // Constrain the freshly integrated segment to the walls already met this frame
            if (planeCount > 0)
            {
                if (!ClipToPlanes(planes[..planeCount], ref delta, out var constrained, out var constrainedZ))
                {
                    Velocity = Vector3.Zero;
                    break;
                }

                freeVelocity = constrained;
                Velocity = constrained;
                SlopeClipNormalZ = constrainedZ;
            }

            if (delta.LengthSquared() < UntraceableDistanceSquared)
            {
                remaining -= segDt;
                continue;
            }

            var (moved, fraction, hitNormal, hit) = StepSweep(position, delta, halfExtents);
            position = moved;

            // A bump that hit nothing traversed its whole segment, whatever lateral fraction the
            // stepped branch reports for it: the time fraction is 1 by definition
            var timeFraction = hit ? fraction : 1f;
            var segDistance = delta.Length();

            if (hit && segDt > 0f && segDistance > NegligibleMoveDistance)
            {
                var direction = delta / segDistance;
                var speedChangeAlongSweep = Vector3.Dot(segVelocityChange, direction);
                var speedAlongSweep = segDistance / segDt;

                // Known tradeoff: accurate per contact, but only the first contact of an approach
                // converts to a nonzero fraction (later flush contacts report zero), and where
                // that first fraction lands depends on the frame boundary — per-impact accuracy
                // at the cost of a phase-dependent term uncorrelated across framerates.
                if (speedAlongSweep > NegligibleMoveDistance)
                {
                    timeFraction = DistanceToTimeFraction(fraction, speedChangeAlongSweep, speedAlongSweep);
                }
            }

            remaining -= timeFraction * segDt;

            if (!hit)
            {
                Velocity = freeVelocity;
                continue;
            }

            // Undo the unearned (1 - timeFraction) of this segment's velocity change, then clip
            // the impact velocity to the surface it landed on
            Velocity = Vector3.Lerp(segStartVelocity, freeVelocity, timeFraction);

            // Re-contacting a surface already in the set teaches the clip nothing, so it must not
            // consume a slot. Combined with a zero time fraction it also means the bump neither
            // advanced nor learned anything, and repeating it would only burn the remaining
            // budget: the move is as constrained as it is going to get, so stop after the reclip.
            var newPlane = !ContainsPlane(planes[..planeCount], hitNormal);

            if (newPlane)
            {
                planes[planeCount++] = hitNormal;
            }

            // A genuinely trapped move (three or more planes with no valid direction) stops
            // dead. The reversal guard only applies to a moving player: when clipping flips
            // the velocity against the way the frame entered. At rest (or crawling) the entry
            // velocity is ~0, so it must not fire — otherwise the first wall contact zeroes
            // the acceleration every frame and the player can never slide out along a wall.
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

            if (!newPlane && timeFraction <= 0f)
            {
                break;
            }
        }

        return position;
    }

    /// <summary>
    /// One swept ground step with step (stairs) support, mirroring Source's StepMove branch
    /// compare but as a single sweep: try the direct move, and also a stepped-up move that
    /// settles back down, and keep whichever travelled farther laterally. Reports the lateral
    /// fraction achieved, the surface normal the move stopped against, and whether it was
    /// blocked at all (a fully cleared stepped move reports no hit).
    /// </summary>
    private (Vector3 position, float fraction, Vector3 hitNormal, bool hit) StepSweep(Vector3 start, Vector3 delta, Vector3 halfExtents)
    {
        var direct = TraceBBox(start, start + delta, halfExtents);

        if (!direct.Hit)
        {
            return (start + delta, 1f, Vector3.UnitZ, false);
        }

        // Direct branch: stop at the wall
        var directPosition = direct.HitPosition;
        var directLateral = new Vector2(directPosition.X - start.X, directPosition.Y - start.Y).LengthSquared();

        // Stepped branch: step up as far as headroom allows, sweep, then settle back down
        var stepUpEnd = start + new Vector3(0, 0, StepSize);
        var upTrace = TraceBBox(start, stepUpEnd, halfExtents);
        var steppedStart = upTrace.Hit ? upTrace.HitPosition : stepUpEnd;

        var steppedSweep = TraceBBox(steppedStart, steppedStart + delta, halfExtents);
        var steppedSlide = steppedSweep.Hit ? steppedSweep.HitPosition : steppedStart + delta;

        var downEnd = steppedSlide + new Vector3(0, 0, -(StepSize + GroundProbeDistance));
        var downTrace = TraceBBox(steppedSlide, downEnd, halfExtents);

        // Reject unwalkable or embedded landings; the down tolerance keeps a low ceiling
        // from turning a step into a ledge drop
        var landingInvalid = !downTrace.Hit
            || downTrace.IsMinimalDistance
            || downTrace.HitNormal.Z < WalkableSlope
            || downTrace.HitPosition.Z - start.Z < -StepDownTolerance;

        if (!landingInvalid)
        {
            var steppedPosition = downTrace.HitPosition;
            var steppedLateral = new Vector2(steppedPosition.X - start.X, steppedPosition.Y - start.Y).LengthSquared();

            if (steppedLateral >= directLateral)
            {
                var stepDist = steppedPosition.Z - directPosition.Z;
                if (stepDist > 0f)
                {
                    Effects.OnStep(stepDist);
                }

                // The stepped move is blocked only if its lateral sweep hit a wall
                return (steppedPosition, LateralFraction(start, steppedPosition, delta), steppedSweep.HitNormal, steppedSweep.Hit);
            }
        }

        return (directPosition, LateralFraction(start, directPosition, delta), direct.HitNormal, true);
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

        var achieved = new Vector2(position.X - start.X, position.Y - start.Y).Length();
        return Math.Clamp(achieved / intended, 0f, 1f);
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
                var projectedDir = horizontalDir - normal * Vector3.Dot(horizontalDir, normal);

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
                var projectedDeltaDir = deltaDir - normal * Vector3.Dot(deltaDir, normal);

                if (projectedDeltaDir.LengthSquared() > 0.001f)
                {
                    delta = Vector3.Normalize(projectedDeltaDir) * deltaLength;
                }
            }

            return normal.Z;
        }
        else
        {
            delta -= normal * Vector3.Dot(delta, normal);
            velocity -= normal * Vector3.Dot(velocity, normal);
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
    /// Handle jump input - applies upward impulse
    /// Ported from cs_gamemovement.cpp CheckJumpButton()
    /// </summary>
    /// <remarks>
    /// The impulse is a discrete change to the velocity itself and nothing more. Clearing
    /// <see cref="OnGround"/> routes the rest of the frame down the air path, which puts this
    /// timestep's gravity into the frame's velocity delta like any other airborne frame - so the
    /// jump frame travels (v_impulse - g*dt/2) * dt and ends at v_impulse - g*dt, exactly what
    /// Source's post-jump FinishGravity half-step used to produce.
    /// </remarks>
    private void CheckJump()
    {
        OnGround = false;

        // Jump impulse scales by stamina as in CS: drained stamina makes successive jumps lower
        Velocity = new Vector3(Velocity.X, Velocity.Y, JumpImpulseValue * Stamina);
        SlopeClipNormalZ = 1f; // jump impulse is genuine vertical velocity
    }

    /// <summary>
    /// Calculate desired movement direction and speed from input
    /// </summary>
    private (Vector3 wishdir, float wishspeed) CalculateWishVelocity(float yaw, bool isWalking)
    {
        var (sinYaw, cosYaw) = MathF.SinCos(yaw);
        var forward = new Vector3(cosYaw, sinYaw, 0);
        var right = new Vector3(sinYaw, -cosYaw, 0);

        float forwardMove = 0, sideMove = 0;

        if (Input.Holding(TrackedKeys.W))
        {
            forwardMove += RunSpeed;
        }

        if (Input.Holding(TrackedKeys.S))
        {
            forwardMove -= RunSpeed;
        }

        if (Input.Holding(TrackedKeys.D))
        {
            sideMove += RunSpeed;
        }

        if (Input.Holding(TrackedKeys.A))
        {
            sideMove -= RunSpeed;
        }

        var wishvel = forward * forwardMove + right * sideMove;

        var wishspeed = wishvel.Length();
        var wishdir = wishspeed > 0 ? Vector3.Normalize(wishvel) : Vector3.Zero;

        // Walking lowers the max speed itself; deceleration to it then comes from friction alone
        var maxSpeed = RunSpeed;
        if (isWalking)
        {
            maxSpeed *= WalkSpeedModifier;
        }

        wishspeed = MathF.Min(wishspeed, maxSpeed);
        wishspeed *= DuckSpeedModifierActive;

        return (wishdir, wishspeed);
    }

    /// <summary>
    /// Ground friction in closed form, framerate-independent:
    /// exponential decay above stopspeed, linear below.
    /// </summary>
    private void Friction(float deltaTime)
    {
        var speed = Velocity.Length();

        // Provisional frame displacement under friction alone; Accelerate/the cap
        // overwrite it when they change the trajectory
        GroundMoveDelta = FrictionDisplacement(Velocity, deltaTime, FrictionValue * SurfaceFriction);

        if (speed < 0.1f)
        {
            return;
        }

        var frictionRate = FrictionValue * SurfaceFriction;
        float newSpeed;

        if (speed <= StopSpeedValue)
        {
            newSpeed = speed - StopSpeedValue * frictionRate * deltaTime;
        }
        else
        {
            newSpeed = speed * MathF.Exp(-frictionRate * deltaTime);

            if (newSpeed < StopSpeedValue)
            {
                // Crossed into the constant-drop regime mid-frame
                var timeToStopSpeed = MathF.Log(speed / StopSpeedValue) / frictionRate;
                newSpeed = StopSpeedValue - StopSpeedValue * frictionRate * (deltaTime - timeToStopSpeed);
            }
        }

        newSpeed = Math.Max(0, newSpeed);

        if (newSpeed != speed)
        {
            Velocity *= newSpeed / speed;
        }
    }

    /// <summary>
    /// Accelerate in the desired direction, integrating the combined friction+acceleration
    /// velocity change from <paramref name="preFrictionVelocity"/>. Returns how long the
    /// integrated segment is valid for: normally the full <paramref name="deltaTime"/>, but
    /// shorter when the trajectory hits an acceleration kink inside the frame (the
    /// sub-stopspeed kick boost switching off), so the caller can end the segment there and
    /// sweep the boosted and unboosted stretches as separate traces.
    /// </summary>
    private float Accelerate(Vector3 wishdir, float wishspeed, Vector3 preFrictionVelocity, float deltaTime, float duckModifier, bool isWalking, float wishScale = 1f)
    {
        var currentspeed = Vector3.Dot(Velocity, wishdir);
        var addspeed = wishspeed - currentspeed;

        if (addspeed <= 0)
        {
            return deltaTime;
        }

        var goalSpeed = AccelerationGoalSpeed(wishspeed, duckModifier, isWalking, wishScale);
        var frictionRate = FrictionValue * SurfaceFriction;

        // Walking tapers acceleration over the last 5 u/s to the goal; that band stays a
        // linear ODE along wishdir, so it is solved exactly (needs exponential-regime
        // friction, and for walking wishspeed equals the goal so addspeed never binds)
        if (isWalking && wishspeed >= goalSpeed - 0.01f && preFrictionVelocity.Length() > StopSpeedValue)
        {
            Velocity = WalkBandAccelerate(Velocity, wishdir, goalSpeed, preFrictionVelocity, deltaTime, frictionRate, AccelerateValue * goalSpeed * SurfaceFriction);
            GroundMoveDelta = TrapezoidDisplacement(preFrictionVelocity, Velocity, deltaTime);
            return deltaTime;
        }

        // Gradually reduce walk acceleration near goal speed
        var finalAccel = AccelerateValue;
        if (isWalking && currentspeed > goalSpeed - WalkTaperBand)
        {
            var ratio = (currentspeed - (goalSpeed - WalkTaperBand)) / WalkTaperBand;
            finalAccel *= Math.Max(0f, 1f - ratio);
        }

        var preFrictionSpeed = preFrictionVelocity.Length();
        var accelMagnitude = finalAccel * goalSpeed * SurfaceFriction;

        // Below stopspeed the friction+acceleration frame is integrated from the
        // pre-friction velocity (no elementary closed form there), superseding the
        // friction step already applied
        if (preFrictionSpeed <= StopSpeedValue)
        {
            // Kick accel: below accel·wishspeed·tick the sub-stopspeed friction is cancelled so
            // the wishdir component reaches this floor within one emulated tick (like the
            // engine's discrete accelspeed jump), keeping starts from rest snappy
            var kickSpeed = AccelerateValue * wishspeed * SurfaceFriction * TickInterval;
            var (kickVelocity, kickEndVelocity, kickEndTime) = SubStopSpeedAccelerate(preFrictionVelocity, wishdir, wishspeed, accelMagnitude, deltaTime, frictionRate, kickSpeed);

            // The boost switching off is a jump in acceleration and a natural segment
            // boundary: end the segment on the kink so GroundMove sweeps the boosted stretch
            // as its own trace, and report the shortened horizon. The unboosted remainder is
            // integrated by the next segment, starting from the kink velocity.
            if (kickEndTime >= 0f && kickEndTime < deltaTime - 1e-6f)
            {
                Velocity = kickEndVelocity;
                GroundMoveDelta = TrapezoidDisplacement(preFrictionVelocity, kickEndVelocity, kickEndTime);
                return kickEndTime;
            }

            Velocity = kickVelocity;
            GroundMoveDelta = TrapezoidDisplacement(preFrictionVelocity, Velocity, deltaTime);
            return deltaTime;
        }

        var equilibrium = wishdir * (accelMagnitude / frictionRate);
        var decay = MathF.Exp(-frictionRate * deltaTime);
        var odeVelocity = equilibrium + (preFrictionVelocity - equilibrium) * decay;

        // Pure friction+acceleration ODE frame: the addspeed gate stays open (wishdir
        // component ends at or below wishspeed) and the trajectory never leaves the
        // exponential friction regime. Exact and composing across any frame partition
        if (Vector3.Dot(odeVelocity, wishdir) <= wishspeed
            && OdeMinSpeedSquared(preFrictionVelocity, equilibrium, decay) > StopSpeedValue * StopSpeedValue)
        {
            Velocity = odeVelocity;
            GroundMoveDelta = TrapezoidDisplacement(preFrictionVelocity, Velocity, deltaTime);
            return deltaTime;
        }

        // Gate-bound or regime-crossing frame: the discrete update, whose clamp lands the
        // wishdir component exactly on wishspeed — matching the continuous pinned constraint
        var effectiveTime = (1f - decay) / frictionRate;
        Velocity += Math.Min(accelMagnitude * effectiveTime, addspeed) * wishdir;

        GroundMoveDelta = TrapezoidDisplacement(preFrictionVelocity, Velocity, deltaTime);
        return deltaTime;
    }

    /// <summary>
    /// The speed the acceleration ramp aims for: at least 250, scaled by
    /// <paramref name="duckModifier"/> and the walk modifier, and by how much of the wish
    /// survived clipping.
    ///
    /// The 250 floor is applied before <paramref name="wishScale"/> deliberately. Clipping shrinks
    /// the wishspeed below the floor, so folding the two the other way round would hide the
    /// reduction entirely and hand a wall-constrained segment the full open-ground acceleration.
    /// </summary>
    private static float AccelerationGoalSpeed(float wishspeed, float duckModifier, bool isWalking, float wishScale = 1f)
    {
        var goalSpeed = MathF.Max(250.0f, wishspeed) * wishScale * duckModifier;

        if (isWalking)
        {
            goalSpeed *= WalkSpeedModifier;
        }

        return goalSpeed;
    }

    /// <summary>
    /// Integrates one ground segment's combined friction+acceleration velocity change from
    /// the current <see cref="Velocity"/>, leaving the end velocity in <see cref="Velocity"/>
    /// and the segment displacement in <see cref="GroundMoveDelta"/>. Friction and
    /// acceleration are not separate steps: friction supplies the decay term of the same ODE
    /// that acceleration drives, so this runs friction first only to seed the addspeed gate
    /// and the no-prestrafe cap, then <see cref="Accelerate"/> integrates them together.
    /// Returns the segment horizon (the full <paramref name="deltaTime"/>, or an earlier
    /// acceleration kink), so the caller can split its swept move on the same boundary.
    ///
    /// <paramref name="wishScale"/> is how much of the raw wish survived the caller's clip to the
    /// surfaces met this frame (1 when unobstructed). The wishdir/wishspeed passed in are already
    /// the clipped ones; the scale additionally shrinks the acceleration magnitude, so a
    /// surface-parallel segment gains exactly the tangential acceleration the unclipped wish
    /// would have contributed.
    ///
    /// Note that <see cref="GroundMoveDelta"/> is deliberately the trapezoid on every path, even
    /// where an exact closed form exists. The trapezoid *is* the
    /// linear-velocity model, which is the model <see cref="DistanceToTimeFraction"/> inverts,
    /// so with it the distance -> time conversion applied to a swept segment is exact for the
    /// delta being swept rather than an approximation of it. An exact-ODE delta would make the
    /// segment displacement itself more accurate while breaking that: the average speed the
    /// conversion is handed (segDistance / segDt) would no longer be (v0 + v1) / 2, so the
    /// quadratic would no longer match the chord it is describing. Keeping the whole ground path
    /// on one consistent velocity model is worth more than the second-order distance error.
    /// </summary>
    private float WalkMove(Vector3 wishdir, float wishspeed, float deltaTime, float duckModifier, bool isWalking, float wishScale = 1f)
    {
        // Come to a complete stop from a crawl. Source runs this before Accelerate; after
        // it, high framerates would re-zero every frame's sub-unit acceleration gain and
        // the player could never start moving
        if (Velocity.LengthSquared() < 1f)
        {
            Velocity = Vector3.Zero;
        }

        var preFrictionVelocity = Velocity;

        Friction(deltaTime);
        var previousSpeed = Velocity.Length();

        var segDt = Accelerate(wishdir, wishspeed, preFrictionVelocity, deltaTime, duckModifier, isWalking, wishScale);

        // The cap's closed forms assume a full frame; a shortened (kinked) segment stays
        // deep below the cap, so only apply it when the whole frame was integrated
        if (!PrestrafeEnabled && segDt >= deltaTime - 1e-6f)
        {
            var frictionRate = FrictionValue * SurfaceFriction;
            var accelMagnitude = AccelerateValue * AccelerationGoalSpeed(wishspeed, duckModifier, isWalking, wishScale) * SurfaceFriction;
            var preCapVelocity = Velocity;
            Velocity = CapSpeedNoPrestrafe(Velocity, wishdir, wishspeed, previousSpeed, preFrictionVelocity, deltaTime, frictionRate, accelMagnitude);

            // A cap intervention changes the trajectory mid-frame; fall back to the trapezoid
            if ((Velocity - preCapVelocity).LengthSquared() > 1e-8f)
            {
                GroundMoveDelta = TrapezoidDisplacement(preFrictionVelocity, Velocity, deltaTime);
            }
        }

        return segDt;
    }

    /// <summary>
    /// Linear (non-rotating) air acceleration for a segment that is riding a surface, returning
    /// the velocity gain. Three things separate it from <see cref="AirMove"/>:
    ///
    /// The gain is applied along the wish clipped to the surfaces rather than along wishdir, so
    /// none of it is spent pushing into a ramp only for the sweep to strip it again.
    ///
    /// The budget is scaled by 1/changeRate. The gate is still measured along the raw wishdir
    /// (unchanged from the engine), but travelling along the clipped direction only raises that
    /// component at rate |acceldir|², so closing a given addspeed costs proportionally more
    /// magnitude. Without this the clip would slow the approach to the gate as well as redirect it.
    ///
    /// The gain per unit time is capped at what the emulated tick would deliver: a discrete engine
    /// closes at most the whole remaining addspeed once per tick, so a continuous form that is not
    /// bounded the same way out-accelerates the reference engine as the frame time shrinks.
    /// TODO for refactor: This should be rewritten
    /// </summary>
    private Vector3 AirAccelerateClipped(Vector3 velocity, Vector3 wishdir, float wishspeed, float segTime, ReadOnlySpan<Vector3> planes)
    {
        var cap = MathF.Min(wishspeed, AirMaxWishSpeed);

        // Note: the accel rate uses the original wishspeed, NOT the capped value
        var accelRate = AirAccelerate * wishspeed * SurfaceFriction;

        // Deliberately not renormalised: the length is how much of the wish is achievable, and
        // both the budget scaling and the applied gain are defined in terms of it
        var acceldir = ClipWishVelocity(wishdir, planes, onGround: false, out var resolved);

        var changeRate = resolved ? Vector3.Dot(wishdir, acceldir) : 0f;

        if (changeRate <= 1e-6f)
        {
            return Vector3.Zero; // wish points straight into the surface; nothing is achievable
        }

        var currentspeed = Vector3.Dot(velocity, wishdir);
        var addspeed = cap - currentspeed;

        if (addspeed <= 0f)
        {
            return Vector3.Zero;
        }

        var accelspeed = MathF.Min(accelRate * segTime, addspeed / changeRate);

        accelspeed = MathF.Min(accelspeed, addspeed * EmulatedTickRate * segTime);

        return accelspeed * acceldir;
    }

    /// <summary>
    /// Air acceleration, tickless: the frame's view rotation is integrated in closed form
    /// so strafe gain depends on degrees turned, not framerate. Key-change wishdir jumps
    /// stay discrete events (instant top-up). Falls back to the plain discrete update
    /// when the acceleration budget binds, which is linear in dt and already invariant.
    /// </summary>
    private void AirMove(Vector3 wishdir, float wishspeed, float deltaTime, float yawDelta)
    {
        if (wishspeed <= 0f)
        {
            return;
        }

        var cap = Math.Min(wishspeed, AirMaxWishSpeed);

        // Note: the accel rate uses original wishspeed, NOT the capped value
        var accelRate = AirAccelerate * wishspeed * SurfaceFriction;

        if (TicklessAirStrafe(Velocity, wishdir, cap, yawDelta, accelRate, deltaTime) is Vector3 strafed)
        {
            Velocity = strafed;
            return;
        }

        var currentspeed = Vector3.Dot(Velocity, wishdir);
        var addspeed = cap - currentspeed;

        if (addspeed <= 0)
        {
            return;
        }

        Velocity += Math.Min(accelRate * deltaTime, addspeed) * wishdir;
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
        // form hands back a spurious fraction of travel every frame — one that grows as the slide
        // angle, and with it approach, shrinks with the frame time.
        var perpendicularSlack = (raw.Distance * approach) - SurfaceEpsilon;

        // Within this the hull counts as already flush: a resting contact, credited no travel
        var positionScale = MathF.Max(MathF.Abs(from.X), MathF.Max(MathF.Abs(from.Y), MathF.Abs(from.Z)));
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

    private Rubikon.TraceResult TraceBBoxRaw(Vector3 from, Vector3 to, Vector3 halfExtents, bool detectStartSolid)
    {
        var result = Physics != null
            ? Physics.TraceAABB(from, to, halfExtents, "player", detectStartSolid)
            : TraceInfiniteGroundPlane(from, to, halfExtents, detectStartSolid);

        if (Physics != null && GridPlaneCollisionEnabled)
        {
            result.MinimizeWith(TraceInfiniteGroundPlane(from, to, halfExtents, detectStartSolid));
        }

        foreach (var plane in DebugCollisionPlanes)
        {
            var normal = new Vector3(plane.X, plane.Y, plane.Z);
            result.MinimizeWith(TraceStaticPlane(from, to, halfExtents, normal, plane.W, detectStartSolid));
        }

        return result;
    }

    /// <summary>
    /// Swept-AABB trace against a static half-space (n·x ≤ d is solid, <paramref name="normal"/>
    /// pointing out of the solid). Backs <see cref="TraceInfiniteGroundPlane"/> and the headless
    /// collision tests' <see cref="DebugCollisionPlanes"/>.
    /// </summary>
    private static Rubikon.TraceResult TraceStaticPlane(Vector3 from, Vector3 to, Vector3 halfExtents, Vector3 normal, float planeOffset, bool detectStartSolid)
    {
        // Extent of the box toward the plane along the normal (support half-width)
        var extent = MathF.Abs(normal.X) * halfExtents.X + MathF.Abs(normal.Y) * halfExtents.Y + MathF.Abs(normal.Z) * halfExtents.Z;

        // Signed gap of the box's nearest face to the plane at each end (>0 outside the solid)
        var gapFrom = Vector3.Dot(normal, from) - planeOffset - extent;
        var gapTo = Vector3.Dot(normal, to) - planeOffset - extent;

        if (gapFrom < 0f)
        {
            return new Rubikon.TraceResult(true, from, normal, 0f, -1) { StartSolid = detectStartSolid };
        }

        var closing = gapFrom - gapTo; // positive when the sweep moves toward the plane

        if (closing <= 0f)
        {
            return new Rubikon.TraceResult(); // moving away or parallel while outside
        }

        var fraction = gapFrom / closing;

        if (fraction > 1f)
        {
            return new Rubikon.TraceResult(); // the sweep ends before reaching the plane
        }

        var hitPosition = Vector3.Lerp(from, to, fraction);
        return new Rubikon.TraceResult(true, hitPosition, normal, Vector3.Distance(from, hitPosition), -1);
    }

    /// <summary>
    /// Trace against an infinite ground plane at Z=0.
    /// </summary>
    private static Rubikon.TraceResult TraceInfiniteGroundPlane(Vector3 from, Vector3 to, Vector3 halfExtents, bool detectStartSolid)
        => TraceStaticPlane(from, to, halfExtents, Vector3.UnitZ, 0f, detectStartSolid);

    /// <summary>
    /// Cosmetic view-feel state driven by the simulation: jump stamina, the landing
    /// dip/roll punch, and stair-step view smoothing.
    /// </summary>
    private sealed class ViewEffects
    {
        private const float StepSmoothingSlope = 0.6f;    // decay per unit of horizontal speed (~stair rise/run)
        private const float StepSmoothingMinSpeed = 100f; // decay floor

        /// <summary>Jump stamina from 0 to 1; landing drains it and jump impulses scale by it.</summary>
        public float Stamina { get; private set; } = 1f;

        /// <summary>Downward view punch from landing, in degrees (Source's m_viewPunchAngle pitch).</summary>
        public float ViewPunchPitchDegrees { get; private set; }

        /// <summary>Camera roll from a hard landing, in degrees.</summary>
        public float LandingRollDegrees { get; private set; }

        // Stair-step view smoothing (Source m_outStepHeight): pending signed view offset
        // from hull step jumps, decays toward zero
        public float StepOffset { get; private set; }

        private const float PlayerFatalFallSpeed = 1024f; // PLAYER_FATAL_FALL_SPEED

        public void Reset()
        {
            Stamina = 1f;
            ViewPunchPitchDegrees = 0f;
            LandingRollDegrees = 0f;
            StepOffset = 0f;
        }

        /// <summary>
        /// Touchdown effects, applied once on the frame the player lands: drain stamina and punch
        /// the view down. Falls past the safe threshold also kick the CS 1.6-style camera roll.
        /// </summary>
        /// <param name="impactSpeed">Downward speed at the moment of impact, in units per second.</param>
        /// <param name="audible">Whether this landing made a noise; a silent one does not punch the view.</param>
        public void OnLanded(float impactSpeed, bool audible)
        {
            Stamina = MathF.Max(0f, Stamina - 0.1f);
            LandingRollDegrees = Math.Clamp((impactSpeed - 300f) * 0.013f, 0f, 10f);

            if (audible)
            {
                ViewPunchPitchDegrees = MathF.Max(MathF.Min(impactSpeed, PlayerFatalFallSpeed) * 0.001f, 0.75f);
            }
        }

        /// <summary>
        /// CGameMovement::DecayViewPunchAngle.
        /// </summary>
        public void DecayViewPunch(float deltaTime, float decayRate)
        {
            ViewPunchPitchDegrees *= MathF.Exp(-decayRate * deltaTime);
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

            // Stamina recovers toward full (sv_staminarecoveryrate 60 / sv_staminamax 80)
            Stamina = MathF.Min(1f, Stamina + 0.75f * deltaTime);

            // The landing roll snaps on at impact; recovering it is a fast exponential ease-out
            var landingRecovery = MathF.Exp(-12f * deltaTime);
            LandingRollDegrees = LandingRollDegrees > 0.01f ? LandingRollDegrees * landingRecovery : 0f;
        }
    }
}
