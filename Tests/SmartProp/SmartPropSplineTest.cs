using System.Threading.Tasks;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.SmartProps;

namespace Tests.SmartProp
{
    public class SmartPropSplineTest
    {
        private const float Tolerance = 1e-3f;

        private static readonly Vector3[] CurvedPoints =
        [
            new(-400f, 0f, 0f),
            new(-200f, 32f, 0f),
            new(200f, -32f, 0f),
            new(400f, 0f, 0f),
        ];

        [Test]
        public async Task SplinePassesThroughControlPoints()
        {
            var curve = SmartPropSpline.CentripetalCatmullRom(CurvedPoints);

            await Assert.That(curve.Length).IsEqualTo((CurvedPoints.Length - 1) * SmartPropSpline.DefaultSamplesPerSegment + 1);
            await Assert.That(curve[0]).IsEqualTo(CurvedPoints[0]);
            await Assert.That(curve[^1]).IsEqualTo(CurvedPoints[^1]);

            // Each interior control point lands exactly at its segment boundary sample
            for (var i = 1; i < CurvedPoints.Length - 1; i++)
            {
                await Assert.That(Vector3.Distance(curve[i * SmartPropSpline.DefaultSamplesPerSegment], CurvedPoints[i])).IsLessThan(Tolerance);
            }
        }

        [Test]
        public async Task SplineHandlesDegenerateInputs()
        {
            await Assert.That(SmartPropSpline.CentripetalCatmullRom([])).IsEmpty();

            var single = SmartPropSpline.CentripetalCatmullRom([Vector3.One]);
            await Assert.That(single.Length).IsEqualTo(1);
            await Assert.That(single[0]).IsEqualTo(Vector3.One);

            var pair = SmartPropSpline.CentripetalCatmullRom([Vector3.Zero, new Vector3(10f, 0f, 0f)]);
            await Assert.That(pair.Length).IsEqualTo(SmartPropSpline.DefaultSamplesPerSegment + 1);
        }

        [Test]
        public async Task StraightLineSplineMatchesEuclideanLength()
        {
            Vector3[] points = [new(0f, 0f, 0f), new(100f, 0f, 0f), new(200f, 0f, 0f)];

            var (samples, totalLength) = SmartPropSpline.ComputeSamples(points);

            await Assert.That(totalLength).IsEqualTo(200f).Within(Tolerance);
            await Assert.That(samples[0].Distance).IsEqualTo(0f);

            // Every sample sits on the line and every tangent points along +X
            foreach (var sample in samples)
            {
                await Assert.That(sample.Position.Y).IsEqualTo(0f).Within(Tolerance);
                await Assert.That(sample.Position.Z).IsEqualTo(0f).Within(Tolerance);
                await Assert.That(sample.Tangent.X).IsEqualTo(1f).Within(Tolerance);
            }

            // Distances accumulate monotonically
            for (var i = 1; i < samples.Length; i++)
            {
                await Assert.That(samples[i].Distance).IsGreaterThanOrEqualTo(samples[i - 1].Distance);
            }
        }

        [Test]
        public async Task CurvedSplineHasPositiveLength()
        {
            var (samples, totalLength) = SmartPropSpline.ComputeSamples(CurvedPoints);

            // The curve bows away from the straight line between the ends (800 units),
            // so its arc length must exceed that chord
            await Assert.That(totalLength).IsGreaterThan(800f);
            await Assert.That(samples[^1].Distance).IsEqualTo(totalLength).Within(Tolerance);
            await Assert.That(samples[0].Position).IsEqualTo(CurvedPoints[0]);
        }

        [Test]
        public async Task SinglePointCurveYieldsOneZeroSample()
        {
            var (samples, totalLength) = SmartPropSpline.ComputeSamples([Vector3.One]);

            await Assert.That(samples.Length).IsEqualTo(1);
            await Assert.That(totalLength).IsEqualTo(0f);
            await Assert.That(samples[0].Tangent).IsEqualTo(Vector3.UnitX);
        }

        [Test]
        public async Task ProjectedDistanceIgnoresClimbAlongUpAxis()
        {
            // A path that runs 100 units along X while climbing 100 units in Z
            Vector3[] points = [new(0f, 0f, 0f), new(50f, 0f, 50f), new(100f, 0f, 100f)];

            var (_, trueLength) = SmartPropSpline.ComputeSamples(points);
            var (_, projectedLength) = SmartPropSpline.ComputeSamples(points, projectedUp: Vector3.UnitZ);

            await Assert.That(trueLength).IsGreaterThan(100f);
            await Assert.That(projectedLength).IsEqualTo(100f).Within(Tolerance);
        }

        [Test]
        public async Task InterpolateAtDistanceClampsToEnds()
        {
            var (samples, totalLength) = SmartPropSpline.ComputeSamples(CurvedPoints);

            var (atStart, startTangent) = SmartPropSpline.InterpolateAtDistance(samples, totalLength, -5f);
            await Assert.That(atStart).IsEqualTo(samples[0].Position);
            await Assert.That(startTangent).IsEqualTo(samples[0].Tangent);

            var (atEnd, endTangent) = SmartPropSpline.InterpolateAtDistance(samples, totalLength, totalLength * 2f);
            await Assert.That(atEnd).IsEqualTo(samples[^1].Position);
            await Assert.That(endTangent).IsEqualTo(samples[^1].Tangent);
        }

        [Test]
        public async Task InterpolateAtDistanceMidpointSitsOnCurve()
        {
            var (samples, totalLength) = SmartPropSpline.ComputeSamples(CurvedPoints);
            var half = totalLength / 2f;

            var (position, tangent) = SmartPropSpline.InterpolateAtDistance(samples, totalLength, half);

            // The interpolated position must sit between its bracketing samples
            var distanceToStart = Vector3.Distance(samples[0].Position, position);
            var distanceToEnd = Vector3.Distance(samples[^1].Position, position);
            await Assert.That(distanceToStart).IsGreaterThan(1f);
            await Assert.That(distanceToEnd).IsGreaterThan(1f);

            await Assert.That(MathF.Abs(tangent.Length() - 1f)).IsLessThan(Tolerance);

            // Asking for a sample's own distance returns it exactly
            var sampleIndex = samples.Length / 2;
            var (exact, _) = SmartPropSpline.InterpolateAtDistance(samples, totalLength, samples[sampleIndex].Distance);
            await Assert.That(Vector3.Distance(exact, samples[sampleIndex].Position)).IsLessThan(Tolerance);
        }

        [Test]
        public async Task EmptySamplesInterpolateToOrigin()
        {
            var (position, tangent) = SmartPropSpline.InterpolateAtDistance([], 0f, 10f);
            await Assert.That(position).IsEqualTo(Vector3.Zero);
            await Assert.That(tangent).IsEqualTo(Vector3.UnitX);
        }
    }

    public class SmartPropTransformTest
    {
        private const float Tolerance = 1e-3f;

        [Test]
        public async Task FrameWithForwardXAndUpZIsIdentityBasis()
        {
            var frame = SmartPropTransform.CreateFrame(new Vector3(10f, 20f, 30f), Vector3.UnitX);

            await Assert.That(Row(frame, 0)).IsEqualTo(new Vector3(1f, 0f, 0f));
            await Assert.That(Row(frame, 1)).IsEqualTo(new Vector3(0f, 1f, 0f));
            await Assert.That(Row(frame, 2)).IsEqualTo(new Vector3(0f, 0f, 1f));
            await Assert.That(new Vector3(frame.M41, frame.M42, frame.M43)).IsEqualTo(new Vector3(10f, 20f, 30f));
        }

        [Test]
        public async Task FrameRowsStayOrthonormalAndRightHanded()
        {
            var forward = Vector3.Normalize(new Vector3(1f, 2f, 3f));
            var frame = SmartPropTransform.CreateFrame(Vector3.Zero, forward);

            var f = Row(frame, 0);
            var l = Row(frame, 1);
            var u = Row(frame, 2);

            await Assert.That(Vector3.Distance(f, forward)).IsLessThan(Tolerance);
            await Assert.That(MathF.Abs(Vector3.Dot(f, l))).IsLessThan(Tolerance);
            await Assert.That(MathF.Abs(Vector3.Dot(f, u))).IsLessThan(Tolerance);
            await Assert.That(MathF.Abs(Vector3.Dot(l, u))).IsLessThan(Tolerance);
            await Assert.That(MathF.Abs(f.Length() - 1f)).IsLessThan(Tolerance);

            // Row basis: x cross y = z, so forward x left = up
            await Assert.That(Vector3.Cross(f, l)).IsEqualTo(u);
        }

        [Test]
        public async Task FrameWithCustomUpHonorsUpReference()
        {
            var frame = SmartPropTransform.CreateFrame(Vector3.Zero, Vector3.UnitX, up: Vector3.UnitX);

            // Up collinear with forward falls back to +Y as the reference, which still
            // produces a fully orthogonal frame
            var u = Row(frame, 2);
            await Assert.That(MathF.Abs(Vector3.Dot(u, Vector3.UnitX))).IsLessThan(Tolerance);
            await Assert.That(MathF.Abs(u.Length() - 1f)).IsLessThan(Tolerance);
        }

        [Test]
        public async Task FrameForDownwardForwardUsesFallbackUp()
        {
            var frame = SmartPropTransform.CreateFrame(Vector3.Zero, -Vector3.UnitZ);

            // Forward straight down with default up (+Z) is collinear, so the fallback up (+Y)
            // kicks in: left = y x -z = -x, orthogonal up = -z x -x = ... verify by orthogonality
            var f = Row(frame, 0);
            await Assert.That(f).IsEqualTo(new Vector3(0f, 0f, -1f));

            var l = Row(frame, 1);
            var u = Row(frame, 2);
            await Assert.That(l).IsEqualTo(new Vector3(-1f, 0f, 0f));
            await Assert.That(u).IsEqualTo(new Vector3(0f, 1f, 0f));
        }

        [Test]
        public async Task DecomposeRoundTripsEulerRotationAndPosition()
        {
            Vector3[] anglesSet =
            [
                new(0f, 0f, 0f),
                new(30f, 45f, 0f),
                new(-15f, 200f, 60f),
                new(89f, 0f, 0f),
            ];

            foreach (var angles in anglesSet)
            {
                var rotation = EntityTransformHelper.EulerAnglesToRotationMatrix(angles);
                var matrix = rotation * Matrix4x4.CreateTranslation(new Vector3(5f, -3f, 12f));

                var (position, decomposedAngles, scale) = SmartPropTransform.DecomposeTRS(matrix);

                await Assert.That(position).IsEqualTo(new Vector3(5f, -3f, 12f));
                await Assert.That(Vector3.Distance(scale, Vector3.One)).IsLessThan(Tolerance);
                await Assert.That(AngleDelta(decomposedAngles.X, angles.X)).IsLessThan(Tolerance);
                await Assert.That(AngleDelta(decomposedAngles.Y, angles.Y)).IsLessThan(Tolerance);
                await Assert.That(AngleDelta(decomposedAngles.Z, angles.Z)).IsLessThan(Tolerance);
            }
        }

        [Test]
        public async Task DecomposeHandlesGimbalLock()
        {
            foreach (var pitch in new[] { 90f, -90f })
            {
                var rotation = EntityTransformHelper.EulerAnglesToRotationMatrix(new Vector3(pitch, 0f, 40f));
                var matrix = rotation * Matrix4x4.CreateTranslation(Vector3.Zero);

                var (_, angles, _) = SmartPropTransform.DecomposeTRS(matrix);

                await Assert.That(AngleDelta(angles.X, pitch)).IsLessThan(Tolerance);

                // Rebuilding from the decomposed angles must reproduce the same rotation
                var rebuilt = EntityTransformHelper.EulerAnglesToRotationMatrix(angles);
                await Assert.That(RowDelta(rebuilt, matrix, 0)).IsLessThan(Tolerance);
                await Assert.That(RowDelta(rebuilt, matrix, 1)).IsLessThan(Tolerance);
                await Assert.That(RowDelta(rebuilt, matrix, 2)).IsLessThan(Tolerance);
            }
        }

        [Test]
        public async Task DecomposeReadsPerAxisScaleFromRowLengths()
        {
            var rotation = EntityTransformHelper.EulerAnglesToRotationMatrix(new Vector3(20f, 50f, 10f));
            var scale = new Vector3(2f, 3f, 0.5f);
            var scaled = new Matrix4x4(
                rotation.M11 * scale.X, rotation.M12 * scale.X, rotation.M13 * scale.X, 0f,
                rotation.M21 * scale.Y, rotation.M22 * scale.Y, rotation.M23 * scale.Y, 0f,
                rotation.M31 * scale.Z, rotation.M32 * scale.Z, rotation.M33 * scale.Z, 0f,
                1f, 2f, 3f, 1f);

            var (position, _, decomposedScale) = SmartPropTransform.DecomposeTRS(scaled);

            await Assert.That(position).IsEqualTo(new Vector3(1f, 2f, 3f));
            await Assert.That(decomposedScale.X).IsEqualTo(2f).Within(Tolerance);
            await Assert.That(decomposedScale.Y).IsEqualTo(3f).Within(Tolerance);
            await Assert.That(decomposedScale.Z).IsEqualTo(0.5f).Within(Tolerance);
        }

        [Test]
        public async Task WorldPathOffsetShiftsTranslation()
        {
            var frame = SmartPropTransform.CreateFrame(new Vector3(10f, 0f, 0f), Vector3.UnitX);
            var offset = SmartPropTransform.ApplyPathOffset(frame, new Vector3(1f, 2f, 3f), worldSpace: true);

            await Assert.That(new Vector3(offset.M41, offset.M42, offset.M43)).IsEqualTo(new Vector3(11f, 2f, 3f));
        }

        [Test]
        public async Task LocalPathOffsetShiftsAlongFrameAxes()
        {
            // Frame with forward +X gives left +Y and up +Z
            var frame = SmartPropTransform.CreateFrame(Vector3.Zero, Vector3.UnitX);

            var offset = SmartPropTransform.ApplyPathOffset(frame, new Vector3(2f, 3f, 99f), worldSpace: false);

            // X shifts along left (+Y), Y shifts along up (+Z); the Z component is unused locally
            await Assert.That(new Vector3(offset.M41, offset.M42, offset.M43)).IsEqualTo(new Vector3(0f, 2f, 3f));

            // A rotated frame shifts along its own left/up rows
            var rotated = SmartPropTransform.CreateFrame(Vector3.Zero, Vector3.UnitZ);
            var rotatedOffset = SmartPropTransform.ApplyPathOffset(rotated, new Vector3(1f, 0f, 0f), worldSpace: false);
            var left = new Vector3(rotated.M21, rotated.M22, rotated.M23);
            await Assert.That(new Vector3(rotatedOffset.M41, rotatedOffset.M42, rotatedOffset.M43)).IsEqualTo(left);
        }

        [Test]
        public async Task TransformPointAppliesRowVectorMath()
        {
            var identity = SmartPropTransform.TransformPoint(Matrix4x4.Identity, new Vector3(1f, 2f, 3f));
            await Assert.That(identity).IsEqualTo(new Vector3(1f, 2f, 3f));

            var rotation = EntityTransformHelper.EulerAnglesToRotationMatrix(new Vector3(0f, 90f, 0f));
            var matrix = rotation * Matrix4x4.CreateTranslation(new Vector3(10f, 0f, 0f));

            // Yaw 90 sends +X forward to +Y: (1, 0, 0) rotates to (0, 1, 0), then translates
            var transformed = SmartPropTransform.TransformPoint(matrix, Vector3.UnitX);
            await Assert.That(transformed.X).IsEqualTo(10f).Within(Tolerance);
            await Assert.That(transformed.Y).IsEqualTo(1f).Within(Tolerance);
            await Assert.That(transformed.Z).IsEqualTo(0f).Within(Tolerance);
        }

        private static Vector3 Row(Matrix4x4 matrix, int row) => row switch
        {
            0 => new(matrix.M11, matrix.M12, matrix.M13),
            1 => new(matrix.M21, matrix.M22, matrix.M23),
            _ => new(matrix.M31, matrix.M32, matrix.M33),
        };

        private static float AngleDelta(float a, float b)
        {
            var delta = MathF.Abs(a - b) % 360f;
            return delta > 180f ? 360f - delta : delta;
        }

        private static float RowDelta(Matrix4x4 a, Matrix4x4 b, int row)
            => Vector3.Distance(Row(a, row), Row(b, row));
    }
}
