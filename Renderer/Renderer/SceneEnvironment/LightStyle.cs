using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.Particles.Utils;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.SceneEnvironment;

/// <summary>Looping brightness animation of a light, such as a flicker or a pulse.</summary>
public abstract class LightStyle
{
    /// <summary>Gets the length of one loop in seconds.</summary>
    public abstract float Duration { get; }

    /// <summary>Gets the events fired along the loop, sorted by time.</summary>
    public virtual IReadOnlyList<LightStyleEvent> Events => [];

    /// <summary>Gets the brightness multiplier at a time between zero and <see cref="Duration"/>.</summary>
    public abstract float GetBrightness(float time);
}

/// <summary>Named event a light style fires at a time of its loop.</summary>
public readonly record struct LightStyleEvent(string Name, float Time);

/// <summary>Letter pattern style of older lights, from <c>a</c> for dark through <c>m</c> for normal to <c>z</c>.</summary>
public sealed class PatternLightStyle : LightStyle
{
    private const float LettersPerSecond = 10f;

    // Appearance numbers, 0 is normal
    private static readonly string[] Presets =
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

    /// <summary>Gets the letters stepped through.</summary>
    public string Pattern { get; }

    /// <inheritdoc/>
    public override float Duration => Pattern.Length / LettersPerSecond;

    /// <summary>Initializes a style from its letters.</summary>
    public PatternLightStyle(string pattern)
    {
        ArgumentException.ThrowIfNullOrEmpty(pattern);

        Pattern = pattern;
    }

    /// <summary>Gets a numbered appearance, or <see langword="null"/> for normal and unknown numbers.</summary>
    public static PatternLightStyle? FromAppearance(int appearance)
        => appearance > 0 && appearance < Presets.Length ? new PatternLightStyle(Presets[appearance]) : null;

    /// <inheritdoc/>
    public override float GetBrightness(float time)
    {
        var steps = MathF.Max(time, 0f) * LettersPerSecond;
        var step = MathF.Floor(steps);
        var index = (int)step % Pattern.Length;

        var current = GetBrightness(Pattern[index]);
        var next = GetBrightness(Pattern[(index + 1) % Pattern.Length]);

        return float.Lerp(current, next, steps - step);
    }

    private static float GetBrightness(char letter) => (char.ToLowerInvariant(letter) - 'a') / 12f;
}

/// <summary>Style from <c>scripts/light_styles.vdata</c>, as named by barn, rect and omni2 lights.</summary>
public sealed class CurveLightStyle : LightStyle
{
    private const string LightStylesFile = "scripts/light_styles.vdata";

    private readonly PiecewiseCurve? dimmer;
    private readonly LightStyleEvent[] events;

    /// <inheritdoc/>
    public override float Duration => dimmer != null ? MathF.Max(dimmer.DomainMax.X - dimmer.DomainMin.X, 0f) : 0f;

    /// <inheritdoc/>
    public override IReadOnlyList<LightStyleEvent> Events => events;

    /// <summary>Initializes a style from its light styles entry.</summary>
    public CurveLightStyle(KVObject style)
    {
        ArgumentNullException.ThrowIfNull(style);

        dimmer = style.GetSubCollection("dimmer") is { } curve ? new PiecewiseCurve(curve, isLooped: false) : null;

        events = [.. (style.GetArray("events") ?? [])
            .Select(static e => new LightStyleEvent(e.GetStringProperty("event") ?? string.Empty, e.GetFloatProperty("fire_time")))
            .OrderBy(static e => e.Time)];
    }

    /// <summary>Loads every light style of a game by name.</summary>
    public static Dictionary<string, CurveLightStyle> LoadAll(IFileLoader fileLoader)
    {
        ArgumentNullException.ThrowIfNull(fileLoader);

        var styles = new Dictionary<string, CurveLightStyle>(StringComparer.OrdinalIgnoreCase);
        using var resource = fileLoader.LoadFileCompiled(LightStylesFile);

        if (resource?.DataBlock is not BinaryKV3 kv3)
        {
            return styles;
        }

        foreach (var (name, style) in kv3.Data.Root)
        {
            if (style.ValueType == KVValueType.Collection)
            {
                styles[name] = new CurveLightStyle(style);
            }
        }

        return styles;
    }

    /// <inheritdoc/>
    public override float GetBrightness(float time) => dimmer?.Evaluate(dimmer.DomainMin.X + time) ?? 1f;
}
