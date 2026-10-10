using System.Threading.Tasks;
using ValveResourceFormat.Renderer.Gameplay;

namespace Tests.Renderer
{
    public class PlayerAnimationStateTest
    {
        private const float Tick = 1f / 64f;

        private static PlayerAnimationState.Input Standing(float aimYaw) => new()
        {
            AimYaw = aimYaw,
            MaxSpeed = 250f,
            OnGround = true,
        };

        private static void Run(PlayerAnimationState state, PlayerAnimationState.Input input, float seconds)
        {
            for (var time = 0f; time < seconds; time += Tick)
            {
                state.Update(input, Tick);
            }
        }

        [Test]
        public async Task FeetFollowARestingAimWithATurnOnTheSpot()
        {
            var state = new PlayerAnimationState();
            Run(state, Standing(0f), 0.5f);

            // The feet stay planted while the aim turns away and rests there
            Run(state, Standing(20f), 1f);
            var bodyYawWhileWaiting = state.BodyYaw;
            var aimYawWhileWaiting = state.AimYaw;
            var stateWhileWaiting = state.GroundState;

            Run(state, Standing(20f), 0.75f);
            var stateWhileTurning = state.GroundState;
            var turnAngle = state.TurnOnSpotAngle;

            Run(state, Standing(20f), 1.5f);

            await Assert.That(bodyYawWhileWaiting).IsBetween(0f, 1f);
            await Assert.That(aimYawWhileWaiting).IsBetween(19f, 20f);
            await Assert.That(stateWhileWaiting).IsEqualTo(PlayerAnimationState.GroundMoveState.Idle);
            await Assert.That(stateWhileTurning).IsEqualTo(PlayerAnimationState.GroundMoveState.TurnOnSpot);
            await Assert.That(turnAngle).IsBetween(19f, 20f);
            await Assert.That(state.GroundState).IsEqualTo(PlayerAnimationState.GroundMoveState.Idle);
            await Assert.That(state.BodyYaw).IsBetween(19.5f, 20.5f);
        }

        [Test]
        public async Task AimCannotLeadTheBodyByMoreThanItsLimit()
        {
            var state = new PlayerAnimationState();
            Run(state, Standing(0f), 0.5f);

            // Faster than the turn on the spot can keep up with
            state.Update(Standing(170f), Tick);

            await Assert.That(MathF.Abs(state.AimYaw)).IsLessThanOrEqualTo(70f);
        }

        [Test]
        public async Task AirActionsLastOneUpdate()
        {
            var state = new PlayerAnimationState();
            Run(state, Standing(0f), 0.5f);

            var jump = Standing(0f) with { Jumped = true };
            state.Update(jump, Tick);
            var onJump = state.CurrentAirAction;

            var airborne = Standing(0f) with { OnGround = false };
            state.Update(airborne, Tick);
            var whileAirborne = state.CurrentAirAction;
            var moveTypeWhileAirborne = state.CurrentMoveType;

            state.Update(Standing(0f), Tick);
            var onLanding = state.CurrentAirAction;

            state.Update(Standing(0f), Tick);

            await Assert.That(onJump).IsEqualTo(PlayerAnimationState.AirAction.Jump);
            await Assert.That(whileAirborne).IsEqualTo(PlayerAnimationState.AirAction.None);
            await Assert.That(moveTypeWhileAirborne).IsEqualTo(PlayerAnimationState.MoveType.Air);
            await Assert.That(onLanding).IsEqualTo(PlayerAnimationState.AirAction.Land);
            await Assert.That(state.CurrentAirAction).IsEqualTo(PlayerAnimationState.AirAction.None);
        }

        [Test]
        public async Task PlantsAndTurnsWhenTheKeysReverseTheMovement()
        {
            var state = new PlayerAnimationState();
            Run(state, Standing(0f), 0.5f);

            // Strafing left, which is +Y for a body facing +X
            var strafing = Standing(0f) with { Velocity = new Vector3(0f, 200f, 0f), WishMove = new Vector2(0f, 1f) };
            Run(state, strafing, 0.5f);
            var whileStrafing = state.GroundState;

            // Still sliding left with the right strafe key down
            var reversing = strafing with { WishMove = new Vector2(0f, -1f) };
            state.Update(reversing, Tick);
            var onReversal = state.GroundState;
            var plantDirection = state.GroundActionDirection;

            Run(state, reversing, 0.25f);

            await Assert.That(whileStrafing).IsEqualTo(PlayerAnimationState.GroundMoveState.Move);
            await Assert.That(onReversal).IsEqualTo(PlayerAnimationState.GroundMoveState.PlantAndTurn);
            await Assert.That(plantDirection).IsEqualTo(PlayerAnimationState.Direction.E);
            await Assert.That(state.GroundState).IsEqualTo(PlayerAnimationState.GroundMoveState.PlantAndTurn);
        }

        [Test]
        public async Task StartsBeforeMovingAndIdlesBelowTheMovingSpeed()
        {
            var state = new PlayerAnimationState();
            Run(state, Standing(0f), 0.5f);

            var running = Standing(0f) with { Velocity = new Vector3(0f, 200f, 0f) };
            state.Update(running, Tick);
            var onStart = state.GroundState;
            var startDirection = state.GroundActionDirection;

            Run(state, running, 0.25f);
            var whileRunning = state.GroundState;
            var runDirection = state.MoveDirection;

            // A stop this sudden is eased in over the following ticks
            var slow = Standing(0f) with { Velocity = new Vector3(0f, 9f, 0f) };
            state.Update(slow, Tick);
            var speedAfterSuddenStop = state.HorizontalSpeed;
            Run(state, slow, 1.5f);

            await Assert.That(onStart).IsEqualTo(PlayerAnimationState.GroundMoveState.Start);
            await Assert.That(startDirection).IsEqualTo(PlayerAnimationState.Direction.W);
            await Assert.That(whileRunning).IsEqualTo(PlayerAnimationState.GroundMoveState.Move);
            await Assert.That(runDirection).IsEqualTo(PlayerAnimationState.Direction.W);
            await Assert.That(speedAfterSuddenStop).IsBetween(170f, 190f);
            await Assert.That(state.GroundState).IsEqualTo(PlayerAnimationState.GroundMoveState.Idle);
        }
    }
}
