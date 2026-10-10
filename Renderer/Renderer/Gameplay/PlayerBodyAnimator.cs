using ValveResourceFormat.Renderer.AnimLib;
using ValveResourceFormat.Renderer.Input;
using ValveResourceFormat.Renderer.SceneNodes;

namespace ValveResourceFormat.Renderer.Gameplay;

internal sealed class PlayerBodyAnimator
{
    // What is held and what is being done with it, in the names the graph knows them by.
    public struct WeaponState
    {
        public string Category;
        public string Type;
        public bool IsSilenced;

        // The fastest the weapon lets the player run.
        public float MaxSpeed;

        public string Action;
        public string AttackType;
        public float ThrowStrength;

        // Whether the action began again since the last update, like each shot of a burst.
        public bool ActionRestarted;

        // How far firing has kicked the aim off the view, as (pitch, yaw) degrees.
        public Vector2 AimPunch;
    }

    // How the body moves and where it aims, whoever steers it.
    public struct BodyState
    {
        // Where the bottom of the hull is.
        public Vector3 Position;
        public Vector3 Velocity;

        // Where it aims, in degrees.
        public float AimYaw;
        public float AimPitch;

        // The way it is trying to go, as (forward, left).
        public Vector2 WishMove;

        public bool OnGround;
        public bool OnLadder;
        public bool Jumped;
        public bool IsWalking;
        public float CrouchAmount;
        public Vector3 LadderNormal;
    }

    // Traces a ray through the world and what stands in it, against the named collision.
    public delegate Rubikon.TraceResult WorldTrace(Vector3 from, Vector3 to, string collisionName);

    // What the graph calls the states of the body, indexed by their enums
    private static readonly string[] MoveTypeNames = ["", "move_type_ground", "move_type_air", "move_type_ladder"];

    private static readonly string[] GroundActionNames =
    [
        "", "ground_action_idle", "ground_action_start", "ground_action_move",
        "ground_action_turn_on_spot", "ground_action_turn_on_spot_loop", "ground_action_plant_and_turn",
    ];

    private static readonly string[] AirActionNames = ["", "air_action_jump", "air_action_start_fall", "air_action_land"];
    private static readonly string[] DirectionNames = ["", "N", "NE", "E", "SE", "S", "SW", "W", "NW"];

    // How far below the feet the ground is looked for while in the air
    private const float MaxHeightAboveGround = 72f;

    // Height climbed over one cycle of the ladder animation
    private const float LadderCycleHeight = 200f;

    private readonly AnimationGraph Graph;
    private readonly PlayerAnimationState State = new();
    private readonly WorldTrace InputTrace;
    private UserInput? TraceInput;
    private WorldTrace? Trace;

    // Which way the body faces, in degrees.
    public float BodyYaw => State.BodyYaw;

    public PlayerBodyAnimator(AnimationGraph graph)
    {
        Graph = graph;
        InputTrace = (from, to, collisionName) => ViewmodelSceneNode.TraceWorldAndEntities(TraceInput!, from, to, collisionName);
    }

    public void Update(UserInput input, in WeaponState weapon, float deltaTime)
    {
        var movement = input.PlayerMovement;

        TraceInput = input;

        Update(new BodyState
        {
            Position = movement.Position,
            Velocity = input.Velocity,
            AimYaw = float.RadiansToDegrees(input.Camera.Yaw),
            AimPitch = float.RadiansToDegrees(input.Camera.Pitch),
            WishMove = new Vector2(
                (input.Holding(TrackedKeys.W) ? 1f : 0f) - (input.Holding(TrackedKeys.S) ? 1f : 0f),
                (input.Holding(TrackedKeys.A) ? 1f : 0f) - (input.Holding(TrackedKeys.D) ? 1f : 0f)),
            OnGround = movement.OnGround,
            OnLadder = movement.OnLadder,
            Jumped = movement.Jumped,
            IsWalking = input.Holding(TrackedKeys.Shift) && !input.Holding(TrackedKeys.Control),
            CrouchAmount = movement.CrouchBlend,
            LadderNormal = movement.LadderNormal,
        }, weapon, InputTrace, deltaTime);
    }

    public void Update(in BodyState body, in WeaponState weapon, WorldTrace trace, float deltaTime)
    {
        var ids = Graph.IdParameters;
        var floats = Graph.FloatParameters;
        var bools = Graph.BoolParameters;

        Trace = trace;

        UpdateMovement(body, weapon, deltaTime);

        ids["weapon_category"] = weapon.Category;
        ids["weapon_type"] = weapon.Type;
        bools["weapon_is_silenced"] = weapon.IsSilenced;
        ids["action"] = weapon.Action;
        ids["attack_type"] = weapon.AttackType;
        floats["attack_throw_strength"] = weapon.ThrowStrength;

        // An action repeated back to back has to be told to start over
        if (weapon.ActionRestarted)
        {
            Graph.SignalBoolParameter("action_reset");
        }
    }

    private void UpdateMovement(in BodyState body, in WeaponState weapon, float deltaTime)
    {
        var ids = Graph.IdParameters;
        var floats = Graph.FloatParameters;
        var bools = Graph.BoolParameters;

        State.Update(new PlayerAnimationState.Input
        {
            // The body aims where the recoil has the weapon pointing, which is what kicks it when firing
            AimYaw = body.AimYaw + weapon.AimPunch.Y,
            AimPitch = body.AimPitch + weapon.AimPunch.X,
            Velocity = body.Velocity,
            WishMove = body.WishMove,
            MaxSpeed = weapon.MaxSpeed,
            OnGround = body.OnGround,
            OnLadder = body.OnLadder,
            Jumped = body.Jumped,
        }, deltaTime);

        ids["move_type"] = MoveTypeNames[(int)State.CurrentMoveType];
        ids["air_action"] = AirActionNames[(int)State.CurrentAirAction];
        ids["move_direction_id"] = DirectionNames[(int)State.MoveDirection];
        floats["move_speed_x"] = State.LocalVelocity.X;
        floats["move_speed_y"] = State.LocalVelocity.Y;
        floats["move_speed_horizontal"] = State.HorizontalSpeed;
        floats["move_speed_horizontal_previous"] = State.PreviousHorizontalSpeed;
        floats["move_crouch_amount"] = body.CrouchAmount;
        bools["move_is_walking"] = State.HorizontalSpeed > 0f && body.IsWalking;
        floats["aim_angle_pitch"] = State.AimPitch;
        floats["aim_angle_yaw"] = State.AimYaw;

        var feet = body.Position;
        var heightAboveGround = 0f;

        switch (State.CurrentMoveType)
        {
            case PlayerAnimationState.MoveType.Ground:
                ids["ground_action"] = GroundActionNames[(int)State.GroundState];
                ids["ground_action_direction_id"] = DirectionNames[(int)State.GroundActionDirection];
                floats["ground_turn_angle_or_velocity"] = State.GroundState switch
                {
                    PlayerAnimationState.GroundMoveState.TurnOnSpot => State.TurnOnSpotAngle,
                    PlayerAnimationState.GroundMoveState.TurnOnSpotLoop => State.TurnVelocity,
                    _ => 0f,
                };

                break;

            case PlayerAnimationState.MoveType.Air:
                var ground = Trace!(feet, feet - new Vector3(0f, 0f, MaxHeightAboveGround), Rubikon.DecalGeometry);
                heightAboveGround = ground.Hit ? feet.Z - ground.HitPosition.Z : MaxHeightAboveGround;
                break;

            case PlayerAnimationState.MoveType.Ladder:
                // The climb cycles with the height, whichever way it is climbed
                var height = feet.Z % LadderCycleHeight;
                var cycle = MathF.Abs(height) / LadderCycleHeight;
                floats["ladder_cycle"] = MathUtils.Saturate(height < 0f ? 1f - cycle : cycle);

                // Where the ladder is, as seen from where the body faces
                var toLadder = -body.LadderNormal;
                var ladderYaw = float.RadiansToDegrees(MathF.Atan2(toLadder.Y, toLadder.X));
                var ladderAngle = ValveMath.AngleNormalize(ladderYaw - State.BodyYaw);
                floats["ladder_angle_yaw"] = ladderAngle;
                floats["ladder_angle_yaw_backwards"] = ladderAngle > 0f ? ladderAngle - 180f : ladderAngle + 180f;
                break;
        }

        floats["air_height_above_ground"] = heightAboveGround;
    }
}
