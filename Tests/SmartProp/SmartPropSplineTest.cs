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

    }
}
