namespace ValveResourceFormat.Renderer.Input;

/// <summary>
/// Deadlock-style movement layered on the shared trace machinery: stamina-driven dashes,
/// slides, mantling, wall jumps and air jumps, tuned from the game's convar dump and wiki
/// measurements. Hero stats are Celeste's (the unicorn): 4 stamina, -25% gravity, strong
/// air control.
/// </summary>
public partial class PlayerMovement
{
    // Hero stats (Celeste). 1 hu = 1 inch; 6.2 m/s run, +1.6 m/s sprint.
    private const float DlRunSpeed = 244f;
    private const float DlSprintBonus = 63f;
    private const float DlSprintDelay = 5f;              // out-of-combat sprint delay; the viewer has no combat
    private const float DlGravityScale = 0.75f;          // Celeste: -25% Gravity Scale
    private const float DlAirWishSpeedCap = 110f;        // Celeste: +44% Air Control over an already-direct base
    private const float DlAirAccelerate = 14f;
    private const float DlStepHeight = 24f;              // Deadlock maps are unclipped; forgive taller lips than CS

    private const float DlStaminaMax = 4f;               // Celeste has 4 bars
    private const float DlStaminaRegenPerSecond = 1f / 5f;
    private const float DlWallJumpRegenPenalty = 0.75f;  // -25% stamina regen for 5s after a wall jump
    private const float DlWallJumpRegenPenaltyTime = 5f;

    private const float DlJumpImpulse = 265f;
    private const float DlAirJumpImpulse = 250f;
    private const float DlAirJumpStaminaCost = 1f;

    // Dashing: bucket-1 heroes peak at 635 u/s for 0.62s; ground dashes set velocity outright
    // and the momentum resets afterwards, air dashes are impulses.
    private const float DlDashSpeed = 635f;
    private const float DlDashDuration = 0.62f;
    private const float DlDashStaminaCost = 1f;          // citadel_player_dash_stamina_cost
    private const float DlDashJumpWindowStart = 0.3f;    // 0.2s window opens 0.3s into a ground dash
    private const float DlDashJumpWindowEnd = 0.5f;
    private const float DlDashJumpImpulse = 240f;        // fast long jump: flatter arc, dash speed carried
    private const float DlDownDashSpeed = 700f;
    private const float DlDownDashStaminaCost = 0.5f;    // citadel_player_dash_down_stamina_cost
    private const float DlDownDashDoubleTapWindow = 0.44f; // 28 ticks to double-tap duck

    // Sliding: needs 350 u/s while crouching (or a ramp), free, low friction until the
    // grace elapses (0.75s, 1.0s off a dash jump), then heavy friction bleeds it off.
    private const float DlSlideMinSpeed = 350f;
    private const float DlSlideRampMinSpeed = 60f;
    private const float DlSlideEndSpeed = 130f;
    private const float DlSlideFriction = 0.25f;
    private const float DlSlideLateFriction = 3.2f;
    private const float DlSlideGrace = 0.75f;
    private const float DlSlideGraceDashJump = 1.0f;
    private const float DlSlideSteerRate = 2.5f;
    private const float DlDashGlideFriction = 0.12f;     // during the dash itself speed barely decays

    // Mantling: citadel_mantle_probe_depth 32, citadel_mantle_max_height 134, forward
    // movement onto the ledge 16; duration buckets match the mantle_32..128 anim clips.
    private const float DlMantleProbeDepth = 32f;
    private const float DlMantleMaxHeight = 134f;
    private const float DlMantleMinHeight = 30f;         // below this the step sweep already climbs it
    private const float DlMantleForwardDistance = 16f;
    private const float DlMantleExitSpeed = 60f;

    // Wall jumps: first is free (citadel_initial_wall_jump_stamina_cost 0), then 0.5 each,
    // with a 1.25s fatigue that guts the vertical launch until landing clears it.
    private const float DlWallProbeDistance = 20f;
    private const float DlWallJumpStaminaCost = 0.5f;
    private const float DlWallJumpUpVelocity = 285f;
    private const float DlWallJumpOutVelocity = 300f;
    private const float DlWallJumpFatigueTime = 1.25f;
    private const float DlWallJumpFatigueScale = 0.35f;

    private bool deadlockMode;

    /// <summary>
    /// Gets or sets a value indicating whether Deadlock movement is active. Enabling it
    /// configures gravity, air control and jump behavior for Deadlock and resets the
    /// Deadlock-specific state.
    /// </summary>
    public bool DeadlockMode
    {
        get => deadlockMode;
        set
        {
            deadlockMode = value;

            if (value)
            {
                AutoBunnyHop = false;
                PrestrafeEnabled = false;
                GravityScale = DlGravityScale;
                AirWishSpeedCap = DlAirWishSpeedCap;
                AirAccelerate = DlAirAccelerate;
                RunSpeed = DlRunSpeed;
            }
            else
            {
                GravityScale = 1f;
                AirWishSpeedCap = AirMaxWishSpeed;
            }

            ResetDeadlockState();
        }
    }

    private float DlStamina = DlStaminaMax;
    private float DlModeTime;
    private float DlRegenPenaltyTimer;
    private float DlDashTimer = -1f;                     // >= 0 while a ground dash is active
    private float DlSlideTimer = -1f;                    // >= 0 while sliding
    private float DlSlideGraceTime = DlSlideGrace;
    private bool DlDashJumped;                           // airborne off a dash jump; extends the next slide's grace
    private int DlAirJumpsUsed;
    private int DlAirDashesUsed;
    private int DlDownDashesUsed;
    private int DlWallJumpsUsed;
    private float DlWallJumpFatigue;
    private bool DlMantleActive;
    private float DlMantleTime;
    private float DlMantleDuration;
    private Vector3 DlMantleStart;
    private Vector3 DlMantleTarget;
    private Vector3 DlMantleDirection;

    /// <summary>Gets the current Deadlock stamina, in bars.</summary>
    public float DeadlockStamina => DlStamina;

    /// <summary>Gets the Deadlock stamina capacity, in bars.</summary>
    public float DeadlockStaminaMax => DlStaminaMax;

    /// <summary>Gets a value indicating whether the player is sliding.</summary>
    public bool IsSliding => DlSlideTimer >= 0f;

    /// <summary>Gets a value indicating whether a ground dash is in progress.</summary>
    public bool IsDashing => DlDashTimer >= 0f;

    /// <summary>Gets a value indicating whether a mantle is in progress.</summary>
    public bool IsMantling => DlMantleActive;

    /// <summary>Gets a value indicating whether an air dash happened since last touching the ground.</summary>
    public bool AirDashed => DlAirDashesUsed > 0;

    /// <summary>Gets a value indicating whether the player is airborne off a dash jump.</summary>
    public bool DashJumped => DlDashJumped;

    /// <summary>Gets how far the current mantle has progressed, from 0 to 1.</summary>
    public float MantleFraction => DlMantleActive ? MathUtils.Saturate(DlMantleTime / DlMantleDuration) : 0f;

    /// <summary>Gets the height of the current or last mantle, in units.</summary>
    public float MantleHeight { get; private set; }

    private void ResetDeadlockState()
    {
        DlStamina = DlStaminaMax;
        DlModeTime = 0f;
        DlRegenPenaltyTimer = 0f;
        DlDashTimer = -1f;
        DlSlideTimer = -1f;
        DlDashJumped = false;
        DlAirJumpsUsed = 0;
        DlAirDashesUsed = 0;
        DlDownDashesUsed = 0;
        DlWallJumpsUsed = 0;
        DlWallJumpFatigue = 0f;
        DlMantleActive = false;
    }

    private (Vector3 Forward, Vector3 Right) ViewBasis()
    {
        var (sinYaw, cosYaw) = MathF.SinCos(Input.Camera.Yaw);
        return (new Vector3(cosYaw, sinYaw, 0), new Vector3(sinYaw, -cosYaw, 0));
    }

    /// <summary>Horizontal wish direction from the held movement keys, or zero.</summary>
    private Vector3 DeadlockWishDirection()
    {
        var (forward, right) = ViewBasis();
        var wish = Vector3.Zero;

        if (Input.Holding(TrackedKeys.W))
        {
            wish += forward;
        }

        if (Input.Holding(TrackedKeys.S))
        {
            wish -= forward;
        }

        if (Input.Holding(TrackedKeys.D))
        {
            wish += right;
        }

        if (Input.Holding(TrackedKeys.A))
        {
            wish -= right;
        }

        return wish.LengthSquared() > 0f ? Vector3.Normalize(wish) : Vector3.Zero;
    }

    private static Vector3 Horizontal(Vector3 v) => new(v.X, v.Y, 0f);

    /// <summary>
    /// Per-frame Deadlock logic, run after the position was categorized and landings were
    /// handled: stamina, sprint, dash and slide state, and all the airborne verticality
    /// (mantle, wall jump, air jump, air dash, down dash). Returns true when a mantle
    /// consumed the whole frame, in which case the caller must return immediately.
    /// </summary>
    private bool DeadlockFrame(Camera camera, float deltaTime, Vector3 position, Vector3 playerHull)
    {
        DlModeTime += deltaTime;
        DlRegenPenaltyTimer = MathF.Max(0f, DlRegenPenaltyTimer - deltaTime);
        DlWallJumpFatigue = MathF.Max(0f, DlWallJumpFatigue - deltaTime);

        var regenRate = DlStaminaRegenPerSecond * (DlRegenPenaltyTimer > 0f ? DlWallJumpRegenPenalty : 1f);
        DlStamina = MathF.Min(DlStaminaMax, DlStamina + regenRate * deltaTime);

        if (OnGround)
        {
            DlAirJumpsUsed = 0;
            DlAirDashesUsed = 0;
            DlDownDashesUsed = 0;
            DlWallJumpsUsed = 0;
            DlWallJumpFatigue = 0f;
        }

        // Out-of-combat sprint; the viewer has no combat, so it simply ramps in after the delay
        RunSpeed = DlRunSpeed + (DlModeTime > DlSprintDelay ? DlSprintBonus : 0f);

        var horizontalSpeed = Horizontal(Velocity).Length();
        var wishdir = DeadlockWishDirection();

        // Ground dash lifetime: ends on its timer; leaving the ground hands it to the air path
        if (DlDashTimer >= 0f)
        {
            DlDashTimer += deltaTime;

            if (DlDashTimer > DlDashDuration || !OnGround)
            {
                var endedOnGround = OnGround && DlDashTimer > DlDashDuration;
                DlDashTimer = -1f;

                // A ground dash sets velocity to a fixed amount, so the momentum resets when it
                // ends - unless the player slid or jumped out of it, which is the whole tech
                if (endedOnGround && !IsSliding && horizontalSpeed > RunSpeed)
                {
                    var h = Horizontal(Velocity);
                    Velocity = h * (RunSpeed / horizontalSpeed) + new Vector3(0, 0, Velocity.Z);
                    horizontalSpeed = RunSpeed;
                }
            }
        }

        UpdateSlideState(deltaTime, position, playerHull, horizontalSpeed);

        // Dash key (Shift)
        if (Input.Pressed(TrackedKeys.Shift))
        {
            if (OnGround && DlDashTimer < 0f && DlStamina >= DlDashStaminaCost)
            {
                StartGroundDash(wishdir);
            }
            else if (!OnGround && DlAirDashesUsed < 1 && DlStamina >= DlDashStaminaCost)
            {
                AirDash(wishdir);
            }
        }

        // Down dash: double-tap duck while airborne
        if (!OnGround && DlDownDashesUsed < 1 && DlStamina >= DlDownDashStaminaCost
            && Input.PressedSuccessive(TrackedKeys.Control, DlDownDashDoubleTapWindow))
        {
            DlStamina -= DlDownDashStaminaCost;
            DlDownDashesUsed++;
            Velocity = Horizontal(Velocity) * 0.5f + new Vector3(0, 0, -DlDownDashSpeed);
            SlopeClipNormalZ = 1f;
        }

        var jumpPressed = Input.Pressed(TrackedKeys.Space);
        var jumpHeld = Input.Holding(TrackedKeys.Space);

        if (OnGround)
        {
            if (jumpPressed)
            {
                // Running at a wall and jumping mantles it; otherwise a normal jump
                if (Input.Holding(TrackedKeys.W) && TryStartMantle(position, playerHull))
                {
                    DeadlockMantleFrame(camera, deltaTime);
                    return true;
                }

                DeadlockGroundJump(position, playerHull);
            }
        }
        else
        {
            // Holding jump buffers a mantle onto whatever ledge comes into range
            if (jumpHeld && TryStartMantle(position, playerHull))
            {
                DeadlockMantleFrame(camera, deltaTime);
                return true;
            }

            if (jumpPressed)
            {
                if (!TryWallJump(position, playerHull, wishdir)
                    && DlAirJumpsUsed < 1 && DlStamina >= DlAirJumpStaminaCost)
                {
                    DlStamina -= DlAirJumpStaminaCost;
                    DlAirJumpsUsed++;
                    Jumped = true;
                    JumpImpulse = DlAirJumpImpulse;
                    Velocity = new Vector3(Velocity.X, Velocity.Y, DlAirJumpImpulse);
                    SlopeClipNormalZ = 1f;
                }
            }
        }

        return false;
    }

    private void UpdateSlideState(float deltaTime, Vector3 position, Vector3 playerHull, float horizontalSpeed)
    {
        var onRamp = TryGetGroundSlope(position, playerHull, out _, out _);

        if (DlSlideTimer >= 0f)
        {
            DlSlideTimer += deltaTime;

            var keepSliding = HoldingCtrl && OnGround
                && (horizontalSpeed > DlSlideEndSpeed || onRamp);

            if (!keepSliding)
            {
                DlSlideTimer = -1f;
            }
        }
        else if (HoldingCtrl && OnGround
            && (horizontalSpeed > DlSlideMinSpeed || (onRamp && horizontalSpeed > DlSlideRampMinSpeed)))
        {
            // Crouching mid-dash converts it into a slide that inherits the dash speed
            DlSlideTimer = 0f;
            DlSlideGraceTime = DlDashJumped ? DlSlideGraceDashJump : DlSlideGrace;
            DlDashJumped = false;
            DlDashTimer = -1f;
        }

        if (OnGround && !HoldingCtrl)
        {
            DlDashJumped = false;
        }
    }

    private void StartGroundDash(Vector3 wishdir)
    {
        DlStamina -= DlDashStaminaCost;
        DlDashTimer = 0f;

        var direction = wishdir;
        if (direction == Vector3.Zero)
        {
            (direction, _) = ViewBasis();
        }

        Velocity = direction * DlDashSpeed;
        SlopeClipNormalZ = 1f;
    }

    private void AirDash(Vector3 wishdir)
    {
        DlStamina -= DlDashStaminaCost;
        DlAirDashesUsed++;

        var direction = wishdir;
        if (direction == Vector3.Zero)
        {
            (direction, _) = ViewBasis();
        }

        // The air dash is an impulse: it stacks with momentum already pointing its way,
        // and it arrests the fall
        var alongDash = MathF.Max(DlDashSpeed, Vector3.Dot(Horizontal(Velocity), direction));
        Velocity = direction * alongDash;
        SlopeClipNormalZ = 1f;
    }

    private void DeadlockGroundJump(Vector3 position, Vector3 playerHull)
    {
        var impulse = DlJumpImpulse;

        // Dash jump: an additive input inside the dash's timing window, one more bar, and
        // the dash speed is carried into a fast, flat leap
        if (DlDashTimer >= DlDashJumpWindowStart && DlDashTimer <= DlDashJumpWindowEnd
            && DlStamina >= DlAirJumpStaminaCost)
        {
            DlStamina -= DlAirJumpStaminaCost;
            impulse = DlDashJumpImpulse;
            DlDashJumped = true;
            DlDashTimer = -1f;
        }
        else if (DlDashTimer >= 0f)
        {
            // Jumping outside the window still leaves the ground, keeping whatever speed the
            // dash had at that instant, but it is a plain jump
            DlDashTimer = -1f;
        }

        OnGround = false;
        Jumped = true;
        JumpImpulse = impulse;
        Velocity = new Vector3(Velocity.X, Velocity.Y, impulse);
        SlopeClipNormalZ = 1f;

        PlaySound(JumpSoundEvent, position, playerHull);
    }

    /// <summary>
    /// Finds walkable ground under the hull that is sloped enough to slide on, and the
    /// downhill direction along it.
    /// </summary>
    private bool TryGetGroundSlope(Vector3 position, Vector3 playerHull, out Vector3 downhill, out float steepness)
    {
        downhill = Vector3.Zero;
        steepness = 0f;

        if (!OnGround)
        {
            return false;
        }

        var probe = TraceBBox(position, position + new Vector3(0, 0, -GroundProbeDistance * 2f), playerHull);

        if (!probe.Hit || probe.HitNormal.Z < WalkableSlope || probe.HitNormal.Z > 0.995f)
        {
            return false;
        }

        var normal = probe.HitNormal;
        var horizontal = new Vector2(normal.X, normal.Y);
        var horizontalLength = horizontal.Length();

        if (horizontalLength < 1e-4f)
        {
            return false;
        }

        downhill = new Vector3(horizontal.X / horizontalLength, horizontal.Y / horizontalLength, 0f);

        // Along-slope gravity projected back to the horizontal glide plane: g·sinθ·cosθ
        var sinTheta = horizontalLength;
        steepness = sinTheta * normal.Z;
        return true;
    }

    /// <summary>
    /// The velocity change for one frame of gliding (sliding, or riding out a ground dash):
    /// low friction, downhill acceleration, and gentle steering that redirects without
    /// changing speed.
    /// </summary>
    private Vector3 DeadlockGlideDelta(float deltaTime, Vector3 position, Vector3 playerHull)
    {
        var horizontal = Horizontal(Velocity);
        var speed = horizontal.Length();
        var delta = Vector3.Zero;

        // Steering: rotate the velocity toward the held direction, keeping the speed
        var wishdir = DeadlockWishDirection();

        if (wishdir != Vector3.Zero && speed > 1f)
        {
            var direction = horizontal / speed;
            var blend = 1f - MathF.Exp(-DlSlideSteerRate * deltaTime);
            var steered = Vector3.Lerp(direction, wishdir, blend);
            var steeredLength = steered.Length();

            if (steeredLength > 1e-4f)
            {
                delta += (steered / steeredLength * speed) - horizontal;
            }
        }

        var current = horizontal + delta;

        // Friction: nearly free during the dash and the slide grace, punishing afterwards
        float frictionRate;

        if (IsDashing)
        {
            frictionRate = DlDashGlideFriction;
        }
        else
        {
            frictionRate = DlSlideTimer < DlSlideGraceTime ? DlSlideFriction : DlSlideLateFriction;
        }

        delta -= current * (1f - MathF.Exp(-frictionRate * deltaTime));

        // Slopes feed the slide; this is what makes ramps slideable at any speed
        if (IsSliding && TryGetGroundSlope(position, playerHull, out var downhill, out var steepness))
        {
            delta += downhill * (Gravity * steepness * deltaTime);
        }

        return delta;
    }

    /// <summary>
    /// Looks for a mantleable ledge in front of the view: a wall within probe range whose
    /// top is walkable, high enough to be worth a mantle and low enough to reach, with room
    /// for the hull. Starts the mantle when found.
    /// </summary>
    private bool TryStartMantle(Vector3 position, Vector3 playerHull)
    {
        var (facing, _) = ViewBasis();

        var wall = TraceBBox(position, position + facing * DlMantleProbeDepth, playerHull);

        if (!wall.Hit || MathF.Abs(wall.HitNormal.Z) > 0.45f)
        {
            return false;
        }

        var inwardHorizontal = -Horizontal(wall.HitNormal);
        var inwardLength = inwardHorizontal.Length();

        if (inwardLength < 1e-4f)
        {
            return false;
        }

        var inward = inwardHorizontal / inwardLength;

        // Only mantle a wall the player is actually moving or looking into
        if (Vector3.Dot(facing, inward) < 0.5f)
        {
            return false;
        }

        var feetZ = position.Z - playerHull.Z;

        // Hull center resting spot past the ledge edge
        var extentAlongInward = MathF.Abs(inward.X) * playerHull.X + MathF.Abs(inward.Y) * playerHull.Y;
        var candidate = wall.HitPosition + inward * (extentAlongInward + DlMantleForwardDistance);

        // Drop the hull onto the ledge top from the highest reachable start. A start inside
        // geometry means a ceiling is in the way there; step down and retry so ledges under
        // overhangs can still be found.
        Rubikon.TraceResult down = default;
        var found = false;

        for (var startHeight = DlMantleMaxHeight; startHeight > DlMantleMinHeight; startHeight -= 32f)
        {
            var topStart = new Vector3(candidate.X, candidate.Y, feetZ + startHeight + playerHull.Z + 1f);
            var drop = startHeight + 1f - DlMantleMinHeight;
            down = TraceBBox(topStart, topStart + new Vector3(0, 0, -drop), playerHull, detectStartSolid: true);

            if (down.StartSolid)
            {
                continue;
            }

            found = down.Hit && down.HitNormal.Z >= WalkableSlope;
            break;
        }

        if (!found)
        {
            return false;
        }

        var target = down.HitPosition;
        var ledgeHeight = target.Z - playerHull.Z - feetZ;

        if (ledgeHeight < DlMantleMinHeight || ledgeHeight > DlMantleMaxHeight)
        {
            return false;
        }

        if (IsStuck(target, playerHull))
        {
            return false;
        }

        MantleHeight = ledgeHeight;
        DlMantleActive = true;
        DlMantleTime = 0f;

        // Duration buckets from the wiki (and the mantle_32..128 clip set)
        DlMantleDuration = ledgeHeight <= 64f ? 0.2f
            : ledgeHeight <= 96f ? 0.3f
            : ledgeHeight <= 128f ? 0.4f
            : 0.5f;

        DlMantleStart = position;
        DlMantleTarget = target;
        DlMantleDirection = inward;
        Velocity = Vector3.Zero;
        OnGround = false;
        DlDashTimer = -1f;
        DlSlideTimer = -1f;
        Effects.ClearStepOffset();

        return true;
    }

    /// <summary>
    /// One frame of an active mantle: the player is locked out and carried up and over the
    /// ledge, vertical first, then forward.
    /// </summary>
    private void DeadlockMantleFrame(Camera camera, float deltaTime)
    {
        DlMantleTime += deltaTime;

        var t = MathUtils.Saturate(DlMantleTime / DlMantleDuration);

        // Rise for most of the climb, translate over the edge on the back half
        var vertical = MathUtils.Smoothstep(0f, 0.7f, t);
        var horizontal = MathUtils.Smoothstep(0.3f, 1f, t);

        var position = new Vector3(
            float.Lerp(DlMantleStart.X, DlMantleTarget.X, horizontal),
            float.Lerp(DlMantleStart.Y, DlMantleTarget.Y, horizontal),
            float.Lerp(DlMantleStart.Z, DlMantleTarget.Z, vertical));

        TracePosition = position;
        TracePositionSmooth = position;
        Velocity = Vector3.Zero;

        if (t >= 1f)
        {
            DlMantleActive = false;
            OnGround = true;
            WasOnGroundLastFrame = true;
            Velocity = DlMantleDirection * DlMantleExitSpeed;
        }

        BlendedEyeHeight = ViewHeightStanding + (ViewHeightDucked - ViewHeightStanding) * CrouchBlend;
        EyePosition = Position + Vector3.UnitZ * BlendedEyeHeight;
        camera.Location = EyePosition;
        camera.Roll = 0f;
    }

    /// <summary>
    /// Wall jump: airborne, near a wall in any cardinal view direction. Jumping toward the
    /// wall launches up it; jumping away kicks off it. The first is free, the rest cost
    /// half a bar, and fatigue within 1.25s of the last one guts the launch.
    /// </summary>
    private bool TryWallJump(Vector3 position, Vector3 playerHull, Vector3 wishdir)
    {
        var (facing, right) = ViewBasis();

        Span<Vector3> probes = [facing, -facing, right, -right];
        Rubikon.TraceResult bestWall = default;
        var bestDistance = float.MaxValue;

        foreach (var probe in probes)
        {
            var trace = TraceBBox(position, position + probe * DlWallProbeDistance, playerHull);

            if (trace.Hit && MathF.Abs(trace.HitNormal.Z) < 0.45f && trace.Distance < bestDistance)
            {
                bestWall = trace;
                bestDistance = trace.Distance;
            }
        }

        if (bestDistance == float.MaxValue)
        {
            return false;
        }

        var cost = DlWallJumpsUsed == 0 ? 0f : DlWallJumpStaminaCost;

        if (DlStamina < cost)
        {
            return false;
        }

        DlStamina -= cost;
        DlWallJumpsUsed++;
        DlRegenPenaltyTimer = DlWallJumpRegenPenaltyTime;

        var fatigueScale = DlWallJumpFatigue > 0f ? DlWallJumpFatigueScale : 1f;
        DlWallJumpFatigue = DlWallJumpFatigueTime;

        var awayHorizontal = Horizontal(bestWall.HitNormal);
        var away = awayHorizontal.LengthSquared() > 1e-8f ? Vector3.Normalize(awayHorizontal) : -facing;

        var intent = wishdir != Vector3.Zero ? wishdir : facing;
        var towardWall = Vector3.Dot(intent, -away) > 0.3f;

        Vector3 velocity;

        if (towardWall)
        {
            // Up the wall: high vertical, a nudge off the surface
            velocity = Horizontal(Velocity) * 0.4f
                + away * 130f
                + new Vector3(0, 0, DlWallJumpUpVelocity * fatigueScale);
        }
        else
        {
            // Off the wall: high horizontal away from it, modest vertical
            velocity = Horizontal(Velocity) * 0.5f
                + away * DlWallJumpOutVelocity
                + new Vector3(0, 0, 200f * fatigueScale);
        }

        Velocity = velocity;
        SlopeClipNormalZ = 1f;
        Jumped = true;
        JumpImpulse = velocity.Z;

        return true;
    }
}
