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

    private SoundEventCurve((float X, float Y)[] points)
    {
        this.points = points;
    }

    /// <summary>Parses a mapping curve property from sound event data, or returns null when it is missing or empty.</summary>
    public static SoundEventCurve? Parse(KVObject soundEventData, string name)
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

        foreach (var point in array)
        {
            // Each point is [x, y, tangents...]; skip malformed points instead of throwing on bad data
            if (point.Count < 2)
            {
                continue;
            }

            points.Add((
                Convert.ToSingle(point[0], CultureInfo.InvariantCulture),
                Convert.ToSingle(point[1], CultureInfo.InvariantCulture)));
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
