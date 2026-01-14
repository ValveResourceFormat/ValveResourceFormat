
using System.Diagnostics;

namespace ValveResourceFormat.Renderer;

public class PlayerMovement
{
    // Player collision hull
    // Standing hull: 32x32x64 (16 units radius, 64 units tall - about 5'4" at 1 unit = 1 inch)
    // Ducked hull: 32x32x48 (16 units radius, 48 units tall - 4 feet crouched)
    // Note: Hull is centered horizontally but extends from feet (Z=0) upward
    private static readonly AABB PlayerHullStanding = new(new Vector3(-16, -16, 0), new Vector3(16, 16, 72));
    private static readonly AABB PlayerHullDucked = new(new Vector3(-16, -16, 0), new Vector3(16, 16, 48));

    // Movement constants from Source engine (movevars_shared.cpp)
    private const float GravityValue = 800f;              // sv_gravity
    private const float FrictionValue = 5.2f;             // sv_friction
    private const float StopSpeedValue = 80f;             // sv_stopspeed
    private const float AccelerateValue = 5.5f;           // sv_accelerate
    private const float AirAccelerateValue = 12f;         // sv_airaccelerate
    private const float MaxSpeedValue = 320f;             // sv_maxspeed
    private const float JumpImpulseValue = 301.993377f;   // sv_jump_impulse = sqrt(2*800*57)
    private const float MaxVelocityValue = 3500f;         // sv_maxvelocity

    private const float WalkSpeedModifier = 0.52f;        // CS_PLAYER_SPEED_WALK_MODIFIER
    private const float DuckSpeedModifier = 0.34f;        // CS_PLAYER_SPEED_DUCK_MODIFIER

    private const float ViewHeightOffset = 8f;
    private static readonly float ViewHeightStanding = PlayerHullStanding.Size.Z - ViewHeightOffset;
    private static readonly float ViewHeightDucked = PlayerHullDucked.Size.Z - ViewHeightOffset;

    // Bunnyhopping prevention (CS:GO)
    private const float BunnyjumpMaxSpeedFactor = 1.1f;   // Only allow bunny jumping up to 1.1x max speed

    // Collision constants
    private const float SurfaceEpsilon = 0.03125f;        // Minimum distance from surfaces (1/32 unit) to prevent getting stuck
    private const float StepSize = 18f;                   // Maximum height of steps/obstacles player can climb

    // Crouch blend constants
    private const float CrouchBlendTime = 0.2f;           // Time to complete crouch/uncrouch animation (seconds)

    // Movement state
    public Vector3 Velocity { get; private set; }
    private Vector3 AABBCenteredPosition;
    private bool OnGround;
    private bool WasOnGroundLastFrame;
    private bool WasDuckingLastFrame;

    private bool HoldingCtrl => Input.Holding(TrackedKeys.Control);
    private bool HoldingShift => Input.Holding(TrackedKeys.Shift);

    private UserInput Input { get; }
    private Rubikon? Physics => Input.PhysicsWorld;

    private float CrouchBlend; // 0 = standing, 1 = fully ducked
    private AABB SnappedHull => HoldingCtrl ? PlayerHullDucked : PlayerHullStanding;

    private AABB Hull
    {
        get
        {
            var standingHeight = PlayerHullStanding.Size.Z;
            var duckedHeight = PlayerHullDucked.Size.Z;
            var lerpedHeight = standingHeight + (duckedHeight - standingHeight) * CrouchBlend;
            var lerpedHull = new AABB(new Vector3(-16, -16, 0), new Vector3(16, 16, lerpedHeight));
            return lerpedHull;
        }
    }

    private const float SurfaceFriction = 1.0f;

    // options
    public bool Initialize { get; set; }
    public bool AutoBunnyHop { get; set; } = true;
    public float RunSpeed { get; set; } = 250f;

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
        var hull = HoldingCtrl ? PlayerHullDucked : PlayerHullStanding;
        AABBCenteredPosition = camera.Location - new Vector3(0, 0, hull.Size.Z / 2);
        Velocity = Vector3.Zero;
    }

    /// <summary>
    /// Main movement tick - processes input and updates position/velocity
    /// </summary>
    public void ProcessMovement(UserInput input, Camera camera, float deltaTime)
    {
        // Initialize character position from camera on first tick
        // We need to convert from eye height to feet position
        if (Initialize)
        {
            ResetPosition(camera);
            TryUnstuck(ref AABBCenteredPosition, SnappedHull);
            Initialize = false;
        }

        var position = AABBCenteredPosition; // Use character's feet position for physics
        var pitch = camera.Pitch;
        var yaw = camera.Yaw;

        // Track input state for acceleration modifiers and collision hull
        var isDucking = HoldingCtrl;
        var isWalking = !HoldingCtrl && HoldingShift;

        if (HoldingCtrl)
        {
            CrouchBlend += 1f / CrouchBlendTime * deltaTime;
            CrouchBlend = MathUtils.Saturate(CrouchBlend);
        }
        else
        {
            CrouchBlend = 0f;
        }

        var playerHull = Hull;

        // Handle crouch/uncrouch transitions
        if (WasDuckingLastFrame && !isDucking)
        {
            // Trying to uncrouch - check if there's space above us
            var standingHull = PlayerHullStanding;
            var duckedHull = PlayerHullDucked;
            var heightDifference = standingHull.Size.Z - duckedHull.Size.Z; // 72 - 48 = 24 units

            // Trace upward using a small vertical hull to check clearance above the ducked hull
            // We trace from the current ducked position upward by the height difference
            var traceStart = AABBCenteredPosition;
            var traceEnd = AABBCenteredPosition + new Vector3(0, 0, heightDifference);

            // Use the ducked hull's horizontal dimensions but check vertical clearance
            var checkHull = new AABB(duckedHull.Min, duckedHull.Max);

            var canUncrouch = true;
            if (Physics != null)
            {
                var trace = TraceBBox(traceStart, traceEnd, checkHull);
                canUncrouch = !trace.Hit;
            }

            if (canUncrouch)
            {
                // We have space to stand up - adjust position upward to keep feet at same level
                // The AABB center needs to move up by half the height difference
                AABBCenteredPosition += new Vector3(0, 0, heightDifference / 2);
                position = AABBCenteredPosition;
            }
            else
            {
                // Can't uncrouch - blocked by geometry above, stay ducked and stop blend
                isDucking = true;
                CrouchBlend = 1f;
            }
        }

        WasDuckingLastFrame = isDucking;

        // Store previous ground state
        WasOnGroundLastFrame = OnGround;

        // Categorize position (check if on ground) - use lerped hull for collision
        CategorizePosition(ref position, playerHull);

        // Check if we just landed this frame
        var justLanded = !WasOnGroundLastFrame && OnGround;

        // StartGravity - add gravity at start of frame (like Source does)
        if (!OnGround)
        {
            Velocity = new Vector3(Velocity.X, Velocity.Y, Velocity.Z - GravityValue * deltaTime * 0.5f);
            CheckVelocity(ref position); // StartGravity calls CheckVelocity in Source
        }

        // Check for jump (auto bunny hop if enabled and holding jump)
        var wantsToJump = AutoBunnyHop ? input.Holding(TrackedKeys.Space) : input.Pressed(TrackedKeys.Space);
        wantsToJump = wantsToJump || input.Holding(TrackedKeys.MouseWheelDown) || input.Holding(TrackedKeys.MouseWheelUp);

        // For auto bhop, also jump immediately when landing while holding jump
        if (wantsToJump && (OnGround || (AutoBunnyHop && justLanded)))
        {
            // Prevent bunnyhopping - cap speed before jumping (only if auto bhop is disabled)
            if (!AutoBunnyHop)
            {
                PreventBunnyJumping();
            }
            CheckJump(deltaTime);
        }

        // Calculate wish velocity from input (with speed modifiers for duck/crouch)
        var (wishdir, wishspeed) = CalculateWishVelocity(input, pitch, yaw);

        // Apply walk speed modifier only when near walk speed (CS:GO behavior)
        // This allows natural deceleration instead of instant capping
        if (isWalking)
        {
            var currentSpeed = Velocity.Length();
            var walkSpeed = MaxSpeedValue * WalkSpeedModifier;
            if (currentSpeed < walkSpeed + 25.0f)
            {
                wishspeed = MathF.Min(wishspeed, walkSpeed);
            }
        }

        // Ground or air movement - use isDucking (input) for speed modifiers
        if (OnGround)
        {
            // Apply friction before movement
            Velocity = new Vector3(Velocity.X, Velocity.Y, 0);
            Friction(deltaTime);
            WalkMove(wishdir, wishspeed, deltaTime, isDucking, isWalking);
        }
        else
        {
            AirMove(wishdir, wishspeed, deltaTime);
        }

        // Check velocity for NaN/bounds
        CheckVelocity(ref position);

        // Update position based on velocity - use lerped hull for collision
        position = TryPlayerMove(position, Velocity * deltaTime, playerHull);

        // StayOnGround - keep player stuck to ground when going down slopes/stairs
        if (OnGround)
        {
            StayOnGround(ref position, playerHull);
        }

        // Recategorize position after movement (now that position is updated)
        CategorizePosition(ref position, playerHull);

        // Check velocity again for NaN/bounds
        CheckVelocity(ref position);

        // FinishGravity - add remaining gravity at end of frame
        if (!OnGround)
        {
            Velocity = new Vector3(Velocity.X, Velocity.Y, Velocity.Z - GravityValue * deltaTime * 0.5f);
            CheckVelocity(ref position); // FinishGravity calls CheckVelocity in Source
        }

        // Store the updated position
        AABBCenteredPosition = position;

        // Set camera at eye height with smooth crouch blend
        var blendedEyeHeight = ViewHeightStanding + (ViewHeightDucked - ViewHeightStanding) * CrouchBlend;
        var groundPos = AABBCenteredPosition - new Vector3(0, 0, playerHull.Size.Z / 2);
        camera.Location = groundPos + Vector3.UnitZ * blendedEyeHeight;

        // Draw player AABB for debugging
        /*
        if (Physics?.SelectedNodeRenderer != null)
        {
            var worldAABB = playerHull.Translate(groundPos);
            var color = OnGround ? new Color32(0f, 1f, 0f, 1f) : new Color32(1f, 1f, 0f, 1f); // Green when grounded, yellow in air
            //ShapeSceneNode.AddBox(Physics.SelectedNodeRenderer.Vertices, worldAABB, color);
        }*/

        return;
    }

    /// <summary>
    /// Keep player stuck to ground when moving down slopes/stairs
    /// Prevents player from becoming airborne and losing friction
    /// Ported from gamemovement.cpp StayOnGround()
    /// </summary>
    private void StayOnGround(ref Vector3 position, AABB aabb)
    {
        if (Physics == null)
        {
            return;
        }

        // Trace down to find ground (trace extra distance to catch steep slopes)
        var traceStart = position;
        var traceEnd = position + new Vector3(0, 0, -StepSize);

        var trace = TraceBBox(traceStart, traceEnd, aabb);

        // If we hit ground, snap down to it
        if (trace.Hit && trace.HitNormal.Z > 0.7f)
        {
            var groundZ = trace.HitPosition.Z + trace.HitNormal.Z * SurfaceEpsilon;
            position = new Vector3(position.X, position.Y, groundZ);
        }
    }

    /// <summary>
    /// Check if player is on ground using swept AABB trace
    /// Traces down based on current downward velocity to detect ground contact
    /// </summary>
    private void CategorizePosition(ref Vector3 position, AABB aabb)
    {
        if (Physics == null)
        {
            // Fallback: simple ground plane at Z=0
            OnGround = position.Z <= 0.1f;
            return;
        }

        // Trace down from current position to check for ground
        // Use a small distance (2 units) to check if we're on or very close to ground
        // This distance should be enough to detect ground contact in Source engine scale
        var traceStart = position;
        var traceEnd = position + new Vector3(0, 0, -2f);

        var result = TraceBBox(traceStart, traceEnd, aabb);

        if (result.Hit && result.HitNormal.Z > 0.8f && Velocity.Z < 140.0f)
        {
            OnGround = true;
            // Snap to ground vertically only, preserve XY position to prevent sliding on slopes
            var groundZ = result.HitPosition.Z + result.HitNormal.Z * SurfaceEpsilon;
            position = new Vector3(position.X, position.Y, groundZ);
        }
        else
        {
            OnGround = false;
        }
    }

    /// <summary>
    /// Attempt to find a valid position if stuck inside geometry
    /// We're stuck if traces result in no movement (start pos = end pos, normally we're epsilon units away)
    /// </summary>
    private bool TryUnstuck(ref Vector3 position, AABB aabb)
    {
        if (Physics == null)
        {
            return false;
        }

        // Check if we're actually stuck by trying a small downward trace
        var testTrace = TraceBBox(position, position + new Vector3(0, 0, -0.1f), aabb);
        if (!testTrace.Hit || (testTrace.HitPosition - position).Length() > 0.01f)
        {
            return true; // Not stuck
        }

        // Try moving in various directions to find a valid position
        var directions = new[]
        {
            Vector3.UnitZ,        // Up
            -Vector3.UnitZ,       // Down
            Vector3.UnitX,        // Right
            -Vector3.UnitX,       // Left
            Vector3.UnitY,        // Forward
            -Vector3.UnitY,       // Back
            Vector3.Normalize(new Vector3(1, 1, 0)),   // Diagonal
            Vector3.Normalize(new Vector3(-1, 1, 0)),
            Vector3.Normalize(new Vector3(1, -1, 0)),
            Vector3.Normalize(new Vector3(-1, -1, 0)),
        };

        // Try increasingly larger offsets
        for (var distance = 1f; distance <= 64f; distance *= 4f)
        {
            foreach (var dir in directions)
            {
                var testPos = position + dir * distance;

                // Try a trace to this position to see if it's valid
                var trace = TraceBBox(position, testPos, aabb);

                // If we can move at least halfway there without hitting, it's a good position
                if (!trace.Hit || trace.Distance > distance * 0.5f)
                {
                    // Found a valid position
                    position = testPos;
                    Velocity = Vector3.Zero; // Reset velocity when unstucking
                    return true;
                }
            }
        }

        return false; // Couldn't find a valid position
    }

    /// <summary>
    /// Perform swept AABB collision detection for player movement with multi-bounce sliding
    /// </summary>
    private Vector3 TryPlayerMove(Vector3 start, Vector3 delta, AABB aabb)
    {
        if (Physics == null || delta.LengthSquared() < 1e-6f)
        {
            return start + delta;
        }

        const int MaxBumps = 6;

        // Try step climbing immediately if on ground and moving horizontally

        var position = start;
        var remainingDelta = delta;
        var remainingDistance = delta.Length();
        var remainingFraction = 1.0f;

        for (var bump = 0; bump < MaxBumps && remainingFraction > 0; bump++)
        {
            var result = TraceBBox(position, position + remainingDelta, aabb);

            if (!result.Hit)
            {
                return position + remainingDelta;
            }
            else if (OnGround && remainingFraction > 0.5f)
            {
                var obstacle = result.Distance < remainingDistance;
                if (obstacle)
                {
                    var (newPos, stepped) = TryStepMove(position, delta, aabb);
                    if (stepped && (newPos - position).Length() > remainingDistance)
                    {
                        return newPos;
                    }
                }
            }

            // Move to hit point with surface epsilon
            var adjustedDistance = Math.Max(result.Distance + SurfaceEpsilon / Vector3.Dot(Vector3.Normalize(remainingDelta), result.HitNormal), 0.0f);
            var fraction = adjustedDistance / remainingDistance;

            position += remainingDelta * fraction;
            remainingFraction -= fraction * remainingFraction;

            // Clip velocity based on surface type
            var vel = Velocity;
            ClipVelocity(ref remainingDelta, ref vel, result.HitNormal, OnGround);
            Velocity = vel;

            remainingDistance = remainingDelta.Length();
            if (remainingDistance <= SurfaceEpsilon)
            {
                // We're stuck
                break;
            }
        }

        return position;
    }

    /// <summary>
    /// Attempt to step up and over an obstacle (even tiny ones)
    /// </summary>
    private (Vector3 StepPos, bool Stepped) TryStepMove(Vector3 start, Vector3 delta, AABB aabb)
    {
        Debug.Assert(Physics != null);

        // Step 1: Move up by step height
        var stepUpEnd = start + new Vector3(0, 0, StepSize);
        var upTrace = TraceBBox(start, stepUpEnd, aabb);

        // Use whatever height we can achieve (even if blocked)
        var steppedUpPosition = upTrace.Hit
            ? upTrace.HitPosition + upTrace.HitNormal * SurfaceEpsilon
            : stepUpEnd;

        // Step 2: Move forward from the stepped-up position
        var forwardTrace = TraceBBox(steppedUpPosition, steppedUpPosition + delta, aabb);
        var forwardPosition = forwardTrace.Hit
            ? forwardTrace.HitPosition + forwardTrace.HitNormal * SurfaceEpsilon
            : steppedUpPosition + delta;

        // Step 3: Move down to find the ground (trace extra distance to ensure we find it)
        var downEnd = forwardPosition + new Vector3(0, 0, -(StepSize + 2.0f));
        var downTrace = TraceBBox(forwardPosition, downEnd, aabb);

        if (!downTrace.Hit)
        {
            return (start, false);
        }

        var finalPosition = downTrace.HitPosition + downTrace.HitNormal * SurfaceEpsilon;

        // Validate the step
        var stepHeight = finalPosition.Z - start.Z;

        // Accept steps that are reasonable (not too high, not falling off edge)
        if (stepHeight < -2.0f || stepHeight > StepSize)
        {
            return (start, false);
        }

        // Clamp the stepped position to not exceed the intended delta distance
        var steppedDelta = finalPosition - start;
        var steppedDistance = steppedDelta.Length();
        var intendedDistance = delta.Length();

        if (steppedDistance > intendedDistance)
        {
            // Scale down the stepped delta to match intended distance
            //finalPosition = start + steppedDelta * (intendedDistance / steppedDistance);
        }

        return (finalPosition, true);
    }

    /// <summary>
    /// Clips velocity against a surface normal. Special handling for walkable slopes.
    /// </summary>
    private static void ClipVelocity(ref Vector3 delta, ref Vector3 velocity, Vector3 normal, bool onGround)
    {
        const float WalkableSlope = 0.7f; // ~45 degrees

        // Special handling for walkable slopes when on ground - maintain horizontal speed
        if (onGround && normal.Z > WalkableSlope)
        {
            var horizontalVel = new Vector3(velocity.X, velocity.Y, 0);
            var horizontalSpeed = horizontalVel.Length();

            if (horizontalSpeed > 0.001f)
            {
                // Project horizontal direction onto slope while maintaining speed
                var horizontalDir = horizontalVel / horizontalSpeed;
                var projectedDir = horizontalDir - normal * Vector3.Dot(horizontalDir, normal);

                if (projectedDir.LengthSquared() > 0.001f)
                {
                    velocity = Vector3.Normalize(projectedDir) * horizontalSpeed;
                }
            }

            // Same for delta
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
        }
        else
        {
            // Standard clipping for walls and ceilings
            delta -= normal * Vector3.Dot(delta, normal);
            velocity -= normal * Vector3.Dot(velocity, normal);
        }
    }

    /// <summary>
    /// Prevent excessive speed gain from bunnyhopping
    /// Ported from cs_gamemovement.cpp PreventBunnyJumping()
    /// </summary>
    private void PreventBunnyJumping()
    {
        // Speed at which bunny jumping is limited
        var maxscaledspeed = BunnyjumpMaxSpeedFactor * MaxSpeedValue;
        if (maxscaledspeed <= 0.0f)
        {
            return;
        }

        // Current player speed
        var spd = Velocity.Length();

        if (spd <= maxscaledspeed)
        {
            return;
        }

        // Apply this cropping fraction to velocity
        var fraction = maxscaledspeed / spd;

        Velocity *= fraction;
    }

    /// <summary>
    /// Handle jump input - applies upward impulse
    /// Ported from cs_gamemovement.cpp CheckJumpButton()
    /// </summary>
    private void CheckJump(float deltaTime)
    {
        // In the air now (SetGroundEntity NULL in Source)
        OnGround = false;

        // Accelerate upward
        // v = sqrt( g * 2.0 * jumpheight )
        Velocity = new Vector3(Velocity.X, Velocity.Y, JumpImpulseValue);

        // FinishGravity is called after jump in Source
        // This subtracts 0.5 * gravity * dt
        Velocity = new Vector3(Velocity.X, Velocity.Y, Velocity.Z - GravityValue * deltaTime * 0.5f);
    }

    /// <summary>
    /// Calculate desired movement direction and speed from input
    /// </summary>
    private (Vector3 wishdir, float wishspeed) CalculateWishVelocity(UserInput input, float pitch, float yaw)
    {
        // Calculate forward and right vectors from yaw (ignore pitch for horizontal movement)
        var forward = new Vector3(MathF.Cos(yaw), MathF.Sin(yaw), 0);
        var right = new Vector3(MathF.Cos(yaw - MathF.PI / 2f), MathF.Sin(yaw - MathF.PI / 2f), 0);

        // Determine movement amounts
        float forwardMove = 0, sideMove = 0;

        if (input.Holding(TrackedKeys.Forward))
        {
            forwardMove += MaxSpeedValue;
        }

        if (input.Holding(TrackedKeys.Back))
        {
            forwardMove -= MaxSpeedValue;
        }

        if (input.Holding(TrackedKeys.Right))
        {
            sideMove += MaxSpeedValue;
        }

        if (input.Holding(TrackedKeys.Left))
        {
            sideMove -= MaxSpeedValue;
        }

        // Build wish velocity
        var wishvel = forward * forwardMove + right * sideMove;
        wishvel = new Vector3(wishvel.X, wishvel.Y, 0); // Zero out Z

        var wishspeed = wishvel.Length();
        var wishdir = wishspeed > 0 ? Vector3.Normalize(wishvel) : Vector3.Zero;

        // Clamp to max speed
        if (wishspeed > MaxSpeedValue)
        {
            wishvel *= MaxSpeedValue / wishspeed;
            wishspeed = MaxSpeedValue;
        }

        // Apply duck/crouch speed modifier (from CS:GO cs_gamemovement.cpp)
        if (HoldingCtrl) // Duck/crouch
        {
            wishspeed *= DuckSpeedModifier;
        }

        return (wishdir, wishspeed);
    }

    /// <summary>
    /// Apply ground friction to slow down the player
    /// </summary>
    private void Friction(float deltaTime)
    {
        var speed = Velocity.Length();
        if (speed < 0.1f) return;

        var control = speed < StopSpeedValue ? StopSpeedValue : speed;
        var drop = control * FrictionValue * SurfaceFriction * deltaTime;
        var newSpeed = Math.Max(0, speed - drop);

        if (newSpeed != speed)
        {
            Velocity *= newSpeed / speed;
        }
    }

    /// <summary>
    /// Accelerate in desired direction using CS:GO acceleration
    /// </summary>
    private void Accelerate(Vector3 wishdir, float wishspeed, float accel, float deltaTime, bool isDucking, bool isWalking)
    {
        var currentspeed = Vector3.Dot(Velocity, wishdir);
        var addspeed = wishspeed - currentspeed;

        if (addspeed <= 0) return;

        currentspeed = Math.Max(0, currentspeed);

        // CS:GO acceleration scaling
        var accelerationScale = MathF.Max(250.0f, wishspeed);
        var goalSpeed = accelerationScale;

        // Apply duck/walk modifiers
        if (isDucking)
        {
            accelerationScale *= DuckSpeedModifier;
            goalSpeed *= DuckSpeedModifier;
        }
        if (isWalking)
        {
            accelerationScale *= WalkSpeedModifier;
            goalSpeed *= WalkSpeedModifier;
        }

        // Walk speed gradient clamping - gradually reduce acceleration near goal speed
        var finalAccel = accel;
        if (isWalking && currentspeed > goalSpeed - 5)
        {
            var ratio = Math.Max(0.0f, currentspeed - (goalSpeed - 5)) / 5.0f;
            finalAccel *= Math.Clamp(1.0f - ratio, 0.0f, 1.0f);
        }

        var accelspeed = Math.Min(finalAccel * deltaTime * accelerationScale * SurfaceFriction, addspeed);
        Velocity += accelspeed * wishdir;
    }

    /// <summary>
    /// Ground movement with friction and acceleration
    /// </summary>
    private void WalkMove(Vector3 wishdir, float wishspeed, float deltaTime, bool isDucking, bool isWalking)
    {
        Velocity = new Vector3(Velocity.X, Velocity.Y, 0);
        Accelerate(wishdir, wishspeed, AccelerateValue, deltaTime, isDucking, isWalking);
        Velocity = new Vector3(Velocity.X, Velocity.Y, 0);

        // Clamp to effective max speed
        var effectiveMaxSpeed = RunSpeed;
        if (isDucking) effectiveMaxSpeed *= DuckSpeedModifier;
        else if (isWalking) effectiveMaxSpeed *= WalkSpeedModifier;

        if (Velocity.LengthSquared() > effectiveMaxSpeed * effectiveMaxSpeed)
        {
            Velocity *= effectiveMaxSpeed / Velocity.Length();
        }
    }

    /// <summary>
    /// Air movement with reduced acceleration
    /// </summary>
    private void AirMove(Vector3 wishdir, float wishspeed, float deltaTime)
    {
        AirAccelerate(wishdir, wishspeed, AirAccelerateValue, deltaTime);
    }

    /// <summary>
    /// Air acceleration - different from ground acceleration
    /// </summary>
    private void AirAccelerate(Vector3 wishdir, float wishspeed, float accel, float deltaTime)
    {
        var wishspd = Math.Min(wishspeed, 30); // Cap at 30 for air control
        var currentspeed = Vector3.Dot(Velocity, wishdir);
        var addspeed = wishspd - currentspeed;

        if (addspeed <= 0) return;

        // Note: uses original wishspeed, NOT the capped wishspd
        var accelspeed = Math.Min(accel * wishspeed * deltaTime * SurfaceFriction, addspeed);
        Velocity += accelspeed * wishdir;
    }

    /// <summary>
    /// Check and clamp velocity - prevents NaN and enforces max velocity
    /// </summary>
    private void CheckVelocity(ref Vector3 position)
    {
        // Fix NaN values
        if (float.IsNaN(Velocity.X) || float.IsNaN(Velocity.Y) || float.IsNaN(Velocity.Z))
        {
            Velocity = new Vector3(
                float.IsNaN(Velocity.X) ? 0 : Velocity.X,
                float.IsNaN(Velocity.Y) ? 0 : Velocity.Y,
                float.IsNaN(Velocity.Z) ? 0 : Velocity.Z
            );
        }

        var movementBounds = new AABB(Vector3.Zero, 16_000f);
        position = Vector3.Clamp(position, movementBounds.Min, movementBounds.Max);

        // Clamp to max velocity
        Velocity = new Vector3(
            Math.Clamp(Velocity.X, -MaxVelocityValue, MaxVelocityValue),
            Math.Clamp(Velocity.Y, -MaxVelocityValue, MaxVelocityValue),
            Math.Clamp(Velocity.Z, -MaxVelocityValue, MaxVelocityValue)
        );
    }

    private Rubikon.TraceResult TraceBBox(Vector3 from, Vector3 to, AABB aabb)
    {
        Debug.Assert(Physics != null, "Physics world must be initialized");
        return Physics.TraceAABB(from, to, aabb, "player");
    }
}
