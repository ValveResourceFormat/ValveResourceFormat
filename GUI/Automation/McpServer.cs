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
/// Opt-in automation endpoint, enabled with the <c>--mcp</c> command line switch. Serves one loopback
/// HTTP endpoint speaking MCP <c>2026-07-28</c> over JSON-RPC 2.0. That revision has no handshake:
/// every request carries its own protocol version, so nothing is kept between calls.
/// </summary>
/// <remarks>
/// The tools can open any file the user could open through the UI, and there is deliberately no
/// sandbox: this is a local developer feature that only listens when asked to on the command line.
/// </remarks>
internal sealed class McpServer : IDisposable
{
    private const string ProtocolVersion = "2026-07-28";
    private const string Meta = "io.modelcontextprotocol/";

    private const string Instructions = """
        Drives the Source 2 Viewer window: open files, move the camera, change sidebar controls, inspect entities, models and particles, pick, trace, read the log and render stats, and screenshot what it draws.
        Successful calls answer with compact JSON, leaving out fields that would be null or empty; failures answer with plain text and isError.
        Positions are [x, y, z] world units and angles are [pitch, yaw, roll] degrees with pitch positive downwards.
        Tools that take 'tab' use the active tab when it is omitted, and wait for a tab that is still loading. Pass the id from open_file or get_status, because the user can change which tab is active.
        Calls that change the view draw a frame before answering, so the window shows the result even in the background. For repeatable screenshots, pause and then step exact amounts of time. To replay a whole scene from its start, pause and then reload_tab: a tab that loads while paused holds at time zero until stepped. Auto exposure can still differ by a level between runs, so for pixel-exact comparisons also set the Exposure slider.
        """;

    private readonly HttpListener listener = new();
    private readonly McpTools tools = new();
    private readonly CancellationTokenSource shutdown = new();

    // The viewer state the tools drive is global, so calls run one at a time unless marked concurrent
    private readonly SemaphoreSlim callLock = new(1, 1);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> running = new();

    public McpServer(int port)
    {
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        _ = Task.Run(AcceptLoop);

        Log.Info(nameof(McpServer), $"Automation server listening on http://127.0.0.1:{port}/mcp");
    }

    public void Dispose()
    {
        // The lock and the token stay usable, because calls that are running still release and read them
        shutdown.Cancel();
        listener.Close();
    }

    private async Task AcceptLoop()
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;

            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
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

            if (body != null && context.Request.HttpMethod != "HEAD")
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
            return (405, Error(null, -32601, "The only endpoint is POST /mcp."));
        }

        // A page in a browser must not be able to drive the viewer through DNS rebinding
        if (request.Url is not { IsLoopback: true } || request.Headers["Origin"] is { } origin && !(Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback))
        {
            return (403, Error(null, -32600, "Requests from a non-loopback host or origin are rejected."));
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
            return (400, Error(null, -32600, "The request id must be a string or a number."));
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
        var parameters = rpc["params"] as JsonObject ?? (rpc["params"] == null ? null : throw new InvalidOperationException("'params' must be an object."));

        if (method == null)
        {
            return (400, Error(id, -32600, "Expected a single JSON-RPC request object."));
        }

        if (id == null)
        {
            if (method == "notifications/cancelled" && parameters?["requestId"]?.ToJsonString() is { } cancelled
                && running.TryGetValue(cancelled, out var call))
            {
                try
                {
                    await call.CancelAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    // It finished in the meantime
                }
            }

            return (202, null);
        }

        // A client of a revision before this one starts with initialize, which is answered with the versions there are
        if (method == "initialize")
        {
            return (400, Error(id, -32022, "Unsupported protocol version", new JsonObject
            {
                ["supported"] = new JsonArray(ProtocolVersion),
                ["requested"] = parameters?["protocolVersion"]?.DeepClone(),
            }));
        }

        var version = (string?)parameters?["_meta"]?[Meta + "protocolVersion"];
        var toolName = method == "tools/call" ? (string?)parameters?["name"] : null;

        if (CheckHeaders(request, method, version, toolName) is { } mismatch)
        {
            return (400, Error(id, -32020, mismatch));
        }

        if (version == null || parameters?["_meta"]?[Meta + "clientCapabilities"] == null)
        {
            return (400, Error(id, -32602, $"Every request needs '{Meta}protocolVersion' and '{Meta}clientCapabilities' in params._meta."));
        }

        if (version != ProtocolVersion)
        {
            return (400, Error(id, -32022, "Unsupported protocol version", new JsonObject
            {
                ["supported"] = new JsonArray(ProtocolVersion),
                ["requested"] = version,
            }));
        }

        return method switch
        {
            "server/discover" => (200, Result(id, new JsonObject
            {
                ["supportedVersions"] = new JsonArray(ProtocolVersion),
                ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                ["instructions"] = Instructions,
            })),
            "tools/list" => (200, Result(id, tools.List())),
            "tools/call" when toolName == null => (400, Error(id, -32602, "tools/call needs the tool's 'name'.")),
            "tools/call" when !tools.Has(toolName) => (400, Error(id, -32602, $"Unknown tool: {toolName}")),
            "tools/call" when parameters?["arguments"] is not (null or JsonObject) => (400, Error(id, -32602, "'arguments' must be an object.")),
            "tools/call" => (200, Result(id, await Call(toolName, parameters?["arguments"] as JsonObject ?? [], id).ConfigureAwait(false))),
            _ => (404, Error(id, -32601, $"Method not found: {method}")),
        };
    }

    /// <summary>
    /// The transport mirrors the version, method and tool name into headers, so that an intermediary
    /// routing on a header and this server acting on the body can never disagree.
    /// </summary>
    private static string? CheckHeaders(HttpListenerRequest request, string method, string? version, string? toolName)
    {
        return request.Headers["MCP-Protocol-Version"] != version ? "MCP-Protocol-Version header does not match params._meta."
            : request.Headers["Mcp-Method"] != method ? "Mcp-Method header does not match the method."
            : toolName != null && request.Headers["Mcp-Name"] != toolName ? "Mcp-Name header does not match the tool name."
            : null;
    }

    private async Task<JsonObject> Call(string name, JsonObject arguments, JsonNode id)
    {
        using var call = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
        var key = id.ToJsonString();
        var locked = false;

        // Ids are only unique per client, so a second call with the same id is not cancellable by it
        var cancellable = running.TryAdd(key, call);

        try
        {
            if (!tools.IsConcurrent(name))
            {
                await callLock.WaitAsync(call.Token).ConfigureAwait(false);
                locked = true;
            }

            return await tools.Call(name, arguments, call.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return McpTools.Reply("The call was cancelled.", isError: true);
        }
        finally
        {
            if (locked)
            {
                callLock.Release();
            }

            if (cancellable)
            {
                running.TryRemove(key, out _);
            }
        }
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
