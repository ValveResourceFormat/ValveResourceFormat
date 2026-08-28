using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Input;

namespace Tests.Renderer
{
    /// <summary>
    /// Deadlock movement verifiers: stamina, dashes, slides and air jumps, driven headlessly
    /// against the infinite ground plane like <see cref="PlayerMovementTest"/>.
    /// </summary>
    public class DeadlockMovementTest
    {
        private readonly List<IDisposable> HarnessContexts = [];

        [After(HookType.Test)]
        public void DisposeHarnessContexts()
        {
            for (var i = HarnessContexts.Count - 1; i >= 0; i--)
            {
                HarnessContexts[i].Dispose();
            }

            HarnessContexts.Clear();
        }

        private async Task<(UserInput Input, Camera RenderCamera)> CreateGroundedDeadlockInput()
        {
            var fileLoader = new GameFileLoader(null, null);
            HarnessContexts.Add(fileLoader);

            var context = new RendererContext(fileLoader, NullLogger.Instance);
            HarnessContexts.Add(context);

            var renderer = new ValveResourceFormat.Renderer.Renderer(context);
            var input = new UserInput(renderer);
            var renderCamera = new Camera();

            input.Camera.Location = new Vector3(0, 0, 64f);
            input.Camera.Yaw = 0f;
            input.Camera.Pitch = 0f;
            input.Tick(1f / 128f, TrackedKeys.X, Vector2.Zero, renderCamera);

            await Assert.That(input.NoClip).IsFalse();

            input.PlayerMovement.DeadlockMode = true;

            const float dt = 1f / 128f;
            for (var i = 0; i < 128; i++)
            {
                input.Tick(dt, TrackedKeys.None, Vector2.Zero, renderCamera);
            }

            await Assert.That(input.PlayerMovement.OnGround).IsTrue().Because("player did not land during settling");
            return (input, renderCamera);
        }

        private static void Run(UserInput input, Camera renderCamera, TrackedKeys keys, float seconds, float fps = 128f)
        {
            var dt = 1f / fps;
            for (var i = 0; i < (int)(seconds * fps); i++)
            {
                input.Tick(dt, keys, Vector2.Zero, renderCamera);
            }
        }

        private static double HorizontalSpeed(Vector3 v) => double.Hypot(v.X, v.Y);

        [Test]
        public async Task AirJumpBoostsAndSpendsStamina()
        {
            var (input, renderCamera) = await CreateGroundedDeadlockInput();
            var movement = input.PlayerMovement;

            var staminaBefore = movement.DeadlockStamina;

            // Ground jump, then wait for the descending half of the arc
            input.Tick(1f / 128f, TrackedKeys.Space, Vector2.Zero, renderCamera);
            await Assert.That(movement.OnGround).IsFalse().Because("ground jump did not leave the ground");

            Run(input, renderCamera, TrackedKeys.None, 0.6f);
            await Assert.That(movement.Velocity.Z).IsLessThan(0f).Because("jump should be descending after 0.6s");
            await Assert.That(movement.OnGround).IsFalse();

            // Air jump resets the vertical velocity upward and costs a bar
            input.Tick(1f / 128f, TrackedKeys.Space, Vector2.Zero, renderCamera);

            await Assert.That(movement.Velocity.Z).IsGreaterThan(200f).Because("air jump should relaunch upward");
            await Assert.That(movement.DeadlockStamina).IsLessThan(staminaBefore - 0.9f).Because("air jump costs one bar");

            // A second air jump in the same airtime does nothing
            Run(input, renderCamera, TrackedKeys.None, 0.1f);
            var staminaAfterFirst = movement.DeadlockStamina;
            input.Tick(1f / 128f, TrackedKeys.Space, Vector2.Zero, renderCamera);

            await Assert.That(movement.DeadlockStamina).IsEqualTo(staminaAfterFirst).Within(0.05f)
                .Because("only one air jump per airtime");
        }

        [Test]
        public async Task GroundDashSetsSpeedThenMomentumResets()
        {
            var (input, renderCamera) = await CreateGroundedDeadlockInput();
            var movement = input.PlayerMovement;

            var staminaBefore = movement.DeadlockStamina;

            input.Tick(1f / 128f, TrackedKeys.W | TrackedKeys.Shift, Vector2.Zero, renderCamera);

            await Assert.That(movement.IsDashing).IsTrue();
            await Assert.That(HorizontalSpeed(movement.Velocity)).IsGreaterThan(600.0).Because("ground dash sets velocity to ~635 u/s");
            await Assert.That(movement.DeadlockStamina).IsLessThan(staminaBefore - 0.9f).Because("dash costs one bar");

            // Ride the dash out; the momentum resets back to run speed
            Run(input, renderCamera, TrackedKeys.W, 1.0f);

            await Assert.That(movement.IsDashing).IsFalse();
            await Assert.That(HorizontalSpeed(movement.Velocity)).IsLessThan(330.0).Because("dash momentum resets when it ends");
            await Assert.That(HorizontalSpeed(movement.Velocity)).IsGreaterThan(180.0).Because("running should continue after the dash");
        }

        [Test]
        public async Task DashSlideKeepsSpeedThroughGraceThenBleeds()
        {
            var (input, renderCamera) = await CreateGroundedDeadlockInput();
            var movement = input.PlayerMovement;

            input.Tick(1f / 128f, TrackedKeys.W | TrackedKeys.Shift, Vector2.Zero, renderCamera);
            await Assert.That(movement.IsDashing).IsTrue();

            // Crouch converts the dash into a slide that inherits the dash speed
            Run(input, renderCamera, TrackedKeys.W | TrackedKeys.Control, 0.4f);

            await Assert.That(movement.IsSliding).IsTrue().Because("crouching at dash speed slides");
            await Assert.That(HorizontalSpeed(movement.Velocity)).IsGreaterThan(500.0).Because("slide keeps most of the dash speed inside the grace window");

            // Past the grace the late friction bleeds it off and the slide ends
            Run(input, renderCamera, TrackedKeys.W | TrackedKeys.Control, 2.5f);

            await Assert.That(movement.IsSliding).IsFalse().Because("slide ends once the speed is gone");
            await Assert.That(HorizontalSpeed(movement.Velocity)).IsLessThan(250.0);
        }

        [Test]
        public async Task WallJumpKicksAwayFromWall()
        {
            var (input, renderCamera) = await CreateGroundedDeadlockInput();
            var movement = input.PlayerMovement;

            // Solid half-space x >= 60: a wall ahead of the player (facing +X)
            movement.DebugCollisionPlanes.Add(new Vector4(-1f, 0f, 0f, -60f));

            // Run into the wall, jump, and while airborne against it jump again
            Run(input, renderCamera, TrackedKeys.W, 0.6f);
            input.Tick(1f / 128f, TrackedKeys.W | TrackedKeys.Space, Vector2.Zero, renderCamera);
            await Assert.That(movement.OnGround).IsFalse().Because("ground jump did not leave the ground");

            Run(input, renderCamera, TrackedKeys.W, 0.15f);

            var staminaBefore = movement.DeadlockStamina;
            input.Tick(1f / 128f, TrackedKeys.W | TrackedKeys.Space, Vector2.Zero, renderCamera);

            await Assert.That(movement.Velocity.Z).IsGreaterThan(200f).Because("a wall jump toward the wall launches upward");
            await Assert.That(movement.Velocity.X).IsLessThan(0f).Because("the wall jump pushes away from the wall");
            await Assert.That(movement.DeadlockStamina).IsEqualTo(staminaBefore).Within(0.05f)
                .Because("the first wall jump is free");
        }

        [Test]
        public async Task StaminaRegenerates()
        {
            var (input, renderCamera) = await CreateGroundedDeadlockInput();
            var movement = input.PlayerMovement;

            input.Tick(1f / 128f, TrackedKeys.W | TrackedKeys.Shift, Vector2.Zero, renderCamera);
            await Assert.That(movement.DeadlockStamina).IsLessThan(movement.DeadlockStaminaMax - 0.9f);

            // One bar takes 5 seconds to come back
            Run(input, renderCamera, TrackedKeys.None, 5.5f);

            await Assert.That(movement.DeadlockStamina).IsEqualTo(movement.DeadlockStaminaMax).Within(0.05f);
        }
    }
}
