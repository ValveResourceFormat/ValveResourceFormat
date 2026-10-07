namespace ValveResourceFormat.ResourceTypes.SmartProps
{
    /// <summary>
    /// A path of cubic Bezier segments between knots, parameterised by arc length per knot span.
    /// </summary>
    internal sealed class SmartPropPathCurve
    {
        public readonly record struct Knot(Vector3 Position, Vector3 InTangent, Vector3 OutTangent);

        public List<Knot> Knots { get; } = [];
        public float[] Parameters { get; private set; } = [];
        public float TotalLength { get; private set; }

        /// <summary>
        /// Builds a curve through the points with Catmull-Rom tangent directions; fails with fewer than two points.
        /// </summary>
        public static SmartPropPathCurve? Build(IReadOnlyList<Vector3> points)
        {
            var count = points.Count;

            if (count < 2)
            {
                return null;
            }

            var curve = new SmartPropPathCurve();

            for (var i = 0; i < count; i++)
            {
                var previous = points[Math.Max(i - 1, 0)];
                var next = points[Math.Min(i + 1, count - 1)];
                var current = points[i];

                curve.Knots.Add(new Knot(current, KnotTangent(next, current, previous), KnotTangent(previous, current, next)));
            }

            curve.ComputeArcLengths();
            return curve;
        }

        private static Vector3 KnotTangent(Vector3 a, Vector3 b, Vector3 c) => MathUtils.SafeNormalize(c - a) * Vector3.Distance(b, c) / 3f;

        private (Vector3 P0, Vector3 P1, Vector3 P2, Vector3 P3) Segment(int index)
        {
            var from = Knots[index];
            var to = Knots[index + 1];
            return (from.Position, from.Position + from.OutTangent, to.Position + to.InTangent, to.Position);
        }

        private void ComputeArcLengths()
        {
            Parameters = new float[Knots.Count];
            var total = 0f;

            for (var i = 0; i < Knots.Count - 1; i++)
            {
                var (p0, p1, p2, p3) = Segment(i);
                total += ApproximateLength(p0, p1, p2, p3);
                Parameters[i + 1] = total;
            }

            TotalLength = total;

            if (total > 0f)
            {
                for (var i = 0; i < Parameters.Length; i++)
                {
                    Parameters[i] /= total;
                }
            }
        }

        private static float ApproximateLength(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
        {
            var a = -p0 + 3f * p1 - 3f * p2 + p3;
            var b = 3f * p0 - 6f * p1 + 3f * p2;
            var c = 3f * p1 - 3f * p0;

            Vector3 Point(float t) => (a * t * t * t + b * t * t) + c * t + p0;

            var length = 0f;

            for (var samples = 2; samples <= 128; samples *= 2)
            {
                var previousLength = length;
                length = 0f;

                for (var k = 1; k < samples; k++)
                {
                    length += Vector3.Distance(Point(k / (float)(samples - 1)), Point((k - 1) / (float)(samples - 1)));
                }

                if (0.01f > MathF.Abs(length - previousLength) / length)
                {
                    break;
                }
            }

            return length;
        }

        /// <summary>
        /// Evaluates position and unit tangent at parameter <paramref name="t"/>, clamped to the curve.
        /// </summary>
        public (Vector3 Position, Vector3 Tangent) Evaluate(float t)
        {
            var clamped = Math.Clamp(t, Parameters[0], Parameters[^1]);
            var index = 0;

            while (index < Parameters.Length - 2 && clamped > Parameters[index + 1])
            {
                index++;
            }

            var span = Parameters[index + 1] - Parameters[index];
            var u = Parameters[index] < Parameters[index + 1] ? Math.Clamp((clamped - Parameters[index]) / span, 0f, 1f) : 0f;
            var (p0, p1, p2, p3) = Segment(index);
            var v = 1f - u;

            var position = ((p1 * (3f * v * v * u) + p0 * (v * v * v)) + p2 * (3f * v * u * u)) + p3 * (u * u * u);
            var tangent = 3f * (p1 - p0) + 2f * u * (3f * p0 - 6f * p1 + 3f * p2) + 3f * u * u * (-p0 + 3f * p1 - 3f * p2 + p3);

            return (position, MathUtils.SafeNormalize(tangent));
        }
    }
}
