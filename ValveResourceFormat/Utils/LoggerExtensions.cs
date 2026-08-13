using System.Threading;
using Microsoft.Extensions.Logging;

namespace ValveResourceFormat.Utils;

/// <summary>
/// Logging helpers shared across the renderer.
/// </summary>
public static class LoggerExtensions
{
    private static readonly Lock LoggedWarningsLock = new();
    private static readonly HashSet<long> LoggedWarnings = [];

    /// <summary>
    /// Logs a warning the first time it occurs with these arguments, for warnings raised from per-frame
    /// code (an unsupported class, an unknown name) that would otherwise repeat forever.
    /// </summary>
    /// <param name="logger">Logger to warn through.</param>
    /// <param name="message">Message template, e.g. "Unknown sound event {SoundEventName}".</param>
    /// <param name="args">Values for the template, which together with it identify the warning.</param>
    public static void LogUniqueWarning(this ILogger logger, string message, params ReadOnlySpan<object?> args)
    {
        if (FirstOccurrence(message, args))
        {
            Write(logger, message, args);
        }
    }

    /// <summary>
    /// Logs a warning the first time it occurs for <paramref name="identity"/>, for warnings whose
    /// message also carries context that varies per occurrence - which event asked for it, which file
    /// hit it - without that context making it a warning you have not already seen. The full message is
    /// formatted either way; only <paramref name="identity"/> decides whether it is logged.
    /// </summary>
    /// <param name="logger">Logger to warn through.</param>
    /// <param name="identity">The values that, together with <paramref name="message"/>, identify the warning.</param>
    /// <param name="message">Message template, e.g. "Unsupported type {Type} for {Name}".</param>
    /// <param name="args">Values for the template. Any not in <paramref name="identity"/> are context only.</param>
    public static void LogUniqueWarningFor(this ILogger logger, ReadOnlySpan<object?> identity, string message, params ReadOnlySpan<object?> args)
    {
        if (FirstOccurrence(message, identity))
        {
            Write(logger, message, args);
        }
    }

    private static bool FirstOccurrence(string message, ReadOnlySpan<object?> identity)
    {
        var low = new HashCode();
        var high = new HashCode();
        high.Add(0x9E3779B9u);

        low.Add(message);
        high.Add(message);

        foreach (var value in identity)
        {
            low.Add(value);
            high.Add(value);
        }

        var key = ((long)high.ToHashCode() << 32) | (uint)low.ToHashCode();

        using (LoggedWarningsLock.EnterScope())
        {
            return LoggedWarnings.Add(key);
        }
    }

    private static void Write(ILogger logger, string message, ReadOnlySpan<object?> args)
    {
#pragma warning disable CA2254 // The template is a constant at every call site
        logger.LogWarning(message, args.ToArray());
#pragma warning restore CA2254
    }
}
