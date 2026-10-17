using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace GUI.Automation;

/// <summary>
/// The seam between the viewer and the automation server. The server is compiled into debug builds
/// only, so callers go through here and get a no-op everywhere else; debug-only code calls the
/// server's classes directly.
/// </summary>
internal static class Automation
{
    private const int DefaultPort = 13338;

    /// <summary>Port the server listens on, or null when it is not running.</summary>
    private static int? port;

    /// <summary>Whether this process was started to be driven by an agent.</summary>
    public static bool IsEnabled => port != null;

    /// <summary>
    /// Takes <c>--mcp</c> or <c>--mcp=port</c> out of <paramref name="args"/>, before anything else
    /// reads them, so the switch is never forwarded to another instance or opened as a file.
    /// </summary>
    public static void TakeSwitch(ref string[] args)
    {
        static bool IsSwitch(string arg) => arg == "--mcp" || arg.StartsWith("--mcp=", StringComparison.Ordinal);

        foreach (var arg in Array.FindAll(args, IsSwitch))
        {
            port = arg == "--mcp" ? DefaultPort
                : int.TryParse(arg.AsSpan(6), CultureInfo.InvariantCulture, out var parsed) && parsed is > 0 and < 65536 ? parsed
                : Fail($"Invalid port in '{arg}'.");
        }

        args = Array.FindAll(args, static arg => !IsSwitch(arg));

#if !DEBUG
        if (IsEnabled)
        {
            Fail("--mcp is only available in debug builds.");
        }
#endif
    }

    /// <summary>Starts the server when <c>--mcp</c> was given, returning the handle to dispose on shutdown.</summary>
    public static IDisposable? Start()
    {
#if DEBUG
        if (port is { } listenPort)
        {
            try
            {
                return new McpServer(listenPort);
            }
            catch (System.Net.HttpListenerException e)
            {
                Fail($"Could not listen on port {listenPort}: {e.Message}");
            }
        }
#endif

        return null;
    }

    /// <summary>
    /// Hands an unhandled exception to the automation server in place of the error dialog. Returns
    /// false when no agent is driving the viewer, so the caller shows the dialog as usual.
    /// </summary>
    public static bool TryReportUnhandledException(Exception exception)
    {
#if DEBUG
        if (IsEnabled)
        {
            UnhandledExceptions.Report(exception);
            return true;
        }
#endif

        return false;
    }

    [DoesNotReturn]
    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        Environment.Exit(1);

        throw new UnreachableException();
    }
}
