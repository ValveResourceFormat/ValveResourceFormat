using NUnit.Framework;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Utils;

namespace Tests
{
    public class EntityTransformHelperTest
    {
        private const float Tolerance = 1e-4f;

        private static EntityLump.Entity MakeEntity(params (string Key, string Value)[] properties)
        {
            var entity = new EntityLump.Entity { ParentLump = new EntityLump { Resource = new Resource() } };

            foreach (var (key, value) in properties)
            {
                entity.Add(key, value);
            }

            return entity;
        }

        // A spread that covers all four yaw quadrants, both pitch signs and non-zero roll,
        // while staying clear of the +/-90 degree pitch singularity the helpers special-case.
        private static readonly Vector3[] Angles =
        [
            new(0f, 0f, 0f),
            new(30f, 45f, 0f),
            new(-30f, 135f, 20f),
            new(15f, -160f, -75f),
            new(-60f, 250f, 110f),
            new(89f, 10f, 5f),
            new(-89f, -10f, -5f),
        ];

        /// <summary>
        /// The forward direction has to agree with the matrix the angles build, since callers mix the two.
        /// </summary>
        [Test]
        public void EulerAnglesToForwardDirectionMatchesRotationMatrix()
        {
            foreach (var angles in Angles)
            {
                var fromMatrix = Vector3.Transform(Vector3.UnitX, EntityTransformHelper.EulerAnglesToRotationMatrix(angles));
                var direction = EntityTransformHelper.EulerAnglesToForwardDirection(angles);

                Assert.That(direction.X, Is.EqualTo(fromMatrix.X).Within(Tolerance), $"X for {angles}");
                Assert.That(direction.Y, Is.EqualTo(fromMatrix.Y).Within(Tolerance), $"Y for {angles}");
                Assert.That(direction.Z, Is.EqualTo(fromMatrix.Z).Within(Tolerance), $"Z for {angles}");
            }
        }

        /// <summary>
        /// Pitch is positive downwards in the Source convention, so forward's Z is negative sine of pitch.
        /// </summary>
        [Test]
        public void EulerAnglesToForwardDirectionUsesSourcePitchSign()
        {
            var down = EntityTransformHelper.EulerAnglesToForwardDirection(new Vector3(90f, 0f, 0f));
            var up = EntityTransformHelper.EulerAnglesToForwardDirection(new Vector3(-90f, 0f, 0f));

            Assert.That(down.Z, Is.EqualTo(-1f).Within(Tolerance));
            Assert.That(up.Z, Is.EqualTo(1f).Within(Tolerance));
        }

        /// <summary>
        /// Round trip through a direction, which cannot carry roll, so only pitch and yaw come back.
        /// </summary>
        [Test]
        public void ForwardDirectionToEulerAnglesRoundTripsThroughForwardDirection()
        {
            foreach (var angles in Angles)
            {
                var direction = EntityTransformHelper.EulerAnglesToForwardDirection(angles);
                var roundTripped = EntityTransformHelper.ForwardDirectionToEulerAngles(direction);

                Assert.That(roundTripped.X, Is.EqualTo(angles.X).Within(1e-2f), $"pitch for {angles}");
                Assert.That(NormalizeDegrees(roundTripped.Y), Is.EqualTo(NormalizeDegrees(angles.Y)).Within(1e-2f), $"yaw for {angles}");
                Assert.That(roundTripped.Z, Is.EqualTo(0f).Within(Tolerance), $"roll for {angles}");
            }
        }

        /// <summary>
        /// Straight up or down leaves yaw undetermined, and it is pinned to zero rather than read out of noise.
        /// </summary>
        [Test]
        public void ForwardDirectionToEulerAnglesPinsYawWhenVertical()
        {
            var up = EntityTransformHelper.ForwardDirectionToEulerAngles(new Vector3(0f, 0f, 1f));
            var down = EntityTransformHelper.ForwardDirectionToEulerAngles(new Vector3(0f, 0f, -1f));

            Assert.That(up.X, Is.EqualTo(-90f).Within(Tolerance));
            Assert.That(up.Y, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(down.X, Is.EqualTo(90f).Within(Tolerance));
            Assert.That(down.Y, Is.EqualTo(0f).Within(Tolerance));
        }

        /// <summary>
        /// The quaternion and the matrix have to describe the same rotation, since callers pick either.
        /// </summary>
        [Test]
        public void EulerAnglesToQuaternionMatchesRotationMatrix()
        {
            foreach (var angles in Angles)
            {
                var quaternion = EntityTransformHelper.EulerAnglesToQuaternion(angles);
                var matrix = EntityTransformHelper.EulerAnglesToRotationMatrix(angles);

                foreach (var axis in new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
                {
                    var byQuaternion = Vector3.Transform(axis, quaternion);
                    var byMatrix = Vector3.Transform(axis, matrix);

                    Assert.That(byQuaternion.X, Is.EqualTo(byMatrix.X).Within(Tolerance), $"X of {axis} for {angles}");
                    Assert.That(byQuaternion.Y, Is.EqualTo(byMatrix.Y).Within(Tolerance), $"Y of {axis} for {angles}");
                    Assert.That(byQuaternion.Z, Is.EqualTo(byMatrix.Z).Within(Tolerance), $"Z of {axis} for {angles}");
                }
            }
        }

        [Test]
        public void ToEulerAnglesInvertsEulerAnglesToQuaternion()
        {
            foreach (var angles in Angles)
            {
                var roundTripped = EntityTransformHelper.ToEulerAngles(EntityTransformHelper.EulerAnglesToQuaternion(angles));

                Assert.That(roundTripped.X, Is.EqualTo(angles.X).Within(1e-2f), $"pitch for {angles}");
                Assert.That(NormalizeDegrees(roundTripped.Y), Is.EqualTo(NormalizeDegrees(angles.Y)).Within(1e-2f), $"yaw for {angles}");
                Assert.That(NormalizeDegrees(roundTripped.Z), Is.EqualTo(NormalizeDegrees(angles.Z)).Within(1e-2f), $"roll for {angles}");
            }
        }

        /// <summary>
        /// The angles a quaternion reports have to name the same rotation the caller handed in.
        /// </summary>
        [Test]
        public void ToEulerAnglesMatchesQuaternionAxes()
        {
            foreach (var angles in Angles)
            {
                var quaternion = EntityTransformHelper.EulerAnglesToQuaternion(angles);
                var forward = Vector3.Transform(Vector3.UnitX, quaternion);
                var reported = EntityTransformHelper.EulerAnglesToForwardDirection(EntityTransformHelper.ToEulerAngles(quaternion));

                Assert.That(reported.X, Is.EqualTo(forward.X).Within(Tolerance), $"X for {angles}");
                Assert.That(reported.Y, Is.EqualTo(forward.Y).Within(Tolerance), $"Y for {angles}");
                Assert.That(reported.Z, Is.EqualTo(forward.Z).Within(Tolerance), $"Z for {angles}");
            }
        }

        [Test]
        public void ForwardDirectionToRotationMatrixIsOrthonormal()
        {
            Vector3[] directions =
            [
                Vector3.UnitX,
                Vector3.UnitZ,
                Vector3.Normalize(new Vector3(1f, 2f, 3f)),
                Vector3.Normalize(new Vector3(-4f, 0.5f, -1f)),
                Vector3.Normalize(new Vector3(0f, 1f, 0f)),
            ];

            foreach (var forward in directions)
            {
                var matrix = EntityTransformHelper.ForwardDirectionToRotationMatrix(forward);

                var right = new Vector3(matrix.M11, matrix.M12, matrix.M13);
                var up = new Vector3(matrix.M21, matrix.M22, matrix.M23);
                var outForward = new Vector3(matrix.M31, matrix.M32, matrix.M33);

                Assert.That(outForward.X, Is.EqualTo(forward.X).Within(Tolerance), $"forward X for {forward}");
                Assert.That(outForward.Y, Is.EqualTo(forward.Y).Within(Tolerance), $"forward Y for {forward}");
                Assert.That(outForward.Z, Is.EqualTo(forward.Z).Within(Tolerance), $"forward Z for {forward}");

                Assert.That(right.Length(), Is.EqualTo(1f).Within(Tolerance), $"right length for {forward}");
                Assert.That(up.Length(), Is.EqualTo(1f).Within(Tolerance), $"up length for {forward}");

                Assert.That(Vector3.Dot(right, up), Is.EqualTo(0f).Within(Tolerance), $"right/up for {forward}");
                Assert.That(Vector3.Dot(right, outForward), Is.EqualTo(0f).Within(Tolerance), $"right/forward for {forward}");
                Assert.That(Vector3.Dot(up, outForward), Is.EqualTo(0f).Within(Tolerance), $"up/forward for {forward}");
            }
        }

        /// <summary>
        /// Yaw turns +X toward +Y, and roll and pitch leave forward where a roll about it would.
        /// </summary>
        [Test]
        public void EulerAnglesToRotationMatrixTurnsTheExpectedAxes()
        {
            var yawed = Vector3.Transform(Vector3.UnitX, EntityTransformHelper.EulerAnglesToRotationMatrix(new Vector3(0f, 90f, 0f)));
            Assert.That(yawed.X, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(yawed.Y, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(yawed.Z, Is.EqualTo(0f).Within(Tolerance));

            // Roll turns about forward, so it leaves forward alone and lifts left toward up
            var rolled = Vector3.Transform(Vector3.UnitY, EntityTransformHelper.EulerAnglesToRotationMatrix(new Vector3(0f, 0f, 90f)));
            Assert.That(rolled.Z, Is.EqualTo(1f).Within(Tolerance));

            var identity = EntityTransformHelper.EulerAnglesToRotationMatrix(Vector3.Zero);
            Assert.That(identity, Is.EqualTo(Matrix4x4.Identity));
        }

        [Test]
        public void GetTransformComponentsReadsEntityKeyValues()
        {
            var entity = MakeEntity(("origin", "10 20 30"), ("angles", "0 90 0"), ("scales", "2 3 4"));

            EntityTransformHelper.GetTransformComponents(entity, out var scale, out var rotation, out var position);

            Assert.That(scale, Is.EqualTo(new Vector3(2f, 3f, 4f)));
            Assert.That(position, Is.EqualTo(new Vector3(10f, 20f, 30f)));
            Assert.That(rotation, Is.EqualTo(EntityTransformHelper.EulerAnglesToRotationMatrix(new Vector3(0f, 90f, 0f))));
        }

        /// <summary>
        /// An entity that says nothing about its placement sits at the origin, unrotated and unscaled.
        /// </summary>
        [Test]
        public void GetTransformComponentsDefaultsToIdentity()
        {
            EntityTransformHelper.GetTransformComponents(MakeEntity(), out var scale, out var rotation, out var position);

            Assert.That(scale, Is.EqualTo(Vector3.One));
            Assert.That(position, Is.EqualTo(Vector3.Zero));
            Assert.That(rotation, Is.EqualTo(Matrix4x4.Identity));
        }

        [Test]
        public void ToTransformationMatrixAppliesScaleRotationThenTranslation()
        {
            var entity = MakeEntity(("origin", "10 20 30"), ("angles", "0 90 0"), ("scales", "2 2 2"));

            var transformed = Vector3.Transform(Vector3.UnitX, EntityTransformHelper.ToTransformationMatrix(entity));

            // Scaled to length 2, yawed onto +Y, then moved to the origin point
            Assert.That(transformed.X, Is.EqualTo(10f).Within(Tolerance));
            Assert.That(transformed.Y, Is.EqualTo(22f).Within(Tolerance));
            Assert.That(transformed.Z, Is.EqualTo(30f).Within(Tolerance));
        }

        /// <summary>
        /// The rigid transform is the same thing with the entity's own scale left out.
        /// </summary>
        [Test]
        public void ToRigidTransformationMatrixDropsScale()
        {
            var entity = MakeEntity(("origin", "10 20 30"), ("angles", "0 90 0"), ("scales", "2 2 2"));

            var transformed = Vector3.Transform(Vector3.UnitX, EntityTransformHelper.ToRigidTransformationMatrix(entity));

            Assert.That(transformed.X, Is.EqualTo(10f).Within(Tolerance));
            Assert.That(transformed.Y, Is.EqualTo(21f).Within(Tolerance));
            Assert.That(transformed.Z, Is.EqualTo(30f).Within(Tolerance));
        }

        [Test]
        public void ParseVectorReadsThreeComponents()
        {
            Assert.That(EntityTransformHelper.ParseVector3("1.5 -2 3"), Is.EqualTo(new Vector3(1.5f, -2f, 3f)));
        }

        /// <summary>
        /// Anything that is not three numbers is not a vector, and callers get the default rather than a throw.
        /// </summary>
        [Test]
        public void ParseVectorReturnsDefaultForMalformedInput()
        {
            Assert.That(EntityTransformHelper.ParseVector3(""), Is.EqualTo(Vector3.Zero));
            Assert.That(EntityTransformHelper.ParseVector3("1 2"), Is.EqualTo(Vector3.Zero));
            Assert.That(EntityTransformHelper.ParseVector3("1 2 3 4"), Is.EqualTo(Vector3.Zero));
        }

        [Test]
        public void ParseVector2ReadsTwoComponents()
        {
            Assert.That(EntityTransformHelper.ParseVector2("1.5 -2"), Is.EqualTo(new Vector2(1.5f, -2f)));
        }

        [Test]
        public void ParseVector2ReturnsDefaultForMalformedInput()
        {
            Assert.That(EntityTransformHelper.ParseVector2(""), Is.EqualTo(Vector2.Zero));
            Assert.That(EntityTransformHelper.ParseVector2("1"), Is.EqualTo(Vector2.Zero));
            Assert.That(EntityTransformHelper.ParseVector2("1 2 3"), Is.EqualTo(Vector2.Zero));
        }

        private static float NormalizeDegrees(float degrees)
        {
            var wrapped = degrees % 360f;

            return wrapped < 0f ? wrapped + 360f : wrapped;
        }
    }
}
