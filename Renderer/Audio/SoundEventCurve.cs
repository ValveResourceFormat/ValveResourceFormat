using System.Globalization;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// A piecewise mapping curve from sound event data (e.g. "distance_volume_mapping_curve").
/// Each point is [x, y, tangent_in, tangent_out, curve_type_left, curve_type_right]; evaluation is linear between points.
/// </summary>
public sealed class SoundEventCurve
{
    private readonly (float X, float Y)[] points;

    /// <summary>Gets the largest x value covered by the curve.</summary>
    public float MaxX => points[^1].X;

    /// <summary>
    /// Gets whether the curve actually falls off. A flat curve carries no distance information - it is a
    /// constant gain trim left in the data - and using it as an attenuation makes a sound audible at every
    /// distance, so callers treat it as its value rather than as a curve.
    /// </summary>
    public bool Attenuates => points[^1].Y < points[0].Y - 0.0001f;

    private SoundEventCurve((float X, float Y)[] points)
    {
        this.points = points;
    }

    /// <summary>
    /// Creates a two-point linear curve directly, e.g. to represent an authored distance range
    /// ("spread_min"/"spread_max") as a curve without going through <see cref="Parse"/>.
    /// </summary>
    internal static SoundEventCurve Linear(float x0, float y0, float x1, float y1)
    {
        return x0 <= x1
            ? new SoundEventCurve([(x0, y0), (x1, y1)])
            : new SoundEventCurve([(x1, y1), (x0, y0)]);
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

        var cut = new (float X, float Y)[kept + 1];
        points.AsSpan(0, kept).CopyTo(cut);
        cut[kept] = (x, 0f);

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

        var points = new List<(float X, float Y)>(array.Count);

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

            points.Add((
                Convert.ToSingle(point[0], CultureInfo.InvariantCulture),
                decibels ? MathUtils.DecibelsToLinear(y) : y));
        }

        if (points.Count == 0)
        {
            return null;
        }

        points.Sort(static (a, b) => a.X.CompareTo(b.X));

        return new SoundEventCurve([.. points]);
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

        for (var i = 1; i < points.Length; i++)
        {
            if (x <= points[i].X)
            {
                var (x0, y0) = points[i - 1];
                var (x1, y1) = points[i];
                var t = (x - x0) / (x1 - x0);
                return float.Lerp(y0, y1, t);
            }
        }

        return points[^1].Y;
    }
}
