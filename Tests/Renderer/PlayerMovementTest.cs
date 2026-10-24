using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Input;

namespace Tests.Renderer
{
    /// <summary>
    /// Movement verifiers. Drives the real UserInput/PlayerMovement pipeline headlessly
    /// (no GL context, no physics world; traces fall back to the infinite ground plane)
    /// and checks it against double-precision ground truth.
    ///
    /// The thresholds are regression locks set just above the best measured precision:
    /// when precision improves, LOWER them so it cannot silently regress.
    /// </summary>
    [Explicit]
    public class PlayerMovementTest
    {
        // The harness contexts a test creates, disposed after it. The helper hands back only
        // what the tests use, so the disposables are tracked here rather than at call sites.
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

        private async Task<(UserInput Input, Camera RenderCamera)> CreateHeadlessFpsInput(float spawnHeight)
        {
            var fileLoader = new GameFileLoader(null, null);
            HarnessContexts.Add(fileLoader);

            var context = new RendererContext(fileLoader, NullLogger.Instance);
            HarnessContexts.Add(context);

            var renderer = new ValveResourceFormat.Renderer.Renderer(context);
            var input = new UserInput(renderer);
            var renderCamera = new Camera();

            // Leave noclip into FPS movement; the spawn height decides grounded vs airborne
            input.Camera.Location = new Vector3(0, 0, spawnHeight);
            input.Camera.Yaw = 0f;
            input.Camera.Pitch = 0f;
            input.Tick(1f / 128f, TrackedKeys.X, Vector2.Zero, renderCamera);

            await Assert.That(input.WalkMode).IsTrue();
            return (input, renderCamera);
        }

        private static double HorizontalSpeed(Vector3 v) => double.Hypot(v.X, v.Y);

        /// <summary>The feet as of the last tick, rather than as drawn between the last two.</summary>
        private static Vector3 SimulatedFeet(PlayerMovement movement) => movement.HullCenter - new Vector3(0, 0, movement.HullHalfExtents.Z);

        private static readonly float[] ComparisonFramerates = [50f, 100f, 250f, 1000f];

        /// <summary>Largest deviation from the first value, the cross-framerate invariance signal.</summary>
        private static double Spread(double[] values)
        {
            var spread = 0.0;
            foreach (var value in values)
            {
                spread = Math.Max(spread, Math.Abs(value - values[0]));
            }
            return spread;
        }

        /// <summary>Settles the player onto the ground with no input held.</summary>
        private static async Task SettleOntoGround(UserInput input, Camera renderCamera, float fps)
        {
            var dt = 1f / fps;

            for (var i = 0; i < (int)(1f * fps); i++)
            {
                input.Tick(dt, TrackedKeys.None, Vector2.Zero, renderCamera);
            }

            await Assert.That(input.PlayerMovement.OnGround).IsTrue().Because("player did not land during settling");
        }

        [Test]
        public async Task AirStrafeGainIsFramerateInvariant()
        {
            // Regression lock on the cross-fps end-speed spread per turn rate
            const double SpreadLock = 1e-3; // best measured: 4.7e-4 at 180 deg/s, from the yaw summed per frame

            // Multiples of the tick rate, so every tick samples the view at the same moment
            float[] framerates = [64f, 128f, 256f, 1024f];
            float[] turnRatesDegPerSec = [90f, 180f, 360f];
            var worstSpread = 0.0;

            await Console.Out.WriteLineAsync("Air strafe end speed after 2s of hold-W turning (u/s):");
            await Console.Out.WriteLineAsync($"  {"turn °/s",-10} {"64 fps",-12} {"128 fps",-12} {"256 fps",-12} {"1024 fps",-12} spread");

            foreach (var turnRateDeg in turnRatesDegPerSec)
            {
                var speeds = new double[framerates.Length];

                for (var run = 0; run < framerates.Length; run++)
                {
                    var (input, renderCamera) = await CreateHeadlessFpsInput(spawnHeight: 6000f);
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
                await Console.Out.WriteLineAsync($"  {turnRateDeg,-10:F0} {speeds[0],-12:F4} {speeds[1],-12:F4} {speeds[2],-12:F4} {speeds[3],-12:F4} {spread:E2}");

                await Assert.That(speeds[0]).IsGreaterThan(90).Because("strafe produced no speed; the scenario is broken");
            }

            await Assert.That(worstSpread).IsLessThan(SpreadLock).Because("air strafe framerate invariance regressed");
        }

        /// <summary>
        /// Runs a grounded scenario at the given framerate: settle onto the plane, then
        /// hold W for <paramref name="accelSeconds"/>, then release and coast until
        /// stopped. Returns the acceleration distance/end speed and the stop distance.
        /// </summary>
        private async Task<(double AccelDistance, double AccelEndSpeed, double StopDistance)> RunGroundScenario(float fps, float accelSeconds)
        {
            var (input, renderCamera) = await CreateHeadlessFpsInput(spawnHeight: 100f);
            var movement = input.PlayerMovement;
            var dt = 1f / fps;

            await SettleOntoGround(input, renderCamera, fps);
            await Assert.That(HorizontalSpeed(movement.Velocity)).IsLessThan(0.001).Because("player did not come to rest");

            var accelStart = SimulatedFeet(movement);

            for (var i = 0; i < (int)(accelSeconds * fps); i++)
            {
                input.Tick(dt, TrackedKeys.W, Vector2.Zero, renderCamera);
            }

            var releasePoint = SimulatedFeet(movement);
            var accelEndSpeed = HorizontalSpeed(movement.Velocity);

            // Coast to a stop under friction alone
            for (var i = 0; i < (int)(2f * fps) && HorizontalSpeed(movement.Velocity) > 0; i++)
            {
                input.Tick(dt, TrackedKeys.None, Vector2.Zero, renderCamera);
            }

            await Assert.That(HorizontalSpeed(movement.Velocity)).IsZero().Because("player did not stop under friction");

            var stopPoint = SimulatedFeet(movement);

            return (
                double.Hypot(releasePoint.X - accelStart.X, releasePoint.Y - accelStart.Y),
                accelEndSpeed,
                double.Hypot(stopPoint.X - releasePoint.X, stopPoint.Y - releasePoint.Y));
        }

        [Test]
        public async Task GroundMovementIsFramerateInvariant()
        {
            // Regression locks on cross-fps spreads. The simulation runs fixed ticks, so what is
            // left is float noise from the tick accumulator. Lower when precision improves.
            const double AccelDistanceSpreadLock = 1e-4;
            const double AccelSpeedSpreadLock = 1e-5;
            const double StopDistanceSpreadLock = 1e-4;

            // From rest, 2s of W reaches the 250 u/s cap; friction-only stop from the cap
            const float AccelSeconds = 2f;

            var accelDistances = new double[ComparisonFramerates.Length];
            var accelSpeeds = new double[ComparisonFramerates.Length];
            var stopDistances = new double[ComparisonFramerates.Length];

            for (var run = 0; run < ComparisonFramerates.Length; run++)
            {
                (accelDistances[run], accelSpeeds[run], stopDistances[run]) = await RunGroundScenario(ComparisonFramerates[run], AccelSeconds);
            }

            await Console.Out.WriteLineAsync($"Ground movement: {AccelSeconds}s W from rest, then friction-only stop:");
            await Console.Out.WriteLineAsync($"  {"fps",-8} {"accel dist",-14} {"end speed",-14} {"stop dist",-14}");

            for (var run = 0; run < ComparisonFramerates.Length; run++)
            {
                await Console.Out.WriteLineAsync($"  {ComparisonFramerates[run],-8:F0} {accelDistances[run],-14:F5} {accelSpeeds[run],-14:F5} {stopDistances[run],-14:F5}");
            }

            await Console.Out.WriteLineAsync($"  spreads: dist={Spread(accelDistances):E2} speed={Spread(accelSpeeds):E2} stop={Spread(stopDistances):E2}");

            // Sanity anchors: the stop distance of per-tick friction from 250 u/s, moving at each tick's mid velocity
            await Assert.That(accelSpeeds[0]).IsEqualTo(250.0).Within(0.01).Because("did not reach the run speed cap");
            await Assert.That(stopDistances[0]).IsEqualTo(39.016).Within(0.01).Because("stop distance far from the per-tick 39.016");

            await Assert.That(Spread(accelDistances)).IsLessThan(AccelDistanceSpreadLock).Because("ground acceleration distance invariance regressed");
            await Assert.That(Spread(accelSpeeds)).IsLessThan(AccelSpeedSpreadLock).Because("ground speed invariance regressed");
            await Assert.That(Spread(stopDistances)).IsLessThan(StopDistanceSpreadLock).Because("friction stop distance invariance regressed");
        }

        // A single wall turned 45 deg in XY, so its normal is on neither axis. The player walks
        // straight down +X into it and slides along (1,1)/sqrt(2), which puts motion on both axes and
        // makes ClipVelocity's projection inexact; an axis-aligned wall zeroes the normal
        // component exactly and hides any drift off the SurfaceEpsilon standoff.
        private static readonly Vector3 WallNormal = Vector3.Normalize(new Vector3(-1, 1, 0));
        private const float WallContactX = 20f;
        private static readonly float WallExtent = 16f * (MathF.Abs(WallNormal.X) + MathF.Abs(WallNormal.Y));
        private static readonly float WallOffset = (WallNormal.X * WallContactX) - WallExtent;
        private const float SurfaceEpsilon = 0.03125f;

        /// <summary>Perpendicular gap from the hull face to the wall; should rest at SurfaceEpsilon.</summary>
        private static double WallGap(Vector3 position)
            => (WallNormal.X * position.X) + (WallNormal.Y * position.Y) - WallOffset - WallExtent;

        /// <summary>
        /// One wall-slide run: settle, then walk straight down +X into the 45 deg wall for 3s.
        /// Returns the slide displacement on each axis, the resting gap and the end speed.
        /// </summary>
        private async Task<(double SlideX, double SlideY, double Gap, double EndSpeed)> RunWallSlide(float fps)
        {
            var (input, renderCamera) = await CreateHeadlessFpsInput(spawnHeight: 100f);
            var movement = input.PlayerMovement;
            var dt = 1f / fps;

            movement.DebugCollisionPlanes.Add(new Vector4(WallNormal.X, WallNormal.Y, WallNormal.Z, WallOffset));

            await SettleOntoGround(input, renderCamera, fps);

            var start = SimulatedFeet(movement);

            // Straight down +X; the wall deflects the run onto the (1,1)/sqrt(2) diagonal
            input.Camera.Yaw = 0f;

            for (var i = 0; i < (int)(3f * fps); i++)
            {
                input.Tick(dt, TrackedKeys.W, Vector2.Zero, renderCamera);
            }

            var pos = SimulatedFeet(movement);
            return (pos.X - start.X, pos.Y - start.Y, WallGap(pos), HorizontalSpeed(movement.Velocity));
        }

        /// <summary>
        /// Walks straight down +X into a 45 deg wall across framerates and reports how far the player
        /// slides along it, per axis. The slide runs on the (1,1)/sqrt(2) diagonal, so X and Y carry the
        /// dynamics jointly and neither is pinned; the perpendicular gap is the constrained
        /// direction and should hold at SurfaceEpsilon. Exercises GroundMove, and because the wall
        /// normal is off-axis the velocity clip leaves a rounding residual every frame; the
        /// cross-fps spread per axis is the invariance signal.
        /// </summary>
        [Test]
        public async Task WallSlideComparison()
        {
            var slideX = new double[ComparisonFramerates.Length];
            var slideY = new double[ComparisonFramerates.Length];
            var gap = new double[ComparisonFramerates.Length];
            var endSpeed = new double[ComparisonFramerates.Length];

            for (var run = 0; run < ComparisonFramerates.Length; run++)
            {
                (slideX[run], slideY[run], gap[run], endSpeed[run]) = await RunWallSlide(ComparisonFramerates[run]);
            }

            await Console.Out.WriteLineAsync($"Straight +X into a 45° wall (contact at x={WallContactX}, 3s hold W):");
            await Console.Out.WriteLineAsync($"  {"fps",-8} {"slide X",-16} {"slide Y",-16} {"gap",-14} {"end speed",-14}");

            for (var run = 0; run < ComparisonFramerates.Length; run++)
            {
                await Console.Out.WriteLineAsync(
                    $"  {ComparisonFramerates[run],-8:F0} {slideX[run],-16:F5} {slideY[run],-16:F5}"
                    + $" {gap[run],-14:F8} {endSpeed[run],-14:F5}");
            }

            await Console.Out.WriteLineAsync(
                $"  spreads: slideX={Spread(slideX):E2} slideY={Spread(slideY):E2}"
                + $" gap={Spread(gap):E2} endSpeed={Spread(endSpeed):E2}");
            await Console.Out.WriteLineAsync($"  gap should rest at SurfaceEpsilon = {SurfaceEpsilon}");

            foreach (var g in gap)
            {
                await Assert.That(g).IsGreaterThan(-SurfaceEpsilon).Because("player passed through the wall");
            }

            await Assert.That(slideX[0]).IsGreaterThan(1f).Because("player did not slide along the wall");
            await Assert.That(slideY[0]).IsGreaterThan(1f).Because("wall did not deflect the run onto its diagonal");

            // Locks: slide distance carries the documented high-fps lookahead outlier (measured
            // 4.1e-1), so its lock only catches something qualitatively worse; the resting gap
            // and end speed are tight (measured 3.1e-5 and 0.0)
            await Assert.That(Spread(slideX)).IsLessThan(8e-1).Because("wall slide X distance invariance regressed");
            await Assert.That(Spread(slideY)).IsLessThan(8e-1).Because("wall slide Y distance invariance regressed");
            await Assert.That(Spread(gap)).IsLessThan(1e-4).Because("wall resting gap invariance regressed");
            await Assert.That(Spread(endSpeed)).IsLessThan(1e-3).Because("wall slide end speed invariance regressed");
        }

        // Headless traces always include the infinite z=0 ground plane, so the ramp has to be
        // lifted clear of it for a 3s descent to stay on the ramp rather than end on the floor
        private const float SurfSpawnHeight = 3000f;
        private const float SurfDropHeight = 10f;

        /// <summary>
        /// One surf run: drop onto an inclined plane whose face starts <see cref="SurfDropHeight"/>
        /// units below the feet and hold A for 3 seconds, with the view yawed 10 deg right so the
        /// strafe converts into speed along the ramp instead of straight into it. The ramp is
        /// tilted about Y, so its downhill direction is -X and the wish input is nearly across it.
        /// Returns the horizontal travel, the end speed, how far the player descended and whether
        /// the mover ever considered the surface walkable ground.
        /// </summary>
        private async Task<(double Travel, double EndSpeed, double Descent, bool Grounded, double WishDot)> RunSurf(float fps)
        {
            var (input, renderCamera) = await CreateHeadlessFpsInput(spawnHeight: SurfSpawnHeight);
            var movement = input.PlayerMovement;
            var dt = 1f / fps;

            // Outward normal of a plane inclined by SurfSlopeDegrees, tilted about Y; solid is dot(n, x) <= d
            var alpha = float.DegreesToRadians(SurfSlopeDegrees);
            var n = new Vector3(-MathF.Sin(alpha), 0f, MathF.Cos(alpha));

            // Place the face SurfDropHeight below the hull, measured perpendicular to it. Offsetting
            // the spawn straight down instead would embed the hull once the ramp is tilted: the
            // box's support half-width along a tilted normal is much larger than its half-height.
            var half = movement.HullHalfExtents;
            var center = movement.HullCenter;
            var extent = (MathF.Abs(n.X) * half.X) + (MathF.Abs(n.Y) * half.Y) + (MathF.Abs(n.Z) * half.Z);
            var d = Vector3.Dot(n, center) - extent - SurfDropHeight;

            movement.DebugCollisionPlanes.Add(new Vector4(n.X, n.Y, n.Z, d));

            // 10 deg to the right of +X. Forward is (cos yaw, sin yaw); right is yaw - 90 deg, so
            // looking right is a negative yaw step.
            var yaw = float.DegreesToRadians(-10f);
            input.Camera.Yaw = yaw;

            // Holding A is sideMove = -RunSpeed, so the wish points along -right (CalculateWishVelocity)
            var wishdir = new Vector3(-MathF.Sin(yaw), MathF.Cos(yaw), 0f);

            var start = SimulatedFeet(movement);
            var grounded = false;

            for (var i = 0; i < (int)(3f * fps); i++)
            {
                input.Tick(dt, TrackedKeys.A, Vector2.Zero, renderCamera);
                grounded |= movement.OnGround;
            }

            var end = SimulatedFeet(movement);

            return (
                double.Hypot(end.X - start.X, end.Y - start.Y),
                HorizontalSpeed(movement.Velocity),
                start.Z - end.Z,
                grounded,
                Vector3.Dot(wishdir, movement.Velocity));
        }

        // Steep enough to actually surf: WalkableSlope is 0.7, so everything up to 45.573 deg counts
        // as standable ground and would be walked down through GroundMove instead. Past the
        // threshold the ramp never grounds the player and the run stays on AirMove/TryPlayerMove,
        // which is the path this locks. The grounded column is the guard on that staying true.
        private const float SurfSlopeDegrees = 50f;

        [Test]
        public async Task SurfIsFramerateInvariant()
        {
            // Regression locks; the fixed tick makes every framerate bit-identical over a ~1780u run
            const double TravelSpreadLock = 1e-3;
            const double EndSpeedSpreadLock = 1e-3;
            const double DescentSpreadLock = 1e-3;
            const double WishDotSpreadLock = 1e-3;

            // The strafe gate saturates near AirMaxWishSpeed (30) when surfing works. The bug this
            // guards against (the post-contact strafe gain being recomputed and then dropped)
            // pushed it to about -175 at ordinary framerates while 1000 fps stayed healthy, so it
            // is asserted per framerate, not on the spread.
            const double WishDotFloor = 20.0;

            var travel = new double[ComparisonFramerates.Length];
            var endSpeed = new double[ComparisonFramerates.Length];
            var descent = new double[ComparisonFramerates.Length];
            var grounded = new bool[ComparisonFramerates.Length];
            var wishDot = new double[ComparisonFramerates.Length];

            for (var run = 0; run < ComparisonFramerates.Length; run++)
            {
                (travel[run], endSpeed[run], descent[run], grounded[run], wishDot[run]) = await RunSurf(ComparisonFramerates[run]);
            }

            await Console.Out.WriteLineAsync($"{SurfSlopeDegrees}° ramp, dropped {SurfDropHeight}u above it, 3s holding A with the view 10° right:");
            await Console.Out.WriteLineAsync($"  {"fps",-8} {"travel",-16} {"end speed",-14} {"descent",-14} {"v·wishdir",-12} {"grounded",-9}");

            for (var run = 0; run < ComparisonFramerates.Length; run++)
            {
                await Console.Out.WriteLineAsync(
                    $"  {ComparisonFramerates[run],-8:F0} {travel[run],-16:F5} {endSpeed[run],-14:F5}"
                    + $" {descent[run],-14:F5} {wishDot[run],-12:F5} {grounded[run],-9}");
            }

            await Console.Out.WriteLineAsync(
                $"  spreads: travel={Spread(travel):E2} endSpeed={Spread(endSpeed):E2}"
                + $" descent={Spread(descent):E2} wishDot={Spread(wishDot):E2}");
            await Console.Out.WriteLineAsync("  (AirMaxWishSpeed is 30; the addspeed gate stops adding once v·wishdir reaches it)");

            await Assert.That(travel[0]).IsGreaterThan(1f).Because("player did not move along the ramp; the scenario is broken");

            foreach (var g in grounded)
            {
                await Assert.That(g).IsFalse().Because("ramp was treated as walkable ground; this is no longer a surf test");
            }

            foreach (var w in wishDot)
            {
                await Assert.That(w).IsGreaterThan(WishDotFloor).Because("strafe input lost against the ramp; surfing is broken at this framerate");
            }

            await Assert.That(Spread(travel)).IsLessThan(TravelSpreadLock).Because("surf travel framerate invariance regressed");
            await Assert.That(Spread(endSpeed)).IsLessThan(EndSpeedSpreadLock).Because("surf end speed framerate invariance regressed");
            await Assert.That(Spread(descent)).IsLessThan(DescentSpreadLock).Because("surf descent framerate invariance regressed");
            await Assert.That(Spread(wishDot)).IsLessThan(WishDotSpreadLock).Because("surf wish gate framerate invariance regressed");
        }

        /// <summary>
        /// One overhang-bounce run: settle, jump straight up into a 45 deg overhang, then fly until
        /// landing and slide out the remaining speed. Returns the total horizontal distance from
        /// launch to the resting position, and the apex height reached.
        /// </summary>
        private async Task<(double Horizontal, double Apex, bool Landed)> RunOverhangBounce(float fps)
        {
            var (input, renderCamera) = await CreateHeadlessFpsInput(spawnHeight: 100f);
            var movement = input.PlayerMovement;
            var dt = 1f / fps;

            // 45 deg overhang above the player: solid where dot(n, x) <= d, n pointing down and +x.
            // Offset chosen so the plane is clear of the spawn/fall column (hull center ~72)
            // yet gets struck mid-jump (contact near hull center ~82, below the ~93 apex)
            var n = Vector3.Normalize(new Vector3(1, 0, -1));
            movement.DebugCollisionPlanes.Add(new Vector4(n.X, n.Y, n.Z, -95f));

            await SettleOntoGround(input, renderCamera, fps);

            var launch = SimulatedFeet(movement);

            // Jump held (AutoBunnyHop) launches straight up on the next tick, which a frame may not reach
            for (var i = 0; i <= (int)(fps / 64f) && movement.OnGround; i++)
            {
                input.Tick(dt, TrackedKeys.Space, Vector2.Zero, renderCamera);
            }

            await Assert.That(movement.OnGround).IsFalse().Because("jump did not leave the ground");

            var apexZ = SimulatedFeet(movement).Z;
            var frames = (int)(4f * fps);

            var landed = false;

            for (var i = 0; i < frames; i++)
            {
                input.Tick(dt, TrackedKeys.None, Vector2.Zero, renderCamera);
                apexZ = MathF.Max(apexZ, SimulatedFeet(movement).Z);

                if (movement.OnGround)
                {
                    landed = true;
                    break;
                }
            }

            // Landing itself is sampled on the frame the ground snap fires, which credits a whole
            // frame of horizontal travel however little of it was actually airborne. Let friction
            // bring the slide to a full stop instead: the stopping distance is integrated
            // analytically, so the measurement lands on a physical rest position rather than on
            // whichever frame boundary happened to catch the ground.
            var settleFrames = (int)(3f * fps);

            for (var i = 0; i < settleFrames && landed; i++)
            {
                input.Tick(dt, TrackedKeys.None, Vector2.Zero, renderCamera);

                if (HorizontalSpeed(movement.Velocity) < 0.01f)
                {
                    break;
                }
            }

            var land = SimulatedFeet(movement);
            return (double.Hypot(land.X - launch.X, land.Y - launch.Y), apexZ, landed && movement.OnGround);
        }

        /// <summary>
        /// Jumps straight up into a 45 deg overhang across framerates and measures how far the
        /// deflection flings the player, from launch to where the post-landing slide comes to
        /// rest. Measuring at rest rather than on the landing frame keeps the result off the
        /// ground snap, which credits a whole frame of horizontal travel regardless of how much
        /// of that frame was actually spent airborne. The cross-fps spread is the invariance
        /// signal for the bounce in the air mover.
        /// </summary>
        [Test]
        public async Task OverhangJumpBounceComparison()
        {
            var horizontal = new double[ComparisonFramerates.Length];
            var apex = new double[ComparisonFramerates.Length];

            for (var run = 0; run < ComparisonFramerates.Length; run++)
            {
                var (h, a, landed) = await RunOverhangBounce(ComparisonFramerates[run]);
                horizontal[run] = h;
                apex[run] = a;
                await Assert.That(landed).IsTrue().Because("player never landed");
            }

            await Console.Out.WriteLineAsync("45° overhang jump bounce (jump into normal (1,0,-1)):");
            await Console.Out.WriteLineAsync($"  {"fps",-8} {"horizontal",-16} {"apex",-14}");

            for (var run = 0; run < ComparisonFramerates.Length; run++)
            {
                await Console.Out.WriteLineAsync($"  {ComparisonFramerates[run],-8:F0} {horizontal[run],-16:F5} {apex[run],-14:F5}");
            }

            await Console.Out.WriteLineAsync($"  spreads: horizontal={Spread(horizontal):E2} apex={Spread(apex):E2}");

            await Assert.That(horizontal[0]).IsGreaterThan(1.0).Because("overhang did not deflect the jump horizontally");

            // Locks; the fixed tick makes every framerate bit-identical
            await Assert.That(Spread(horizontal)).IsLessThan(1e-3).Because("overhang bounce invariance regressed");
            await Assert.That(Spread(apex)).IsLessThan(1e-3).Because("overhang apex invariance regressed");
        }

        [Test]
        public async Task GroundDuckLowersEyesBeforeHull()
        {
            const float Fps = 128f;
            var (input, renderCamera) = await CreateHeadlessFpsInput(100f);
            await SettleOntoGround(input, renderCamera, Fps);

            var movement = input.PlayerMovement;
            var feet = movement.Position.Z;

            for (var i = 0; i < 10; i++)
            {
                input.Tick(1f / Fps, TrackedKeys.Control, Vector2.Zero, renderCamera);
            }

            await Assert.That(movement.CrouchBlend).IsGreaterThan(0f).And.IsLessThan(1f);
            await Assert.That(movement.HullHalfExtents.Z).IsEqualTo(36f).Because("hull switched before the duck finished");
            await Assert.That(movement.Position.Z).IsEqualTo(feet).Within(1e-3f);
            await Assert.That(movement.BlendedEyeHeight).IsLessThan(64f).And.IsGreaterThan(46f);

            for (var i = 0; i < 30; i++)
            {
                input.Tick(1f / Fps, TrackedKeys.Control, Vector2.Zero, renderCamera);
            }

            await Assert.That(movement.CrouchBlend).IsEqualTo(1f);
            await Assert.That(movement.HullHalfExtents.Z).IsEqualTo(27f);
            await Assert.That(movement.Position.Z).IsEqualTo(feet).Within(1e-3f);
            await Assert.That(movement.BlendedEyeHeight).IsEqualTo(46f).Within(1e-3f);
        }

        [Test]
        public async Task AirDuckTucksLegs()
        {
            const float Fps = 128f;
            var feet = new float[2];
            var eye = new float[2];

            for (var run = 0; run < 2; run++)
            {
                var (input, renderCamera) = await CreateHeadlessFpsInput(1000f);
                var keys = run == 1 ? TrackedKeys.Control : TrackedKeys.None;

                for (var i = 0; i < 64; i++)
                {
                    input.Tick(1f / Fps, keys, Vector2.Zero, renderCamera);
                }

                var movement = input.PlayerMovement;
                await Assert.That(movement.OnGround).IsFalse();
                await Assert.That(movement.HullHalfExtents.Z).IsEqualTo(run == 1 ? 27f : 36f);

                feet[run] = movement.Position.Z;
                eye[run] = movement.EyePosition.Z;
            }

            // Ducking in the air raises the feet by half the height difference and lowers the eyes by the rest
            await Assert.That(feet[1] - feet[0]).IsEqualTo(9f).Within(1e-2f);
            await Assert.That(eye[1] - eye[0]).IsEqualTo(-9f).Within(1e-2f);
        }
    }
}
