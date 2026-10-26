using ValveKeyValue;
using ValveResourceFormat.Animation;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Particles.Utils
{
    /// <summary>
    /// A piecewise curve used in particle systems' dynamic parameters: cubic Hermite segments between
    /// keys whose slopes come from each key's tangent modes.
    /// </summary>
    public class PiecewiseCurve
    {
        private const float MinimumKeySpacing = 0.0001f;
        private const float SineSteepSlope = 1.60305f;
        private const float SineShallowSlope = 0.0413377f;

        private enum TangentType
        {
            Linear,
            Spline,
            Free,
            Mirror,
            Sine,
            Flat,
        }

        private readonly Vector2 curveDomainMin;
        private readonly Vector2 curveDomainMax;
        private readonly bool isLooped;

        private readonly float[] keyX;
        private readonly float[] keyY;
        private readonly float[] slopeIncoming;
        private readonly float[] slopeOutgoing;

        /// <summary>Gets the lowest input (X) and output (Y) the curve is defined over.</summary>
        public Vector2 DomainMin => curveDomainMin;

        /// <summary>Gets the highest input (X) and output (Y) the curve is defined over.</summary>
        public Vector2 DomainMax => curveDomainMax;

        /// <summary>
        /// Reads a piecewise curve from its compiled data.
        /// </summary>
        /// <param name="curveInfo">The curve data.</param>
        /// <param name="isLooped">Whether the input wraps with a period of the domain's upper X.</param>
        public PiecewiseCurve(KVObject curveInfo, bool isLooped)
        {
            this.isLooped = isLooped;

            curveDomainMin = curveInfo.GetSubCollection("m_vDomainMins").ToVector2();
            curveDomainMax = curveInfo.GetSubCollection("m_vDomainMaxs").ToVector2();

            var splines = curveInfo.GetArray("m_spline");
            var tangents = curveInfo.GetArray("m_tangents") ?? [];
            var count = Math.Min(splines.Count, tangents.Count);

            keyX = new float[count];
            keyY = new float[count];
            slopeIncoming = new float[count];
            slopeOutgoing = new float[count];

            var incoming = new TangentType[count];
            var outgoing = new TangentType[count];

            for (var i = 0; i < count; i++)
            {
                keyX[i] = splines[i].GetFloatProperty("x");
                keyY[i] = splines[i].GetFloatProperty("y");
                slopeIncoming[i] = splines[i].GetFloatProperty("m_flSlopeIncoming");
                slopeOutgoing[i] = splines[i].GetFloatProperty("m_flSlopeOutgoing");
                incoming[i] = GetTangentType(tangents[i].GetStringProperty("m_nIncomingTangent"));
                outgoing[i] = GetTangentType(tangents[i].GetStringProperty("m_nOutgoingTangent"));

                if (i > 0)
                {
                    keyX[i] = MathF.Max(keyX[i], keyX[i - 1] + MinimumKeySpacing);
                }
            }

            ComputeSlopes(incoming, outgoing);
        }

        private static TangentType GetTangentType(string value) => value switch
        {
            "CURVE_TANGENT_LINEAR" => TangentType.Linear,
            "CURVE_TANGENT_FREE" => TangentType.Free,
            "CURVE_TANGENT_MIRROR" => TangentType.Mirror,
            "CURVE_TANGENT_SINE" => TangentType.Sine,
            "CURVE_TANGENT_FLAT" => TangentType.Flat,
            _ => TangentType.Spline,
        };

        /// <summary>
        /// Replaces the authored slopes with ones derived from the tangent modes, keeping them only for
        /// free tangents. A mirrored incoming tangent takes the outgoing slope.
        /// </summary>
        private void ComputeSlopes(TangentType[] incoming, TangentType[] outgoing)
        {
            var last = keyX.Length - 1;

            for (var i = 0; i <= last; i++)
            {
                float dxPrevious = 0f, dyPrevious = 0f, slopePrevious = 0f;
                float dxNext = 0f, dyNext = 0f, slopeNext = 0f;
                var slopeCentral = 0f;

                if (i > 0)
                {
                    dyPrevious = keyY[i] - keyY[i - 1];
                    dxPrevious = keyX[i] - keyX[i - 1];
                    slopePrevious = dyPrevious / dxPrevious;
                }

                if (i < last)
                {
                    if (i > 0)
                    {
                        slopeCentral = (keyY[i + 1] - keyY[i - 1]) / (keyX[i + 1] - keyX[i - 1]);
                    }

                    dyNext = keyY[i + 1] - keyY[i];
                    dxNext = keyX[i + 1] - keyX[i];
                    slopeNext = dyNext / dxNext;
                }
                else if (i > 0)
                {
                    slopeCentral = slopePrevious;
                }

                if (i == 0)
                {
                    slopeCentral = slopeNext;
                }

                slopeIncoming[i] = incoming[i] switch
                {
                    TangentType.Linear => slopePrevious,
                    TangentType.Spline => slopeCentral,
                    TangentType.Free => slopeIncoming[i],
                    TangentType.Sine => (dxPrevious == 0f ? 1f : 1f / dxPrevious) * (dyPrevious <= 0f ? -SineSteepSlope : -SineShallowSlope),
                    _ => 0f,
                };

                slopeOutgoing[i] = outgoing[i] switch
                {
                    TangentType.Linear => slopeNext,
                    TangentType.Spline => slopeCentral,
                    TangentType.Free => slopeOutgoing[i],
                    TangentType.Mirror => slopeIncoming[i],
                    TangentType.Sine => (dxNext == 0f ? 1f : 1f / dxNext) * (dyNext <= 0f ? SineShallowSlope : SineSteepSlope),
                    _ => 0f,
                };

                if (incoming[i] == TangentType.Mirror)
                {
                    slopeIncoming[i] = slopeOutgoing[i];
                }
            }
        }

        /// <summary>
        /// Evaluates the curve at the given input value. Inputs before the first key or after the last
        /// one hold that key's value, and the result is clamped to the domain's Y range.
        /// </summary>
        public float Evaluate(float value)
        {
            if (keyX.Length < 2)
            {
                return 0f;
            }

            if (isLooped && curveDomainMax.X != 0f)
            {
                value = MathUtils.Fract(value / curveDomainMax.X) * curveDomainMax.X;
            }

            var end = FindSegmentEnd(value);
            var start = end - 1;

            var dx = keyX[end] - keyX[start];
            var t = value - keyX[start];

            if (dx != 0f)
            {
                t /= dx;
            }

            t = MathUtils.Saturate(t);

            var y = CubicHermite.GetPoint(keyY[start], slopeOutgoing[start] * dx, keyY[end], slopeIncoming[end] * dx, t);

            return MathF.Min(MathF.Max(y, curveDomainMin.Y), curveDomainMax.Y);
        }

        /// <summary>
        /// Gets the end of the curve's domain and the value of its last key, which the curve holds from
        /// there on. Returns false for a curve with fewer than two keys.
        /// </summary>
        public bool TryGetEnd(out float x, out float y)
        {
            x = curveDomainMax.X;
            y = 0f;

            if (keyX.Length < 2)
            {
                return false;
            }

            y = keyY[^1];
            return true;
        }

        /// <summary>The first key after the first one that is not below <paramref name="value"/>, or the last key.</summary>
        private int FindSegmentEnd(float value)
        {
            var low = 1;
            var high = keyX.Length - 1;

            while (low < high)
            {
                var middle = (low + high) >> 1;

                if (keyX[middle] < value)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            return low;
        }
    }
}
