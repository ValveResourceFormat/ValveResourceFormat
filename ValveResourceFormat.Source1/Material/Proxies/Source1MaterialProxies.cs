using System.Globalization;
using Microsoft.Extensions.Logging;

namespace ValveResourceFormat.Source1.Material.Proxies;

/// <summary>A variable of a material and the component of it a proxy key selects, or -1 for the whole variable.</summary>
internal readonly record struct Source1VarReference(string Name, Source1MaterialVar Var, int Component);

/// <summary>A number a proxy reads, which is either a constant or a variable of the material.</summary>
internal readonly record struct Source1FloatInput(float Constant, Source1VarReference? Reference)
{
    public float Get() => Reference is { } reference ? reference.Var.GetFloat(reference.Component) : Constant;
}

/// <summary>A texture that steps through its frames at a constant rate.</summary>
internal readonly record struct Source1TextureAnimation(int Frames, float FramesPerSecond);

/// <summary>
/// Evaluates the proxies of a Source 1 material over time. The variables the proxies write are kept apart from the
/// parsed material, so they can be bound to the uniforms of the material afterwards.
/// </summary>
internal sealed class Source1MaterialProxies
{
    private static readonly HashSet<string> LoggedUnknownProxies = new(StringComparer.OrdinalIgnoreCase);

    private readonly VmtFile vmt;
    private readonly Func<string, int> getFrameCount;
    private readonly Dictionary<string, Source1MaterialVar> vars = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> writtenNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> writerCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Source1MaterialProxy> proxies = [];
    private float lastTime = float.NaN;

    public IReadOnlyCollection<string> WrittenVars => writtenNames;

    public bool HasProxies => proxies.Count > 0;

    public Random Random { get; }

    public float Time { get; private set; }

    /// <summary>Creates the proxies of a material.</summary>
    /// <param name="vmt">The material whose <c>Proxies</c> block is created.</param>
    /// <param name="seed">The seed of the random numbers of the noise proxies.</param>
    /// <param name="getFrameCount">Returns the number of frames of the texture at a normalized path.</param>
    /// <param name="logger">Receives a warning for a proxy that does not exist or cannot be set up.</param>
    public Source1MaterialProxies(VmtFile vmt, int seed, Func<string, int> getFrameCount, ILogger logger)
    {
        this.vmt = vmt;
        this.getFrameCount = getFrameCount;
        Random = new Random(seed);

        foreach (var definition in vmt.Proxies)
        {
            var proxy = Source1MaterialProxy.Create(definition.Name);

            if (proxy is null)
            {
                LogUnknownProxy(logger, definition.Name);
                continue;
            }

            if (!proxy.Init(this, definition.Keys))
            {
                logger.LogWarning("Material proxy {Proxy} of {Material} is missing a variable", definition.Name, vmt.MaterialPath);
                continue;
            }

            proxies.Add(proxy);
        }
    }

    /// <summary>Evaluates the proxies once for a time. Returns <see langword="false"/> when they already were evaluated for it.</summary>
    public bool Update(float time)
    {
        if (time == lastTime)
        {
            return false;
        }

        lastTime = time;
        Time = time;

        foreach (var proxy in proxies)
        {
            proxy.OnBind();
        }

        return true;
    }

    public Source1MaterialVar? Find(string name)
    {
        if (vars.TryGetValue(name, out var existing))
        {
            return existing;
        }

        if (vmt.Parameters.TryGetValue(name, out var text))
        {
            var parsed = new Source1MaterialVar();
            parsed.SetFromParameter(name, text, vmt.Values);
            vars.Add(name, parsed);
            return parsed;
        }

        // Every texture transform is a parameter of the shader, so it exists without being in the file
        if (Source1MaterialVar.IsTransformName(name))
        {
            var transform = new Source1MaterialVar();
            transform.SetMatrix(IdentityMatrix);
            vars.Add(name, transform);
            return transform;
        }

        return null;
    }

    public Source1VarReference? FindReference(string? text, bool create)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var name = text.Trim();
        var component = -1;
        var bracket = name.IndexOf('[', StringComparison.Ordinal);

        if (bracket > 0 && name.EndsWith(']'))
        {
            var index = name.AsSpan(bracket + 1, name.Length - bracket - 2);

            if (int.TryParse(index, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                component = parsed;
                name = name[..bracket];
            }
        }

        var found = Find(name);

        if (found is null)
        {
            if (!create)
            {
                return null;
            }

            found = new Source1MaterialVar();
            vars.Add(name, found);
        }

        return new Source1VarReference(name, found, component);
    }

    public Source1VarReference? ReadResult(IReadOnlyDictionary<string, string> keys)
    {
        keys.TryGetValue("resultVar", out var text);
        var result = FindReference(text, create: true);

        if (result is { } reference)
        {
            MarkWritten(reference.Name);
        }

        return result;
    }

    public void MarkWritten(string name)
    {
        writtenNames.Add(name);
        writerCounts[name] = writerCounts.GetValueOrDefault(name) + 1;
    }

    public bool IsWritten(string name) => writtenNames.Contains(name);

    /// <summary>
    /// Returns how fast a variable scrolls per second when a single proxy writes it and it grows linearly with the time,
    /// or <see langword="null"/> when it is written by anything else.
    /// </summary>
    public Vector2? GetScrollVelocity(string variable)
    {
        if (writerCounts.GetValueOrDefault(variable) != 1)
        {
            return null;
        }

        foreach (var proxy in proxies)
        {
            if (proxy.GetScrollVelocity(variable) is { } velocity)
            {
                return velocity;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the frames a single proxy steps through at a constant rate for a frame variable, or <see langword="null"/>
    /// when the frame is written by anything else.
    /// </summary>
    public Source1TextureAnimation? GetAnimation(string frameVariable, string textureVariable)
    {
        if (writerCounts.GetValueOrDefault(frameVariable) != 1)
        {
            return null;
        }

        foreach (var proxy in proxies)
        {
            if (proxy.GetAnimation(frameVariable, textureVariable) is { } animation)
            {
                return animation;
            }
        }

        return null;
    }

    public Source1FloatInput ReadFloat(IReadOnlyDictionary<string, string> keys, string key, float fallback)
    {
        if (!keys.TryGetValue(key, out var text))
        {
            return new Source1FloatInput(fallback, null);
        }

        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var constant))
        {
            return new Source1FloatInput(constant, null);
        }

        var reference = FindReference(text, create: true);
        return new Source1FloatInput(fallback, reference);
    }

    public int GetFrameCount(string texturePath) => getFrameCount(texturePath);

    private static readonly float[] IdentityMatrix =
    [
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 1f, 0f,
        0f, 0f, 0f, 1f,
    ];

    private static void LogUnknownProxy(ILogger logger, string name)
    {
        lock (LoggedUnknownProxies)
        {
            if (!LoggedUnknownProxies.Add(name))
            {
                return;
            }
        }

        logger.LogWarning("Unknown material proxy {Proxy}", name);
    }
}
