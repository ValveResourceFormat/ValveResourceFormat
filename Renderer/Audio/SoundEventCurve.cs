using System.Globalization;
using System.Runtime.InteropServices;
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
        CurveTangentType TypeOut,
        int Order);

    private const float XEpsilon = 0.0001f;
    private const float SineSteep = 1.6030499935150146f;
    private const float SineShallow = 0.04133769869804382f;

    private readonly Knot[] points;
    private readonly float outputMin;
    private readonly float outputMax;

    /// <summary>Gets the largest x value covered by the curve.</summary>
    public float MaxX => points[^1].X;

    /// <summary>
    /// Gets whether the curve actually falls off. A flat curve carries no distance information - it is a
    /// constant gain trim left in the data - and using it as an attenuation makes a sound audible at every
    /// distance, so callers treat it as its value rather than as a curve.
    /// </summary>
    public bool Attenuates => points[^1].Y < points[0].Y - 0.0001f;

    private SoundEventCurve(Knot[] points)
    {
        this.points = points;
        outputMin = points[0].Y;
        outputMax = points[0].Y;

        for (var i = 1; i < points.Length; i++)
        {
            outputMin = MathF.Min(outputMin, points[i].Y);
            outputMax = MathF.Max(outputMax, points[i].Y);
        }
    }

    /// <summary>
    /// Creates a straight curve through two points, for events that author a min/max pair instead of a curve.
    /// The points may be given in either order.
    /// </summary>
    internal static SoundEventCurve Linear(float x0, float y0, float x1, float y1)
    {
        if (x0 > x1)
        {
            (x0, x1) = (x1, x0);
            (y0, y1) = (y1, y0);
        }

        x1 = MathF.Max(x1, x0 + XEpsilon);
        var slope = (y1 - y0) / (x1 - x0);

        return new SoundEventCurve([
            new Knot { X = x0, Y = y0, InTangent = slope, OutTangent = slope },
            new Knot { X = x1, Y = y1, InTangent = slope, OutTangent = slope },
        ]);
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

        var left = kept - 1;
        var slope = -cut[left].Y / (x - cut[left].X);
        cut[left].OutTangent = slope;
        cut[kept] = new Knot { X = x, Y = 0f, InTangent = slope };

        return new SoundEventCurve(cut);
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
                    : (CurveTangentType)Convert.ToInt32(point[5], CultureInfo.InvariantCulture),
                points.Count));
        }

        if (points.Count == 0)
        {
            return null;
        }

        return Build(CollectionsMarshal.AsSpan(points));
    }

    private static SoundEventCurve Build(Span<AuthoredPoint> authored)
    {
        authored.Sort(static (a, b) =>
        {
            var byX = a.Knot.X.CompareTo(b.Knot.X);
            return byX != 0 ? byX : a.Order.CompareTo(b.Order);
        });

        var count = 0;

        for (var i = 0; i < authored.Length; i++)
        {
            if (count > 0 && authored[i].Knot.X == authored[count - 1].Knot.X)
            {
                count--;
            }

            authored[count++] = authored[i];
        }

        authored = authored[..count];

        var knots = new Knot[count];

        for (var i = 0; i < count; i++)
        {
            knots[i] = authored[i].Knot;
        }

        for (var i = 1; i < count; i++)
        {
            knots[i].X = MathF.Max(knots[i].X, knots[i - 1].X + XEpsilon);
        }

        if (count > 1)
        {
            ResolveTangents(knots, authored);
        }

        return new SoundEventCurve(knots);
    }

    private static void ResolveTangents(Knot[] knots, ReadOnlySpan<AuthoredPoint> authored)
    {
        for (var i = 0; i < knots.Length; i++)
        {
            ref var knot = ref knots[i];
            var (_, typeIn, typeOut, _) = authored[i];
            var hasPrev = i > 0;
            var hasNext = i + 1 < knots.Length;
            var prev = hasPrev ? knots[i - 1] : knot;
            var next = hasNext ? knots[i + 1] : knot;
            var prevSlope = hasPrev ? Slope(prev, knot) : 0f;
            var nextSlope = hasNext ? Slope(knot, next) : 0f;
            var spanSlope = hasPrev && hasNext ? Slope(prev, next) : hasPrev ? prevSlope : nextSlope;

            var inTangent = typeIn switch
            {
                CurveTangentType.Linear => prevSlope,
                CurveTangentType.Spline => spanSlope,
                CurveTangentType.Mirror => 0f,
                CurveTangentType.Sine => SineTangent(knot.Y > prev.Y ? -SineShallow : -SineSteep, knot.X - prev.X),
                _ => knot.InTangent,
            };

            var outTangent = typeOut switch
            {
                CurveTangentType.Linear => nextSlope,
                CurveTangentType.Spline => spanSlope,
                CurveTangentType.Mirror => inTangent,
                CurveTangentType.Sine => SineTangent(next.Y > knot.Y ? SineSteep : SineShallow, next.X - knot.X),
                _ => knot.OutTangent,
            };

            knot.InTangent = typeIn == CurveTangentType.Mirror ? outTangent : inTangent;
            knot.OutTangent = outTangent;
        }
    }

    private static float Slope(in Knot from, in Knot to)
    {
        return (to.Y - from.Y) / (to.X - from.X);
    }

    private static float SineTangent(float slope, float width)
    {
        return width != 0f ? slope / width : slope;
    }

    /// <summary>Evaluates the curve at the given x, clamping to the first and last points.</summary>
    public float Evaluate(float x)
    {
        if (x <= points[0].X)
        {
            return points[0].Y;
        }

        if (x >= points[^1].X)
        {
            return points[^1].Y;
        }

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

        ref readonly var left = ref points[low - 1];
        ref readonly var right = ref points[low];
        var width = right.X - left.X;
        var t = (x - left.X) / width;
        var t2 = t * t;
        var t3 = t2 * t;

        var h00 = 2f * t3 - 3f * t2 + 1f;
        var h10 = t3 - 2f * t2 + t;
        var h01 = -2f * t3 + 3f * t2;
        var h11 = t3 - t2;

        var value = h00 * left.Y
            + h10 * width * left.OutTangent
            + h01 * right.Y
            + h11 * width * right.InTangent;

        return float.ClampNative(value, outputMin, outputMax);
    }
}
