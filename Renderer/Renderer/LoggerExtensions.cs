using System.Threading;
using Microsoft.Extensions.Logging;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// Logging helpers shared across the renderer.
/// </summary>
public static class LoggerExtensions
{
    private static readonly Lock LoggedWarningsLock = new();
    private static readonly HashSet<int> LoggedWarnings = [];

    /// <summary>
    /// Logs a warning the first time it occurs with these arguments, for warnings raised from per-frame
    /// code (an unsupported class, an unknown name) that would otherwise repeat forever.
    /// </summary>
    /// <param name="logger">Logger to warn through.</param>
    /// <param name="message">Message template, e.g. "Unknown sound event {SoundEventName}".</param>
    /// <param name="args">Values for the template, which together with it identify the warning.</param>
    public static void LogUniqueWarning(this ILogger logger, string message, params ReadOnlySpan<object?> args)
    {
        var key = new HashCode();
        key.Add(message);

        foreach (var arg in args)
        {
            key.Add(arg);
        }

        using (LoggedWarningsLock.EnterScope())
        {
            if (!LoggedWarnings.Add(key.ToHashCode()))
            {
                return;
            }
        }

#pragma warning disable CA2254 // The template is a constant at every call site
        logger.LogWarning(message, args.ToArray());
#pragma warning restore CA2254
    }
}
