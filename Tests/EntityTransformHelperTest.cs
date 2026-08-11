using System.Linq;
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
        /// Checked against the engine's own AngleVectors formula rather than against the matrix the helper
        /// builds it from, which would only be comparing the implementation with itself.
        /// </summary>
        [Test]
        public void EulerAnglesToForwardDirectionMatchesEngineFormula()
        {
            foreach (var angles in Angles)
            {
                var (sinPitch, cosPitch) = MathF.SinCos(float.DegreesToRadians(angles.X));
                var (sinYaw, cosYaw) = MathF.SinCos(float.DegreesToRadians(angles.Y));

                var expected = new Vector3(cosPitch * cosYaw, cosPitch * sinYaw, -sinPitch);
                var direction = EntityTransformHelper.EulerAnglesToForwardDirection(angles);

                Assert.That(direction.X, Is.EqualTo(expected.X).Within(Tolerance), $"X for {angles}");
                Assert.That(direction.Y, Is.EqualTo(expected.Y).Within(Tolerance), $"Y for {angles}");
                Assert.That(direction.Z, Is.EqualTo(expected.Z).Within(Tolerance), $"Z for {angles}");
            }
        }

        /// <summary>
        /// At a pitch of +/-90 yaw and roll turn about the same axis, so the recovered angles cannot match
        /// what went in; what they must still do is describe the same rotation.
        /// </summary>
        [Test]
        public void ToEulerAnglesHandlesGimbalLock()
        {
            Vector3[] lockedAngles =
            [
                new(90f, 0f, 0f),
                new(-90f, 0f, 0f),
                new(90f, 40f, 0f),
                new(-90f, 200f, 0f),
                new(90f, 0f, 35f),
            ];

            foreach (var angles in lockedAngles)
            {
                var quaternion = EntityTransformHelper.EulerAnglesToQuaternion(angles);
                var recovered = EntityTransformHelper.ToEulerAngles(quaternion);

                Assert.That(MathF.Abs(recovered.X), Is.EqualTo(90f).Within(1e-2f), $"pitch for {angles}");
                Assert.That(MathF.Sign(recovered.X), Is.EqualTo(MathF.Sign(angles.X)), $"pitch sign for {angles}");
                Assert.That(recovered.Z, Is.EqualTo(0f).Within(Tolerance), $"roll is pinned for {angles}");

                // Rebuilding from the recovered angles has to land on the same rotation
                var original = Vector3.Transform(Vector3.UnitY, quaternion);
                var rebuilt = Vector3.Transform(Vector3.UnitY, EntityTransformHelper.EulerAnglesToQuaternion(recovered));

                Assert.That(rebuilt.X, Is.EqualTo(original.X).Within(1e-2f), $"left X for {angles}");
                Assert.That(rebuilt.Y, Is.EqualTo(original.Y).Within(1e-2f), $"left Y for {angles}");
                Assert.That(rebuilt.Z, Is.EqualTo(original.Z).Within(1e-2f), $"left Z for {angles}");
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

        private static readonly Vector3[] Directions =
        [
            Vector3.UnitX,
            Vector3.UnitZ,
            -Vector3.UnitZ,
            Vector3.Normalize(new Vector3(1f, 2f, 3f)),
            Vector3.Normalize(new Vector3(-4f, 0.5f, -1f)),
            Vector3.Normalize(new Vector3(0f, 1f, 0f)),
        ];

        /// <summary>
        /// The frame built straight from a direction has to be the one the angles read off that direction
        /// would build, since it exists only to skip going through them.
        /// </summary>
        [Test]
        public void ForwardDirectionToRotationMatrixMatchesGoingThroughAngles()
        {
            foreach (var direction in Directions.Append(new Vector3(3f, -4f, 12f)))
            {
                var direct = EntityTransformHelper.ForwardDirectionToRotationMatrix(direction);
                var viaAngles = EntityTransformHelper.EulerAnglesToRotationMatrix(
                    EntityTransformHelper.ForwardDirectionToEulerAngles(direction));

                foreach (var axis in new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
                {
                    var byDirect = Vector3.Transform(axis, direct);
                    var byAngles = Vector3.Transform(axis, viaAngles);

                    Assert.That(byDirect.X, Is.EqualTo(byAngles.X).Within(1e-3f), $"X of {axis} for {direction}");
                    Assert.That(byDirect.Y, Is.EqualTo(byAngles.Y).Within(1e-3f), $"Y of {axis} for {direction}");
                    Assert.That(byDirect.Z, Is.EqualTo(byAngles.Z).Within(1e-3f), $"Z of {axis} for {direction}");
                }
            }
        }

        /// <summary>
        /// Completing a frame from a bare direction has to put forward on the first row, the same place a
        /// full rotation puts it, or the two ways of orienting something disagree about which axis is which.
        /// </summary>
        [Test]
        public void FrameFromDirectionKeepsForwardOnFirstRow()
        {
            foreach (var direction in Directions)
            {
                var matrix = EntityTransformHelper.ForwardDirectionToRotationMatrix(direction);

                var forward = new Vector3(matrix.M11, matrix.M12, matrix.M13);
                var left = new Vector3(matrix.M21, matrix.M22, matrix.M23);
                var up = new Vector3(matrix.M31, matrix.M32, matrix.M33);

                Assert.That(forward.X, Is.EqualTo(direction.X).Within(Tolerance), $"forward X for {direction}");
                Assert.That(forward.Y, Is.EqualTo(direction.Y).Within(Tolerance), $"forward Y for {direction}");
                Assert.That(forward.Z, Is.EqualTo(direction.Z).Within(Tolerance), $"forward Z for {direction}");

                Assert.That(left.Length(), Is.EqualTo(1f).Within(Tolerance), $"left length for {direction}");
                Assert.That(up.Length(), Is.EqualTo(1f).Within(Tolerance), $"up length for {direction}");

                Assert.That(Vector3.Dot(forward, left), Is.EqualTo(0f).Within(Tolerance), $"forward/left for {direction}");
                Assert.That(Vector3.Dot(forward, up), Is.EqualTo(0f).Within(Tolerance), $"forward/up for {direction}");
                Assert.That(Vector3.Dot(left, up), Is.EqualTo(0f).Within(Tolerance), $"left/up for {direction}");
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

        [Test]
        public void TryParseVectorReportsWhetherItParsed()
        {
            Assert.That(EntityTransformHelper.TryParseVector3("1 2 3", out var vector3), Is.True);
            Assert.That(vector3, Is.EqualTo(new Vector3(1f, 2f, 3f)));

            Assert.That(EntityTransformHelper.TryParseVector3("1 2", out _), Is.False);
            Assert.That(EntityTransformHelper.TryParseVector3("1 2 banana", out _), Is.False);
            Assert.That(EntityTransformHelper.TryParseVector3("", out _), Is.False);

            Assert.That(EntityTransformHelper.TryParseVector2("1 2", out var vector2), Is.True);
            Assert.That(vector2, Is.EqualTo(new Vector2(1f, 2f)));

            Assert.That(EntityTransformHelper.TryParseVector2("1 2 3", out _), Is.False);
        }

        /// <summary>
        /// A malformed value has to fall back to what the caller asked for, not to zero: "scales" defaults
        /// to one, and collapsing it to zero would make the entity vanish.
        /// </summary>
        [Test]
        public void GetVector3PropertyFallsBackToItsDefaultForMalformedInput()
        {
            var entity = MakeEntity(("scales", "1 1"), ("origin", "not a vector"));

            Assert.That(entity.GetVector3Property("scales", Vector3.One), Is.EqualTo(Vector3.One));
            Assert.That(entity.GetVector3Property("origin"), Is.EqualTo(Vector3.Zero));

            EntityTransformHelper.GetTransformComponents(entity, out var scale, out _, out _);

            Assert.That(scale, Is.EqualTo(Vector3.One));
        }

        /// <summary>
        /// The six axis directions, with the angles the engine reports for each. Yaw is given here in the
        /// -180..180 form these helpers return, where the engine's own tests use the 0..360 one.
        /// </summary>
        [Test]
        public void ForwardDirectionToEulerAnglesMatchesEngineCardinals()
        {
            (Vector3 Direction, float Pitch, float Yaw)[] cases =
            [
                (new Vector3(1f, 0f, 0f), 0f, 0f),      // forward
                (new Vector3(-1f, 0f, 0f), 0f, 180f),   // backward
                (new Vector3(0f, 1f, 0f), 0f, 90f),     // left
                (new Vector3(0f, -1f, 0f), 0f, -90f),   // right, the engine's yaw 270
                (new Vector3(0f, 0f, 1f), -90f, 0f),    // up, the engine's pitch 270
                (new Vector3(0f, 0f, -1f), 90f, 0f),    // down
            ];

            foreach (var (direction, pitch, yaw) in cases)
            {
                var angles = EntityTransformHelper.ForwardDirectionToEulerAngles(direction);

                Assert.That(angles.X, Is.EqualTo(pitch).Within(1e-3f), $"pitch for {direction}");
                Assert.That(angles.Y, Is.EqualTo(yaw).Within(1e-3f), $"yaw for {direction}");
                Assert.That(angles.Z, Is.EqualTo(0f).Within(Tolerance), $"roll for {direction}");
            }
        }

        /// <summary>
        /// At the poles yaw and roll turn about the same axis, so the pair collapses into yaw alone. These
        /// are the collapsed values the engine produces, which it in turn checks against other engines.
        /// </summary>
        [Test]
        public void ToEulerAnglesCollapsesGimbalLockLikeTheEngine()
        {
            (Vector3 Input, Vector3 Expected)[] cases =
            [
                (new Vector3(90f, 112f, 19f), new Vector3(90f, 93f, 0f)),
                (new Vector3(90f, 12f, 180f), new Vector3(90f, -168f, 0f)),
                (new Vector3(-90f, 90f, -60f), new Vector3(-90f, 30f, 0f)),
            ];

            foreach (var (input, expected) in cases)
            {
                var angles = EntityTransformHelper.ToEulerAngles(EntityTransformHelper.EulerAnglesToQuaternion(input));

                Assert.That(angles.X, Is.EqualTo(expected.X).Within(1e-2f), $"pitch for {input}");
                Assert.That(angles.Y, Is.EqualTo(expected.Y).Within(1e-2f), $"yaw for {input}");
                Assert.That(angles.Z, Is.EqualTo(expected.Z).Within(1e-2f), $"roll for {input}");
            }
        }

        /// <summary>
        /// Sweeping through the pole, the angles that come back must always name the rotation that went in,
        /// even where they cannot name it with the same components.
        /// </summary>
        [Test]
        public void ToEulerAnglesRoundTripsThroughThePole()
        {
            for (var pitch = 87f; pitch <= 93f; pitch += 0.125f)
            {
                for (var yaw = -180f; yaw < 180f; yaw += 15f)
                {
                    foreach (var sign in new[] { -1f, 1f })
                    {
                        var input = new Vector3(pitch * sign, yaw, 0f);
                        var rotation = EntityTransformHelper.EulerAnglesToQuaternion(input);
                        var roundTripped = EntityTransformHelper.EulerAnglesToQuaternion(
                            EntityTransformHelper.ToEulerAngles(rotation));

                        // Quaternions double cover, so q and -q are the same rotation
                        var dot = MathF.Abs(Quaternion.Dot(rotation, roundTripped));

                        Assert.That(dot, Is.EqualTo(1f).Within(1e-3f), $"round trip for {input}");
                    }
                }
            }
        }

        /// <summary>
        /// Pinned against known quaternions so a sign error cannot hide by being made twice, once here and
        /// once in the matrix this is otherwise checked against.
        /// </summary>
        [Test]
        public void EulerAnglesToQuaternionMatchesKnownValues()
        {
            var root = MathF.Sqrt(0.5f);

            (Vector3 Angles, Quaternion Expected)[] cases =
            [
                (Vector3.Zero, Quaternion.Identity),
                (new Vector3(0f, 90f, 0f), new Quaternion(0f, 0f, root, root)),   // yaw turns about +Z
                (new Vector3(90f, 0f, 0f), new Quaternion(0f, root, 0f, root)),   // pitch turns about +Y
                (new Vector3(0f, 0f, 90f), new Quaternion(root, 0f, 0f, root)),   // roll turns about +X
            ];

            foreach (var (angles, expected) in cases)
            {
                var quaternion = EntityTransformHelper.EulerAnglesToQuaternion(angles);

                Assert.That(quaternion.X, Is.EqualTo(expected.X).Within(Tolerance), $"X for {angles}");
                Assert.That(quaternion.Y, Is.EqualTo(expected.Y).Within(Tolerance), $"Y for {angles}");
                Assert.That(quaternion.Z, Is.EqualTo(expected.Z).Within(Tolerance), $"Z for {angles}");
                Assert.That(quaternion.W, Is.EqualTo(expected.W).Within(Tolerance), $"W for {angles}");
            }
        }

        /// <summary>
        /// A zero direction says nothing about where to face, and callers get the vertical answer rather
        /// than a NaN out of the horizontal length.
        /// </summary>
        [Test]
        public void ForwardDirectionToEulerAnglesHandlesZeroVector()
        {
            var angles = EntityTransformHelper.ForwardDirectionToEulerAngles(Vector3.Zero);

            Assert.That(float.IsNaN(angles.X), Is.False);
            Assert.That(float.IsNaN(angles.Y), Is.False);
            Assert.That(float.IsNaN(angles.Z), Is.False);
        }

        private static float NormalizeDegrees(float degrees)
        {
            var wrapped = degrees % 360f;

            return wrapped < 0f ? wrapped + 360f : wrapped;
        }
    }
}
