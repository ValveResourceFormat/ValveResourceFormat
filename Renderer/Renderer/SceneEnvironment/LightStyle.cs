using System.Linq;
using System.Runtime.CompilerServices;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.SceneEnvironment;

/// <summary>
/// How the brightness and color of a light change over time, such as a flicker or a slow pulse.
/// </summary>
public abstract class LightStyle
{
    /// <summary>Picks the time a light starts the style at, so lights sharing a style need not pulse together.</summary>
    /// <returns>Seconds into the style.</returns>
    public virtual float PickStartTime() => 0f;

    /// <summary>Gets the linear color the color of a light is multiplied by at a time.</summary>
    /// <param name="time">Seconds since the light started the style.</param>
    /// <returns>The multiplier.</returns>
    public abstract Vector3 Evaluate(float time);

    /// <summary>Gets the style the keys of a light entity give it.</summary>
    /// <param name="entity">The keys of the light.</param>
    /// <param name="usesLightStyles">
    /// Whether the light is a barn, rect or omni2 light, which name an entry of <c>scripts/light_styles.vdata</c>,
    /// rather than an older light with a pattern of letters.
    /// </param>
    /// <param name="fileLoader">Where the light styles of the game are loaded from.</param>
    /// <returns>The style, or <see langword="null"/> when the light keeps its color.</returns>
    public static LightStyle? FromEntity(EntityLump.Entity entity, bool usesLightStyles, GameFileLoader? fileLoader)
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (!usesLightStyles)
        {
            return PatternLightStyle.FromEntity(entity);
        }

        // Baked lights stay as they were baked
        if (entity.GetInt32Property("directlight", 2) < 2 || entity.GetStringProperty("light_style") is not { Length: > 0 } name)
        {
            return null;
        }

        return fileLoader != null ? CurveLightStyle.Load(fileLoader, name) : null;
    }
}

/// <summary>
/// A style of an older light: a pattern of letters stepped through ten a second, from <c>a</c> for dark through
/// <c>m</c> for normal brightness to <c>z</c> for about twice that, blending from each letter to the next.
/// </summary>
public sealed class PatternLightStyle : LightStyle
{
    private const float LettersPerSecond = 10f;

    // The styles numbered 0 to 11. The first two never animate, a light set to them keeps its color.
    private static readonly string[] Patterns =
    [
        "m",
        "mmnmmommommnonmmonqnmmo",
        "abcdefghijklmnopqrstuvwxyzyxwvutsrqponmlkjihgfedcba",
        "mmmmmaaaaammmmmaaaaaabcdefgabcdefg",
        "mamamamamama",
        "jklmnopqrstuvwxyzyxwvutsrqponmlkj",
        "nmonqnmomnmomomno",
        "mmmaaaabcdefgmmmmaaaammmaamm",
        "mmmaaammmaaammmabcdefaaaammmmabcdefmmmaaaa",
        "aaaaaaaazzzzzzzz",
        "mmamammmmammamamaaamammma",
        "abcdefghijklmnopqrrqponmlkjihgfedcba",
    ];

    /// <summary>Gets the letters the light steps through.</summary>
    public string Pattern { get; }

    /// <summary>Initializes a style from its letters.</summary>
    /// <param name="pattern">The letters, at least one.</param>
    public PatternLightStyle(string pattern)
    {
        ArgumentException.ThrowIfNullOrEmpty(pattern);

        Pattern = pattern;
    }

    /// <summary>Gets the style of an older light: its own <c>pattern</c>, or else the numbered <c>style</c>.</summary>
    /// <param name="entity">The keys of the light.</param>
    /// <returns>The style, or <see langword="null"/> when the light keeps its color.</returns>
    public static PatternLightStyle? FromEntity(EntityLump.Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (entity.GetStringProperty("pattern") is { Length: > 0 } pattern)
        {
            return new PatternLightStyle(pattern);
        }

        var style = entity.GetInt32Property("style");

        return style > 1 && style < Patterns.Length ? new PatternLightStyle(Patterns[style]) : null;
    }

    /// <inheritdoc/>
    public override Vector3 Evaluate(float time)
    {
        var steps = MathF.Max(time, 0f) * LettersPerSecond;
        var step = MathF.Floor(steps);
        var index = (int)step;

        var current = GetBrightness(Pattern[index % Pattern.Length]);
        var next = GetBrightness(Pattern[(index + 1) % Pattern.Length]);

        return new Vector3(float.Lerp(current, next, steps - step));
    }

    private static float GetBrightness(char letter) => (letter - 'a') / 12f;
}

/// <summary>
/// A style from <c>scripts/light_styles.vdata</c>: a dimmer curve the light loops through, a tint along the same
/// loop, and events fired at times of it, which do not change the light.
/// </summary>
public sealed class CurveLightStyle : LightStyle
{
    private const string LightStylesFile = "scripts/light_styles.vdata";

    private static readonly ConditionalWeakTable<GameFileLoader, Dictionary<string, CurveLightStyle?>> LoadedStyles = [];

    private enum TangentMode
    {
        Linear,
        Spline,
        Free,
        Mirror,
        Sine,
        Flat,
    }

    private readonly record struct CurvePoint(float X, float Y, float SlopeIncoming, float SlopeOutgoing);

    private readonly record struct TintStop(float Position, Color32 Color);

    private readonly CurvePoint[] dimmer;
    private readonly Vector2 domainMins;
    private readonly Vector2 domainMaxs;
    private readonly TintStop[] tint;

    /// <inheritdoc/>
    /// <remarks>Somewhere along the loop of the dimmer, a different time for each light.</remarks>
    public override float PickStartTime() => float.Lerp(domainMins.X, domainMaxs.X, Random.Shared.NextSingle());

    /// <summary>Initializes a style from its entry in the light styles of a game.</summary>
    /// <param name="style">The entry, with its <c>dimmer</c> curve and <c>tint</c> gradient.</param>
    public CurveLightStyle(KVObject style)
    {
        ArgumentNullException.ThrowIfNull(style);

        var curve = style.GetSubCollection("dimmer");
        dimmer = curve != null ? ReadCurve(curve) : [];

        if (curve != null)
        {
            domainMins = ReadVector2(curve, "m_vDomainMins");
            domainMaxs = ReadVector2(curve, "m_vDomainMaxs");
        }

        tint = style.GetSubCollection("tint") is { } gradient ? ReadGradient(gradient) : [];
    }

    /// <summary>Loads a style of the game.</summary>
    /// <param name="fileLoader">Where the light styles of the game are loaded from.</param>
    /// <param name="name">The name of the style.</param>
    /// <returns>The style, or <see langword="null"/> when the game has none of that name.</returns>
    public static CurveLightStyle? Load(GameFileLoader fileLoader, string name)
    {
        ArgumentNullException.ThrowIfNull(fileLoader);
        ArgumentNullException.ThrowIfNull(name);

        var styles = LoadedStyles.GetValue(fileLoader, static _ => []);

        lock (styles)
        {
            if (!styles.TryGetValue(name, out var style))
            {
                using var resource = fileLoader.LoadFileCompiled(LightStylesFile);

                style = resource?.DataBlock is BinaryKV3 { Data.Root: var root } && root.GetSubCollection(name) is { } data
                    ? new CurveLightStyle(data)
                    : null;

                styles[name] = style;
            }

            return style;
        }
    }

    /// <inheritdoc/>
    public override Vector3 Evaluate(float time)
    {
        var dimness = 1f;
        var position = 0.5f;

        if (dimmer.Length > 0)
        {
            var start = domainMins.X;
            var length = domainMaxs.X - start;
            var x = start;

            // Loops over the domain, where a whole number of loops lands on its end rather than its start
            if (length > 0f)
            {
                var elapsed = time - start;
                elapsed -= MathF.Ceiling(elapsed / length - 1f) * length;
                position = elapsed / length;
                x = elapsed + start;
            }

            dimness = EvaluateCurve(x);
        }

        return EvaluateGradient(position).ToLinearColor().AsVector3() * dimness;
    }

    private float EvaluateCurve(float x)
    {
        var value = x;

        if (dimmer.Length >= 2)
        {
            var low = 1;
            var high = dimmer.Length - 1;
            var index = 0;
            var found = false;

            while (low <= high)
            {
                index = (low + high) >> 1;

                if (x > dimmer[index].X)
                {
                    low = index + 1;
                }
                else if (dimmer[index].X > x)
                {
                    high = index - 1;
                }
                else
                {
                    found = true;
                    break;
                }
            }

            if (!found && dimmer[index].X <= x && index < dimmer.Length - 1)
            {
                index++;
            }

            var previous = dimmer[index - 1];
            var next = dimmer[index];
            var width = next.X - previous.X;
            var height = next.Y - previous.Y;
            var t = x - previous.X;

            if (width != 0f)
            {
                t /= width;
            }

            t = t >= 0f ? MathF.Min(1f, t) : 0f;

            // Cubic Hermite through both points with their slopes
            var outgoing = previous.SlopeOutgoing;
            var incoming = next.SlopeIncoming;

            value = ((((outgoing + incoming) * width - 2f * height) * t
                + ((-incoming - 2f * outgoing) * width + 3f * height)) * t
                + outgoing * width) * t
                + previous.Y;
        }

        return MathF.Min(MathF.Max(value, domainMins.Y), domainMaxs.Y);
    }

    private Color32 EvaluateGradient(float position)
    {
        if (tint.Length == 0)
        {
            return Color32.White;
        }

        var low = 0;
        var high = tint.Length - 1;
        var index = -2;

        while (low <= high)
        {
            var middle = (low + high) >> 1;

            if (position > tint[middle].Position)
            {
                low = middle + 1;
            }
            else if (tint[middle].Position > position)
            {
                high = middle - 1;
            }
            else
            {
                index = middle;
                break;
            }
        }

        if (index == -2)
        {
            index = high;
        }

        if (index == -1)
        {
            return tint[0].Color;
        }

        var nextIndex = index == tint.Length - 1 ? index : index + 1;
        var (from, to) = (tint[index], tint[nextIndex]);

        if (index == nextIndex || to.Position - from.Position < 0.00001f)
        {
            return from.Color;
        }

        var fraction = (position - from.Position) / (to.Position - from.Position);

        static byte Blend(byte a, byte b, float fraction) => (byte)(int)((b - a) * fraction + a);

        return new Color32(
            Blend(from.Color.R, to.Color.R, fraction),
            Blend(from.Color.G, to.Color.G, fraction),
            Blend(from.Color.B, to.Color.B, fraction),
            Blend(from.Color.A, to.Color.A, fraction));
    }

    private static Vector2 ReadVector2(KVObject data, string key)
    {
        var values = data.GetFloatArray(key);

        return values.Length >= 2 ? new Vector2(values[0], values[1]) : Vector2.Zero;
    }

    // The points the curve passes through, each with the slopes its tangent modes give it
    private static CurvePoint[] ReadCurve(KVObject curve)
    {
        var splines = curve.GetArray("m_spline") ?? [];
        var tangents = curve.GetArray("m_tangents") ?? [];
        var count = Math.Min(splines.Count, tangents.Count);

        var points = new CurvePoint[count];
        var modes = new (TangentMode Incoming, TangentMode Outgoing)[count];

        for (var i = 0; i < count; i++)
        {
            var spline = splines[i];
            var x = spline.GetFloatProperty("x");

            // Each point lies at least a little past the one before it
            if (i > 0)
            {
                x = MathF.Max(x, points[i - 1].X + 0.0001f);
            }

            points[i] = new CurvePoint(x, spline.GetFloatProperty("y"), spline.GetFloatProperty("m_flSlopeIncoming"), spline.GetFloatProperty("m_flSlopeOutgoing"));
            modes[i] = (ReadTangentMode(tangents[i], "m_nIncomingTangent"), ReadTangentMode(tangents[i], "m_nOutgoingTangent"));
        }

        for (var i = 0; i < count; i++)
        {
            points[i] = ApplyTangentModes(points, i, modes[i].Incoming, modes[i].Outgoing);
        }

        return points;
    }

    private static CurvePoint ApplyTangentModes(CurvePoint[] points, int i, TangentMode incomingMode, TangentMode outgoingMode)
    {
        var point = points[i];
        var last = points.Length - 1;

        var (previousWidth, previousHeight, previousSlope) = (0f, 0f, 0f);
        var (nextWidth, nextHeight, nextSlope) = (0f, 0f, 0f);

        if (i > 0)
        {
            previousWidth = point.X - points[i - 1].X;
            previousHeight = point.Y - points[i - 1].Y;
            previousSlope = previousHeight / previousWidth;
        }

        if (i < last)
        {
            nextWidth = points[i + 1].X - point.X;
            nextHeight = points[i + 1].Y - point.Y;
            nextSlope = nextHeight / nextWidth;
        }

        // Through the points on either side, or along the only segment at an end
        var smoothSlope = i == 0
            ? nextSlope
            : i == last ? previousSlope : (points[i + 1].Y - points[i - 1].Y) / (points[i + 1].X - points[i - 1].X);

        var incoming = incomingMode switch
        {
            TangentMode.Linear => previousSlope,
            TangentMode.Spline => smoothSlope,
            TangentMode.Mirror or TangentMode.Flat => 0f,
            TangentMode.Sine => (previousWidth == 0f ? 1f : 1f / previousWidth) * (previousHeight <= 0f ? -1.60305f : -0.0413377f),
            _ => point.SlopeIncoming,
        };

        var outgoing = outgoingMode switch
        {
            TangentMode.Linear => nextSlope,
            TangentMode.Spline => smoothSlope,
            TangentMode.Mirror => incoming,
            TangentMode.Sine => (nextWidth == 0f ? 1f : 1f / nextWidth) * (nextHeight <= 0f ? 0.0413377f : 1.60305f),
            TangentMode.Flat => 0f,
            _ => point.SlopeOutgoing,
        };

        if (incomingMode == TangentMode.Mirror)
        {
            incoming = outgoing;
        }

        return point with { SlopeIncoming = incoming, SlopeOutgoing = outgoing };
    }

    private static TangentMode ReadTangentMode(KVObject tangent, string key)
    {
        if (!tangent.TryGetValue(key, out var value))
        {
            return TangentMode.Linear;
        }

        if (value.ValueType == KVValueType.String)
        {
            return (string)value switch
            {
                "CURVE_TANGENT_SPLINE" => TangentMode.Spline,
                "CURVE_TANGENT_FREE" => TangentMode.Free,
                "CURVE_TANGENT_MIRROR" => TangentMode.Mirror,
                "CURVE_TANGENT_SINE" => TangentMode.Sine,
                "CURVE_TANGENT_FLAT" => TangentMode.Flat,
                _ => TangentMode.Linear,
            };
        }

        return (TangentMode)tangent.GetInt32Property(key);
    }

    // The stops sorted along the gradient, each within it
    private static TintStop[] ReadGradient(KVObject gradient)
    {
        var stops = gradient.GetArray("m_Stops") ?? [];

        return [.. stops
            .Select(static stop =>
            {
                var color = stop.GetIntegerArray("m_Color");

                byte Channel(int index) => index < color.Length ? (byte)color[index] : byte.MaxValue;

                return new TintStop(Math.Clamp(stop.GetFloatProperty("m_flPosition"), 0f, 1f), new Color32(Channel(0), Channel(1), Channel(2), Channel(3)));
            })
            .OrderBy(static stop => stop.Position)];
    }
}
