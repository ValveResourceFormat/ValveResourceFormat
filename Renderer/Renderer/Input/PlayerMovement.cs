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
    private const float AirAccelerateValue = 150f;        // sv_airaccelerate (engine default is 12; raised to allow surfing)
    private const float JumpImpulseValue = 301.993377f;   // sv_jump_impulse = sqrt(2*800*57)
    private const float MaxVelocityValue = 3500f;         // sv_maxvelocity

    private const float WalkSpeedModifier = 0.52f;        // CS_PLAYER_SPEED_WALK_MODIFIER
    private const float DuckSpeedModifier = 0.34f;        // CS_PLAYER_SPEED_DUCK_MODIFIER

    private const float NonJumpVelocity = 140f;           // Moving up faster than this means airborne (NON_JUMP_VELOCITY)
    private const float AirMaxWishSpeed = 30f;            // Air-control wishspeed cap (AirAccelerate)

    private const float ViewHeightOffset = 8f;
    private static readonly float ViewHeightStanding = StandingHullHalfExtents.Z * 2f - ViewHeightOffset;
    private static readonly float ViewHeightDucked = DuckedHullHalfExtents.Z * 2f - ViewHeightOffset;

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

    // Analytically integrated displacement of the current ground frame (set by
    // Friction/Accelerate/WalkMove); ground moves use it instead of Velocity * dt so
    // distance traveled is framerate-independent while speed is changing
    private Vector3 GroundMoveDelta;

    private readonly ViewEffects Effects = new();

    /// <summary>
    /// Gets the current jump stamina, from 0 to 1. Landing drains it, it recovers over roughly
    /// a second, and jump impulses scale by it, so spammed hops get progressively lower (like
    /// CS's sv_stamina behavior).
    /// </summary>
    public float Stamina => Effects.Stamina;

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
    /// Linear value from 0 to 1 representing how much the player is crouched.  0 = standing, 1 = fully crouched.
    /// </summary>
    public float CrouchBlend { get; private set; }

    /// <summary>
    /// Gets the current eye height blended between standing and crouched positions.
    /// </summary>
    public float BlendedEyeHeight { get; private set; }

    /// <summary>The current eye position</summary>
    public Vector3 EyePosition { get; private set; }

    private float DuckSpeedModifierActive => (HoldingCtrl || CrouchBlend > 0f) ? DuckSpeedModifier : 1f;
    private Vector3 SnappedHullHalfExtents => HoldingCtrl ? DuckedHullHalfExtents : StandingHullHalfExtents;

    /// <summary>
    /// Gets the current collision hull half-extents, with height blended between the standing
    /// and ducked hulls by <see cref="CrouchBlend"/>.
    /// </summary>
    public Vector3 HullHalfExtents => new(
        StandingHullHalfExtents.X,
        StandingHullHalfExtents.Y,
        float.Lerp(StandingHullHalfExtents.Z, DuckedHullHalfExtents.Z, CrouchBlend));

    private const float SurfaceFriction = 1.0f;
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

    /// <summary>
    /// Initializes a new <see cref="PlayerMovement"/> bound to the given user input source.
    /// </summary>
    /// <param name="input">The user input instance used to read key state.</param>
    public PlayerMovement(UserInput input)
    {
        Input = input;
        Velocity = Vector3.Zero;
        OnGround = false;
        Initialize = false;
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
        Effects.Reset();
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

        deltaTime = MathF.Min(deltaTime, MaxFrameDeltaTime);

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

        if (justLanded)
        {
            Effects.OnLanded(fallSpeed);
        }

        // StartGravity
        if (!OnGround)
        {
            ApplyHalfGravity(deltaTime);
            CheckVelocity(ref position);
        }

        var wantsToJump = AutoBunnyHop ? Input.Holding(TrackedKeys.Space) : Input.Pressed(TrackedKeys.Space);
        wantsToJump = wantsToJump || Input.Holding(TrackedKeys.MouseWheelDown) || Input.Holding(TrackedKeys.MouseWheelUp);

        if (wantsToJump && (OnGround || (AutoBunnyHop && justLanded)))
        {
            if (!AutoBunnyHop)
            {
                PreventBunnyJumping();
            }
            CheckJump(deltaTime);
        }

        var (wishdir, wishspeed) = CalculateWishVelocity(yaw, isWalking);

        if (OnGround)
        {
            ZeroVerticalVelocity();
            var preFrictionVelocity = Velocity;
            Friction(deltaTime);
            WalkMove(wishdir, wishspeed, preFrictionVelocity, deltaTime, isDucking, isWalking);
        }
        else
        {
            AirMove(wishdir, wishspeed, deltaTime);
        }

        CheckVelocity(ref position);

        // Falling continued through StartGravity; keep the freshest impact speed for the
        // landing that the move below may produce
        fallSpeed = MathF.Max(fallSpeed, -Velocity.Z);

        // Ground movement gets step support; in the air there is nothing to step off.
        // The air delta is exact as-is: the half-gravity leapfrog makes Velocity * dt the
        // midpoint integral
        position = OnGround
            ? StepMove(position, GroundMoveDelta, playerHull)
            : TryPlayerMove(position, Velocity * deltaTime, playerHull);

        if (OnGround)
        {
            StayOnGround(ref position, playerHull);
        }

        CategorizePosition(ref position, playerHull);
        CheckVelocity(ref position);

        // FinishGravity
        if (!OnGround)
        {
            ApplyHalfGravity(deltaTime);
            CheckVelocity(ref position);
        }

        CheckStuck(ref position, playerHull);

        // The other landing path: airborne at the start of the frame, grounded by the move
        // above. justLanded already handled the start-of-frame categorize path.
        if (!justLanded && !WasOnGroundLastFrame && OnGround)
        {
            Effects.OnLanded(fallSpeed);
        }

        TracePosition = position;

        var horizontalSpeed = new Vector2(Velocity.X, Velocity.Y).Length();
        Effects.Update(deltaTime, horizontalSpeed, StepSmoothingEnabled);

        TracePositionSmooth = TracePosition - new Vector3(0, 0, Effects.StepOffset);

        BlendedEyeHeight = ViewHeightStanding + (ViewHeightDucked - ViewHeightStanding) * CrouchBlend - Effects.LandingDipOffset;
        EyePosition = Position + Vector3.UnitZ * BlendedEyeHeight;
        camera.Location = EyePosition;
        camera.Roll = float.DegreesToRadians(Effects.LandingRollDegrees);
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

    /// <summary>
    /// Half-step gravity (Source's StartGravity/FinishGravity leapfrog).
    /// </summary>
    private void ApplyHalfGravity(float deltaTime)
    {
        Velocity = new Vector3(Velocity.X, Velocity.Y, Velocity.Z - GravityValue * deltaTime * 0.5f);
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
        => trace.Hit && trace.HitNormal.Z > WalkableSlope;

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

        // A steep surface can shadow walkable ground; retry with quarter-footprint hulls
        if (!grounded && result.Hit)
        {
            grounded = ProbeGroundQuadrants(position, halfExtents);
        }

        // NON_JUMP_VELOCITY guard, on the Z velocity a plain projection would have produced (see SlopeClipNormalZ)
        OnGround = grounded && Velocity.Z * SlopeClipNormalZ < NonJumpVelocity;

        if (OnGround)
        {
            SnapToGround(ref position, result, snapDownOnly: false);
        }
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

        for (var distance = 1f; distance <= 100f; distance += 10f)
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
    /// Perform swept AABB collision detection for player movement with multi-bounce sliding
    /// </summary>
    private Vector3 TryPlayerMove(Vector3 start, Vector3 delta, Vector3 halfExtents)
    {
        if (delta.LengthSquared() < UntraceableDistanceSquared)
        {
            return start;
        }

        const int MaxBumps = 6;

        var position = start;
        var remainingDelta = delta;
        var remainingDistance = delta.Length();
        var remainingFraction = 1.0f;

        // Stop dead if clipping ever turns the velocity against the entry velocity (corner ping-pong)
        var entryVelocity = Velocity;

        // Planes hit without making progress; movement must be clipped to parallel all of them
        Span<Vector3> planes = stackalloc Vector3[MaxBumps];
        var planeCount = 0;

        for (var bump = 0; bump < MaxBumps && remainingFraction > 0; bump++)
        {
            var result = TraceBBox(position, position + remainingDelta, halfExtents);

            if (!result.Hit)
            {
                return position + remainingDelta;
            }

            // Advance to the hit point (already margin-adjusted by TraceBBox)
            var fraction = result.Distance / remainingDistance;

            position = result.HitPosition;
            remainingFraction *= 1f - fraction;

            // Consume the traveled portion of the move budget (Source: time_left -= time_left * fraction)
            remainingDelta *= 1f - fraction;

            // Progress invalidates the accumulated planes
            if (fraction > 0)
            {
                planeCount = 0;
            }

            // 2024-11-07 CS2 update
            if (!OnGround && Velocity.Z < 0f && result.HitNormal.Z > WalkableSlope
                && new Vector2(Velocity.X, Velocity.Y).LengthSquared() < 1f)
            {
                Velocity = Vector3.Zero;
                break;
            }

            planes[planeCount++] = result.HitNormal;

            if (!ClipToPlanes(planes[..planeCount], ref remainingDelta, out var velocity, out var clipNormalZ)
                || Vector3.Dot(velocity, entryVelocity) <= 0)
            {
                // Trapped by three or more planes, or clipping reversed the move
                Velocity = Vector3.Zero;
                break;
            }

            Velocity = velocity;
            SlopeClipNormalZ = clipNormalZ;

            CheckVelocity(ref position);

            remainingDistance = remainingDelta.Length();
            if (remainingDistance <= NegligibleMoveDistance)
            {
                break;
            }
        }

        return position;
    }

    /// <summary>
    /// Clips the movement to parallel every accumulated plane, as in Source's TryPlayerMove.
    /// Returns false when no direction satisfies all planes.
    /// </summary>
    private bool ClipToPlanes(ReadOnlySpan<Vector3> planes, ref Vector3 delta, out Vector3 velocity, out float clipNormalZ)
    {
        // Prefer a single-plane clip that does not move into any other plane
        for (var i = 0; i < planes.Length; i++)
        {
            var candidateVelocity = Velocity;
            var candidateDelta = delta;
            var candidateNormalZ = ClipVelocity(ref candidateDelta, ref candidateVelocity, planes[i], OnGround);

            var valid = true;
            for (var j = 0; j < planes.Length; j++)
            {
                if (j != i && Vector3.Dot(candidateVelocity, planes[j]) < 0)
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
            velocity = Velocity;
            ClipToCrease(ref delta, ref velocity, planes[0], planes[1]);
            return true;
        }

        velocity = Vector3.Zero;
        delta = Vector3.Zero;
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
    /// Ground move with step support, like Source's StepMove: run the slide normally and again
    /// from a stepped-up position, then keep whichever branch traveled farther laterally.
    /// </summary>
    private Vector3 StepMove(Vector3 start, Vector3 delta, Vector3 halfExtents)
    {
        if (delta.LengthSquared() < UntraceableDistanceSquared)
        {
            return start;
        }

        var entryVelocity = Velocity;

        // Consult the step whenever the direct path is obstructed at all (as Source does)
        if (!TraceBBox(start, start + delta, halfExtents).Hit)
        {
            return start + delta;
        }

        // Branch 1: plain slide along the ground
        var downPosition = TryPlayerMove(start, delta, halfExtents);
        var downVelocity = Velocity;
        var downClipNormalZ = SlopeClipNormalZ;

        // Branch 2: step up as far as headroom allows, slide at that height, then settle back down
        Velocity = entryVelocity;

        var stepUpEnd = start + new Vector3(0, 0, StepSize);
        var upTrace = TraceBBox(start, stepUpEnd, halfExtents);
        var steppedStart = upTrace.Hit ? upTrace.HitPosition : stepUpEnd;

        var steppedSlidePosition = TryPlayerMove(steppedStart, delta, halfExtents);

        var downEnd = steppedSlidePosition + new Vector3(0, 0, -(StepSize + GroundProbeDistance));
        var downTrace = TraceBBox(steppedSlidePosition, downEnd, halfExtents);

        // Reject unwalkable or embedded landings, as Source does; the down tolerance
        // keeps a low ceiling from turning a step into a ledge drop
        var landingInvalid = !downTrace.Hit
            || downTrace.IsMinimalDistance
            || downTrace.HitNormal.Z < WalkableSlope
            || downTrace.HitPosition.Z - start.Z < -StepDownTolerance;

        if (!landingInvalid)
        {
            var steppedPosition = downTrace.HitPosition;

            // Keep whichever branch went farther laterally (Source compares fLateralDist the same way)
            var downLateral = new Vector2(downPosition.X - start.X, downPosition.Y - start.Y).LengthSquared();
            var steppedLateral = new Vector2(steppedPosition.X - start.X, steppedPosition.Y - start.Y).LengthSquared();

            if (steppedLateral >= downLateral)
            {
                // Vertical velocity comes from the ground branch so stepping does not manufacture upward speed
                Velocity = new Vector3(Velocity.X, Velocity.Y, downVelocity.Z);
                SlopeClipNormalZ = downClipNormalZ;

                // Record the lift for view smoothing
                var stepDist = steppedPosition.Z - downPosition.Z;
                if (stepDist > 0f)
                {
                    Effects.OnStep(stepDist);
                }

                return steppedPosition;
            }
        }

        Velocity = downVelocity;
        SlopeClipNormalZ = downClipNormalZ;
        return downPosition;
    }

    /// <summary>
    /// Clips velocity against a surface normal. Returns the normal.Z of a walkable-slope clip
    /// (1 otherwise) so callers can recover the plain-projection vertical velocity.
    /// </summary>
    private static float ClipVelocity(ref Vector3 delta, ref Vector3 velocity, Vector3 normal, bool onGround)
    {
        // Walkable slopes on ground maintain horizontal speed
        if (onGround && normal.Z > WalkableSlope)
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
    private void CheckJump(float deltaTime)
    {
        OnGround = false;

        // Jump impulse scales by stamina as in CS: drained stamina makes successive jumps lower
        Velocity = new Vector3(Velocity.X, Velocity.Y, JumpImpulseValue * Stamina);
        SlopeClipNormalZ = 1f; // jump impulse is genuine vertical velocity

        // FinishGravity is called after jump in Source
        ApplyHalfGravity(deltaTime);
    }

    /// <summary>
    /// Calculate desired movement direction and speed from input
    /// </summary>
    private (Vector3 wishdir, float wishspeed) CalculateWishVelocity(float yaw, bool isWalking)
    {
        var forward = new Vector3(MathF.Cos(yaw), MathF.Sin(yaw), 0);
        var right = new Vector3(MathF.Cos(yaw - MathF.PI / 2f), MathF.Sin(yaw - MathF.PI / 2f), 0);

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
        wishvel = new Vector3(wishvel.X, wishvel.Y, 0);

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
    /// Accelerate in desired direction
    /// </summary>
    private void Accelerate(Vector3 wishdir, float wishspeed, Vector3 preFrictionVelocity, float deltaTime, bool isDucking, bool isWalking)
    {
        var currentspeed = Vector3.Dot(Velocity, wishdir);
        var addspeed = wishspeed - currentspeed;

        if (addspeed <= 0)
        {
            return;
        }

        currentspeed = Math.Max(0, currentspeed);

        var goalSpeed = AccelerationGoalSpeed(wishspeed, isDucking, isWalking);
        var frictionRate = FrictionValue * SurfaceFriction;

        // Walking tapers acceleration over the last 5 u/s to the goal; that band stays a
        // linear ODE along wishdir, so it is solved exactly (needs exponential-regime
        // friction, and for walking wishspeed equals the goal so addspeed never binds)
        if (isWalking && wishspeed >= goalSpeed - 0.01f && preFrictionVelocity.Length() > StopSpeedValue)
        {
            Velocity = WalkBandAccelerate(Velocity, wishdir, goalSpeed, preFrictionVelocity, deltaTime, frictionRate, AccelerateValue * goalSpeed * SurfaceFriction);
            GroundMoveDelta = TrapezoidDisplacement(preFrictionVelocity, Velocity, deltaTime);
            return;
        }

        // Gradually reduce walk acceleration near goal speed
        var finalAccel = AccelerateValue;
        if (isWalking && currentspeed > goalSpeed - WalkTaperBand)
        {
            var ratio = Math.Max(0.0f, currentspeed - (goalSpeed - WalkTaperBand)) / WalkTaperBand;
            finalAccel *= Math.Clamp(1.0f - ratio, 0.0f, 1.0f);
        }

        var preFrictionSpeed = preFrictionVelocity.Length();
        var accelMagnitude = finalAccel * goalSpeed * SurfaceFriction;

        // Below stopspeed the friction+acceleration frame is solved analytically from the
        // pre-friction velocity (linear friction regime, split at the stopspeed crossing),
        // superseding the friction step already applied
        if (preFrictionSpeed <= StopSpeedValue)
        {
            var (velocity, displacement) = SubStopSpeedAccelerate(preFrictionVelocity, wishdir, accelMagnitude, deltaTime, frictionRate);

            // The addspeed gate binding mid-frame keeps the discrete update instead
            if (Vector3.Dot(velocity, wishdir) > wishspeed)
            {
                Velocity += Math.Min(accelMagnitude * deltaTime, addspeed) * wishdir;
                GroundMoveDelta = TrapezoidDisplacement(preFrictionVelocity, Velocity, deltaTime);
            }
            else
            {
                Velocity = velocity;
                GroundMoveDelta = displacement;
            }

            return;
        }

        // Exact companion to the exponential friction: A*(1-e^(-f*dt))/f completes the
        // closed-form solution, keeping the friction/acceleration equilibrium framerate-independent
        var effectiveTime = (1f - MathF.Exp(-frictionRate * deltaTime)) / frictionRate;

        var accelspeed = Math.Min(accelMagnitude * effectiveTime, addspeed);
        var clamped = accelspeed >= addspeed;
        Velocity += accelspeed * wishdir;

        // Exact displacement while the frame is the pure friction+acceleration ODE
        // (exponential regime throughout, addspeed gate never binding); otherwise the
        // trapezoid, which is itself exact below stopspeed where velocity is linear in time
        if (!clamped && preFrictionSpeed > StopSpeedValue
            && preFrictionSpeed * MathF.Exp(-frictionRate * deltaTime) > StopSpeedValue)
        {
            var equilibrium = wishdir * (accelMagnitude / frictionRate);
            GroundMoveDelta = LinearOdeDisplacement(preFrictionVelocity, equilibrium, deltaTime, frictionRate);
        }
        else
        {
            GroundMoveDelta = TrapezoidDisplacement(preFrictionVelocity, Velocity, deltaTime);
        }
    }

    /// <summary>
    /// The speed the acceleration ramp aims for: at least 250, scaled by
    /// the duck/walk modifiers.
    /// </summary>
    private float AccelerationGoalSpeed(float wishspeed, bool isDucking, bool isWalking)
    {
        var goalSpeed = MathF.Max(250.0f, wishspeed);

        if (isDucking)
        {
            goalSpeed *= DuckSpeedModifierActive;
        }

        if (isWalking)
        {
            goalSpeed *= WalkSpeedModifier;
        }

        return goalSpeed;
    }

    /// <summary>
    /// Ground movement with friction and acceleration
    /// </summary>
    private void WalkMove(Vector3 wishdir, float wishspeed, Vector3 preFrictionVelocity, float deltaTime, bool isDucking, bool isWalking)
    {
        // Come to a complete stop from a crawl. Source runs this before Accelerate; after
        // it, high framerates would re-zero every frame's sub-unit acceleration gain and
        // the player could never start moving
        if (Velocity.LengthSquared() < 1f)
        {
            Velocity = Vector3.Zero;
        }

        var previousSpeed = Velocity.Length();
        Accelerate(wishdir, wishspeed, preFrictionVelocity, deltaTime, isDucking, isWalking);

        if (!PrestrafeEnabled)
        {
            var frictionRate = FrictionValue * SurfaceFriction;
            var accelMagnitude = AccelerateValue * AccelerationGoalSpeed(wishspeed, isDucking, isWalking) * SurfaceFriction;
            var preCapVelocity = Velocity;
            Velocity = CapSpeedNoPrestrafe(Velocity, wishdir, wishspeed, previousSpeed, preFrictionVelocity, deltaTime, frictionRate, accelMagnitude);

            // A cap intervention changes the trajectory mid-frame; fall back to the trapezoid
            if ((Velocity - preCapVelocity).LengthSquared() > 1e-8f)
            {
                GroundMoveDelta = TrapezoidDisplacement(preFrictionVelocity, Velocity, deltaTime);
            }
        }
    }

    /// <summary>
    /// Air acceleration. Timestep-independent for a fixed wish direction;
    /// residual framerate sensitivity comes from per-frame input sampling.
    /// </summary>
    private void AirMove(Vector3 wishdir, float wishspeed, float deltaTime)
    {
        var wishspd = Math.Min(wishspeed, AirMaxWishSpeed);
        var currentspeed = Vector3.Dot(Velocity, wishdir);
        var addspeed = wishspd - currentspeed;

        if (addspeed <= 0)
        {
            return;
        }

        // Note: uses original wishspeed, NOT the capped wishspd
        var accelspeed = Math.Min(AirAccelerateValue * wishspeed * deltaTime * SurfaceFriction, addspeed);
        Velocity += accelspeed * wishdir;
    }

    /// <summary>
    /// Check and clamp velocity - prevents NaN and enforces max velocity
    /// </summary>
    private void CheckVelocity(ref Vector3 position)
    {
        position.X = float.IsNaN(position.X) ? TracePosition.X : position.X;
        position.Y = float.IsNaN(position.Y) ? TracePosition.Y : position.Y;
        position.Z = float.IsNaN(position.Z) ? TracePosition.Z : position.Z;

        Velocity = new Vector3(
            float.IsNaN(Velocity.X) ? 0f : Velocity.X,
            float.IsNaN(Velocity.Y) ? 0f : Velocity.Y,
            float.IsNaN(Velocity.Z) ? 0f : Velocity.Z);

        var velocityBounds = new AABB(Vector3.Zero, MaxVelocityValue);
        Velocity = Vector3.Clamp(Velocity, velocityBounds.Min, velocityBounds.Max);

        var movementBounds = new AABB(Vector3.Zero, 16_000f);
        position = Vector3.Clamp(position, movementBounds.Min, movementBounds.Max);

        // sanity check, compare against last position
        movementBounds = new AABB(TracePosition, MathF.Max(StepSize * 2f, Velocity.Length()));
        position = Vector3.Clamp(position, movementBounds.Min, movementBounds.Max);
    }

    // How far past the sweep end the raw trace looks so that approaching surfaces are
    // seen before the hull enters their margin. Grazing surfaces (perpendicular gap
    // closing slower than SurfaceEpsilon per this many units) escape the lookahead and
    // are instead handled by the margin-restore push below.
    private const float MarginLookahead = 4f;

    /// <summary>
    /// Forward-looking keep-away margin: the raw trace extends a little past the sweep end
    /// and the move stops where the perpendicular gap to the hit surface equals
    /// SurfaceEpsilon. Resting against a surface then yields a stable zero-distance hit
    /// every frame (velocity gets clipped, position holds), instead of the old
    /// creep-into-the-margin-then-snap-back cycle that made position and speed visibly
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

        // Stop where the perpendicular gap reaches SurfaceEpsilon
        var allowed = raw.Distance - SurfaceEpsilon / approach;

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

        return result;
    }

    /// <summary>
    /// Cheap trace against an infinite ground plane at Z=0
    /// </summary>
    private static Rubikon.TraceResult TraceInfiniteGroundPlane(Vector3 from, Vector3 to, Vector3 halfExtents, bool detectStartSolid)
    {
        var bottomStart = from.Z - halfExtents.Z;

        // Already overlapping the plane at the start: the trace cannot move
        if (bottomStart < 0f)
        {
            return new Rubikon.TraceResult(true, from, Vector3.UnitZ, 0f, -1) { StartSolid = detectStartSolid };
        }

        var descent = from.Z - to.Z; // positive when the sweep moves down

        if (descent <= 0f)
        {
            return new Rubikon.TraceResult(); // moving up or level while above the plane - no hit
        }

        var fraction = bottomStart / descent;

        if (fraction > 1f)
        {
            return new Rubikon.TraceResult(); // the sweep ends before reaching the plane
        }

        var hitPosition = Vector3.Lerp(from, to, fraction);
        return new Rubikon.TraceResult(true, hitPosition, Vector3.UnitZ, Vector3.Distance(from, hitPosition), -1);
    }

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

        /// <summary>Downward eye offset from the landing punch, in units.</summary>
        public float LandingDipOffset { get; private set; }

        /// <summary>Camera roll from a hard landing, in degrees.</summary>
        public float LandingRollDegrees { get; private set; }

        // Stair-step view smoothing (Source m_outStepHeight): pending signed view offset
        // from hull step jumps, decays toward zero
        public float StepOffset { get; private set; }

        public void Reset()
        {
            Stamina = 1f;
            LandingDipOffset = 0f;
            LandingRollDegrees = 0f;
            StepOffset = 0f;
        }

        /// <summary>
        /// Touchdown effects, applied once on the frame the player lands: drain stamina and punch
        /// the view down, scaled by impact speed. Falls past the safe threshold also kick the
        /// CS 1.6-style camera roll.
        /// </summary>
        public void OnLanded(float impactSpeed)
        {
            Stamina = MathF.Max(0f, Stamina - 0.1f);

            // Dip starts past 250 u/s at 0.040 units per u/s; roll past 300 u/s (just above a
            // flat jump's landing speed) at 0.013 deg per u/s (GoldSrc's fall punch factor)
            LandingDipOffset = Math.Clamp((impactSpeed - 250f) * 0.040f, 0f, 10f);
            LandingRollDegrees = Math.Clamp((impactSpeed - 300f) * 0.013f, 0f, 10f);
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

            // The landing punch snaps on at impact; recovering it is a fast exponential ease-out
            var landingRecovery = MathF.Exp(-12f * deltaTime);
            LandingDipOffset = LandingDipOffset > 0.01f ? LandingDipOffset * landingRecovery : 0f;
            LandingRollDegrees = LandingRollDegrees > 0.01f ? LandingRollDegrees * landingRecovery : 0f;
        }
    }
}
