using GUI.Utils;

namespace GUI.Types.Renderer;

/// <summary>
/// FPS-style movement physics ported from Source engine (base gamemovement.cpp + CS:GO speed modifiers)
/// Implements MOVETYPE_WALK behavior with ground friction, acceleration, jumping
///
/// IMPLEMENTED:
/// - Split gravity (StartGravity/FinishGravity) for proper integration
/// - Ground friction and acceleration (base Source engine)
/// - Air acceleration with 30 unit/sec cap for air control
/// - Jumping with proper impulse
/// - Velocity checking (NaN protection) and clamping
/// - Walk speed modifier (Shift key - 52% speed, CS:GO)
/// - Duck/crouch speed modifier (Control key - 34% speed, CS:GO)
///
/// NOT IMPLEMENTED (intentionally simplified):
/// - Collision detection (uses infinite ground plane at Z=0)
/// - Water movement/swimming
/// - Traces/raycasts
/// - CS:GO exponential acceleration curves (uses base Source engine linear acceleration)
/// - Weapon speed modifiers (AWP scoped speed, etc.)
/// - Bunnyhopping prevention (can gain speed by jumping repeatedly)
/// - Stamina system (jump height/speed penalties when tired)
/// - Full duck mechanics (view height changes, collision hull shrinking)
/// - StayOnGround() for slopes/stairs
/// </summary>
class FpsMovement
{
    // Movement constants from Source engine (movevars_shared.cpp)
    private const float GravityValue = 800f;              // sv_gravity
    private const float FrictionValue = 5.2f;             // sv_friction
    private const float StopSpeedValue = 80f;             // sv_stopspeed
    private const float AccelerateValue = 5.5f;           // sv_accelerate
    private const float AirAccelerateValue = 12f;         // sv_airaccelerate
    private const float MaxSpeedValue = 320f;             // sv_maxspeed
    private const float JumpImpulseValue = 301.993377f;   // sv_jump_impulse = sqrt(2*800*57)
    private const float MaxVelocityValue = 3500f;         // sv_maxvelocity

    // Speed modifiers from CS:GO (cs_shareddefs.cpp)
    private const float WalkSpeedModifier = 0.52f;        // CS_PLAYER_SPEED_WALK_MODIFIER
    private const float DuckSpeedModifier = 0.34f;        // CS_PLAYER_SPEED_DUCK_MODIFIER

    // Movement state
    public Vector3 Velocity { get; private set; }
    private bool OnGround;
    private bool OldButtonJump;

    // Surface properties (simplified - always 1.0 for now)
    private const float SurfaceFriction = 1.0f;

    public FpsMovement()
    {
        Velocity = Vector3.Zero;
        OnGround = false;
        OldButtonJump = false;
    }

    /// <summary>
    /// Main movement tick - processes input and updates position/velocity
    /// </summary>
    public Vector3 ProcessMovement(Vector3 currentPosition, TrackedKeys input, float deltaTime, float pitch, float yaw)
    {
        var position = currentPosition;

        // Categorize position (check if on ground)
        CategorizePosition(ref position);

        // StartGravity - add gravity at start of frame (like Source does)
        if (!OnGround)
        {
            Velocity = new Vector3(Velocity.X, Velocity.Y, Velocity.Z - GravityValue * deltaTime * 0.5f);
            CheckVelocity(ref position); // StartGravity calls CheckVelocity in Source
        }

        // Check for jump
        if ((input & TrackedKeys.Jump) != 0 && !OldButtonJump && OnGround)
        {
            CheckJump(deltaTime);
        }
        OldButtonJump = (input & TrackedKeys.Jump) != 0;

        // Calculate wish velocity from input
        var (wishdir, wishspeed) = CalculateWishVelocity(input, pitch, yaw);

        // Ground or air movement
        if (OnGround)
        {
            // Apply friction before movement
            Velocity = new Vector3(Velocity.X, Velocity.Y, 0);
            Friction(deltaTime);
            WalkMove(wishdir, wishspeed, deltaTime);
        }
        else
        {
            AirMove(wishdir, wishspeed, deltaTime);
        }

        // Check velocity for NaN/bounds
        CheckVelocity(ref position);

        // Update position based on velocity (in Source this happens inside TryPlayerMove)
        position += Velocity * deltaTime;

        // Enforce ground plane (infinite plane at Z=0)
        if (position.Z < 0)
        {
            position = new Vector3(position.X, position.Y, 0);
        }

        // Recategorize position after movement (now that position is updated)
        CategorizePosition(ref position);

        // Check velocity again for NaN/bounds
        CheckVelocity(ref position);

        // FinishGravity - add remaining gravity at end of frame
        if (!OnGround)
        {
            Velocity = new Vector3(Velocity.X, Velocity.Y, Velocity.Z - GravityValue * deltaTime * 0.5f);
            CheckVelocity(ref position); // FinishGravity calls CheckVelocity in Source
        }

        // If on ground, no downward velocity
        if (OnGround)
        {
            Velocity = new Vector3(Velocity.X, Velocity.Y, 0);
        }

        return position;
    }

    /// <summary>
    /// Check if player is on ground (simplified - infinite plane at Z=0)
    /// </summary>
    private void CategorizePosition(ref Vector3 position)
    {
        OnGround = position.Z <= 0.1f; // Small epsilon for floating point
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
    private static (Vector3 wishdir, float wishspeed) CalculateWishVelocity(TrackedKeys input, float pitch, float yaw)
    {
        // Calculate forward and right vectors from yaw (ignore pitch for horizontal movement)
        var forward = new Vector3(MathF.Cos(yaw), MathF.Sin(yaw), 0);
        var right = new Vector3(MathF.Cos(yaw - MathF.PI / 2f), MathF.Sin(yaw - MathF.PI / 2f), 0);

        // Determine movement amounts
        float forwardMove = 0, sideMove = 0;

        if ((input & TrackedKeys.Forward) != 0)
        {
            forwardMove += MaxSpeedValue;
        }

        if ((input & TrackedKeys.Back) != 0)
        {
            forwardMove -= MaxSpeedValue;
        }

        if ((input & TrackedKeys.Right) != 0)
        {
            sideMove += MaxSpeedValue;
        }

        if ((input & TrackedKeys.Left) != 0)
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

        // Apply walk/duck speed modifiers (from CS:GO cs_gamemovement.cpp)
        var speedModifier = 1.0f;
        if ((input & TrackedKeys.Control) != 0) // Duck/crouch
        {
            speedModifier = DuckSpeedModifier;
        }
        else if ((input & TrackedKeys.Shift) != 0) // Walk
        {
            speedModifier = WalkSpeedModifier;
        }

        wishspeed *= speedModifier;

        return (wishdir, wishspeed);
    }

    /// <summary>
    /// Apply ground friction to slow down the player
    /// Ported from gamemovement.cpp Friction()
    /// </summary>
    private void Friction(float deltaTime)
    {
        // Calculate speed
        var speed = Velocity.Length();

        // If too slow, return
        if (speed < 0.1f)
        {
            return;
        }

        var drop = 0f;

        // Apply ground friction (only when on ground, but this is only called when on ground)
        var friction = FrictionValue * SurfaceFriction;

        // Bleed off some speed, but if we have less than the bleed
        // threshold, bleed the threshold amount.
        var control = (speed < StopSpeedValue) ? StopSpeedValue : speed;

        // Add the amount to the drop amount.
        drop += control * friction * deltaTime;

        // scale the velocity
        var newspeed = speed - drop;
        if (newspeed < 0)
        {
            newspeed = 0;
        }

        if (newspeed != speed)
        {
            // Determine proportion of old speed we are using.
            newspeed /= speed;
            // Adjust velocity according to proportion.
            Velocity *= newspeed;
        }
    }

    /// <summary>
    /// Accelerate in desired direction
    /// Ported from gamemovement.cpp Accelerate()
    /// </summary>
    private void Accelerate(Vector3 wishdir, float wishspeed, float accel, float deltaTime)
    {
        // See if we are changing direction a bit
        var currentspeed = Vector3.Dot(Velocity, wishdir);

        // Reduce wishspeed by the amount of veer.
        var addspeed = wishspeed - currentspeed;

        // If not going to add any speed, done.
        if (addspeed <= 0)
        {
            return;
        }

        // Determine amount of acceleration (CS:GO uses max 250, wishspeed scaling)
        var accelScale = MathF.Max(250.0f, wishspeed);
        var accelspeed = accel * deltaTime * accelScale * SurfaceFriction;

        // Cap at addspeed
        if (accelspeed > addspeed)
        {
            accelspeed = addspeed;
        }

        // Adjust velocity
        Velocity += accelspeed * wishdir;
    }

    /// <summary>
    /// Ground movement with friction and acceleration
    /// Ported from gamemovement.cpp WalkMove()
    /// Simplified - no traces, assumes flat ground
    /// </summary>
    private void WalkMove(Vector3 wishdir, float wishspeed, float deltaTime)
    {
        // Set pmove velocity (zero out Z component)
        Velocity = new Vector3(Velocity.X, Velocity.Y, 0);

        // Accelerate
        Accelerate(wishdir, wishspeed, AccelerateValue, deltaTime);

        Velocity = new Vector3(Velocity.X, Velocity.Y, 0);

        // Clamp to max speed to prevent going faster while turning
        // Use LengthSquared for performance (avoids sqrt)
        if (Velocity.LengthSquared() > MaxSpeedValue * MaxSpeedValue)
        {
            var speed = Velocity.Length();
            Velocity *= MaxSpeedValue / speed;
        }
    }

    /// <summary>
    /// Air movement with reduced acceleration
    /// Ported from gamemovement.cpp AirMove()
    /// </summary>
    private void AirMove(Vector3 wishdir, float wishspeed, float deltaTime)
    {
        // Air accelerate uses different function!
        AirAccelerate(wishdir, wishspeed, AirAccelerateValue, deltaTime);
    }

    /// <summary>
    /// Air acceleration - different from ground acceleration
    /// Ported from gamemovement.cpp AirAccelerate()
    /// </summary>
    private void AirAccelerate(Vector3 wishdir, float wishspeed, float accel, float deltaTime)
    {
        var wishspd = wishspeed;

        // Cap speed at 30 for air control
        if (wishspd > 30)
        {
            wishspd = 30;
        }

        // Determine veer amount
        var currentspeed = Vector3.Dot(Velocity, wishdir);

        // See how much to add
        var addspeed = wishspd - currentspeed;

        // If not adding any, done.
        if (addspeed <= 0)
        {
            return;
        }

        // Determine acceleration speed after acceleration
        // Note: uses original wishspeed, NOT the capped wishspd
        var accelspeed = accel * wishspeed * deltaTime * SurfaceFriction;

        // Cap it
        if (accelspeed > addspeed)
        {
            accelspeed = addspeed;
        }

        // Adjust velocity
        Velocity += accelspeed * wishdir;
    }

    /// <summary>
    /// Check and clamp velocity - prevents NaN and enforces max velocity
    /// Ported from gamemovement.cpp CheckVelocity()
    /// </summary>
    private void CheckVelocity(ref Vector3 position)
    {
        // Check for NaN and fix
        if (float.IsNaN(Velocity.X) || float.IsNaN(Velocity.Y) || float.IsNaN(Velocity.Z))
        {
            Velocity = new Vector3(
                float.IsNaN(Velocity.X) ? 0 : Velocity.X,
                float.IsNaN(Velocity.Y) ? 0 : Velocity.Y,
                float.IsNaN(Velocity.Z) ? 0 : Velocity.Z
            );
        }

        // Check for NaN in position and fix
        if (float.IsNaN(position.X) || float.IsNaN(position.Y) || float.IsNaN(position.Z))
        {
            position = new Vector3(
                float.IsNaN(position.X) ? 0 : position.X,
                float.IsNaN(position.Y) ? 0 : position.Y,
                float.IsNaN(position.Z) ? 0 : position.Z
            );
        }

        // Clamp each component to max velocity
        Velocity = new Vector3(
            Math.Clamp(Velocity.X, -MaxVelocityValue, MaxVelocityValue),
            Math.Clamp(Velocity.Y, -MaxVelocityValue, MaxVelocityValue),
            Math.Clamp(Velocity.Z, -MaxVelocityValue, MaxVelocityValue)
        );
    }
}
