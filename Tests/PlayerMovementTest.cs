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
    /// Movement verifiers. Drives the real UserInput/PlayerMovement pipeline headlessly
    /// (no GL context, no physics world — traces fall back to the infinite ground plane)
    /// and checks it against double-precision ground truth.
    ///
    /// The thresholds are regression locks set just above the best measured precision:
    /// when precision improves, LOWER them so it cannot silently regress.
    /// </summary>
    public class PlayerMovementTest
    {
        private const float AirCap = 30f;                 // AirMaxWishSpeed
        private const float WishSpeed = 250f;             // RunSpeed, no walk/duck modifiers
        private const float AirAccelRate = 150f * WishSpeed;

        // Rotations below PlayerMovement's tickless guard run the engine's discrete rule,
        // which deviates from the continuous model by design (bounded, non-accumulating)
        private const float TicklessMinTurn = 2e-4f;

        private static (UserInput Input, Camera RenderCamera) CreateHeadlessFpsInput(float spawnHeight)
        {
            var context = new RendererContext(new GameFileLoader(null, null), NullLogger.Instance);
            var renderer = new Renderer(context);
            var input = new UserInput(renderer);
            var renderCamera = new Camera(context);

            // Leave noclip into FPS movement; the spawn height decides grounded vs airborne
            input.Camera.Location = new Vector3(0, 0, spawnHeight);
            input.Camera.Yaw = 0f;
            input.Camera.Pitch = 0f;
            input.Tick(1f / 128f, TrackedKeys.X, Vector2.Zero, renderCamera);

            Assert.That(input.NoClip, Is.False);
            return (input, renderCamera);
        }

        private static double HorizontalSpeed(Vector3 v) => double.Hypot(v.X, v.Y);

        /// <summary>
        /// Ground truth for one air frame: micro-substepped AirAccelerate with the wishdir
        /// rotating incrementally across the frame, in double precision.
        /// </summary>
        private static (double X, double Y) AirOracle(double vx, double vy, double wishAngleEnd, double yawDelta, double dt)
        {
            const int Substeps = 4000;
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
            // Regression locks: best measured maxima are 9.8e-4 (machine, 4000-substep
            // oracle) and 1.0e-2 (guard-zone discrete). Lower these when precision improves.
            const double MachineErrorLock = 1.5e-3;
            const double GuardZoneErrorLock = 2e-2;

            var rng = new Random(1234);
            var maxMachineError = 0.0;
            var maxGuardZoneError = 0.0;
            var machineFrames = 0;
            var guardFrames = 0;

            TrackedKeys[] keyChoices =
            [
                TrackedKeys.W, TrackedKeys.A, TrackedKeys.S, TrackedKeys.D,
                TrackedKeys.W | TrackedKeys.A, TrackedKeys.W | TrackedKeys.D,
                TrackedKeys.S | TrackedKeys.A, TrackedKeys.S | TrackedKeys.D,
                TrackedKeys.None,
            ];

            for (var trial = 0; trial < 30; trial++)
            {
                var (input, renderCamera) = CreateHeadlessFpsInput(spawnHeight: 6000f);
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

                    if (MathF.Abs(yawDelta) > TicklessMinTurn)
                    {
                        machineFrames++;
                        maxMachineError = Math.Max(maxMachineError, error);
                    }
                    else
                    {
                        guardFrames++;
                        maxGuardZoneError = Math.Max(maxGuardZoneError, error);
                    }
                }
            }

            TestContext.Out.WriteLine("Air strafe vs continuous oracle (random WASD + mouse, 30 trials, 60-560 fps):");
            TestContext.Out.WriteLine($"  {"frames",-10} {"max err (u/s)",-16} {"lock",-10}");
            TestContext.Out.WriteLine($"  machine    {machineFrames,-10} {maxMachineError,-16:E3} {MachineErrorLock:E1}");
            TestContext.Out.WriteLine($"  guard-zone {guardFrames,-10} {maxGuardZoneError,-16:E3} {GuardZoneErrorLock:E1}");

            Assert.That(machineFrames, Is.GreaterThan(5000));
            Assert.That(maxMachineError, Is.LessThan(MachineErrorLock), "tickless machine precision regressed");
            Assert.That(maxGuardZoneError, Is.LessThan(GuardZoneErrorLock), "sub-guard discrete frames deviate beyond their design bound");
        }

        [Test]
        public void AirStrafeGainIsFramerateInvariant()
        {
            // Regression lock on the cross-fps end-speed spread per turn rate
            const double SpreadLock = 8e-3;   // best measured: 5.6e-3 at 360°/s

            float[] framerates = [64f, 128f, 250f, 1000f];
            float[] turnRatesDegPerSec = [90f, 180f, 360f];
            var worstSpread = 0.0;

            TestContext.Out.WriteLine("Air strafe end speed after 2s of hold-W turning (u/s):");
            TestContext.Out.WriteLine($"  {"turn °/s",-10} {"64 fps",-12} {"128 fps",-12} {"250 fps",-12} {"1000 fps",-12} spread");

            foreach (var turnRateDeg in turnRatesDegPerSec)
            {
                var speeds = new double[framerates.Length];

                for (var run = 0; run < framerates.Length; run++)
                {
                    var (input, renderCamera) = CreateHeadlessFpsInput(spawnHeight: 6000f);
                    var dt = 1f / framerates[run];
                    var turnRate = float.DegreesToRadians(turnRateDeg);
                    var frames = (int)(2f * framerates[run]);

                    for (var i = 0; i < frames; i++)
                    {
                        input.Camera.Yaw += turnRate * dt;
                        input.Tick(dt, TrackedKeys.W, Vector2.Zero, renderCamera);
                    }

                    speeds[run] = HorizontalSpeed(input.PlayerMovement.Velocity);
                }

                var spread = 0.0;
                foreach (var speed in speeds)
                {
                    spread = Math.Max(spread, Math.Abs(speed - speeds[0]));
                }

                worstSpread = Math.Max(worstSpread, spread);
                TestContext.Out.WriteLine($"  {turnRateDeg,-10:F0} {speeds[0],-12:F4} {speeds[1],-12:F4} {speeds[2],-12:F4} {speeds[3],-12:F4} {spread:E2}");

                Assert.That(speeds[0], Is.GreaterThan(90), "strafe produced no speed; the scenario is broken");
            }

            Assert.That(worstSpread, Is.LessThan(SpreadLock), "air strafe framerate invariance regressed");
        }

        /// <summary>
        /// Runs a grounded scenario at the given framerate: settle onto the plane, then
        /// hold W for <paramref name="accelSeconds"/>, then release and coast until
        /// stopped. Returns the acceleration distance/end speed and the stop distance.
        /// </summary>
        private static (double AccelDistance, double AccelEndSpeed, double StopDistance) RunGroundScenario(float fps, float accelSeconds)
        {
            var (input, renderCamera) = CreateHeadlessFpsInput(spawnHeight: 100f);
            var movement = input.PlayerMovement;
            var dt = 1f / fps;

            // Settle onto the ground plane
            for (var i = 0; i < (int)(1f * fps); i++)
            {
                input.Tick(dt, TrackedKeys.None, Vector2.Zero, renderCamera);
            }

            Assert.That(movement.OnGround, Is.True, "player did not land during settling");
            Assert.That(HorizontalSpeed(movement.Velocity), Is.LessThan(0.001), "player did not come to rest");

            var accelStart = movement.Position;

            for (var i = 0; i < (int)(accelSeconds * fps); i++)
            {
                input.Tick(dt, TrackedKeys.W, Vector2.Zero, renderCamera);
            }

            var releasePoint = movement.Position;
            var accelEndSpeed = HorizontalSpeed(movement.Velocity);

            // Coast to a stop under friction alone
            for (var i = 0; i < (int)(2f * fps) && HorizontalSpeed(movement.Velocity) > 0; i++)
            {
                input.Tick(dt, TrackedKeys.None, Vector2.Zero, renderCamera);
            }

            Assert.That(HorizontalSpeed(movement.Velocity), Is.Zero, "player did not stop under friction");

            var stopPoint = movement.Position;

            return (
                double.Hypot(releasePoint.X - accelStart.X, releasePoint.Y - accelStart.Y),
                accelEndSpeed,
                double.Hypot(stopPoint.X - releasePoint.X, stopPoint.Y - releasePoint.Y));
        }

        [Test]
        public void GroundMovementIsFramerateInvariant()
        {
            // Regression locks on cross-fps spreads. Best measured: distance 6.1e-5,
            // speed 0.0 (bit-exact), stop distance 4.0e-4. Lower when precision improves.
            const double AccelDistanceSpreadLock = 5e-4;
            const double AccelSpeedSpreadLock = 1e-4;
            const double StopDistanceSpreadLock = 1.5e-3;

            // From rest, 2s of W reaches the 250 u/s cap; friction-only stop from the cap
            const float AccelSeconds = 2f;

            float[] framerates = [50f, 100f, 250f, 1000f];
            var accelDistances = new double[framerates.Length];
            var accelSpeeds = new double[framerates.Length];
            var stopDistances = new double[framerates.Length];

            for (var run = 0; run < framerates.Length; run++)
            {
                (accelDistances[run], accelSpeeds[run], stopDistances[run]) = RunGroundScenario(framerates[run], AccelSeconds);
            }

            TestContext.Out.WriteLine($"Ground movement: {AccelSeconds}s W from rest, then friction-only stop:");
            TestContext.Out.WriteLine($"  {"fps",-8} {"accel dist",-14} {"end speed",-14} {"stop dist",-14}");

            for (var run = 0; run < framerates.Length; run++)
            {
                TestContext.Out.WriteLine($"  {framerates[run],-8:F0} {accelDistances[run],-14:F5} {accelSpeeds[run],-14:F5} {stopDistances[run],-14:F5}");
            }

            static double Spread(double[] values)
            {
                var spread = 0.0;
                foreach (var value in values)
                {
                    spread = Math.Max(spread, Math.Abs(value - values[0]));
                }
                return spread;
            }

            TestContext.Out.WriteLine($"  spreads: dist={Spread(accelDistances):E2} speed={Spread(accelSpeeds):E2} stop={Spread(stopDistances):E2}");

            // Sanity anchors: analytic values for the continuous model
            Assert.That(accelSpeeds[0], Is.EqualTo(250.0).Within(0.01), "did not reach the run speed cap");
            Assert.That(stopDistances[0], Is.EqualTo(40.38).Within(0.2), "stop distance far from the analytic 40.38");

            Assert.That(Spread(accelDistances), Is.LessThan(AccelDistanceSpreadLock), "ground acceleration distance invariance regressed");
            Assert.That(Spread(accelSpeeds), Is.LessThan(AccelSpeedSpreadLock), "ground speed invariance regressed");
            Assert.That(Spread(stopDistances), Is.LessThan(StopDistanceSpreadLock), "friction stop distance invariance regressed");
        }
    }
}
