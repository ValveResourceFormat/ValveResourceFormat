using System.Linq;
using System.Threading.Tasks;
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
        public async Task EulerAnglesToForwardDirectionMatchesEngineFormula()
        {
            foreach (var angles in Angles)
            {
                var (sinPitch, cosPitch) = MathF.SinCos(float.DegreesToRadians(angles.X));
                var (sinYaw, cosYaw) = MathF.SinCos(float.DegreesToRadians(angles.Y));

                var expected = new Vector3(cosPitch * cosYaw, cosPitch * sinYaw, -sinPitch);
                var direction = EntityTransformHelper.EulerAnglesToForwardDirection(angles);

                await Assert.That(direction.X).IsEqualTo(expected.X).Within(Tolerance).Because($"X for {angles}");
                await Assert.That(direction.Y).IsEqualTo(expected.Y).Within(Tolerance).Because($"Y for {angles}");
                await Assert.That(direction.Z).IsEqualTo(expected.Z).Within(Tolerance).Because($"Z for {angles}");
            }
        }

        /// <summary>
        /// At a pitch of +/-90 yaw and roll turn about the same axis, so the recovered angles cannot match
        /// what went in; what they must still do is describe the same rotation.
        /// </summary>
        [Test]
        public async Task ToEulerAnglesHandlesGimbalLock()
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

                await Assert.That(MathF.Abs(recovered.X)).IsEqualTo(90f).Within(1e-2f).Because($"pitch for {angles}");
                await Assert.That(MathF.Sign(recovered.X)).IsEqualTo(MathF.Sign(angles.X)).Because($"pitch sign for {angles}");
                await Assert.That(recovered.Z).IsEqualTo(0f).Within(Tolerance).Because($"roll is pinned for {angles}");

                // Rebuilding from the recovered angles has to land on the same rotation
                var original = Vector3.Transform(Vector3.UnitY, quaternion);
                var rebuilt = Vector3.Transform(Vector3.UnitY, EntityTransformHelper.EulerAnglesToQuaternion(recovered));

                await Assert.That(rebuilt.X).IsEqualTo(original.X).Within(1e-2f).Because($"left X for {angles}");
                await Assert.That(rebuilt.Y).IsEqualTo(original.Y).Within(1e-2f).Because($"left Y for {angles}");
                await Assert.That(rebuilt.Z).IsEqualTo(original.Z).Within(1e-2f).Because($"left Z for {angles}");
            }
        }

        /// <summary>
        /// Pitch is positive downwards in the Source convention, so forward's Z is negative sine of pitch.
        /// </summary>
        [Test]
        public async Task EulerAnglesToForwardDirectionUsesSourcePitchSign()
        {
            var down = EntityTransformHelper.EulerAnglesToForwardDirection(new Vector3(90f, 0f, 0f));
            var up = EntityTransformHelper.EulerAnglesToForwardDirection(new Vector3(-90f, 0f, 0f));

            await Assert.That(down.Z).IsEqualTo(-1f).Within(Tolerance);
            await Assert.That(up.Z).IsEqualTo(1f).Within(Tolerance);
        }

        /// <summary>
        /// Round trip through a direction, which cannot carry roll, so only pitch and yaw come back.
        /// </summary>
        [Test]
        public async Task ForwardDirectionToEulerAnglesRoundTripsThroughForwardDirection()
        {
            foreach (var angles in Angles)
            {
                var direction = EntityTransformHelper.EulerAnglesToForwardDirection(angles);
                var roundTripped = EntityTransformHelper.ForwardDirectionToEulerAngles(direction);

                await Assert.That(roundTripped.X).IsEqualTo(angles.X).Within(1e-2f).Because($"pitch for {angles}");
                await Assert.That(NormalizeDegrees(roundTripped.Y)).IsEqualTo(NormalizeDegrees(angles.Y)).Within(1e-2f).Because($"yaw for {angles}");
                await Assert.That(roundTripped.Z).IsEqualTo(0f).Within(Tolerance).Because($"roll for {angles}");
            }
        }

        /// <summary>
        /// Straight up or down leaves yaw undetermined, and it is pinned to zero rather than read out of noise.
        /// </summary>
        [Test]
        public async Task ForwardDirectionToEulerAnglesPinsYawWhenVertical()
        {
            var up = EntityTransformHelper.ForwardDirectionToEulerAngles(new Vector3(0f, 0f, 1f));
            var down = EntityTransformHelper.ForwardDirectionToEulerAngles(new Vector3(0f, 0f, -1f));

            await Assert.That(up.X).IsEqualTo(-90f).Within(Tolerance);
            await Assert.That(up.Y).IsEqualTo(0f).Within(Tolerance);
            await Assert.That(down.X).IsEqualTo(90f).Within(Tolerance);
            await Assert.That(down.Y).IsEqualTo(0f).Within(Tolerance);
        }

        /// <summary>
        /// The quaternion and the matrix have to describe the same rotation, since callers pick either.
        /// </summary>
        [Test]
        public async Task EulerAnglesToQuaternionMatchesRotationMatrix()
        {
            foreach (var angles in Angles)
            {
                var quaternion = EntityTransformHelper.EulerAnglesToQuaternion(angles);
                var matrix = EntityTransformHelper.EulerAnglesToRotationMatrix(angles);

                foreach (var axis in new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
                {
                    var byQuaternion = Vector3.Transform(axis, quaternion);
                    var byMatrix = Vector3.Transform(axis, matrix);

                    await Assert.That(byQuaternion.X).IsEqualTo(byMatrix.X).Within(Tolerance).Because($"X of {axis} for {angles}");
                    await Assert.That(byQuaternion.Y).IsEqualTo(byMatrix.Y).Within(Tolerance).Because($"Y of {axis} for {angles}");
                    await Assert.That(byQuaternion.Z).IsEqualTo(byMatrix.Z).Within(Tolerance).Because($"Z of {axis} for {angles}");
                }
            }
        }

        [Test]
        public async Task ToEulerAnglesInvertsEulerAnglesToQuaternion()
        {
            foreach (var angles in Angles)
            {
                var roundTripped = EntityTransformHelper.ToEulerAngles(EntityTransformHelper.EulerAnglesToQuaternion(angles));

                await Assert.That(roundTripped.X).IsEqualTo(angles.X).Within(1e-2f).Because($"pitch for {angles}");
                await Assert.That(NormalizeDegrees(roundTripped.Y)).IsEqualTo(NormalizeDegrees(angles.Y)).Within(1e-2f).Because($"yaw for {angles}");
                await Assert.That(NormalizeDegrees(roundTripped.Z)).IsEqualTo(NormalizeDegrees(angles.Z)).Within(1e-2f).Because($"roll for {angles}");
            }
        }

        /// <summary>
        /// The angles a quaternion reports have to name the same rotation the caller handed in.
        /// </summary>
        [Test]
        public async Task ToEulerAnglesMatchesQuaternionAxes()
        {
            foreach (var angles in Angles)
            {
                var quaternion = EntityTransformHelper.EulerAnglesToQuaternion(angles);
                var forward = Vector3.Transform(Vector3.UnitX, quaternion);
                var reported = EntityTransformHelper.EulerAnglesToForwardDirection(EntityTransformHelper.ToEulerAngles(quaternion));

                await Assert.That(reported.X).IsEqualTo(forward.X).Within(Tolerance).Because($"X for {angles}");
                await Assert.That(reported.Y).IsEqualTo(forward.Y).Within(Tolerance).Because($"Y for {angles}");
                await Assert.That(reported.Z).IsEqualTo(forward.Z).Within(Tolerance).Because($"Z for {angles}");
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
        public async Task ForwardDirectionToRotationMatrixMatchesGoingThroughAngles()
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

                    await Assert.That(byDirect.X).IsEqualTo(byAngles.X).Within(1e-3f).Because($"X of {axis} for {direction}");
                    await Assert.That(byDirect.Y).IsEqualTo(byAngles.Y).Within(1e-3f).Because($"Y of {axis} for {direction}");
                    await Assert.That(byDirect.Z).IsEqualTo(byAngles.Z).Within(1e-3f).Because($"Z of {axis} for {direction}");
                }
            }
        }

        /// <summary>
        /// Completing a frame from a bare direction has to put forward on the first row, the same place a
        /// full rotation puts it, or the two ways of orienting something disagree about which axis is which.
        /// </summary>
        [Test]
        public async Task FrameFromDirectionKeepsForwardOnFirstRow()
        {
            foreach (var direction in Directions)
            {
                var matrix = EntityTransformHelper.ForwardDirectionToRotationMatrix(direction);

                var forward = new Vector3(matrix.M11, matrix.M12, matrix.M13);
                var left = new Vector3(matrix.M21, matrix.M22, matrix.M23);
                var up = new Vector3(matrix.M31, matrix.M32, matrix.M33);

                await Assert.That(forward.X).IsEqualTo(direction.X).Within(Tolerance).Because($"forward X for {direction}");
                await Assert.That(forward.Y).IsEqualTo(direction.Y).Within(Tolerance).Because($"forward Y for {direction}");
                await Assert.That(forward.Z).IsEqualTo(direction.Z).Within(Tolerance).Because($"forward Z for {direction}");

                await Assert.That(left.Length()).IsEqualTo(1f).Within(Tolerance).Because($"left length for {direction}");
                await Assert.That(up.Length()).IsEqualTo(1f).Within(Tolerance).Because($"up length for {direction}");

                await Assert.That(Vector3.Dot(forward, left)).IsEqualTo(0f).Within(Tolerance).Because($"forward/left for {direction}");
                await Assert.That(Vector3.Dot(forward, up)).IsEqualTo(0f).Within(Tolerance).Because($"forward/up for {direction}");
                await Assert.That(Vector3.Dot(left, up)).IsEqualTo(0f).Within(Tolerance).Because($"left/up for {direction}");
            }
        }

        /// <summary>
        /// Yaw turns +X toward +Y, and roll and pitch leave forward where a roll about it would.
        /// </summary>
        [Test]
        public async Task EulerAnglesToRotationMatrixTurnsTheExpectedAxes()
        {
            var yawed = Vector3.Transform(Vector3.UnitX, EntityTransformHelper.EulerAnglesToRotationMatrix(new Vector3(0f, 90f, 0f)));
            await Assert.That(yawed.X).IsEqualTo(0f).Within(Tolerance);
            await Assert.That(yawed.Y).IsEqualTo(1f).Within(Tolerance);
            await Assert.That(yawed.Z).IsEqualTo(0f).Within(Tolerance);

            // Roll turns about forward, so it leaves forward alone and lifts left toward up
            var rolled = Vector3.Transform(Vector3.UnitY, EntityTransformHelper.EulerAnglesToRotationMatrix(new Vector3(0f, 0f, 90f)));
            await Assert.That(rolled.Z).IsEqualTo(1f).Within(Tolerance);

            var identity = EntityTransformHelper.EulerAnglesToRotationMatrix(Vector3.Zero);
            await Assert.That(identity).IsEqualTo(Matrix4x4.Identity);
        }

        [Test]
        public async Task GetTransformComponentsReadsEntityKeyValues()
        {
            var entity = MakeEntity(("origin", "10 20 30"), ("angles", "0 90 0"), ("scales", "2 3 4"));

            EntityTransformHelper.GetTransformComponents(entity, out var scale, out var rotation, out var position);

            await Assert.That(scale).IsEqualTo(new Vector3(2f, 3f, 4f));
            await Assert.That(position).IsEqualTo(new Vector3(10f, 20f, 30f));
            await Assert.That(rotation).IsEqualTo(EntityTransformHelper.EulerAnglesToRotationMatrix(new Vector3(0f, 90f, 0f)));
        }

        /// <summary>
        /// An entity that says nothing about its placement sits at the origin, unrotated and unscaled.
        /// </summary>
        [Test]
        public async Task GetTransformComponentsDefaultsToIdentity()
        {
            EntityTransformHelper.GetTransformComponents(MakeEntity(), out var scale, out var rotation, out var position);

            await Assert.That(scale).IsEqualTo(Vector3.One);
            await Assert.That(position).IsEqualTo(Vector3.Zero);
            await Assert.That(rotation).IsEqualTo(Matrix4x4.Identity);
        }

        [Test]
        public async Task ToTransformationMatrixAppliesScaleRotationThenTranslation()
        {
            var entity = MakeEntity(("origin", "10 20 30"), ("angles", "0 90 0"), ("scales", "2 2 2"));

            var transformed = Vector3.Transform(Vector3.UnitX, EntityTransformHelper.ToTransformationMatrix(entity));

            // Scaled to length 2, yawed onto +Y, then moved to the origin point
            await Assert.That(transformed.X).IsEqualTo(10f).Within(Tolerance);
            await Assert.That(transformed.Y).IsEqualTo(22f).Within(Tolerance);
            await Assert.That(transformed.Z).IsEqualTo(30f).Within(Tolerance);
        }

        /// <summary>
        /// The rigid transform is the same thing with the entity's own scale left out.
        /// </summary>
        [Test]
        public async Task ToRigidTransformationMatrixDropsScale()
        {
            var entity = MakeEntity(("origin", "10 20 30"), ("angles", "0 90 0"), ("scales", "2 2 2"));

            var transformed = Vector3.Transform(Vector3.UnitX, EntityTransformHelper.ToRigidTransformationMatrix(entity));

            await Assert.That(transformed.X).IsEqualTo(10f).Within(Tolerance);
            await Assert.That(transformed.Y).IsEqualTo(21f).Within(Tolerance);
            await Assert.That(transformed.Z).IsEqualTo(30f).Within(Tolerance);
        }

        [Test]
        public async Task ParseVectorReadsThreeComponents()
        {
            await Assert.That(EntityTransformHelper.ParseVector3("1.5 -2 3")).IsEqualTo(new Vector3(1.5f, -2f, 3f));
        }

        /// <summary>
        /// Anything that is not three numbers is not a vector, and callers get the default rather than a throw.
        /// </summary>
        [Test]
        public async Task ParseVectorReturnsDefaultForMalformedInput()
        {
            await Assert.That(EntityTransformHelper.ParseVector3("")).IsEqualTo(Vector3.Zero);
            await Assert.That(EntityTransformHelper.ParseVector3("1 2")).IsEqualTo(Vector3.Zero);
            await Assert.That(EntityTransformHelper.ParseVector3("1 2 3 4")).IsEqualTo(Vector3.Zero);
        }

        [Test]
        public async Task ParseVector2ReadsTwoComponents()
        {
            await Assert.That(EntityTransformHelper.ParseVector2("1.5 -2")).IsEqualTo(new Vector2(1.5f, -2f));
        }

        [Test]
        public async Task ParseVector2ReturnsDefaultForMalformedInput()
        {
            await Assert.That(EntityTransformHelper.ParseVector2("")).IsEqualTo(Vector2.Zero);
            await Assert.That(EntityTransformHelper.ParseVector2("1")).IsEqualTo(Vector2.Zero);
            await Assert.That(EntityTransformHelper.ParseVector2("1 2 3")).IsEqualTo(Vector2.Zero);
        }

        [Test]
        public async Task TryParseVectorReportsWhetherItParsed()
        {
            await Assert.That(EntityTransformHelper.TryParseVector3("1 2 3", out var vector3)).IsTrue();
            await Assert.That(vector3).IsEqualTo(new Vector3(1f, 2f, 3f));

            await Assert.That(EntityTransformHelper.TryParseVector3("1 2", out _)).IsFalse();
            await Assert.That(EntityTransformHelper.TryParseVector3("1 2 banana", out _)).IsFalse();
            await Assert.That(EntityTransformHelper.TryParseVector3("", out _)).IsFalse();

            await Assert.That(EntityTransformHelper.TryParseVector2("1 2", out var vector2)).IsTrue();
            await Assert.That(vector2).IsEqualTo(new Vector2(1f, 2f));

            await Assert.That(EntityTransformHelper.TryParseVector2("1 2 3", out _)).IsFalse();
        }

        /// <summary>
        /// A malformed value has to fall back to what the caller asked for, not to zero: "scales" defaults
        /// to one, and collapsing it to zero would make the entity vanish.
        /// </summary>
        [Test]
        public async Task GetVector3PropertyFallsBackToItsDefaultForMalformedInput()
        {
            var entity = MakeEntity(("scales", "1 1"), ("origin", "not a vector"));

            await Assert.That(entity.GetVector3Property("scales", Vector3.One)).IsEqualTo(Vector3.One);
            await Assert.That(entity.GetVector3Property("origin")).IsEqualTo(Vector3.Zero);

            EntityTransformHelper.GetTransformComponents(entity, out var scale, out _, out _);

            await Assert.That(scale).IsEqualTo(Vector3.One);
        }

        /// <summary>
        /// The six axis directions, with the angles the engine reports for each. Yaw is given here in the
        /// -180..180 form these helpers return, where the engine's own tests use the 0..360 one.
        /// </summary>
        [Test]
        public async Task ForwardDirectionToEulerAnglesMatchesEngineCardinals()
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

                await Assert.That(angles.X).IsEqualTo(pitch).Within(1e-3f).Because($"pitch for {direction}");
                await Assert.That(angles.Y).IsEqualTo(yaw).Within(1e-3f).Because($"yaw for {direction}");
                await Assert.That(angles.Z).IsEqualTo(0f).Within(Tolerance).Because($"roll for {direction}");
            }
        }

        /// <summary>
        /// At the poles yaw and roll turn about the same axis, so the pair collapses into yaw alone. These
        /// are the collapsed values the engine produces, which it in turn checks against other engines.
        /// </summary>
        [Test]
        public async Task ToEulerAnglesCollapsesGimbalLockLikeTheEngine()
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

                await Assert.That(angles.X).IsEqualTo(expected.X).Within(1e-2f).Because($"pitch for {input}");
                await Assert.That(angles.Y).IsEqualTo(expected.Y).Within(1e-2f).Because($"yaw for {input}");
                await Assert.That(angles.Z).IsEqualTo(expected.Z).Within(1e-2f).Because($"roll for {input}");
            }
        }

        /// <summary>
        /// Sweeping through the pole, the angles that come back must always name the rotation that went in,
        /// even where they cannot name it with the same components.
        /// </summary>
        [Test]
        public async Task ToEulerAnglesRoundTripsThroughThePole()
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

                        await Assert.That(dot).IsEqualTo(1f).Within(1e-3f).Because($"round trip for {input}");
                    }
                }
            }
        }

        /// <summary>
        /// Pinned against known quaternions so a sign error cannot hide by being made twice, once here and
        /// once in the matrix this is otherwise checked against.
        /// </summary>
        [Test]
        public async Task EulerAnglesToQuaternionMatchesKnownValues()
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

                await Assert.That(quaternion.X).IsEqualTo(expected.X).Within(Tolerance).Because($"X for {angles}");
                await Assert.That(quaternion.Y).IsEqualTo(expected.Y).Within(Tolerance).Because($"Y for {angles}");
                await Assert.That(quaternion.Z).IsEqualTo(expected.Z).Within(Tolerance).Because($"Z for {angles}");
                await Assert.That(quaternion.W).IsEqualTo(expected.W).Within(Tolerance).Because($"W for {angles}");
            }
        }

        /// <summary>
        /// A zero direction says nothing about where to face, and callers get the vertical answer rather
        /// than a NaN out of the horizontal length.
        /// </summary>
        [Test]
        public async Task ForwardDirectionToEulerAnglesHandlesZeroVector()
        {
            var angles = EntityTransformHelper.ForwardDirectionToEulerAngles(Vector3.Zero);

            await Assert.That(float.IsNaN(angles.X)).IsFalse();
            await Assert.That(float.IsNaN(angles.Y)).IsFalse();
            await Assert.That(float.IsNaN(angles.Z)).IsFalse();
        }

        private static float NormalizeDegrees(float degrees)
        {
            var wrapped = degrees % 360f;

            return wrapped < 0f ? wrapped + 360f : wrapped;
        }
    }
}
