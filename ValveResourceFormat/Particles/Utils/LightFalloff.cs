namespace ValveResourceFormat.Particles.Utils
{
    /// <summary>A light's distance falloff, <c>1 / (constant + linear*d + quadratic*d^2)</c>.</summary>
    internal readonly record struct LightFalloff(float Constant, float Linear, float Quadratic)
    {
        /// <summary>
        /// A distance past which the light is cut off and the intensity subtracted so it reaches
        /// zero there, both zero when the light has none.
        /// </summary>
        public readonly record struct Cutoff(float Range, float Offset);

        public bool IsFinite => float.IsFinite(Constant) && float.IsFinite(Linear) && float.IsFinite(Quadratic);

        /// <summary>The falloff's denominator at <paramref name="distance"/>.</summary>
        public float Denominator(float distance) => Constant + (Linear * distance) + (Quadratic * distance * distance);

        /// <summary>
        /// Fits the falloff to 1 at the light, 1/2 at <paramref name="fiftyDistance"/> and 1/256 at
        /// <paramref name="zeroDistance"/>, then scales it to exactly 1/2 at the 50% distance. A 0%
        /// distance below the 50% one is replaced by twice the 50% distance.
        /// </summary>
        public static LightFalloff Solve(float fiftyDistance, float zeroDistance)
        {
            if (fiftyDistance > zeroDistance)
            {
                zeroDistance = fiftyDistance + fiftyDistance;
            }

            var (quadratic, linear, constant) = FitQuadratic(0f, 1f, fiftyDistance, 2f, zeroDistance, 256f) ?? (0f, 1f, 0f);
            var scale = 2f / ((((quadratic * fiftyDistance) + linear) * fiftyDistance) + constant);

            return new LightFalloff(constant * scale, linear * scale, quadratic * scale);
        }

        /// <summary>
        /// The cutoff of a falloff without a constant term: where the light falls to a thousandth
        /// of its luminance.
        /// </summary>
        public Cutoff CutoffFor(Vector3 color)
        {
            if (Constant != 0f)
            {
                return default;
            }

            var range = 0f;
            var luminance = Vector3.Dot(color, ParticleMath.LuminanceWeights);

            if (luminance > 0f)
            {
                var scale = 0.001f / luminance;

                if (SolveQuadratic(Quadratic * scale, Linear * scale, (Constant * scale) - 1f) is var (root1, root2))
                {
                    range = MathF.Max(0f, MathF.Max(root1, root2));
                }
            }

            return range > 0f
                ? new Cutoff(range, 1f / ((range * range * Quadratic) + ((range * Linear) + ParticleMath.FloatEpsilon)))
                : default;
        }

        public float Evaluate(float distanceSquared, Cutoff cutoff)
        {
            distanceSquared = MathF.Max(1f, distanceSquared);

            var constant = Constant != 0f ? Constant : ParticleMath.FloatEpsilon;
            var intensity = 1f / (constant + (Linear * MathF.Sqrt(distanceSquared)) + (Quadratic * distanceSquared));

            if (cutoff.Offset != 0f)
            {
                intensity = MathF.Max(0f, intensity - cutoff.Offset);
            }

            if (cutoff.Range != 0f && !(distanceSquared < cutoff.Range * cutoff.Range))
            {
                return 0f;
            }

            return intensity;
        }

        /// <summary>
        /// The quadratic through three points, as (x^2, x, 1) coefficients, or null when two points
        /// share an x. When the middle point would make a monotonic curve turn back at x = 1, it is
        /// moved toward the straight line between the outer points in steps of 5%.
        /// </summary>
        private static (float A, float B, float C)? FitQuadratic(float x0, float y0, float x1, float y1, float x2, float y2)
        {
            if (x0 > x1)
            {
                (x0, y0, x1, y1) = (x1, y1, x0, y0);
            }

            if (x1 > x2)
            {
                (x1, y1, x2, y2) = (x2, y2, x1, y1);
            }

            if (x0 > x1)
            {
                (x0, y0, x1, y1) = (x1, y1, x0, y0);
            }

            var determinant = (x0 - x1) * (x0 - x2) * (x1 - x2);

            if (determinant == 0f)
            {
                return null;
            }

            var linearY1 = ((x1 - x0) * (y2 - y0) / (x2 - x0)) + y0;
            var blend = 0f;

            while (true)
            {
                var middle = (linearY1 * blend) + ((1f - blend) * y1);

                var a = (((middle - y0) * x2) + ((y0 - y2) * x1) + ((y2 - middle) * x0)) / determinant;
                var b = (((y0 - middle) * (x2 * x2)) + ((middle - y2) * (x0 * x0)) + ((x1 * x1) * (y2 - y0))) / determinant;
                var c = (((((y0 * x2) - (x0 * y2)) * (x1 * x1)) + ((x0 * x2 * (x2 - x0)) * middle)) + (((x0 * x0 * y2) - (x2 * x2 * y0)) * x1)) / determinant;

                var slope = a + a + b;
                var turnsBack = y0 < y1 && y1 < y2
                    ? slope < 0f
                    : y0 > y1 && y1 > y2 && slope > 0f;

                if (!turnsBack)
                {
                    return (a, b, c);
                }

                blend += 0.05f;

                if (blend > 1f)
                {
                    return (a, b, c);
                }
            }
        }

        /// <summary>The roots of <c>a*x^2 + b*x + c</c>, or null when it has none.</summary>
        private static (float, float)? SolveQuadratic(float a, float b, float c)
        {
            if (a == 0f)
            {
                if (b != 0f)
                {
                    return (-c / b, -c / b);
                }

                return c == 0f ? (0f, 0f) : null;
            }

            var discriminant = (b * b) - (a * 4f * c);

            if (discriminant < 0f)
            {
                return null;
            }

            var root = MathF.Sqrt(discriminant);
            return ((root - b) / (a + a), (-b - root) / (a + a));
        }
    }
}
