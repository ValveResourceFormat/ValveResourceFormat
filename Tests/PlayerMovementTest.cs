using System;
using System.Numerics;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Input;

namespace Tests
{
    /// <summary>
    /// Air-movement verifier. Drives the real UserInput/PlayerMovement pipeline headlessly
    /// (no GL context, no physics world — traces fall back to the infinite ground plane)
    /// and compares every clean airborne frame against a double-precision substepped
    /// integration of the engine's AirAccelerate rule, i.e. the converged continuous model
    /// that the tickless air strafe claims to evaluate in closed form.
    /// </summary>
    public class PlayerMovementTest
    {
        private const float AirCap = 30f;                 // AirMaxWishSpeed
        private const float WishSpeed = 250f;             // RunSpeed, no walk/duck modifiers
        private const float AirAccelRate = 150f * WishSpeed;

        // Rotations below PlayerMovement's tickless guard run the engine's discrete rule,
        // which deviates from the continuous model by design (bounded, non-accumulating)
        private const float TicklessMinTurn = 2e-4f;

        private static (UserInput Input, Camera RenderCamera) CreateHeadlessFpsInput()
        {
            var context = new RendererContext(new GameFileLoader(null, null), NullLogger.Instance);
            var renderer = new Renderer(context);
            var input = new UserInput(renderer);
            var renderCamera = new Camera(context);

            // Spawn high above the ground plane, then leave noclip into FPS movement
            input.Camera.Location = new Vector3(0, 0, 6000);
            input.Camera.Yaw = 0f;
            input.Camera.Pitch = 0f;
            input.Tick(1f / 128f, TrackedKeys.X, Vector2.Zero, renderCamera);

            Assert.That(input.NoClip, Is.False);
            return (input, renderCamera);
        }

        /// <summary>
        /// Ground truth for one air frame: micro-substepped AirAccelerate with the wishdir
        /// rotating incrementally across the frame, in double precision.
        /// </summary>
        private static (double X, double Y) AirOracle(double vx, double vy, double wishAngleEnd, double yawDelta, double dt)
        {
            const int Substeps = 1000;
            var budgetPerStep = (double)AirAccelRate * dt / Substeps;
            var startAngle = wishAngleEnd - yawDelta;

            for (var i = 1; i <= Substeps; i++)
            {
                var (sin, cos) = Math.SinCos(startAngle + yawDelta * i / Substeps);
                var addspeed = AirCap - (vx * cos + vy * sin);

                if (addspeed > 0)
                {
                    var accelspeed = Math.Min(budgetPerStep, addspeed);
                    vx += accelspeed * cos;
                    vy += accelspeed * sin;
                }
            }

            return (vx, vy);
        }

        /// <summary>
        /// The wishdir angle relative to yaw for a WASD combination, matching
        /// PlayerMovement.CalculateWishVelocity; null when the keys cancel out.
        /// </summary>
        private static double? WishOffset(TrackedKeys keys)
        {
            var forwardMove = (keys.HasFlag(TrackedKeys.W) ? 1 : 0) - (keys.HasFlag(TrackedKeys.S) ? 1 : 0);
            var sideMove = (keys.HasFlag(TrackedKeys.D) ? 1 : 0) - (keys.HasFlag(TrackedKeys.A) ? 1 : 0);

            if (forwardMove == 0 && sideMove == 0)
            {
                return null;
            }

            return Math.Atan2(-sideMove, forwardMove);
        }

        [Test]
        public void AirStrafeMatchesContinuousOracle()
        {
            var rng = new Random(1234);
            var maxMachineError = 0.0;
            var maxGuardZoneError = 0.0;
            var comparedFrames = 0;

            TrackedKeys[] keyChoices =
            [
                TrackedKeys.W, TrackedKeys.A, TrackedKeys.S, TrackedKeys.D,
                TrackedKeys.W | TrackedKeys.A, TrackedKeys.W | TrackedKeys.D,
                TrackedKeys.S | TrackedKeys.A, TrackedKeys.S | TrackedKeys.D,
                TrackedKeys.None,
            ];

            for (var trial = 0; trial < 30; trial++)
            {
                var (input, renderCamera) = CreateHeadlessFpsInput();
                var movement = input.PlayerMovement;

                var fps = 60f + (float)rng.NextDouble() * 500f;
                var dt = 1f / fps;
                var keys = TrackedKeys.W;
                var frames = (int)(2.5f * fps);

                for (var i = 0; i < frames; i++)
                {
                    if (rng.NextDouble() < 0.05)
                    {
                        keys = keyChoices[rng.Next(keyChoices.Length)];
                    }

                    // Mixture of subtle and normal mouse movement
                    var yawDelta = (float)((rng.NextDouble() - 0.5) * 2);
                    yawDelta *= rng.NextDouble() < 0.5 ? 0.004f : 0.08f;

                    var preVelocity = movement.Velocity;
                    var wasAirborne = !movement.OnGround;

                    input.Camera.Yaw += yawDelta;
                    var yaw = input.Camera.Yaw;
                    input.Tick(dt, keys, Vector2.Zero, renderCamera);

                    // Only frames that were fully airborne have pure AirMove semantics
                    if (!wasAirborne || movement.OnGround || WishOffset(keys) is not double wishOffset)
                    {
                        continue;
                    }

                    var (expectedX, expectedY) = AirOracle(preVelocity.X, preVelocity.Y, yaw + wishOffset, yawDelta, dt);
                    var error = double.Hypot(movement.Velocity.X - expectedX, movement.Velocity.Y - expectedY);
                    comparedFrames++;

                    if (MathF.Abs(yawDelta) > TicklessMinTurn)
                    {
                        maxMachineError = Math.Max(maxMachineError, error);
                    }
                    else
                    {
                        maxGuardZoneError = Math.Max(maxGuardZoneError, error);
                    }
                }
            }

            Assert.That(comparedFrames, Is.GreaterThan(5000));
            Assert.That(maxMachineError, Is.LessThan(0.01), "tickless machine deviates from the continuous model");
            Assert.That(maxGuardZoneError, Is.LessThan(0.15), "sub-guard discrete frames deviate beyond their design bound");
        }

        [Test]
        public void AirStrafeGainIsFramerateInvariant()
        {
            var speeds = new double[2];
            float[] framerates = [64f, 1000f];

            for (var run = 0; run < framerates.Length; run++)
            {
                var (input, renderCamera) = CreateHeadlessFpsInput();
                var dt = 1f / framerates[run];
                var turnRate = MathF.PI;   // 180 deg/s
                var frames = (int)(2f * framerates[run]);

                for (var i = 0; i < frames; i++)
                {
                    input.Camera.Yaw += turnRate * dt;
                    input.Tick(dt, TrackedKeys.W, Vector2.Zero, renderCamera);
                }

                var velocity = input.PlayerMovement.Velocity;
                speeds[run] = double.Hypot(velocity.X, velocity.Y);
            }

            Assert.That(speeds[0], Is.GreaterThan(100), "strafe produced no speed; the scenario is broken");
            Assert.That(Math.Abs(speeds[0] - speeds[1]), Is.LessThan(0.5),
                $"strafe speed differs across framerates: {speeds[0]:F3} vs {speeds[1]:F3}");
        }
    }
}
