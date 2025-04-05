using GUI.Utils;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.ParticleRenderer.Utils
{
    class SplineCurve
    {
        public float a { get; set; }
        public float b { get; set; }
        public float c { get; set; }
        public float d { get; set; }
        public Vector2 Start { get; set; }
        public Vector2 End { get; set; }

        public float Evaluate(float x)
        {
            return a + x * (b + x * (c + x * d));
        }

        public bool IsInCurve(float x)
        {
            return x >= Start.X && x <= End.X;
        }
    }
    internal static class CurveFitting
    {
        public static SplineCurve GetCoefficients(Vector2 pos1, Vector2 pos2)
        {
            /*var newVec = new Vector4(pos1.X, 0, pos2.X, 0);
            Matrix4x4 matrix = new();

            matrix.M11 = pos1.Y;
            matrix.M12 = pos1.X;
            matrix.M13 = pos2.Y;
            matrix.M14 = pos1.X;

            Matrix4x4.Invert(matrix, out var newMat);

            var Coefficients = Vector4.Multiply(newVec * newMat);
            */
            // TODO
            return new SplineCurve
            {
                Start = pos1,
                End = pos2,
                a = 0,
                b = 0,
                c = 0,
                d = 0,
            };
        }

        public static SplineCurve GetCoefficients(CurvePoint p0, CurvePoint p1)
        {
            // Here we have to find the coefficients to use to interpolate between p0 and p1.

            // I have no clue what they do to interpolate just from two linear values.

            // There's no way they fit curves in real time. We're working with the same data that the game does.
            // So they have to be doing *something* that lets them interpolate between two
            // curve points with only tangents while still passing through both points.


            // TEMP SOLUTION (sucks): Linear interpolate between points
            return new SplineCurve
            {
                Start = p0.Pos,
                End = p1.Pos,
                a = p0.Y,
                b = (p1.Y - p0.Y) / (p1.X - p0.X),
                c = 0,
                d = 0,
            };
        }
    }
    internal class CurvePoint
    {
        public enum TangentType
        {
            Linear, // Linear, obviously
            Spline, // Cubic
            Free, // Linear but both sides are independent of one another
            Mirror, // Locks onto what the other side is doing
            Sine // uh oh.
        };
        public static TangentType GetTangentType(string value)
        {
            return value switch
            {
                "CURVE_TANGENT_LINEAR" => TangentType.Linear,
                "CURVE_TANGENT_SPLINE" => TangentType.Spline,
                "CURVE_TANGENT_FREE" => TangentType.Free,
                "CURVE_TANGENT_MIRROR" => TangentType.Mirror,
                "CURVE_TANGENT_SINE" => TangentType.Sine,
                _ => throw new NotImplementedException()
            };
        }

        public float SlopeIncoming { get; set; }
        public float SlopeOutgoing { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public TangentType IncomingTangent { get; set; }
        public TangentType OutgoingTangent { get; set; }
        public Vector2 Pos => new(X, Y);
    }

    /// <summary>
    /// A piecewise curve used in particle systems' dynamic parameters.
    /// Vital to the animation of many effects, but also hard as fuck to figure out how they did.
    /// </summary>
    class PiecewiseCurve
    {
        private readonly Vector2 CurveDomainMin;
        private readonly Vector2 CurveDomainMax;
        private readonly SplineCurve[] CurveSegments;
        private readonly bool IsLooped;
        public PiecewiseCurve(KVObject curveInfo, bool isLooped)
        {
            IsLooped = isLooped;

            var domainMin = curveInfo.GetFloatArray("m_vDomainMins");
            var domainMax = curveInfo.GetFloatArray("m_vDomainMaxs");

            CurveDomainMin = new Vector2(domainMin[0], domainMin[1]);
            CurveDomainMax = new Vector2(domainMax[0], domainMin[1]);

            // Gather curve points
            var splines = curveInfo.GetArray("m_spline");
            var tangents = curveInfo.GetArray("m_tangents");

            var CurvePoints = new CurvePoint[splines.Length];

            for (var i = 0; i < splines.Length; i++)
            {
                CurvePoints[i] = new CurvePoint
                {
                    X = splines[i].GetFloatProperty("x"),
                    Y = splines[i].GetFloatProperty("y"),
                    IncomingTangent = CurvePoint.GetTangentType(tangents[i].GetProperty<string>("m_nIncomingTangent")),
                    OutgoingTangent = CurvePoint.GetTangentType(tangents[i].GetProperty<string>("m_nOutgoingTangent")),
                    SlopeIncoming = splines[i].GetFloatProperty("m_flSlopeIncoming"),
                    SlopeOutgoing = splines[i].GetFloatProperty("m_flSlopeIncoming"),
                };
            }

            CurveSegments = new SplineCurve[splines.Length - 1];

            for (var i = 0; i < CurvePoints.Length - 1; i++)
            {
                CurveSegments[i] = CurveFitting.GetCoefficients(CurvePoints[i], CurvePoints[i + 1]);
            }
        }
        private float ClampToDomainSpace(float value)
        {
            var min = CurveDomainMin.X;
            var max = CurveDomainMax.X;

            if (IsLooped)
            {
                // Wrap value past min-max range
                return MathUtils.Wrap(value, min, max);
            }
            else
            {
                // Clamp to edges
                return Math.Clamp(value, min, max);
            }
        }
        public float Evaluate(float value)
        {
            value = ClampToDomainSpace(value);

            // If coordinate is on/before the first point
            if (value <= CurveSegments[0].Start.X)
            {
                return Math.Clamp(CurveSegments[0].Start.Y, CurveDomainMin.Y, CurveDomainMax.Y);
            }
            // If coordinate is on/after the last point
            else if (value >= CurveSegments[^1].End.X)
            {
                return Math.Clamp(CurveSegments[^1].End.Y, CurveDomainMin.Y, CurveDomainMax.Y);
            }
            // If coordinate is on or between two points
            else
            {
                // Find the two points that we want to interpolate between
                for (var i = 0; i < CurveSegments.Length - 1; i++)
                {
                    // If the coordinate is in between two points (the biggie!)
                    if (CurveSegments[i].IsInCurve(value))
                    {
                        value = CurveSegments[i].Evaluate(value);

                        return Math.Clamp(value, CurveDomainMin.Y, CurveDomainMax.Y);
                    }
                }

                // I guess we just return the last point?
                return Math.Clamp(CurveSegments[^1].End.Y, CurveDomainMin.Y, CurveDomainMax.Y);
            }
        }
    }
}
