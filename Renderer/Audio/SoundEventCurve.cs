using System.Globalization;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// A piecewise mapping curve from sound event data (e.g. "distance_volume_mapping_curve").
/// Each point is [x, y, tangent_in, tangent_out, curve_type_left, curve_type_right]; evaluation is cubic Hermite between points.
/// </summary>
public sealed class SoundEventCurve
{
    private enum CurveTangentType
    {
        Linear = 0,
        Spline = 1,
        Free = 2,
        Mirror = 3,
        Sine = 4,
    }

    private struct Knot
    {
        public float X;
        public float Y;
        public float InTangent;
        public float OutTangent;
    }

    private readonly record struct AuthoredPoint(
        Knot Knot,
        CurveTangentType TypeIn,
        CurveTangentType TypeOut);

    private const float XEpsilon = 0.0001f;
    private const float SineSteep = 1.6030499935150146f;
    private const float SineShallow = 0.04133769869804382f;

    private readonly Knot[] points;
    private readonly float outputMin;
    private readonly float outputMax;
    private readonly bool useLegacyLinearInterpolation;

    /// <summary>Gets the largest x value covered by the curve.</summary>
    public float MaxX => points[^1].X;

    /// <summary>
    /// Gets whether the curve actually falls off. A flat curve carries no distance information - it is a
    /// constant gain trim left in the data - and using it as an attenuation makes a sound audible at every
    /// distance, so callers treat it as its value rather than as a curve.
    /// </summary>
    public bool Attenuates => points[^1].Y < points[0].Y - 0.0001f;

    private SoundEventCurve(Knot[] points, float outputMin, float outputMax, bool useLegacyLinearInterpolation = false)
    {
        this.points = points;
        this.outputMin = outputMin;
        this.outputMax = outputMax;
        this.useLegacyLinearInterpolation = useLegacyLinearInterpolation;
    }

    /// <summary>
    /// Creates a two-point linear curve directly, e.g. to represent an authored distance range
    /// ("spread_min"/"spread_max") as a curve without going through <see cref="Parse"/>.
    /// </summary>
    internal static SoundEventCurve Linear(float x0, float y0, float x1, float y1)
    {
        var input = x0 <= x1
            ? new List<AuthoredPoint>
            {
                new(new Knot { X = x0, Y = y0 }, CurveTangentType.Linear, CurveTangentType.Linear),
                new(new Knot { X = x1, Y = y1 }, CurveTangentType.Linear, CurveTangentType.Linear),
            }
            : new List<AuthoredPoint>
            {
                new(new Knot { X = x1, Y = y1 }, CurveTangentType.Linear, CurveTangentType.Linear),
                new(new Knot { X = x0, Y = y0 }, CurveTangentType.Linear, CurveTangentType.Linear),
            };

        return Build(input, useLegacyLinearInterpolation: true);
    }

    /// <summary>
    /// Returns a copy that reaches silence at <paramref name="x"/> and stays there, for events that pair a
    /// falloff curve with an authored cull distance: a curve whose last point is not silent clamps to that
    /// value, leaving the sound audible past the distance the game stops playing it at.
    /// </summary>
    internal SoundEventCurve WithCutoff(float x)
    {
        if (x <= points[0].X || (points[^1].Y <= 0f && x >= points[^1].X))
        {
            return this;
        }

        var kept = 0;

        while (kept < points.Length && points[kept].X < x)
        {
            kept++;
        }

        var cut = new Knot[kept + 1];
        points.AsSpan(0, kept).CopyTo(cut);
        cut[kept] = new Knot { X = x, Y = 0f };

        var left = kept - 1;
        var slope = (cut[kept].Y - cut[left].Y) / (cut[kept].X - cut[left].X);
        cut[left].OutTangent = slope;
        cut[kept].InTangent = slope;

        return CreateWithMeasuredBounds(cut, useLegacyLinearInterpolation);
    }

    /// <summary>Parses a mapping curve property from sound event data, or returns null when it is missing or empty.</summary>
    /// <param name="soundEventData">The event data holding the curve.</param>
    /// <param name="name">Property name of the curve.</param>
    /// <param name="decibels">Whether the curve's values are decibels, converted to linear gain as they are read.</param>
    public static SoundEventCurve? Parse(KVObject soundEventData, string name, bool decibels = false)
    {
        if (!soundEventData.TryGetValue(name, out var value) || value.ValueType != KVValueType.Array)
        {
            return null;
        }

        var array = soundEventData.GetArray(name);
        if (array == null || array.Count == 0)
        {
            return null;
        }

        var points = new List<AuthoredPoint>(array.Count);

        // Indexed rather than foreach: enumerating the interface-typed list boxes an enumerator per
        // call, and this runs three times per event constructor during cold soundscape-tree builds
        for (var i = 0; i < array.Count; i++)
        {
            var point = array[i];

            // Each point is [x, y, tangents...]; skip malformed points instead of throwing on bad data
            if (point.Count < 2)
            {
                continue;
            }

            var y = Convert.ToSingle(point[1], CultureInfo.InvariantCulture);

            points.Add(new AuthoredPoint(
                new Knot
                {
                    X = Convert.ToSingle(point[0], CultureInfo.InvariantCulture),
                    Y = decibels ? MathUtils.DecibelsToLinear(y) : y,
                    InTangent = point.Count > 2 ? Convert.ToSingle(point[2], CultureInfo.InvariantCulture) : 0f,
                    OutTangent = point.Count > 3 ? Convert.ToSingle(point[3], CultureInfo.InvariantCulture) : 0f,
                },
                decibels || point.Count <= 4
                    ? CurveTangentType.Linear
                    : (CurveTangentType)Convert.ToInt32(point[4], CultureInfo.InvariantCulture),
                decibels || point.Count <= 5
                    ? CurveTangentType.Linear
                    : (CurveTangentType)Convert.ToInt32(point[5], CultureInfo.InvariantCulture)));
        }

        if (points.Count == 0)
        {
            return null;
        }

        return Build(points, useLegacyLinearInterpolation: decibels);
    }

    private static SoundEventCurve Build(List<AuthoredPoint> input, bool useLegacyLinearInterpolation = false)
    {
        // Sort indices, keeping authored order between equal X so a later duplicate wins
        var order = new int[input.Count];
        for (var i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (a, b) =>
        {
            var byX = input[a].Knot.X.CompareTo(input[b].Knot.X);
            return byX != 0 ? byX : a.CompareTo(b);
        });
        var knots = new List<Knot>(input.Count);
        var types = new List<(CurveTangentType TypeIn, CurveTangentType TypeOut)>(input.Count);
        var outputMin = input[0].Knot.Y;
        var outputMax = input[0].Knot.Y;
        AuthoredPoint? previous = null;

        for (var sortedIndex = 0; sortedIndex < order.Length; sortedIndex++)
        {
            var source = input[order[sortedIndex]];
            if (sortedIndex != 0)
            {
                outputMin = BoundsMin(outputMin, source.Knot.Y);
                outputMax = BoundsMax(outputMax, source.Knot.Y);
            }

            if (previous is { } previousValue && source.Knot.X == previousValue.Knot.X)
            {
                knots[^1] = source.Knot;
                previous = source;
                continue;
            }

            knots.Add(source.Knot);
            types.Add((source.TypeIn, source.TypeOut));
            previous = source;
        }

        var result = knots.ToArray();
        ResolveTangents(result, types);
        return new SoundEventCurve(result, outputMin, outputMax, useLegacyLinearInterpolation);
    }

    private static void ResolveTangents(Knot[] result, List<(CurveTangentType TypeIn, CurveTangentType TypeOut)> types)
    {
        if (result.Length == 1)
        {
            ResolveSingleKnotTangents(ref result[0], types[0].TypeIn, types[0].TypeOut);
            return;
        }

        var nextMin = result[0].X + XEpsilon;

        for (var i = 1; i < result.Length; i++)
        {
            result[i].X = MathF.Max(result[i].X, nextMin);
            nextMin = result[i].X + XEpsilon;
        }

        for (var i = 0; i < result.Length; i++)
        {
            ref var knot = ref result[i];
            var (typeIn, typeOut) = types[i];
            var hasPrev = i > 0;
            var hasNext = i + 1 < result.Length;
            var prev = hasPrev ? result[i - 1] : default;
            var next = hasNext ? result[i + 1] : default;
            var prevSlope = hasPrev ? Slope(prev, knot) : 0f;
            var nextSlope = hasNext ? Slope(knot, next) : 0f;
            var span = hasPrev && hasNext ? Slope(prev, next) : (hasPrev ? prevSlope : nextSlope);

            knot.InTangent = typeIn switch
            {
                CurveTangentType.Linear => prevSlope,
                CurveTangentType.Spline => span,
                CurveTangentType.Mirror => 0f,
                CurveTangentType.Sine => SineIncoming(
                    hasPrev ? knot.Y - prev.Y : 0f,
                    hasPrev ? knot.X - prev.X : 0f),
                _ => knot.InTangent,
            };

            knot.OutTangent = typeOut switch
            {
                CurveTangentType.Linear => nextSlope,
                CurveTangentType.Spline => span,
                CurveTangentType.Mirror => knot.InTangent,
                CurveTangentType.Sine => SineOutgoing(
                    hasNext ? next.Y - knot.Y : 0f,
                    hasNext ? next.X - knot.X : 0f),
                _ => knot.OutTangent,
            };

            if (typeIn == CurveTangentType.Mirror)
            {
                knot.InTangent = knot.OutTangent;
            }
        }

    }

    private static void ResolveSingleKnotTangents(ref Knot knot, CurveTangentType typeIn, CurveTangentType typeOut)
    {
        var inTangent = typeIn is CurveTangentType.Linear or CurveTangentType.Spline
            ? 0f
            : typeIn == CurveTangentType.Mirror
                ? 0f
                : typeIn == CurveTangentType.Sine ? -SineSteep : knot.InTangent;
        var outTangent = typeOut is CurveTangentType.Linear or CurveTangentType.Spline
            ? 0f
            : typeOut == CurveTangentType.Mirror
                ? inTangent
                : typeOut == CurveTangentType.Sine ? SineShallow : knot.OutTangent;

        if (typeIn == CurveTangentType.Mirror)
        {
            inTangent = outTangent;
        }
        knot.InTangent = inTangent;
        knot.OutTangent = outTangent;
    }

    private static float Slope(in Knot from, in Knot to)
    {
        return (to.Y - from.Y) / (to.X - from.X);
    }

    private static float SineIncoming(float deltaY, float deltaX)
    {
        var value = deltaY > 0f ? -SineShallow : -SineSteep;
        if (deltaX != 0f)
        {
            value = (1f / deltaX) * value;
        }
        return value;
    }

    private static float SineOutgoing(float deltaY, float deltaX)
    {
        var value = deltaY <= 0f ? SineShallow : SineSteep;
        if (deltaX != 0f)
        {
            value = (1f / deltaX) * value;
        }
        return value;
    }

    private static float BoundsMin(float accumulator, float candidate)
        => candidate < accumulator ? candidate : accumulator;

    private static float BoundsMax(float accumulator, float candidate)
        => candidate > accumulator ? candidate : accumulator;

    private static SoundEventCurve CreateWithMeasuredBounds(Knot[] knots, bool useLegacyLinearInterpolation)
    {
        var minimum = knots[0].Y;
        var maximum = knots[0].Y;
        for (var i = 1; i < knots.Length; i++)
        {
            minimum = BoundsMin(minimum, knots[i].Y);
            maximum = BoundsMax(maximum, knots[i].Y);
        }
        return new SoundEventCurve(knots, minimum, maximum, useLegacyLinearInterpolation);
    }

    /// <summary>Evaluates the curve at the given x, clamping to the first and last points.</summary>
    public float Evaluate(float x)
    {
        if (useLegacyLinearInterpolation)
        {
            if (x <= points[0].X)
            {
                return points[0].Y;
            }
            if (x >= points[^1].X)
            {
                return points[^1].Y;
            }
            for (var i = 1; i < points.Length; i++)
            {
                if (x <= points[i].X)
                {
                    var amount = (x - points[i - 1].X) / (points[i].X - points[i - 1].X);
                    return float.Lerp(points[i - 1].Y, points[i].Y, amount);
                }
            }
            return points[^1].Y;
        }

        if (points.Length == 1)
        {
            return EvaluateMin(EvaluateMax(x, outputMin), outputMax);
        }

        int rightIndex;
        if (x <= points[0].X)
        {
            rightIndex = 1;
        }
        else if (x >= points[^1].X)
        {
            rightIndex = points.Length - 1;
        }
        else
        {
            var low = 1;
            var high = points.Length - 1;
            while (low < high)
            {
                var middle = (low + high) >> 1;
                if (x > points[middle].X)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }
            rightIndex = low;
        }

        var left = points[rightIndex - 1];
        var right = points[rightIndex];
        var width = right.X - left.X;
        var t = x - left.X;
        if (width != 0f)
        {
            t /= width;
        }
        t = t >= 0f ? MathF.Min(1f, t) : 0f;

        var delta = right.Y - left.Y;
        var m0 = left.OutTangent;
        var m1 = right.InTangent;
        var term1 = delta * 3f;
        var term2A = (m1 + m0) * width;
        term2A -= delta + delta;
        term2A *= t;
        var term2B = -m1 - (m0 + m0);
        term2B *= width;
        var value = term2A + term2B;
        value = term1 + value;
        value *= t;
        value += width * m0;
        value *= t;
        value += left.Y;

        return EvaluateMin(EvaluateMax(value, outputMin), outputMax);
    }

    // Engine MINSS/MAXSS take the second operand on ties: +0.0 against a -0.0
    // bound stays -0.0. The shipped corpus pins 130 such evaluations.
    private static float EvaluateMax(float value, float bound) => value > bound ? value : bound;

    private static float EvaluateMin(float value, float bound) => value < bound ? value : bound;
}
