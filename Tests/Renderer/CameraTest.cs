using System.Linq;
using System.Threading.Tasks;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Utils;

namespace Tests.Renderer
{
    /// <summary>
    /// The camera holds pitch and yaw the way the engine does, and derives its direction vectors from them
    /// analytically. These pin that the two agree, and that the derivation survives looking straight down,
    /// which is where an euler representation is usually expected to give out.
    /// </summary>
    public class CameraTest
    {
        private const float Tolerance = 1e-4f;

        private static readonly Vector3[] Angles =
        [
            new(0f, 0f, 0f),
            new(30f, 45f, 0f),
            new(-30f, 135f, 0f),
            new(15f, -160f, 0f),
            new(-60f, 250f, 0f),
            new(89f, 10f, 0f),
            new(-89f, -10f, 0f),
        ];

        private static Camera MakeCamera(Vector3 anglesDegrees)
        {
            var camera = new Camera();
            camera.SetFromQAngle(anglesDegrees);
            camera.RecalculateDirectionVectors();

            return camera;
        }

        /// <summary>
        /// The camera and an entity handed the same angles have to face the same way, or anything driven
        /// by a map entity points somewhere else.
        /// </summary>
        [Test]
        public async Task ForwardMatchesTheEntityConventionForTheSameAngles()
        {
            foreach (var angles in Angles)
            {
                var camera = MakeCamera(angles);
                var expected = EntityTransformHelper.EulerAnglesToForwardDirection(angles);

                await Assert.That(camera.Forward.X).IsEqualTo(expected.X).Within(Tolerance).Because($"X for {angles}");
                await Assert.That(camera.Forward.Y).IsEqualTo(expected.Y).Within(Tolerance).Because($"Y for {angles}");
                await Assert.That(camera.Forward.Z).IsEqualTo(expected.Z).Within(Tolerance).Because($"Z for {angles}");
            }
        }

        /// <summary>
        /// A QAngle put in is the QAngle that comes back, so the boundary cannot quietly gain a sign.
        /// </summary>
        [Test]
        public async Task GetQAngleInvertsSetFromQAngle()
        {
            foreach (var angles in Angles.Append(new Vector3(15f, -70f, 25f)))
            {
                var roundTripped = MakeCamera(angles).GetQAngle();

                await Assert.That(roundTripped.X).IsEqualTo(angles.X).Within(1e-3f).Because($"pitch for {angles}");
                await Assert.That(NormalizeDegrees(roundTripped.Y)).IsEqualTo(NormalizeDegrees(angles.Y)).Within(1e-3f).Because($"yaw for {angles}");
                await Assert.That(roundTripped.Z).IsEqualTo(angles.Z).Within(1e-3f).Because($"roll for {angles}");
            }
        }

        /// <summary>
        /// Angles that carry no roll level the camera, rather than leaving whatever roll was there to show
        /// through. A viewer opened while the view is punched used to inherit that punch for a frame.
        /// </summary>
        [Test]
        public async Task SetFromQAngleClearsAnExistingRoll()
        {
            var camera = new Camera { Roll = float.DegreesToRadians(20f) };

            camera.SetFromQAngle(new Vector3(10f, 30f, 0f));

            await Assert.That(camera.Roll).IsEqualTo(0f).Within(Tolerance);
        }

        /// <summary>Positive pitch looks down, as in a QAngle and unlike most camera conventions.</summary>
        [Test]
        public async Task PositivePitchLooksDown()
        {
            await Assert.That(MakeCamera(new Vector3(45f, 0f, 0f)).Forward.Z).IsLessThan(0f);
            await Assert.That(MakeCamera(new Vector3(-45f, 0f, 0f)).Forward.Z).IsGreaterThan(0f);
        }

        [Test]
        public async Task DirectionVectorsAreOrthonormal()
        {
            foreach (var angles in Angles)
            {
                var camera = MakeCamera(angles);

                await AssertOrthonormal(camera, $"{angles}");
            }
        }

        /// <summary>
        /// Straight up and straight down are the interesting ones: yaw and roll turn about the same axis
        /// there, so recovering angles from a direction cannot tell them apart. Going the other way, which
        /// is what the camera does, has no such problem, and the frame stays a frame.
        /// </summary>
        [Test]
        public async Task DirectionVectorsSurviveLookingStraightUpAndDown()
        {
            foreach (var pitch in new[] { 90f, -90f })
            {
                foreach (var yaw in new[] { 0f, 37f, 180f, -120f })
                {
                    var camera = MakeCamera(new Vector3(pitch, yaw, 0f));
                    var context = $"pitch {pitch} yaw {yaw}";

                    await AssertOrthonormal(camera, context);

                    // Forward is vertical, and Up and Right have swung into the horizontal plane with it
                    await Assert.That(MathF.Abs(camera.Forward.Z)).IsEqualTo(1f).Within(Tolerance).Because($"forward is vertical for {context}");
                    await Assert.That(camera.Up.Z).IsEqualTo(0f).Within(Tolerance).Because($"up is horizontal for {context}");
                    await Assert.That(camera.Right.Z).IsEqualTo(0f).Within(Tolerance).Because($"right is horizontal for {context}");
                }
            }
        }

        /// <summary>Vertical is reachable, so the clamp is about not tipping over rather than about the maths.</summary>
        [Test]
        public async Task ClampRotationAllowsLookingStraightDown()
        {
            var camera = new Camera
            {
                Pitch = float.DegreesToRadians(120f),
            };

            camera.ClampRotation();

            await Assert.That(float.RadiansToDegrees(camera.Pitch)).IsEqualTo(90f).Within(1e-3f);

            camera.Pitch = float.DegreesToRadians(-120f);
            camera.ClampRotation();

            await Assert.That(float.RadiansToDegrees(camera.Pitch)).IsEqualTo(-90f).Within(1e-3f);
        }

        /// <summary>
        /// Yaw accumulates without bound from mouse look, and the trig that reads it loses its meaning
        /// long before a session ends, so it is kept to one turn.
        /// </summary>
        [Test]
        public async Task ClampRotationKeepsYawToOneTurn()
        {
            // A fresh camera is framed on the origin, so pitch has to be levelled to isolate yaw here
            var camera = new Camera
            {
                Pitch = 0f,
                Yaw = float.DegreesToRadians(4000f),
            };

            camera.ClampRotation();

            await Assert.That(MathF.Abs(camera.Yaw)).IsLessThanOrEqualTo(MathF.PI + Tolerance);

            // Same direction it was pointing before the wrap
            camera.RecalculateDirectionVectors();
            var wrapped = camera.Forward;
            var expected = EntityTransformHelper.EulerAnglesToForwardDirection(new Vector3(0f, 4000f, 0f));

            await Assert.That(wrapped.X).IsEqualTo(expected.X).Within(1e-3f);
            await Assert.That(wrapped.Y).IsEqualTo(expected.Y).Within(1e-3f);
        }

        /// <summary>
        /// Roll turns Up and Right about Forward, leaving the look direction alone. Pinned against the
        /// entity rotation for the same angles, which fixes the direction it turns: an angle alone would
        /// be satisfied by rolling either way.
        /// </summary>
        [Test]
        public async Task RollTurnsUpAndRightButNotForward()
        {
            var angles = new Vector3(20f, 50f, 30f);
            var upright = MakeCamera(new Vector3(angles.X, angles.Y, 0f));

            var rolled = new Camera();
            rolled.SetFromQAngle(angles);
            rolled.Roll = float.DegreesToRadians(angles.Z);
            rolled.RecalculateDirectionVectors();

            await Assert.That(rolled.Forward.X).IsEqualTo(upright.Forward.X).Within(Tolerance);
            await Assert.That(rolled.Forward.Y).IsEqualTo(upright.Forward.Y).Within(Tolerance);
            await Assert.That(rolled.Forward.Z).IsEqualTo(upright.Forward.Z).Within(Tolerance);

            // The entity rotation's third row is up, so a roll in the wrong direction fails here
            var entity = EntityTransformHelper.EulerAnglesToRotationMatrix(angles);
            var entityUp = new Vector3(entity.M31, entity.M32, entity.M33);

            await Assert.That(rolled.Up.X).IsEqualTo(entityUp.X).Within(1e-3f).Because("up X");
            await Assert.That(rolled.Up.Y).IsEqualTo(entityUp.Y).Within(1e-3f).Because("up Y");
            await Assert.That(rolled.Up.Z).IsEqualTo(entityUp.Z).Within(1e-3f).Because("up Z");

            await AssertOrthonormal(rolled, "rolled");
        }

        /// <summary>
        /// The view matrix is where looking straight down could have bitten, since it completes its own
        /// basis with a cross product against the up it is handed.
        /// </summary>
        [Test]
        public async Task ViewMatrixSurvivesLookingStraightDown()
        {
            foreach (var pitch in new[] { 90f, -90f, 0f, 45f })
            {
                var camera = new Camera { Location = new Vector3(5f, 6f, 7f) };
                camera.SetFromQAngle(new Vector3(pitch, 25f, 0f));
                camera.RecalculateMatrices();

                var view = camera.CameraViewMatrix;
                var context = $"pitch {pitch}";

                await Assert.That(float.IsFinite(view.M11) && float.IsFinite(view.M22) && float.IsFinite(view.M33)).IsTrue().Because($"view matrix is finite for {context}");

                // A point one unit ahead lands one unit down the view's -Z, which is where it looks
                var ahead = Vector3.Transform(camera.Location + camera.Forward, view);

                await Assert.That(ahead.X).IsEqualTo(0f).Within(1e-3f).Because($"ahead X for {context}");
                await Assert.That(ahead.Y).IsEqualTo(0f).Within(1e-3f).Because($"ahead Y for {context}");
                await Assert.That(ahead.Z).IsEqualTo(-1f).Within(1e-3f).Because($"ahead Z for {context}");
            }
        }

        /// <summary>Right points along -Y at zero yaw, which is right in a world where +Y is left.</summary>
        [Test]
        public async Task RightPointsAlongNegativeYAtZeroYaw()
        {
            var camera = MakeCamera(Vector3.Zero);

            await Assert.That(camera.Right.X).IsEqualTo(0f).Within(Tolerance);
            await Assert.That(camera.Right.Y).IsEqualTo(-1f).Within(Tolerance);
            await Assert.That(camera.Right.Z).IsEqualTo(0f).Within(Tolerance);
        }

        [Test]
        public async Task LookAtFacesTheTargetAndSurvivesLookingAtItself()
        {
            var camera = new Camera { Location = new Vector3(10f, 20f, 30f) };
            camera.LookAt(new Vector3(10f, 20f, 0f));
            camera.RecalculateDirectionVectors();

            await Assert.That(camera.Forward.Z).IsEqualTo(-1f).Within(1e-3f).Because("looks straight down at the target");

            // A target on top of the camera says nothing about where to face, but must not produce NaN
            camera.LookAt(camera.Location);

            await Assert.That(float.IsNaN(camera.Pitch)).IsFalse();
            await Assert.That(float.IsNaN(camera.Yaw)).IsFalse();
        }

        private static async Task AssertOrthonormal(Camera camera, string context)
        {
            await Assert.That(camera.Forward.Length()).IsEqualTo(1f).Within(Tolerance).Because($"forward length for {context}");
            await Assert.That(camera.Up.Length()).IsEqualTo(1f).Within(Tolerance).Because($"up length for {context}");
            await Assert.That(camera.Right.Length()).IsEqualTo(1f).Within(Tolerance).Because($"right length for {context}");

            await Assert.That(Vector3.Dot(camera.Forward, camera.Up)).IsEqualTo(0f).Within(Tolerance).Because($"forward/up for {context}");
            await Assert.That(Vector3.Dot(camera.Forward, camera.Right)).IsEqualTo(0f).Within(Tolerance).Because($"forward/right for {context}");
            await Assert.That(Vector3.Dot(camera.Up, camera.Right)).IsEqualTo(0f).Within(Tolerance).Because($"up/right for {context}");

            // Handedness, which lengths and dot products alone cannot see: a negated Right satisfies all
            // six assertions above, and negating it is the mistake the hand derivation invites
            var cross = Vector3.Cross(camera.Forward, camera.Up);

            await Assert.That(cross.X).IsEqualTo(camera.Right.X).Within(Tolerance).Because($"right is Cross(forward, up), X, for {context}");
            await Assert.That(cross.Y).IsEqualTo(camera.Right.Y).Within(Tolerance).Because($"right is Cross(forward, up), Y, for {context}");
            await Assert.That(cross.Z).IsEqualTo(camera.Right.Z).Within(Tolerance).Because($"right is Cross(forward, up), Z, for {context}");
        }

        private static float NormalizeDegrees(float degrees)
        {
            var wrapped = degrees % 360f;

            return wrapped < 0f ? wrapped + 360f : wrapped;
        }
    }
}
