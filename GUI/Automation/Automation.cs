using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace GUI.Automation;

/// <summary>
/// The seam between the viewer and the automation server. Only debug builds compile the server, so
/// callers go through here and get a no-op in other builds. Debug-only code calls the server's
/// classes directly.
/// </summary>
internal static class Automation
{
    private const int DefaultPort = 13338;

    /// <summary>Port the server listens on, or null when it is not running.</summary>
    private static int? Port;

#if DEBUG
    /// <summary>Whether this process was started to be driven by an agent.</summary>
    public static bool IsEnabled => Port != null;
#else
    /// <summary>Always false, because only debug builds compile the server. It is a constant, so every check of it compiles away.</summary>
    public static bool IsEnabled => false;
#endif

    /// <summary>
    /// Removes <c>--mcp</c> or <c>--mcp=port</c> from <paramref name="args"/> before anything else
    /// reads them. The switch is then never forwarded to another instance or opened as a file.
    /// </summary>
    public static void TakeSwitch(ref string[] args)
    {
        static bool IsSwitch(string arg) => arg == "--mcp" || arg.StartsWith("--mcp=", StringComparison.Ordinal);

        foreach (var arg in Array.FindAll(args, IsSwitch))
        {
            if (arg == "--mcp")
            {
                Port = DefaultPort;
                continue;
            }

            if (!int.TryParse(arg.AsSpan("--mcp=".Length), CultureInfo.InvariantCulture, out var parsed) || parsed is < 1 or > 65535)
            {
                Fail($"Invalid port in '{arg}'.");
            }

            Port = parsed;
        }

        args = Array.FindAll(args, static arg => !IsSwitch(arg));

#if !DEBUG
        if (Port != null)
        {
            Fail("--mcp is only available in debug builds.");
        }
#endif
    }

    /// <summary>Starts the server when <c>--mcp</c> was given, returning the handle to dispose on shutdown.</summary>
    public static IDisposable? Start()
    {
#if DEBUG
        if (Port != null)
        {
            try
            {
                return new McpServer(Port.Value);
            }
            catch (System.Net.HttpListenerException e)
            {
                Fail($"Could not listen on port {Port}: {e.Message}");
            }
        }
#endif

        return null;
    }

    /// <summary>
    /// Hands an error to the automation server in place of the error dialog. Returns false when no
    /// agent is driving the viewer, so the caller shows the dialog as usual.
    /// </summary>
    public static bool TryReportError(string report)
    {
#if DEBUG
        if (IsEnabled)
        {
            ReportedErrors.Report(report);
            return true;
        }
#endif

        return false;
    }

    [DoesNotReturn]
    private static void Fail(string message)
    {
        Console.Error.WriteLine(message);
        Environment.Exit(1);
    }
}
