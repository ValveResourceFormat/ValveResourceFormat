namespace ValveResourceFormat.ResourceTypes.SmartProps
{
    /// <summary>
    /// A dense sample along a smart prop path curve: a position on the curve, the
    /// normalized tangent there, and the cumulative distance from the curve start.
    /// </summary>
    public readonly record struct SmartPropPathSample(Vector3 Position, Vector3 Tangent, float Distance);

    /// <summary>
    /// Builds smooth Centripetal Catmull-Rom splines through path control points
    /// (alpha 0.5, matching Source 2's interpolation), parameterizes them by arc
    /// length, and interpolates positions and tangents at arbitrary distances.
    /// </summary>
    public static class SmartPropSpline
    {
        /// <summary>Default interpolation density between two control points.</summary>
        public const int DefaultSamplesPerSegment = 64;

        /// <summary>
        /// Computes a smooth curve through the control points. The curve starts at the
        /// first control point, ends at the last, and passes through every control point
        /// in between. Boundary control points are linear reflections of their neighbors
        /// (P_-1 = 2*P_0 - P_1), and the centripetal parameterization avoids cusps and
        /// overshoot on non-uniformly spaced points.
        /// </summary>
        public static Vector3[] CentripetalCatmullRom(ReadOnlySpan<Vector3> points, float alpha = 0.5f, int samplesPerSegment = DefaultSamplesPerSegment)
        {
            if (points.Length == 0)
            {
                return [];
            }

            if (points.Length < 2 || samplesPerSegment <= 0)
            {
                return [points[0]];
            }

            var curve = new Vector3[(points.Length - 1) * samplesPerSegment + 1];
            var p0 = 2f * points[0] - points[1];
            var pLast = 2f * points[^1] - points[^2];
            var outIndex = 0;

            // Segment i interpolates between points[i] and points[i+1], borrowing the
            // neighbors (or their reflections at the ends) as outer control points
            for (var i = 0; i < points.Length - 1; i++)
            {
                var r0 = i == 0 ? p0 : points[i - 1];
                var r1 = points[i];
                var r2 = points[i + 1];
                var r3 = i + 2 < points.Length ? points[i + 2] : pLast;

                var t0 = 0f;
                var t1 = t0 + KnotSpacing(r0, r1, alpha);
                var t2 = t1 + KnotSpacing(r1, r2, alpha);
                var t3 = t2 + KnotSpacing(r2, r3, alpha);

                for (var s = 0; s < samplesPerSegment; s++)
                {
                    var t = t1 + ((t2 - t1) * (s / (float)samplesPerSegment));

                    // Barry-Goldman three level lerp
                    var a1 = Lerp(r0, r1, InvLerp(t0, t1, t));
                    var a2 = Lerp(r1, r2, InvLerp(t1, t2, t));
                    var a3 = Lerp(r2, r3, InvLerp(t2, t3, t));

                    var b1 = Lerp(a1, a2, InvLerp(t0, t2, t));
                    var b2 = Lerp(a2, a3, InvLerp(t1, t3, t));

                    curve[outIndex++] = Lerp(b1, b2, InvLerp(t1, t2, t));
                }
            }

            curve[^1] = points[^1];
            return curve;
        }

        /// <summary>
        /// Computes dense curve samples with positions, normalized tangents and cumulative
        /// distance. With <paramref name="projectedUp"/>, distances accumulate on the plane
        /// perpendicular to it instead of true arc length, so instance spacing ignores how
        /// steeply the path climbs along that axis.
        /// </summary>
        public static (SmartPropPathSample[] Samples, float TotalLength) ComputeSamples(
            ReadOnlySpan<Vector3> points,
            int samplesPerSegment = DefaultSamplesPerSegment,
            Vector3? projectedUp = null)
        {
            var curve = CentripetalCatmullRom(points, 0.5f, samplesPerSegment);
            if (curve.Length == 0)
            {
                return ([], 0f);
            }

            if (curve.Length == 1)
            {
                return ([new SmartPropPathSample(curve[0], Vector3.UnitX, 0f)], 0f);
            }

            Vector3? projectionNormal = null;
            if (projectedUp is { } up && up.LengthSquared() > 1e-12f)
            {
                projectionNormal = Vector3.Normalize(up);
            }

            var samples = new SmartPropPathSample[curve.Length];
            var totalLength = 0f;

            for (var i = 0; i < curve.Length; i++)
            {
                Vector3 tangent;
                float distance;
                if (i == 0)
                {
                    tangent = curve[1] - curve[0];
                    distance = 0f;
                }
                else
                {
                    var segment = curve[i] - curve[i - 1];
                    tangent = i < curve.Length - 1 ? curve[i + 1] - curve[i - 1] : segment;
                    if (projectionNormal is { } normal)
                    {
                        segment -= Vector3.Dot(segment, normal) * normal;
                    }

                    totalLength += segment.Length();
                    distance = totalLength;
                }

                tangent = tangent.LengthSquared() > 1e-14f ? Vector3.Normalize(tangent) : Vector3.UnitX;
                samples[i] = new SmartPropPathSample(curve[i], tangent, distance);
            }

            return (samples, totalLength);
        }

        /// <summary>
        /// Interpolates the position and normalized tangent at a distance along the dense
        /// samples, binary searching the cumulative distances. Distances outside the curve
        /// clamp to its start and end.
        /// </summary>
        public static (Vector3 Position, Vector3 Tangent) InterpolateAtDistance(
            ReadOnlySpan<SmartPropPathSample> samples,
            float totalLength,
            float targetDistance)
        {
            if (samples.Length == 0)
            {
                return (Vector3.Zero, Vector3.UnitX);
            }

            if (targetDistance <= samples[0].Distance)
            {
                return (samples[0].Position, samples[0].Tangent);
            }

            if (targetDistance >= totalLength)
            {
                return (samples[^1].Position, samples[^1].Tangent);
            }

            var low = 0;
            var high = samples.Length - 1;
            while (low <= high)
            {
                var mid = (low + high) / 2;
                if (samples[mid].Distance < targetDistance)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            var index0 = Math.Clamp(high, 0, samples.Length - 2);
            var index1 = index0 + 1;

            var first = samples[index0];
            var second = samples[index1];

            var segmentLength = second.Distance - first.Distance;
            var fraction = segmentLength > 1e-8f ? Math.Clamp((targetDistance - first.Distance) / segmentLength, 0f, 1f) : 0f;

            var position = Vector3.Lerp(first.Position, second.Position, fraction);
            var tangent = Vector3.Lerp(first.Tangent, second.Tangent, fraction);
            tangent = tangent.LengthSquared() > 1e-14f ? Vector3.Normalize(tangent) : Vector3.UnitX;

            return (position, tangent);
        }

        private static float KnotSpacing(Vector3 a, Vector3 b, float alpha)
        {
            var spacing = MathF.Pow(Vector3.Distance(a, b), alpha);
            return spacing > 1e-7f ? spacing : 1e-7f;
        }

        private static float InvLerp(float from, float to, float value) => (value - from) / (to - from);

        private static Vector3 Lerp(Vector3 from, Vector3 to, float t) => from + ((to - from) * t);
    }
}
