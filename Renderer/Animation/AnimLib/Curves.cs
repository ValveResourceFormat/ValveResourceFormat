namespace ValveResourceFormat.Renderer.AnimLib;

static class CubicBezier
{
    public static Vector3 GetPoint(Vector3 p0, Vector3 cp0, Vector3 cp1, Vector3 p1, float t)
    {
        var tSquared = t * t;
        var tCubed = tSquared * t;
        var oneMinusT = 1 - t;
        var oneMinusTSquared = oneMinusT * oneMinusT;
        var oneMinusTCubed = oneMinusTSquared * oneMinusT;

        var a = p0 * oneMinusTCubed;
        var b = cp0 * 3 * t * oneMinusTSquared;
        var c = cp1 * 3 * tSquared * oneMinusT;
        var d = p1 * tCubed;

        return a + b + c + d;
    }

    /// <summary>The length of the curve estimated by discretizing it into line segments.</summary>
    public static float GetEstimatedLength(Vector3 p0, Vector3 cp0, Vector3 cp1, Vector3 p1, uint numDiscretizations = 10)
    {
        var distance = 0f;
        var step = 1.0f / numDiscretizations;
        var currentT = step;
        var previousPoint = p0;

        while (true)
        {
            var currentPoint = GetPoint(p0, cp0, cp1, p1, currentT);
            distance += Vector3.Distance(previousPoint, currentPoint);
            previousPoint = currentPoint;

            if (currentT == 1.0f)
            {
                break;
            }

            currentT = MathF.Min(currentT + step, 1.0f);
        }

        return distance;
    }
}

static class CubicHermite
{
    // 5-point Gauss-Legendre quadrature (abscissa, weight)
    private static readonly (float Abscissa, float Weight)[] GaussLegendreCoefficients =
    [
        (0.0f, 0.5688889f),
        (-0.5384693f, 0.47862867f),
        (0.5384693f, 0.47862867f),
        (-0.90617985f, 0.23692688f),
        (0.90617985f, 0.23692688f),
    ];

    public static Vector3 GetPoint(Vector3 point0, Vector3 tangent0, Vector3 point1, Vector3 tangent1, float t)
    {
        var tSquared = t * t;
        var tCubed = tSquared * t;
        var threeTSquared = 3 * tSquared;
        var twoTCubed = tCubed * 2;

        var a = point0 * (twoTCubed - threeTSquared + 1);
        var b = tangent0 * (tCubed - (2 * tSquared) + t);
        var c = tangent1 * (tCubed - tSquared);
        var d = point1 * (threeTSquared - twoTCubed);
        return a + b + c + d;
    }

    /// <summary>The point and the (unnormalized) tangent at a parameter on the spline.</summary>
    public static (Vector3 Point, Vector3 Tangent) GetPointAndTangent(Vector3 point0, Vector3 tangent0, Vector3 point1, Vector3 tangent1, float t)
    {
        var sixT = 6 * t;
        var tSquared = t * t;
        var tCubed = tSquared * t;
        var threeTSquared = 3 * tSquared;
        var twoTCubed = tCubed * 2;
        var sixTSquared = 6 * tSquared;

        var a = point0 * (twoTCubed - threeTSquared + 1);
        var b = tangent0 * (tCubed - (2 * tSquared) + t);
        var c = tangent1 * (tCubed - tSquared);
        var d = point1 * (threeTSquared - twoTCubed);

        var e = point0 * (sixTSquared - sixT);
        var f = tangent0 * (threeTSquared - (4 * t) + 1);
        var g = tangent1 * (threeTSquared - (2 * t));
        var h = point1 * (sixT - sixTSquared);

        return (a + b + c + d, e + f + g + h);
    }

    /// <summary>The length of a spline segment, by Gauss-Legendre quadrature.</summary>
    public static float GetSplineLength(Vector3 point0, Vector3 tangent0, Vector3 point1, Vector3 tangent1)
    {
        var c0 = tangent0;
        var c1 = ((point1 - point0) * 6.0f) - (tangent0 * 4.0f) - (tangent1 * 2.0f);
        var c2 = ((point0 - point1) * 6.0f) + ((tangent0 + tangent1) * 3.0f);

        var length = 0f;
        foreach (var (abscissa, weight) in GaussLegendreCoefficients)
        {
            // Change of interval from [-1, 1] to [0, 1]
            var t = 0.5f * (1f + abscissa);
            var derivative = c0 + ((c1 + (c2 * t)) * t);
            length += derivative.Length() * weight;
        }

        return 0.5f * length;
    }
}
