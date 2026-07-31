using System.Threading;
using Microsoft.Extensions.Logging;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// Logging helpers shared across the renderer.
/// </summary>
public static class LoggerExtensions
{
    private static readonly Lock LoggedWarningsLock = new();

    // Warnings are identified by a hash of the template and its arguments rather than by the formatted
    // string: the suppressed repeats are the common case (this is called from per-frame code), and
    // formatting a key on every one of them would be pure garbage. Two independently salted 32-bit
    // hashes, so a collision - which silently swallows an unrelated warning - stays out of reach.
    // The set only ever grows to the number of distinct warnings the loaded content produces.
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
        var low = new HashCode();
        var high = new HashCode();
        high.Add(0x9E3779B9u);

        low.Add(message);
        high.Add(message);

        foreach (var arg in args)
        {
            low.Add(arg);
            high.Add(arg);
        }

        var key = ((long)high.ToHashCode() << 32) | (uint)low.ToHashCode();

        using (LoggedWarningsLock.EnterScope())
        {
            if (!LoggedWarnings.Add(key))
            {
                return;
            }
        }

#pragma warning disable CA2254 // The template is a constant at every call site
        logger.LogWarning(message, args.ToArray());
#pragma warning restore CA2254
    }
}
