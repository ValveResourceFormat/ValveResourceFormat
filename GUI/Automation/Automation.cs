using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace GUI.Automation;

/// <summary>
/// The seam between the viewer and the automation server. The server is compiled into debug builds
/// only, so callers go through here unconditionally and get a no-op everywhere else.
/// </summary>
internal static class Automation
{
    /// <summary>Port used when <c>--mcp</c> is given without one.</summary>
    public const int DefaultPort = 13338;

    /// <summary>Port the server listens on, or null when it is not running.</summary>
    public static int? Port { get; private set; }

    /// <summary>Whether this process was started to be driven by an agent.</summary>
    public static bool IsEnabled => Port != null;

    /// <summary>
    /// Takes <c>--mcp</c> or <c>--mcp=port</c> out of <paramref name="args"/>. Called before
    /// anything else reads them, so the switch is never forwarded to another instance or mistaken
    /// for a file name.
    /// </summary>
    public static void TakeSwitch(ref string[] args)
    {
        int? port = null;
        var remaining = new List<string>(args.Length);

        foreach (var arg in args)
        {
            if (arg.Equals("--mcp", StringComparison.Ordinal))
            {
                port = DefaultPort;
                continue;
            }

            if (arg.StartsWith("--mcp=", StringComparison.Ordinal))
            {
                if (!int.TryParse(arg.AsSpan("--mcp=".Length), CultureInfo.InvariantCulture, out var parsed) || parsed is < 1 or > 65535)
                {
                    Fail($"Invalid port in '{arg}'.");
                }

                port = parsed;
                continue;
            }

            remaining.Add(arg);
        }

        args = [.. remaining];

#if !DEBUG
        if (port != null)
        {
            Fail("--mcp is only available in debug builds.");
        }
#endif

        Port = port;
    }

    /// <summary>
    /// Starts the server when <c>--mcp</c> was given, returning the handle to dispose on shutdown.
    /// Quits rather than carrying on half configured if the port is already taken.
    /// </summary>
    public static IDisposable? Start()
    {
#if DEBUG
        if (Port is not { } port)
        {
            return null;
        }

        try
        {
            var server = new McpServer(port, new McpTools());
            server.Start();
            return server;
        }
        catch (System.Net.HttpListenerException e)
        {
            Fail($"Could not listen on port {port}: {e.Message}");
        }
#endif

        return null;
    }

    [DoesNotReturn]
    private static void Fail(string message)
    {
        Console.Error.WriteLine(message);
        Environment.Exit(1);

        throw new UnreachableException();
    }
}
