using System.Linq;
using NUnit.Framework;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Utils;

namespace Tests.Renderer
{
    /// <summary>
    /// The camera holds pitch and yaw the way the engine does, and derives its direction vectors from them
    /// analytically. These pin that the two agree, and that the derivation survives looking straight down,
    /// which is where an euler representation is usually expected to give out.
    /// </summary>
    [TestFixture]
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
        public void ForwardMatchesTheEntityConventionForTheSameAngles()
        {
            foreach (var angles in Angles)
            {
                var camera = MakeCamera(angles);
                var expected = EntityTransformHelper.EulerAnglesToForwardDirection(angles);

                Assert.That(camera.Forward.X, Is.EqualTo(expected.X).Within(Tolerance), $"X for {angles}");
                Assert.That(camera.Forward.Y, Is.EqualTo(expected.Y).Within(Tolerance), $"Y for {angles}");
                Assert.That(camera.Forward.Z, Is.EqualTo(expected.Z).Within(Tolerance), $"Z for {angles}");
            }
        }

        /// <summary>
        /// A QAngle put in is the QAngle that comes back, so the boundary cannot quietly gain a sign.
        /// </summary>
        [Test]
        public void GetQAngleInvertsSetFromQAngle()
        {
            foreach (var angles in Angles.Append(new Vector3(15f, -70f, 25f)))
            {
                var roundTripped = MakeCamera(angles).GetQAngle();

                Assert.That(roundTripped.X, Is.EqualTo(angles.X).Within(1e-3f), $"pitch for {angles}");
                Assert.That(NormalizeDegrees(roundTripped.Y), Is.EqualTo(NormalizeDegrees(angles.Y)).Within(1e-3f), $"yaw for {angles}");
                Assert.That(roundTripped.Z, Is.EqualTo(angles.Z).Within(1e-3f), $"roll for {angles}");
            }
        }

        /// <summary>
        /// Angles that carry no roll level the camera, rather than leaving whatever roll was there to show
        /// through. A viewer opened while the view is punched used to inherit that punch for a frame.
        /// </summary>
        [Test]
        public void SetFromQAngleClearsAnExistingRoll()
        {
            var camera = new Camera { Roll = float.DegreesToRadians(20f) };

            camera.SetFromQAngle(new Vector3(10f, 30f, 0f));

            Assert.That(camera.Roll, Is.EqualTo(0f).Within(Tolerance));
        }

        /// <summary>Positive pitch looks down, as in a QAngle and unlike most camera conventions.</summary>
        [Test]
        public void PositivePitchLooksDown()
        {
            Assert.That(MakeCamera(new Vector3(45f, 0f, 0f)).Forward.Z, Is.LessThan(0f));
            Assert.That(MakeCamera(new Vector3(-45f, 0f, 0f)).Forward.Z, Is.GreaterThan(0f));
        }

        [Test]
        public void DirectionVectorsAreOrthonormal()
        {
            foreach (var angles in Angles)
            {
                var camera = MakeCamera(angles);

                AssertOrthonormal(camera, $"{angles}");
            }
        }

        /// <summary>
        /// Straight up and straight down are the interesting ones: yaw and roll turn about the same axis
        /// there, so recovering angles from a direction cannot tell them apart. Going the other way, which
        /// is what the camera does, has no such problem, and the frame stays a frame.
        /// </summary>
        [Test]
        public void DirectionVectorsSurviveLookingStraightUpAndDown()
        {
            foreach (var pitch in new[] { 90f, -90f })
            {
                foreach (var yaw in new[] { 0f, 37f, 180f, -120f })
                {
                    var camera = MakeCamera(new Vector3(pitch, yaw, 0f));
                    var context = $"pitch {pitch} yaw {yaw}";

                    AssertOrthonormal(camera, context);

                    // Forward is vertical, and Up and Right have swung into the horizontal plane with it
                    Assert.That(MathF.Abs(camera.Forward.Z), Is.EqualTo(1f).Within(Tolerance), $"forward is vertical for {context}");
                    Assert.That(camera.Up.Z, Is.EqualTo(0f).Within(Tolerance), $"up is horizontal for {context}");
                    Assert.That(camera.Right.Z, Is.EqualTo(0f).Within(Tolerance), $"right is horizontal for {context}");
                }
            }
        }

        /// <summary>Vertical is reachable, so the clamp is about not tipping over rather than about the maths.</summary>
        [Test]
        public void ClampRotationAllowsLookingStraightDown()
        {
            var camera = new Camera
            {
                Pitch = float.DegreesToRadians(120f),
            };

            camera.ClampRotation();

            Assert.That(float.RadiansToDegrees(camera.Pitch), Is.EqualTo(90f).Within(1e-3f));

            camera.Pitch = float.DegreesToRadians(-120f);
            camera.ClampRotation();

            Assert.That(float.RadiansToDegrees(camera.Pitch), Is.EqualTo(-90f).Within(1e-3f));
        }

        /// <summary>
        /// Yaw accumulates without bound from mouse look, and the trig that reads it loses its meaning
        /// long before a session ends, so it is kept to one turn.
        /// </summary>
        [Test]
        public void ClampRotationKeepsYawToOneTurn()
        {
            // A fresh camera is framed on the origin, so pitch has to be levelled to isolate yaw here
            var camera = new Camera
            {
                Pitch = 0f,
                Yaw = float.DegreesToRadians(4000f),
            };

            camera.ClampRotation();

            Assert.That(MathF.Abs(camera.Yaw), Is.LessThanOrEqualTo(MathF.PI + Tolerance));

            // Same direction it was pointing before the wrap
            camera.RecalculateDirectionVectors();
            var wrapped = camera.Forward;
            var expected = EntityTransformHelper.EulerAnglesToForwardDirection(new Vector3(0f, 4000f, 0f));

            Assert.That(wrapped.X, Is.EqualTo(expected.X).Within(1e-3f));
            Assert.That(wrapped.Y, Is.EqualTo(expected.Y).Within(1e-3f));
        }

        /// <summary>
        /// Roll turns Up and Right about Forward, leaving the look direction alone. Pinned against the
        /// entity rotation for the same angles, which fixes the direction it turns: an angle alone would
        /// be satisfied by rolling either way.
        /// </summary>
        [Test]
        public void RollTurnsUpAndRightButNotForward()
        {
            var angles = new Vector3(20f, 50f, 30f);
            var upright = MakeCamera(new Vector3(angles.X, angles.Y, 0f));

            var rolled = new Camera();
            rolled.SetFromQAngle(angles);
            rolled.Roll = float.DegreesToRadians(angles.Z);
            rolled.RecalculateDirectionVectors();

            Assert.That(rolled.Forward.X, Is.EqualTo(upright.Forward.X).Within(Tolerance));
            Assert.That(rolled.Forward.Y, Is.EqualTo(upright.Forward.Y).Within(Tolerance));
            Assert.That(rolled.Forward.Z, Is.EqualTo(upright.Forward.Z).Within(Tolerance));

            // The entity rotation's third row is up, so a roll in the wrong direction fails here
            var entity = EntityTransformHelper.EulerAnglesToRotationMatrix(angles);
            var entityUp = new Vector3(entity.M31, entity.M32, entity.M33);

            Assert.That(rolled.Up.X, Is.EqualTo(entityUp.X).Within(1e-3f), "up X");
            Assert.That(rolled.Up.Y, Is.EqualTo(entityUp.Y).Within(1e-3f), "up Y");
            Assert.That(rolled.Up.Z, Is.EqualTo(entityUp.Z).Within(1e-3f), "up Z");

            AssertOrthonormal(rolled, "rolled");
        }

        /// <summary>
        /// The view matrix is where looking straight down could have bitten, since it completes its own
        /// basis with a cross product against the up it is handed.
        /// </summary>
        [Test]
        public void ViewMatrixSurvivesLookingStraightDown()
        {
            foreach (var pitch in new[] { 90f, -90f, 0f, 45f })
            {
                var camera = new Camera { Location = new Vector3(5f, 6f, 7f) };
                camera.SetFromQAngle(new Vector3(pitch, 25f, 0f));
                camera.RecalculateMatrices();

                var view = camera.CameraViewMatrix;
                var context = $"pitch {pitch}";

                Assert.That(float.IsFinite(view.M11) && float.IsFinite(view.M22) && float.IsFinite(view.M33),
                    Is.True, $"view matrix is finite for {context}");

                // A point one unit ahead lands one unit down the view's -Z, which is where it looks
                var ahead = Vector3.Transform(camera.Location + camera.Forward, view);

                Assert.That(ahead.X, Is.EqualTo(0f).Within(1e-3f), $"ahead X for {context}");
                Assert.That(ahead.Y, Is.EqualTo(0f).Within(1e-3f), $"ahead Y for {context}");
                Assert.That(ahead.Z, Is.EqualTo(-1f).Within(1e-3f), $"ahead Z for {context}");
            }
        }

        /// <summary>Right points along -Y at zero yaw, which is right in a world where +Y is left.</summary>
        [Test]
        public void RightPointsAlongNegativeYAtZeroYaw()
        {
            var camera = MakeCamera(Vector3.Zero);

            Assert.That(camera.Right.X, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(camera.Right.Y, Is.EqualTo(-1f).Within(Tolerance));
            Assert.That(camera.Right.Z, Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void LookAtFacesTheTargetAndSurvivesLookingAtItself()
        {
            var camera = new Camera { Location = new Vector3(10f, 20f, 30f) };
            camera.LookAt(new Vector3(10f, 20f, 0f));
            camera.RecalculateDirectionVectors();

            Assert.That(camera.Forward.Z, Is.EqualTo(-1f).Within(1e-3f), "looks straight down at the target");

            // A target on top of the camera says nothing about where to face, but must not produce NaN
            camera.LookAt(camera.Location);

            Assert.That(float.IsNaN(camera.Pitch), Is.False);
            Assert.That(float.IsNaN(camera.Yaw), Is.False);
        }

        private static void AssertOrthonormal(Camera camera, string context)
        {
            Assert.That(camera.Forward.Length(), Is.EqualTo(1f).Within(Tolerance), $"forward length for {context}");
            Assert.That(camera.Up.Length(), Is.EqualTo(1f).Within(Tolerance), $"up length for {context}");
            Assert.That(camera.Right.Length(), Is.EqualTo(1f).Within(Tolerance), $"right length for {context}");

            Assert.That(Vector3.Dot(camera.Forward, camera.Up), Is.EqualTo(0f).Within(Tolerance), $"forward/up for {context}");
            Assert.That(Vector3.Dot(camera.Forward, camera.Right), Is.EqualTo(0f).Within(Tolerance), $"forward/right for {context}");
            Assert.That(Vector3.Dot(camera.Up, camera.Right), Is.EqualTo(0f).Within(Tolerance), $"up/right for {context}");

            // Handedness, which lengths and dot products alone cannot see: a negated Right satisfies all
            // six assertions above, and negating it is the mistake the hand derivation invites
            var cross = Vector3.Cross(camera.Forward, camera.Up);

            Assert.That(cross.X, Is.EqualTo(camera.Right.X).Within(Tolerance), $"right is Cross(forward, up), X, for {context}");
            Assert.That(cross.Y, Is.EqualTo(camera.Right.Y).Within(Tolerance), $"right is Cross(forward, up), Y, for {context}");
            Assert.That(cross.Z, Is.EqualTo(camera.Right.Z).Within(Tolerance), $"right is Cross(forward, up), Z, for {context}");
        }

        private static float NormalizeDegrees(float degrees)
        {
            var wrapped = degrees % 360f;

            return wrapped < 0f ? wrapped + 360f : wrapped;
        }
    }
}
