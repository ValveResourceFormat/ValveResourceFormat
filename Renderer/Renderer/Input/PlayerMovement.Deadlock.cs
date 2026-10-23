namespace ValveResourceFormat.Renderer.Input;

/// <summary>The one-off moves of <see cref="PlayerMovement.DeadlockMode"/>.</summary>
public enum DeadlockMove
{
    /// <summary>No move was made yet.</summary>
    None,

    /// <summary>A jump off the ground.</summary>
    GroundJump,

    /// <summary>The second jump, made in the air.</summary>
    AirJump,

    /// <summary>A jump out of a ground dash that carries its speed.</summary>
    DashJump,

    /// <summary>A dash along the ground.</summary>
    GroundDash,

    /// <summary>A dash made in the air.</summary>
    AirDash,

    /// <summary>A dash straight down out of the air.</summary>
    DownDash,

    /// <summary>A jump off a wall.</summary>
    WallJump,

    /// <summary>A climb onto a ledge, <see cref="PlayerMovement.MantleHeight"/> high.</summary>
    Mantle,

    /// <summary>A throw by something in the world, such as a jump pad.</summary>
    Launched,

    /// <summary>A hop off a zipline.</summary>
    ZiplineDismount,
}

/// <summary>
/// Deadlock-style movement layered on the shared trace machinery: stamina-driven dashes,
/// slides, mantling, wall jumps and air jumps. What differs between heroes comes from
/// <see cref="Hero"/>.
/// </summary>
public partial class PlayerMovement
{
    private const float DlSprintDelay = 5f;              // out-of-combat sprint delay; the viewer has no combat

    // Air control: the wish speed cap shrinks as the hero gets fast, and drag always bleeds speed
    private const float DlAirAccelerate = 10f;           // sv_airaccelerate
    private const float DlAirWishSpeedSlow = 80f;        // citadel_aircontrol_speed_slow
    private const float DlAirWishSpeedFast = 50f;        // citadel_aircontrol_speed_fast
    private const float DlAirControlSlowSpeed = 600f;
    private const float DlAirControlFastSpeed = 800f;
    private const float DlAirDrag = 0.2f;                // citadel_air_drag_min
    private const float DlAirDragMinSpeed = 100f;

    private const float DlWallJumpRegenPenalty = 0.75f;  // -25% stamina regen for 5s after a wall jump
    private const float DlWallJumpRegenPenaltyTime = 5f;

    private const float DlJumpImpulse = 300f;
    private const float DlAirJumpStaminaCost = 1f;
    private const float DlAirJumpMaxSpeedGain = 200f;    // most an air jump adds along the held direction
    private const float DlLateJumpGrace = 0.15f;         // walking off a ledge still allows a ground jump this long

    // Dashing: how far and for how long is the hero's own. Ground dashes set velocity outright
    // and the momentum resets afterwards.
    private const float DlAirDashEndVelocityScale = 0.2f;
    private const float DlDashStaminaCost = 1f;          // citadel_player_dash_stamina_cost
    private const float DlDashJumpWindowStart = 0.3f;
    private const float DlDashJumpWindowEnd = 0.55f;
    private const float DlDashJumpImpulse = 425f;
    private const float DlDashJumpSpeed = 748f;          // 19 m/s along the held direction
    private const float DlDownDashSpeed = 500f;
    private const float DlDownDashStaminaCost = 0.5f;    // citadel_player_dash_down_stamina_cost
    private const float DlDownDashDoubleTapWindow = 0.22f;

    // Sliding: needs 350 u/s while crouching (250 out of a dash, or a ramp), free. Friction scales
    // the ground friction, and stays low on flat ground until the grace elapses.
    private const float DlSlideFrictionBase = 4f;        // sv_friction
    private const float DlSlideMinSpeed = 350f;
    private const float DlSlideDashMinSpeed = 250f;
    private const float DlSlideRampMinSpeed = 60f;
    private const float DlSlideEndSpeed = 170f;
    private const float DlSlideFrictionGrace = 0.05f;
    private const float DlSlideFriction = 0.8f;
    private const float DlSlideFrictionSlope = 0.15f;
    private const float DlSlideFrictionUphill = 1f;
    private const float DlSlideGrace = 0.6f;
    private const float DlSlideSteerSpeed = 90f;         // degrees per second
    private const float DlSlideSlopeMinAngle = 8f;
    private const float DlSlideSlopeAccelMin = 500f;     // at 5 degrees, rising to the max at 45
    private const float DlSlideSlopeAccelMax = 850f;
    private const float DlSlideSlopeMaxSpeedMin = 1200f; // the slope stops feeding the slide above this
    private const float DlSlideSlopeMaxSpeedMax = 1500f;
    private const float DlDashSlideLockout = 0.3f;       // no slide this early in a dash
    private const float DlDashSlideSpeed = 650f;
    private const float DlDashSlideFriction = 0.175f;
    private const float DlDashSlideGrace = 1f;
    private const float DlDashGlideFriction = 0.12f;     // during the dash itself speed barely decays

    // Mantling: citadel_mantle_probe_depth 32, citadel_mantle_max_height 134, forward
    // movement onto the ledge 16.
    private const float DlMantleProbeDepth = 32f;
    private const float DlMantleMaxHeight = 134f;
    private const float DlMantleMinHeight = 30f;         // below this the step sweep already climbs it
    private const float DlMantleForwardDistance = 16f;
    private const float DlMantleExitSpeed = 60f;

    // Wall jumps: the first of an airtime is free, then 0.5 each. One made soon after another
    // launches weaker, recovering over 1.25s.
    private const float DlWallProbeDistance = 20f;
    private const float DlWallJumpStaminaCost = 0.5f;
    private const float DlWallJumpUpSpeed = 525f;
    private const float DlWallJumpMinOutSpeed = 225f;    // pushing into the wall
    private const float DlWallJumpMaxOutSpeed = 310f;    // pushing away from it
    private const float DlWallJumpRechargeTime = 1.25f;
    private const float DlWallJumpPowerMin = 0.2f;
    private const float DlWallJumpPowerBias = 0.2f;

    private const string DlJumpSound = "Player.Jump";
    private const string DlAirJumpSound = "Player.Jump.Air";
    private const string DlDashJumpSound = "Player.Dash.Jump";
    private const string DlGroundDashSound = "Player.Dash.Ground";
    private const string DlAirDashSound = "Player.Dash.Air";
    private const string DlWallJumpSound = "Player.WallJump";
    private const string DlSlideEnterSound = "Player.Slide.Enter";
    private const string DlSlideExitSound = "Player.Slide.Exit";

    /// <summary>
    /// The scale every sound of a Deadlock hero plays at. Its sounds are mixed to cut through a fight,
    /// and are loud heard alone.
    /// </summary>
    public const float DeadlockSoundScale = 0.4f;

    private bool deadlockMode;
    private DeadlockHero hero = new();

    /// <summary>
    /// Gets or sets the hero whose speeds, stamina, dashes, gravity and air control
    /// Deadlock movement goes by.
    /// </summary>
    public DeadlockHero Hero
    {
        get => hero;
        set
        {
            hero = value;

            if (deadlockMode)
            {
                DeadlockMode = true;
            }
        }
    }

    private float DlDashSpeed => hero.GroundDashDistance / hero.GroundDashDuration;
    private float DlAirDashSpeed => hero.AirDashDistance / hero.AirDashDuration;

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
                GravityScale = hero.GravityScale;
                RunSpeed = hero.RunSpeed;
            }
            else
            {
                GravityScale = 1f;
                AirWishSpeedCap = AirMaxWishSpeed;
            }

            ResetDeadlockState();
        }
    }

    private float DlStamina;
    private float DlModeTime;
    private float DlRegenPenaltyTimer;
    private float DlDashTimer = -1f;                     // >= 0 while a ground dash is active
    private float DlSlideTimer = -1f;                    // >= 0 while sliding
    private float DlSlideGraceTime = DlSlideGrace;
    private float DlSlideGraceFriction = DlSlideFrictionGrace;
    private bool DlDashJumped;                           // airborne off a dash jump
    private float DlAirDashTimer = -1f;                  // >= 0 while an air dash is active
    private Vector3 DlAirDashDirection;
    private float DlAirTime;
    private bool DlLaunched;                             // thrown by a jump pad, which aims without the air drag
    private bool DlLeftGroundByJump;
    private int DlAirJumpsUsed;
    private int DlAirDashesUsed;
    private int DlDownDashesUsed;
    private int DlWallJumpsUsed;
    private float DlWallJumpRecharge;                    // counts down from the last wall jump
    private bool DlMantleActive;
    private float DlMantleTime;
    private float DlMantleDuration;
    private Vector3 DlMantleStart;
    private Vector3 DlMantleTarget;
    private Vector3 DlMantleDirection;

    /// <summary>Gets the current Deadlock stamina, in bars.</summary>
    public float DeadlockStamina => DlStamina;

    /// <summary>Gets the Deadlock stamina capacity, in bars.</summary>
    public float DeadlockStaminaMax => hero.Stamina;

    /// <summary>Gets a value indicating whether the player is sliding.</summary>
    public bool IsSliding => DlSlideTimer >= 0f;

    /// <summary>Gets a value indicating whether a ground dash is in progress.</summary>
    public bool IsDashing => DlDashTimer >= 0f;

    /// <summary>Gets a value indicating whether an air dash is in progress.</summary>
    public bool IsAirDashing => DlAirDashTimer >= 0f;

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

    /// <summary>Gets the last one-off move made.</summary>
    public DeadlockMove LastMove { get; private set; }

    /// <summary>Gets the horizontal direction <see cref="LastMove"/> went in.</summary>
    public Vector3 LastMoveDirection { get; private set; }

    /// <summary>Gets how many one-off moves were made so far, which tells a repeat of <see cref="LastMove"/> apart.</summary>
    public int MoveCount { get; private set; }

    private void NoteMove(DeadlockMove move, Vector3 direction)
    {
        LastMove = move;
        LastMoveDirection = direction;
        MoveCount++;

        var sound = move switch
        {
            DeadlockMove.GroundJump => DlJumpSound,
            DeadlockMove.AirJump => DlAirJumpSound,
            DeadlockMove.DashJump => DlDashJumpSound,
            DeadlockMove.GroundDash => DlGroundDashSound,
            DeadlockMove.AirDash or DeadlockMove.DownDash => DlAirDashSound,
            DeadlockMove.WallJump => DlWallJumpSound,
            _ => null,
        };

        if (sound != null)
        {
            PlaySound(sound, TracePosition, HullHalfExtents, volumeScale: DeadlockSoundScale);
        }
    }

    private void ResetDeadlockState()
    {
        DlStamina = DeadlockStaminaMax;
        DlModeTime = 0f;
        DlRegenPenaltyTimer = 0f;
        DlDashTimer = -1f;
        DlSlideTimer = -1f;
        DlDashJumped = false;
        DlAirJumpsUsed = 0;
        DlAirDashesUsed = 0;
        DlDownDashesUsed = 0;
        DlWallJumpsUsed = 0;
        DlWallJumpRecharge = 0f;
        DlAirDashTimer = -1f;
        DlAirTime = 0f;
        DlLeftGroundByJump = false;
        DlMantleActive = false;
        ResetZipline();
        IsOnRope = false;
        LastMove = DeadlockMove.None;
        MoveCount = 0;
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
        DlWallJumpRecharge = MathF.Max(0f, DlWallJumpRecharge - deltaTime);

        var regenRate = hero.StaminaRegenPerSecond * (DlRegenPenaltyTimer > 0f ? DlWallJumpRegenPenalty : 1f);
        DlStamina = MathF.Min(DeadlockStaminaMax, DlStamina + regenRate * deltaTime);

        if (OnGround)
        {
            DlAirJumpsUsed = 0;
            DlAirDashesUsed = 0;
            DlDownDashesUsed = 0;
            DlWallJumpsUsed = 0;
            DlWallJumpRecharge = 0f;
            DlAirDashTimer = -1f;
            DlAirTime = 0f;
            DlLeftGroundByJump = false;
            DlLaunched = false;
        }
        else
        {
            DlAirTime += deltaTime;
        }

        // Out-of-combat sprint; the viewer has no combat, so it simply ramps in after the delay
        RunSpeed = hero.RunSpeed + (DlModeTime > DlSprintDelay ? hero.SprintSpeed : 0f);

        UpdateAirDash(deltaTime);
        UpdateAirControl(deltaTime);

        var horizontalSpeed = Horizontal(Velocity).Length();
        var wishdir = DeadlockWishDirection();

        // Ground dash lifetime: ends on its timer; leaving the ground hands it to the air path
        if (DlDashTimer >= 0f)
        {
            DlDashTimer += deltaTime;

            if (DlDashTimer > hero.GroundDashDuration || !OnGround)
            {
                var endedOnGround = OnGround && DlDashTimer > hero.GroundDashDuration;
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
            NoteMove(DeadlockMove.DownDash, -Vector3.UnitZ);
            DlAirDashTimer = -1f;
            Velocity = new Vector3(0, 0, -DlDownDashSpeed);
            SlopeClipNormalZ = 1f;
        }

        // Holding jump on a rope or near a zipline takes it instead of jumping
        if (TryGrabRope(position, playerHull, deltaTime))
        {
            DeadlockRopeFrame(camera, deltaTime);
            return true;
        }

        if (TryMountZipline(position, playerHull, deltaTime))
        {
            DeadlockZiplineFrame(camera, deltaTime);
            return true;
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

            if (jumpPressed && !TryWallJump(position, playerHull, wishdir))
            {
                // Having just walked off a ledge, the jump still counts as one off the ground
                if (!DlLeftGroundByJump && DlAirTime <= DlLateJumpGrace && Velocity.Z < 0f)
                {
                    DeadlockGroundJump(position, playerHull);
                }
                else if (DlAirJumpsUsed < 1 && DlStamina >= DlAirJumpStaminaCost)
                {
                    AirJump(wishdir);
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
                PlaySound(DlSlideExitSound, position, playerHull, volumeScale: DeadlockSoundScale);
            }

            return;
        }

        if (!HoldingCtrl || !OnGround)
        {
            return;
        }

        if (IsDashing)
        {
            // Crouching mid-dash turns it into a slide, which leaves faster than the dash itself
            if (DlDashTimer < DlDashSlideLockout || horizontalSpeed < DlSlideDashMinSpeed)
            {
                return;
            }

            DlSlideTimer = 0f;
            DlSlideGraceTime = DlDashSlideGrace;
            DlSlideGraceFriction = DlDashSlideFriction;
            DlDashTimer = -1f;
            PlaySound(DlSlideEnterSound, position, playerHull, volumeScale: DeadlockSoundScale);
            Velocity = Horizontal(Velocity) * (DlDashSlideSpeed / horizontalSpeed) + new Vector3(0, 0, Velocity.Z);
        }
        else if (horizontalSpeed > DlSlideMinSpeed || (onRamp && horizontalSpeed > DlSlideRampMinSpeed))
        {
            DlSlideTimer = 0f;
            DlSlideGraceTime = DlSlideGrace;
            DlSlideGraceFriction = DlSlideFrictionGrace;
            PlaySound(DlSlideEnterSound, position, playerHull, volumeScale: DeadlockSoundScale);
        }
    }

    // The faster the hero flies, the less the held direction steers it, and the air always drags
    private void UpdateAirControl(float deltaTime)
    {
        var horizontal = Horizontal(Velocity);
        var speed = horizontal.Length();

        var fast = MathUtils.Saturate((speed - DlAirControlSlowSpeed) / (DlAirControlFastSpeed - DlAirControlSlowSpeed));
        AirWishSpeedCap = float.Lerp(DlAirWishSpeedSlow, DlAirWishSpeedFast, fast) * hero.AirControlScale;

        // The acceleration goes by the capped wish speed here, not by the full one
        AirAccelerate = DlAirAccelerate * hero.AirAccelerationScale * AirWishSpeedCap / RunSpeed;

        if (OnGround || IsAirDashing || DlLaunched || speed <= 0f)
        {
            return;
        }

        var drop = MathF.Max(DlAirDragMinSpeed, speed) * DlAirDrag * deltaTime;
        Velocity = horizontal * (MathF.Max(0f, speed - drop) / speed) + new Vector3(0, 0, Velocity.Z);
    }

    // An air dash carries the hero level along its line, and leaves with little of its speed
    private void UpdateAirDash(float deltaTime)
    {
        if (DlAirDashTimer < 0f)
        {
            return;
        }

        DlAirDashTimer += deltaTime;

        if (DlAirDashTimer < hero.AirDashDuration)
        {
            Velocity = DlAirDashDirection * DlAirDashSpeed;
            return;
        }

        DlAirDashTimer = -1f;
        Velocity = DlAirDashDirection * (DlAirDashSpeed * DlAirDashEndVelocityScale);
    }

    private void AirJump(Vector3 wishdir)
    {
        DlStamina -= DlAirJumpStaminaCost;
        DlAirJumpsUsed++;
        DlAirDashTimer = -1f;

        var velocity = new Vector3(Velocity.X, Velocity.Y, DlJumpImpulse);

        // The jump also turns: it pushes along the held direction, up to the run speed that way
        if (wishdir != Vector3.Zero)
        {
            var gain = MathF.Min(RunSpeed - Vector3.Dot(Horizontal(Velocity), wishdir), DlAirJumpMaxSpeedGain);

            if (gain > 0f)
            {
                velocity += wishdir * gain;
            }
        }

        Velocity = velocity;
        Jumped = true;
        JumpImpulse = DlJumpImpulse;
        SlopeClipNormalZ = 1f;
        NoteMove(DeadlockMove.AirJump, wishdir);
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
        NoteMove(DeadlockMove.GroundDash, direction);
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

        DlAirDashTimer = 0f;
        DlAirDashDirection = direction;
        Velocity = direction * DlAirDashSpeed;
        NoteMove(DeadlockMove.AirDash, direction);
        SlopeClipNormalZ = 1f;
    }

    private void DeadlockGroundJump(Vector3 position, Vector3 playerHull)
    {
        var velocity = new Vector3(Velocity.X, Velocity.Y, DlJumpImpulse);
        var move = DeadlockMove.GroundJump;

        // Dash jump: a jump inside the timing window of a dash costs one more bar and leaps
        // high and fast along the held direction
        if (DlDashTimer >= DlDashJumpWindowStart && DlDashTimer <= DlDashJumpWindowEnd
            && DlStamina >= DlAirJumpStaminaCost)
        {
            DlStamina -= DlAirJumpStaminaCost;
            DlDashJumped = true;
            move = DeadlockMove.DashJump;

            var direction = DeadlockWishDirection();

            if (direction == Vector3.Zero)
            {
                var horizontal = Horizontal(Velocity);
                direction = horizontal.LengthSquared() > 0f ? Vector3.Normalize(horizontal) : ViewBasis().Forward;
            }

            velocity = direction * DlDashJumpSpeed + new Vector3(0, 0, DlDashJumpImpulse);
        }

        // Jumping outside the window still leaves the ground, keeping whatever speed the dash
        // had at that instant, but it is a plain jump
        DlDashTimer = -1f;
        DlLeftGroundByJump = true;

        OnGround = false;
        Jumped = true;
        JumpImpulse = velocity.Z;
        Velocity = velocity;
        SlopeClipNormalZ = 1f;
        NoteMove(move, Horizontal(Velocity));
    }

    /// <summary>
    /// Finds walkable ground under the hull that is sloped enough to slide on, the downhill
    /// direction along it and how steep it is, in degrees.
    /// </summary>
    private bool TryGetGroundSlope(Vector3 position, Vector3 playerHull, out Vector3 downhill, out float angle)
    {
        downhill = Vector3.Zero;
        angle = 0f;

        if (!OnGround)
        {
            return false;
        }

        var probe = TraceBBox(position, position + new Vector3(0, 0, -GroundProbeDistance * 2f), playerHull);

        if (!probe.Hit || probe.HitNormal.Z < WalkableSlope)
        {
            return false;
        }

        var normal = probe.HitNormal;
        var horizontal = new Vector2(normal.X, normal.Y);
        var horizontalLength = horizontal.Length();

        angle = float.RadiansToDegrees(MathF.Atan2(horizontalLength, normal.Z));

        if (angle < DlSlideSlopeMinAngle)
        {
            return false;
        }

        downhill = new Vector3(horizontal.X / horizontalLength, horizontal.Y / horizontalLength, 0f);
        return true;
    }

    /// <summary>
    /// The velocity change for one frame of gliding (sliding, or riding out a ground dash):
    /// low friction, downhill acceleration, and steering that redirects without changing speed.
    /// </summary>
    private Vector3 DeadlockGlideDelta(float deltaTime, Vector3 position, Vector3 playerHull)
    {
        var horizontal = Horizontal(Velocity);
        var speed = horizontal.Length();
        var delta = Vector3.Zero;

        // Steering: turn the velocity toward the held direction at a fixed rate, keeping the speed
        var wishdir = DeadlockWishDirection();

        if (wishdir != Vector3.Zero && speed > 1f)
        {
            var direction = horizontal / speed;
            var between = MathF.Acos(Math.Clamp(Vector3.Dot(direction, wishdir), -1f, 1f));
            var turn = MathF.Min(between, float.DegreesToRadians(DlSlideSteerSpeed) * deltaTime);
            var side = MathF.Sign((direction.X * wishdir.Y) - (direction.Y * wishdir.X));
            var (sin, cos) = MathF.SinCos(turn * side);
            var steered = new Vector3((direction.X * cos) - (direction.Y * sin), (direction.X * sin) + (direction.Y * cos), 0f);

            delta += (steered * speed) - horizontal;
        }

        var current = horizontal + delta;
        var downhill = Vector3.Zero;
        var angle = 0f;
        var onSlope = IsSliding && TryGetGroundSlope(position, playerHull, out downhill, out angle);

        // Friction: nearly free during the dash and the slide grace, and on the way down a slope
        float friction;

        if (IsDashing)
        {
            friction = DlDashGlideFriction;
        }
        else if (onSlope)
        {
            friction = Vector3.Dot(current, downhill) >= 0f ? DlSlideFrictionSlope : DlSlideFrictionUphill;
        }
        else
        {
            friction = DlSlideTimer < DlSlideGraceTime ? DlSlideGraceFriction : DlSlideFriction;
        }

        var frictionRate = IsDashing ? friction : friction * DlSlideFrictionBase;
        delta -= current * (1f - MathF.Exp(-frictionRate * deltaTime));

        // Slopes feed the slide, more the steeper they are, until it is fast enough
        if (onSlope)
        {
            var steepness = MathUtils.Saturate((angle - 5f) / 40f);
            var maxSpeed = float.Lerp(DlSlideSlopeMaxSpeedMin, DlSlideSlopeMaxSpeedMax, steepness);

            if (Vector3.Dot(current, downhill) < maxSpeed)
            {
                delta += downhill * (float.Lerp(DlSlideSlopeAccelMin, DlSlideSlopeAccelMax, steepness) * deltaTime);
            }
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

        // Ledges are climbed in four kinds, 32 units apart, each taking longer than the last
        DlMantleDuration = ledgeHeight <= 48f ? 0.2f
            : ledgeHeight <= 80f ? 0.3f
            : ledgeHeight <= 112f ? 0.4f
            : 0.5f;

        DlMantleStart = position;
        DlMantleTarget = target;
        DlMantleDirection = inward;
        Velocity = Vector3.Zero;
        OnGround = false;
        DlDashTimer = -1f;
        DlSlideTimer = -1f;
        Effects.ClearStepOffset();
        NoteMove(DeadlockMove.Mantle, inward);

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
    /// Wall jump: airborne, near a wall in any cardinal view direction. It launches up and
    /// off the wall, further out the more the held direction points away from it. The first
    /// is free, the rest cost half a bar, and one made within 1.25s of the last is weaker.
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

        // A wall jump soon after another launches weaker, easing back to full power
        var recharged = 1f - (DlWallJumpRecharge / DlWallJumpRechargeTime);
        var power = MathF.Pow(float.Lerp(DlWallJumpPowerMin, 1f, recharged), MathF.Log(DlWallJumpPowerBias) / MathF.Log(0.5f));
        DlWallJumpRecharge = DlWallJumpRechargeTime;
        DlAirDashTimer = -1f;

        var awayHorizontal = Horizontal(bestWall.HitNormal);
        var away = awayHorizontal.LengthSquared() > 1e-8f ? Vector3.Normalize(awayHorizontal) : -facing;

        // Speed into the wall is lost, and so is most of the rise or fall
        var velocity = Horizontal(Velocity);
        velocity -= away * MathF.Min(0f, Vector3.Dot(velocity, away));

        var vertical = MathF.Abs(Velocity.Z) < 100f ? Velocity.Z * 0.3f : 0f;
        vertical += DlWallJumpUpSpeed * power;

        // It kicks off harder the more the held direction points away from the wall
        var awayIntent = wishdir != Vector3.Zero ? (Vector3.Dot(wishdir, away) + 1f) * 0.5f : 0.5f;
        velocity += away * float.Lerp(DlWallJumpMinOutSpeed, DlWallJumpMaxOutSpeed, awayIntent);
        velocity.Z = vertical;

        Velocity = velocity;
        SlopeClipNormalZ = 1f;
        Jumped = true;
        JumpImpulse = velocity.Z;
        NoteMove(DeadlockMove.WallJump, Horizontal(velocity));

        return true;
    }
}
