#if DEBUG
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using GUI.Utils;

namespace GUI.Automation;

/// <summary>
/// Opt-in automation endpoint, enabled with <c>--mcp</c>. It serves one loopback HTTP endpoint that
/// speaks the MCP revision in <see cref="ProtocolVersion"/> over JSON-RPC 2.0. That revision has no
/// handshake. Every request carries its protocol version, so the server keeps nothing between calls.
/// </summary>
/// <remarks>
/// There is deliberately no sandbox. The tools can open any file that the user can open in the UI.
/// This local developer feature listens only when the command line asks for it.
/// </remarks>
internal sealed class McpServer : IDisposable
{
    private const string ProtocolVersion = "2026-07-28";
    private const string Meta = "io.modelcontextprotocol/";

    private const string Instructions = """
        Drives the Source 2 Viewer window. The tools open files, move the camera, change sidebar controls, and inspect entities, models and particles. They also pick, trace, read the log and render stats, and take screenshots.
        A successful call replies with compact JSON that omits null and empty fields. A failed call replies with plain text and isError.
        Positions are [x, y, z] in world units. Angles are [pitch, yaw, roll] in degrees, with pitch positive downwards.
        Tools that take 'tab' default to the active tab and wait for a tab that is still loading. Pass the id from open_file or get_status, since the user can switch tabs.
        Calls that change the view draw a frame before replying, so the window shows the result even in the background. For repeatable screenshots, pause, then step exact amounts of time. To replay a scene from its start, pause, then reload_tab: a tab loaded while paused holds at time zero until stepped. Auto exposure can still differ by a level between runs, so for pixel-exact comparisons also set the Exposure slider.
        """;

    private readonly HttpListener Listener = new();
    private readonly McpTools Tools = new();
    private readonly CancellationTokenSource Shutdown = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> Running = new();

    public McpServer(int port)
    {
        Listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        Listener.Start();

        _ = Task.Run(AcceptLoop);

        Log.Info(nameof(McpServer), $"Automation server listening on http://127.0.0.1:{port}/mcp");
    }

    public void Dispose()
    {
        Shutdown.Cancel();
        Listener.Close();
        Shutdown.Dispose();
        Tools.Dispose();
    }

    private async Task AcceptLoop()
    {
        while (Listener.IsListening)
        {
            HttpListenerContext context;

            try
            {
                context = await Listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception e) when (e is HttpListenerException or ObjectDisposedException)
            {
                return;
            }

            _ = Task.Run(() => Respond(context));
        }
    }

    private async Task Respond(HttpListenerContext context)
    {
        var response = context.Response;

        try
        {
            var (status, body) = await Handle(context.Request).ConfigureAwait(false);
            response.StatusCode = status;

            if (status == 405)
            {
                response.AddHeader("Allow", "POST");
            }

            if (body != null)
            {
                response.ContentType = "application/json";
                await JsonSerializer.SerializeAsync(response.OutputStream, body, McpTools.Json).ConfigureAwait(false);
            }

            response.Close();
        }
#pragma warning disable CA1031 // A failure answering one request must not take the server down
        catch (Exception e)
#pragma warning restore CA1031
        {
            Log.Error(nameof(McpServer), $"Failed to answer a request: {e}");
            response.Abort();
        }
    }

    private async Task<(int Status, JsonNode? Body)> Handle(HttpListenerRequest request)
    {
        if (request.Url?.AbsolutePath != "/mcp")
        {
            return (404, Error(null, -32601, "The only endpoint is POST /mcp."));
        }

        if (request.HttpMethod != "POST")
        {
            return (405, null);
        }

        if (!IsLoopback(request))
        {
            return (403, Error(null, -32600, "The server accepts only loopback hosts and origins."));
        }

        JsonObject? rpc;

        try
        {
            rpc = await JsonNode.ParseAsync(request.InputStream, documentOptions: new JsonDocumentOptions { AllowDuplicateProperties = false }).ConfigureAwait(false) as JsonObject;
        }
        catch (JsonException)
        {
            return (400, Error(null, -32700, "Parse error"));
        }

        if (rpc == null)
        {
            return (400, Error(null, -32600, "Expected a single JSON-RPC request object."));
        }

        var id = rpc["id"]?.DeepClone();

        // Without an id the message is a notification
        if (rpc.ContainsKey("id") && id?.GetValueKind() is not (JsonValueKind.String or JsonValueKind.Number))
        {
            return (400, Error(null, -32600, "'id' must be a string or a number."));
        }

        try
        {
            return await Handle(request, rpc, id).ConfigureAwait(false);
        }
        catch (InvalidOperationException e)
        {
            // A member of the wrong JSON type
            return (400, Error(id, -32600, $"Invalid request: {e.Message}"));
        }
    }

    private async Task<(int Status, JsonNode? Body)> Handle(HttpListenerRequest request, JsonObject rpc, JsonNode? id)
    {
        var method = (string?)rpc["method"];
        var parameters = rpc["params"] as JsonObject;

        if (method == null)
        {
            return (400, Error(id, -32600, "Expected a single JSON-RPC request object."));
        }

        if (parameters == null && rpc["params"] != null)
        {
            return (400, Error(id, -32600, "'params' must be an object."));
        }

        if (id == null)
        {
            if (method == "notifications/cancelled")
            {
                await Cancel(parameters?["requestId"]).ConfigureAwait(false);
            }

            return (202, null);
        }

        var meta = parameters?["_meta"];
        var version = (string?)meta?[Meta + "protocolVersion"];
        var toolName = method == "tools/call" ? (string?)parameters?["name"] : null;

        if (version == null || meta?[Meta + "clientCapabilities"] == null)
        {
            return (400, Error(id, -32602, $"MCP {ProtocolVersion} needs '{Meta}protocolVersion' and '{Meta}clientCapabilities' in params._meta of every request."));
        }

        if (CheckHeaders(request, method, version, toolName) is { } mismatch)
        {
            return (400, Error(id, -32020, mismatch));
        }

        if (version != ProtocolVersion)
        {
            var versions = new JsonObject
            {
                ["supported"] = new JsonArray(ProtocolVersion),
                ["requested"] = version,
            };

            return (400, Error(id, -32022, $"Unsupported protocol version. This server speaks only MCP {ProtocolVersion}.", versions));
        }

        return method switch
        {
            "server/discover" => (200, Result(id, Cacheable(new JsonObject
            {
                ["supportedVersions"] = new JsonArray(ProtocolVersion),
                ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                ["instructions"] = Instructions,
            }))),
            "tools/list" => (200, Result(id, Cacheable(Tools.List()))),
            "tools/call" when toolName == null => (400, Error(id, -32602, "tools/call needs 'name'.")),
            "tools/call" when !Tools.Has(toolName) => (400, Error(id, -32602, $"Unknown tool '{toolName}'. The tools are {string.Join(", ", Tools.Names)}.")),
            "tools/call" when parameters?["arguments"] is not (null or JsonObject) => (400, Error(id, -32602, "'arguments' must be an object.")),
            "tools/call" => (200, Result(id, await Call(toolName, parameters?["arguments"] as JsonObject ?? [], id).ConfigureAwait(false))),
            _ => (404, Error(id, -32601, $"Method not found: {method}")),
        };
    }

    /// <summary>Rejects non-loopback hosts and origins, so a browser page cannot drive the viewer through DNS rebinding.</summary>
    private static bool IsLoopback(HttpListenerRequest request)
    {
        if (request.Url?.IsLoopback != true)
        {
            return false;
        }

        var origin = request.Headers["Origin"];

        if (origin == null)
        {
            return true;
        }

        return Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback;
    }

    /// <summary>
    /// The transport copies the version, method and tool name into headers. They must match the body,
    /// so that an intermediary that routes on a header and this server that acts on the body agree.
    /// </summary>
    private static string? CheckHeaders(HttpListenerRequest request, string method, string version, string? toolName)
    {
        if (request.Headers["MCP-Protocol-Version"] != version)
        {
            return "MCP-Protocol-Version header does not match params._meta.";
        }

        if (request.Headers["Mcp-Method"] != method)
        {
            return "Mcp-Method header does not match the method.";
        }

        if (toolName != null && request.Headers["Mcp-Name"] != toolName)
        {
            return "Mcp-Name header does not match the tool name.";
        }

        return null;
    }

    private async Task Cancel(JsonNode? requestId)
    {
        if (requestId == null || !Running.TryGetValue(requestId.ToJsonString(), out var call))
        {
            return;
        }

        try
        {
            await call.CancelAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // It finished in the meantime
        }
    }

    private async Task<JsonObject> Call(string name, JsonObject arguments, JsonNode id)
    {
        using var call = CancellationTokenSource.CreateLinkedTokenSource(Shutdown.Token);
        var key = id.ToJsonString();

        // Ids are unique only per client, so a cancel request cannot reach a second running call with the same id
        var cancellable = Running.TryAdd(key, call);

        try
        {
            return await Tools.Call(name, arguments, call.Token).ConfigureAwait(false);
        }
        finally
        {
            if (cancellable)
            {
                Running.TryRemove(key, out _);
            }
        }
    }

    /// <summary>Marks a result as the same for every caller for as long as the server runs.</summary>
    private static JsonObject Cacheable(JsonObject result)
    {
        result["ttlMs"] = int.MaxValue;
        result["cacheScope"] = "public";
        return result;
    }

    private static JsonObject Result(JsonNode id, JsonObject result)
    {
        result["resultType"] = "complete";
        result["_meta"] = new JsonObject
        {
            [Meta + "serverInfo"] = new JsonObject
            {
                ["name"] = "source2viewer",
                ["version"] = Program.ProductVersion,
            },
        };

        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result,
        };
    }

    private static JsonObject Error(JsonNode? id, int code, string message, JsonNode? data = null)
    {
        var error = new JsonObject
        {
            ["code"] = code,
            ["message"] = message,
        };

        if (data != null)
        {
            error["data"] = data;
        }

        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = error,
        };
    }
}
#endif
