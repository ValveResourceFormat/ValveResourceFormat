using ValveResourceFormat.Renderer.AnimLib;

namespace ValveResourceFormat.Renderer.Gameplay;

/// <summary>
/// What a player's body is doing, worked out from how the player moves and aims.
/// It is what the body's animation graph is fed.
/// </summary>
public sealed class PlayerAnimationState
{
    /// <summary>How the body is moving.</summary>
    public enum MoveType : byte
    {
        /// <summary>Not known yet.</summary>
        None,
        /// <summary>On the ground.</summary>
        Ground,
        /// <summary>In the air.</summary>
        Air,
        /// <summary>On a ladder.</summary>
        Ladder,
    }

    /// <summary>What the body is doing on the ground.</summary>
    public enum GroundMoveState : byte
    {
        /// <summary>Nothing.</summary>
        None,
        /// <summary>Standing.</summary>
        Idle,
        /// <summary>Setting off.</summary>
        Start,
        /// <summary>Moving.</summary>
        Move,
        /// <summary>Turning once on the spot to face the aim.</summary>
        TurnOnSpot,
        /// <summary>Turning on the spot for as long as the aim keeps turning.</summary>
        TurnOnSpotLoop,
        /// <summary>Planting a foot to go back the way it came.</summary>
        PlantAndTurn,
    }

    /// <summary>A compass direction as seen from the body, which faces north.</summary>
    public enum Direction : byte
    {
        /// <summary>No direction.</summary>
        None,
        /// <summary>Forward.</summary>
        N,
        /// <summary>Forward and to the right.</summary>
        NE,
        /// <summary>To the right.</summary>
        E,
        /// <summary>Backward and to the right.</summary>
        SE,
        /// <summary>Backward.</summary>
        S,
        /// <summary>Backward and to the left.</summary>
        SW,
        /// <summary>To the left.</summary>
        W,
        /// <summary>Forward and to the left.</summary>
        NW,
    }

    /// <summary>What the body did in the air in one update.</summary>
    public enum AirAction : byte
    {
        /// <summary>Nothing.</summary>
        None,
        /// <summary>Jumped off the ground.</summary>
        Jump,
        /// <summary>Walked off an edge.</summary>
        StartFall,
        /// <summary>Landed.</summary>
        Land,
    }

    /// <summary>What the player did since the last update.</summary>
    public struct Input
    {
        /// <summary>Where the player looks, in degrees.</summary>
        public float AimYaw { get; set; }

        /// <summary>Where the player looks, in degrees, positive downwards.</summary>
        public float AimPitch { get; set; }

        /// <summary>The velocity of the player in the world.</summary>
        public Vector3 Velocity { get; set; }

        /// <summary>The movement keys held: forward and to the left</summary>
        public Vector2 WishMove { get; set; }

        /// <summary>The fastest the held weapon lets the player run.</summary>
        public float MaxSpeed { get; set; }

        /// <summary>Whether the player stands on the ground.</summary>
        public bool OnGround { get; set; }
        /// <summary>Whether the player is on a ladder.</summary>
        public bool OnLadder { get; set; }

        /// <summary>Whether a jump left the ground.</summary>
        public bool Jumped { get; set; }
    }

    // These are counted in ticks of 1/64 s
    private const float StartDuration = 6f / 64f;
    private const float StaticAimDuration = 96f / 64f;
    private const float TurnOnSpotEaseDuration = 47f / 64f;
    private const float TurnOnSpotDuration = 85f / 64f;
    private const float TurnLoopRampDuration = 12f / 64f;
    private const float PlantAndTurnDuration = 12f / 64f;
    private const float PlantAndTurnRepeatDelay = 10f / 64f;
    private const float TickRate = 64f;

    private const float MovingSpeed = 10f;
    private const float StartSpeed = 15f;

    // The aim can lead the body by this much before the body is dragged along
    private const float MaxAimLead = 70f;

    private const float StaticAimTolerance = 5f;
    private const float TurnOnSpotMaxAngle = 30f;
    private const float TurnOnSpotMaxYawSpeed = 45f;
    private const float TurnLoopStopSpeed = 16f;

    /// <summary>How the body is moving.</summary>
    public MoveType CurrentMoveType { get; private set; } = MoveType.None;
    /// <summary>What the body is doing on the ground.</summary>
    public GroundMoveState GroundState { get; private set; } = GroundMoveState.Idle;

    /// <summary>Which way a start heads off, set for as long as the start lasts.</summary>
    public Direction GroundActionDirection { get; private set; }

    /// <summary>The jump, fall or landing that happened in this update, set for that update only.</summary>
    public AirAction CurrentAirAction { get; private set; }

    /// <summary>Which way the body faces, in degrees. The feet stay planted while the aim turns.</summary>
    public float BodyYaw { get; private set; }

    /// <summary>The aim relative to the body, in degrees.</summary>
    public float AimYaw { get; private set; }

    /// <summary>The aim pitch, in degrees, positive downwards.</summary>
    public float AimPitch { get; private set; }

    /// <summary>Velocity in the frame of the body: forward and to the left.</summary>
    public Vector2 LocalVelocity { get; private set; }

    /// <summary>How fast the body moves over the ground.</summary>
    public float HorizontalSpeed { get; private set; }
    /// <summary>How fast the body moved over the ground in the update before.</summary>
    public float PreviousHorizontalSpeed { get; private set; }

    /// <summary>The direction the body moves in, none while it stands.</summary>
    public Direction MoveDirection { get; private set; }

    /// <summary>The angle a turn on the spot covers, in degrees.</summary>
    public float TurnOnSpotAngle { get; private set; }

    /// <summary>How fast a looping turn on the spot turns, in degrees per second.</summary>
    public float TurnVelocity { get; private set; }

    private bool wasOnGround = true;
    private bool initialized;
    private float worldAimYaw;
    private float previousWorldAimYaw;
    private float actionTime;
    private float staticAimTime = -1f;
    private float aimLead;
    private float aimYawSpeed;
    private float speedFraction;
    private Direction wishDirection;
    private Vector2 moveHeading;
    private float plantAndTurnTime;
    private bool braking;

    /// <summary>Works out the state for one update.</summary>
    /// <param name="input">What the player did since the last update.</param>
    /// <param name="deltaTime">The time since the last update, in seconds.</param>
    public void Update(in Input input, float deltaTime)
    {
        worldAimYaw = ValveMath.AngleNormalize(input.AimYaw);
        AimPitch = Math.Clamp(ValveMath.AngleNormalize(input.AimPitch), -89.9f, 89.9f);

        if (!initialized)
        {
            initialized = true;
            BodyYaw = worldAimYaw;
            previousWorldAimYaw = worldAimYaw;
            wasOnGround = input.OnGround;
        }

        aimYawSpeed = deltaTime > 0f ? MathF.Abs(AngleDiff(worldAimYaw, previousWorldAimYaw)) / deltaTime : 0f;

        // The body is dragged along once the aim leads it by too much
        aimLead = AngleDiff(worldAimYaw, BodyYaw);

        if (MathF.Abs(aimLead) > MaxAimLead)
        {
            BodyYaw = worldAimYaw - (MathF.Sign(aimLead) * (MaxAimLead - 1f));
            aimLead = AngleDiff(worldAimYaw, BodyYaw);
        }

        UpdateVelocity(input, deltaTime);

        CurrentAirAction = AirAction.None;
        actionTime += deltaTime;
        plantAndTurnTime += deltaTime;

        if (input.Jumped || (!input.OnGround && !input.OnLadder))
        {
            BodyYaw = worldAimYaw;
            CurrentMoveType = MoveType.Air;
            SetGroundState(GroundMoveState.Idle);

            if (input.Jumped)
            {
                CurrentAirAction = AirAction.Jump;
            }
            else if (wasOnGround)
            {
                CurrentAirAction = AirAction.StartFall;
            }
        }
        else if (input.OnLadder)
        {
            BodyYaw = worldAimYaw;
            CurrentMoveType = MoveType.Ladder;
            SetGroundState(GroundMoveState.Idle);
        }
        else
        {
            if (!wasOnGround)
            {
                CurrentAirAction = AirAction.Land;
            }

            CurrentMoveType = MoveType.Ground;
            UpdateGround(deltaTime);
        }

        AimYaw = AngleDiff(worldAimYaw, BodyYaw);
        wasOnGround = input.OnGround && !input.Jumped;
        previousWorldAimYaw = worldAimYaw;
    }

    private void UpdateVelocity(in Input input, float deltaTime)
    {
        var previousVelocity = LocalVelocity;
        PreviousHorizontalSpeed = HorizontalSpeed;

        wishDirection = GetDirection(input.WishMove);

        var (sin, cos) = MathF.SinCos(float.DegreesToRadians(BodyYaw));
        var forward = (input.Velocity.X * cos) + (input.Velocity.Y * sin);
        var left = (input.Velocity.Y * cos) - (input.Velocity.X * sin);

        LocalVelocity = new Vector2(forward, left);

        if (MathF.Abs(forward) <= 0.01f && MathF.Abs(left) <= 0.01f)
        {
            HorizontalSpeed = 0f;
            moveHeading = Vector2.Zero;
        }
        else
        {
            HorizontalSpeed = LocalVelocity.Length();
            moveHeading = LocalVelocity / HorizontalSpeed;
        }

        MoveDirection = GetDirection(moveHeading);

        // A stop too sudden to animate, like running into a wall, is spread over the next ticks
        var ticks = deltaTime * TickRate;
        var speedChange = ticks > 0f ? (HorizontalSpeed - PreviousHorizontalSpeed) / ticks : 0f;

        if (!braking && speedChange < -50f)
        {
            braking = true;
        }

        if (braking)
        {
            if (speedChange > 0f || MathF.Abs(speedChange) < 10f)
            {
                braking = false;
            }
            else
            {
                var blend = 1f - MathF.Pow(0.9f, ticks);
                var previousHeading = PreviousHorizontalSpeed > 0.01f ? previousVelocity / PreviousHorizontalSpeed : Vector2.Zero;
                var heading = Vector2.Lerp(previousHeading, moveHeading, blend);

                LocalVelocity = heading * float.Lerp(PreviousHorizontalSpeed, HorizontalSpeed, blend);
                HorizontalSpeed = LocalVelocity.Length();
            }
        }

        speedFraction = HorizontalSpeed / MathF.Max(input.MaxSpeed, 1f);
    }

    private void UpdateGround(float deltaTime)
    {
        // A state that hands over to another lets that one have the rest of the update
        for (var i = 0; i < 4; i++)
        {
            var state = GroundState;

            switch (state)
            {
                case GroundMoveState.Start:
                    UpdateStart(deltaTime);
                    break;
                case GroundMoveState.Move:
                    UpdateMove(deltaTime);
                    break;
                case GroundMoveState.TurnOnSpot:
                    UpdateTurnOnSpot(deltaTime);
                    break;
                case GroundMoveState.TurnOnSpotLoop:
                    UpdateTurnOnSpotLoop(deltaTime);
                    break;
                case GroundMoveState.PlantAndTurn:
                    UpdatePlantAndTurn(deltaTime);
                    break;
                default:
                    UpdateIdle(deltaTime);
                    break;
            }

            if (GroundState == state)
            {
                return;
            }
        }
    }

    private void SetGroundState(GroundMoveState state)
    {
        if (GroundState == state)
        {
            return;
        }

        GroundState = state;
        GroundActionDirection = Direction.None;
        actionTime = 0f;

        if (state == GroundMoveState.Idle)
        {
            staticAimTime = -1f;
        }
    }

    private void UpdateIdle(float deltaTime)
    {
        if (HorizontalSpeed > MovingSpeed)
        {
            if (HorizontalSpeed < StartSpeed)
            {
                SetGroundState(GroundMoveState.Move);
                return;
            }

            SetGroundState(GroundMoveState.Start);
            GroundActionDirection = MoveDirection;
            return;
        }

        var lead = MathF.Abs(aimLead);

        if (lead <= StaticAimTolerance)
        {
            staticAimTime = -1f;
            return;
        }

        if (lead > TurnOnSpotMaxAngle || aimYawSpeed >= TurnOnSpotMaxYawSpeed)
        {
            BeginTurn(GroundMoveState.TurnOnSpotLoop);
            return;
        }

        // The aim has to rest off to the side for a while before the feet follow it
        if (aimYawSpeed >= StaticAimTolerance || staticAimTime < 0f)
        {
            staticAimTime = 0f;
            return;
        }

        staticAimTime += deltaTime;

        if (staticAimTime < StaticAimDuration)
        {
            return;
        }

        BeginTurn(GroundMoveState.TurnOnSpot);
    }

    private void BeginTurn(GroundMoveState state)
    {
        SetGroundState(state);
        TurnOnSpotAngle = aimLead;
    }

    private void UpdateStart(float deltaTime)
    {
        if (actionTime <= StartDuration)
        {
            FollowAim(deltaTime);
            return;
        }

        SetGroundState(GroundMoveState.Move);
    }

    private void UpdateMove(float deltaTime)
    {
        if (HorizontalSpeed <= MovingSpeed)
        {
            SetGroundState(GroundMoveState.Idle);
            return;
        }

        PlantOrFollowAim(deltaTime);
    }

    private void UpdatePlantAndTurn(float deltaTime)
    {
        if (TryGetPlantAndTurn(out var direction))
        {
            BeginPlantAndTurn(direction);
            return;
        }

        if (actionTime > PlantAndTurnDuration)
        {
            SetGroundState(GroundMoveState.Move);
            return;
        }

        FollowAim(deltaTime);
    }

    private void PlantOrFollowAim(float deltaTime)
    {
        if (TryGetPlantAndTurn(out var direction))
        {
            BeginPlantAndTurn(direction);
            return;
        }

        FollowAim(deltaTime);
    }

    private void BeginPlantAndTurn(Direction direction)
    {
        GroundState = GroundMoveState.PlantAndTurn;
        GroundActionDirection = direction;
        actionTime = 0f;
        plantAndTurnTime = 0f;
    }

    // The player plants a foot and turns when the keys ask for the opposite of the way the body is moving,
    // as when stopping a strafe with the other strafe key
    private bool TryGetPlantAndTurn(out Direction direction)
    {
        direction = Direction.None;

        if (wishDirection == Direction.None || moveHeading == Vector2.Zero)
        {
            return false;
        }

        ReadOnlySpan<(Vector2 Heading, Direction Opposite)> reversals =
        [
            (Vector2.UnitX, Direction.S),
            (-Vector2.UnitX, Direction.N),
            (-Vector2.UnitY, Direction.W),
            (Vector2.UnitY, Direction.E),
        ];

        foreach (var (heading, opposite) in reversals)
        {
            if (wishDirection != opposite || MathF.Acos(Math.Clamp(Vector2.Dot(heading, moveHeading), -1f, 1f)) > MathF.PI / 6f)
            {
                continue;
            }

            // One already under way the same way is left to play
            if (GroundState == GroundMoveState.PlantAndTurn && GroundActionDirection == opposite && plantAndTurnTime < PlantAndTurnRepeatDelay)
            {
                return false;
            }

            direction = opposite;
            return true;
        }

        return false;
    }

    // While moving the body turns towards the aim, faster the faster it moves
    private void FollowAim(float deltaTime)
    {
        var lag = Math.Clamp(AngleDiff(BodyYaw, worldAimYaw), -45f, 45f);
        var step = deltaTime * ((speedFraction * 80f) + 55f);

        lag = lag < 0f ? MathF.Min(0f, lag + step) : MathF.Max(0f, lag - step);
        BodyYaw = ValveMath.AngleNormalize(worldAimYaw + lag);
    }

    private void UpdateTurnOnSpot(float deltaTime)
    {
        if (HorizontalSpeed > MovingSpeed)
        {
            SetGroundState(GroundMoveState.Move);
            return;
        }

        if (MathF.Abs(aimLead) > TurnOnSpotMaxAngle || aimYawSpeed >= TurnOnSpotMaxYawSpeed)
        {
            BeginTurn(GroundMoveState.TurnOnSpotLoop);
            return;
        }

        if (actionTime > TurnOnSpotDuration)
        {
            SetGroundState(GroundMoveState.Idle);
            return;
        }

        // The turn eases in and out over the first part of the animation
        static float Ease(float time) => 0.5f - (0.5f * MathF.Cos(MathF.PI * time / TurnOnSpotEaseDuration));

        var turned = MathF.Max(0f, Ease(actionTime) - Ease(actionTime - deltaTime));
        BodyYaw = ValveMath.AngleNormalize(BodyYaw + (turned * TurnOnSpotAngle));
    }

    private void UpdateTurnOnSpotLoop(float deltaTime)
    {
        if (HorizontalSpeed > MovingSpeed)
        {
            SetGroundState(GroundMoveState.Move);
            return;
        }

        if (deltaTime <= 0f)
        {
            return;
        }

        // Turns faster the further the aim is ahead, coming up to speed over the first few ticks. The ramp
        // counts whole ticks, so that a turn gets going the same at any frame rate: its first step has to
        // be fast enough to keep it from being called off again below.
        var lead = MathF.Abs(aimLead);
        var rampTicks = MathF.Floor(actionTime * TickRate) + 1f;
        var speed = lead * 8f * MathF.Min(1f, rampTicks / (TurnLoopRampDuration * TickRate));
        var step = MathF.Min(deltaTime * speed, lead) * MathF.Sign(aimLead);

        BodyYaw = ValveMath.AngleNormalize(BodyYaw + step);
        TurnVelocity = step / deltaTime;

        if (MathF.Abs(TurnVelocity) < TurnLoopStopSpeed)
        {
            SetGroundState(GroundMoveState.Idle);
        }
    }

    private static float AngleDiff(float to, float from) => ValveMath.AngleNormalize(to - from);

    /// <summary>Gets the compass direction of a vector given as forward and left.</summary>
    /// <param name="forwardLeft">The vector, forward on X and left on Y.</param>
    /// <returns>The nearest of the eight directions, or none for a vector with no length.</returns>
    public static Direction GetDirection(Vector2 forwardLeft)
    {
        if (forwardLeft.LengthSquared() < 0.0001f)
        {
            return Direction.None;
        }

        var octant = (int)MathF.Round(MathF.Atan2(forwardLeft.Y, forwardLeft.X) / (MathF.PI / 4f));

        // Turning left from north
        return octant switch
        {
            0 => Direction.N,
            1 => Direction.NW,
            2 => Direction.W,
            3 => Direction.SW,
            -1 => Direction.NE,
            -2 => Direction.E,
            -3 => Direction.SE,
            _ => Direction.S,
        };
    }
}
