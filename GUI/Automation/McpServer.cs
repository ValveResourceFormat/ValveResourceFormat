#if DEBUG
using System.Buffers;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using GUI.Utils;

namespace GUI.Automation;

/// <summary>
/// Opt-in automation endpoint, enabled with the <c>--mcp</c> command line switch. Serves one
/// loopback HTTP endpoint speaking MCP <c>2026-07-28</c> over JSON-RPC 2.0. That revision has no
/// handshake: every request carries its own protocol version, so there is nothing to keep between
/// calls and no server-to-client stream to hold open.
/// </summary>
/// <remarks>
/// The tools can open any file the user could open through the UI, and there is deliberately no
/// sandbox: this is a local developer feature that only listens when asked to on the command line.
/// </remarks>
internal sealed class McpServer : IDisposable
{
    private const string EndpointPath = "/mcp";
    private const string ProtocolVersion = "2026-07-28";
    private const string MetaPrefix = "io.modelcontextprotocol/";
    private const string Base64Prefix = "=?base64?";
    private const string Base64Suffix = "?=";

    // Nothing here changes while the process runs, so a client may cache the tool list for as long
    // as it likes, and the list is the same for everyone.
    private const int ListCacheTtlMs = 3_600_000;
    private const int MaxRequestBytes = 4 * 1024 * 1024;

    private readonly HttpListener Listener = new();
    private readonly McpTools Tools;
    private readonly CancellationTokenSource Cancellation = new();

    // The viewer state the tools drive is global, so calls run one at a time.
    private readonly SemaphoreSlim CallLock = new(1, 1);

    public string Url { get; }

    public McpServer(int port, McpTools tools)
    {
        Tools = tools;
        Url = $"http://127.0.0.1:{port}{EndpointPath}";

        Listener.Prefixes.Add($"http://127.0.0.1:{port}/");
    }

    public void Start()
    {
        Listener.Start();

        _ = Task.Run(AcceptLoop);

        Log.Info(nameof(McpServer), $"Automation server listening on {Url}");
    }

    public void Dispose()
    {
        Cancellation.Cancel();

        if (Listener.IsListening)
        {
            Listener.Stop();
        }

        ((IDisposable)Listener).Dispose();
        Cancellation.Dispose();
        CallLock.Dispose();
    }

    private async Task AcceptLoop()
    {
        while (!Cancellation.IsCancellationRequested)
        {
            HttpListenerContext context;

            try
            {
                context = await Listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException)
            {
                return; // Listener stopped
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            _ = Task.Run(() => HandleContext(context));
        }
    }

    private async Task HandleContext(HttpListenerContext context)
    {
        try
        {
            await Handle(context).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A failure handling one request must not take the server down
        catch (Exception e)
#pragma warning restore CA1031
        {
            Log.Error(nameof(McpServer), $"Failed to handle request: {e}");

            try
            {
                await WriteRpcError(context.Response, 500, null, -32603, $"Internal error: {e.Message}").ConfigureAwait(false);
            }
#pragma warning disable CA1031 // The response may already be on the wire, in which case all that is left is to drop it
            catch (Exception)
#pragma warning restore CA1031
            {
                try
                {
                    context.Response.Abort();
                }
                catch (InvalidOperationException)
                {
                    // Already closed
                }
            }
        }
    }

    private async Task Handle(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        if (!string.Equals(request.Url?.AbsolutePath, EndpointPath, StringComparison.Ordinal))
        {
            await WriteText(response, 404, $"Not found. The only endpoint is POST {EndpointPath}.").ConfigureAwait(false);
            return;
        }

        if (!string.Equals(request.HttpMethod, "POST", StringComparison.Ordinal))
        {
            response.AddHeader("Allow", "POST");
            await WriteText(response, 405, "Only POST is supported, this server offers no stream and no sessions.").ConfigureAwait(false);
            return;
        }

        // A browser page on another origin must not be able to drive the viewer (DNS rebinding).
        var origin = request.Headers["Origin"];

        if (origin != null && !IsLoopbackOrigin(origin))
        {
            await WriteText(response, 403, "Requests from a non-loopback Origin are rejected.").ConfigureAwait(false);
            return;
        }

        if (request.ContentType == null || !request.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
        {
            await WriteText(response, 415, "Content-Type must be application/json.").ConfigureAwait(false);
            return;
        }

        if (request.ContentLength64 > MaxRequestBytes)
        {
            await WriteText(response, 413, "Request body is too large.").ConfigureAwait(false);
            return;
        }

        var body = await ReadBody(request).ConfigureAwait(false);

        JsonNode? message;

        try
        {
            message = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            await WriteRpcError(response, 400, null, -32700, "Parse error").ConfigureAwait(false);
            return;
        }

        // Batching is not required by this revision of the spec, and a bare value is not a request.
        if (message is not JsonObject rpc)
        {
            await WriteRpcError(response, 400, null, -32600, "Invalid Request, expected a single JSON-RPC object").ConfigureAwait(false);
            return;
        }

        var method = rpc["method"]?.GetValue<string>();
        var hasId = rpc.TryGetPropertyValue("id", out var id) && id != null;

        if (method == null)
        {
            await WriteRpcError(response, 400, hasId ? id : null, -32600, "Invalid Request, missing method").ConfigureAwait(false);
            return;
        }

        if (!hasId)
        {
            // A notification gets no body at all.
            response.StatusCode = 202;
            response.Close();
            return;
        }

        var parameters = rpc["params"];
        var toolName = method == "tools/call" ? parameters?["name"]?.GetValue<string>() : null;

        if (ValidateHeaders(request, method, toolName, parameters) is { } mismatch)
        {
            await WriteRpcError(response, 400, id, -32020, mismatch).ConfigureAwait(false);
            return;
        }

        var meta = parameters?["_meta"];

        // Both are required on every request, and without a version there is nothing for the header
        // check above to have compared against.
        if (meta?[MetaPrefix + "protocolVersion"]?.GetValue<string>() is not { } clientVersion)
        {
            await WriteRpcError(response, 400, id, -32602, $"Missing '{MetaPrefix}protocolVersion' in params._meta.").ConfigureAwait(false);
            return;
        }

        if (meta[MetaPrefix + "clientCapabilities"] == null)
        {
            await WriteRpcError(response, 400, id, -32602, $"Missing '{MetaPrefix}clientCapabilities' in params._meta.").ConfigureAwait(false);
            return;
        }

        // Applies to server/discover too: the rejection is itself how a client on another revision
        // learns what this server speaks, because it carries the supported list.
        if (clientVersion != ProtocolVersion)
        {
            await WriteUnsupportedVersion(response, id, clientVersion).ConfigureAwait(false);
            return;
        }

        switch (method)
        {
            case "server/discover":
                await WriteRpcResult(response, id, Discover()).ConfigureAwait(false);
                return;

            case "tools/list":
                var tools = Tools.List();
                tools["ttlMs"] = ListCacheTtlMs;
                tools["cacheScope"] = "public";

                await WriteRpcResult(response, id, tools).ConfigureAwait(false);
                return;

            case "tools/call":
                // An unknown tool is a protocol error; isError is for a tool that ran and failed.
                if (toolName == null)
                {
                    await WriteRpcError(response, 400, id, -32602, "Missing tool name.").ConfigureAwait(false);
                    return;
                }

                if (!Tools.Has(toolName))
                {
                    await WriteRpcError(response, 400, id, -32602, $"Unknown tool: {toolName}").ConfigureAwait(false);
                    return;
                }

                await WriteRpcResult(response, id, await CallTool(toolName, parameters).ConfigureAwait(false)).ConfigureAwait(false);
                return;

            case "initialize":
                // A client old enough to send this cannot fall forward, so the version it would
                // have to speak is the only useful thing to tell it.
                await WriteRpcError(response, 404, id, -32601,
                    $"This server implements MCP {ProtocolVersion}, which has no initialize handshake.").ConfigureAwait(false);
                return;

            default:
                await WriteRpcError(response, 404, id, -32601, $"Method not found: {method}").ConfigureAwait(false);
                return;
        }
    }

    /// <summary>
    /// Checks the headers the transport mirrors the body into, so that an intermediary routing on a
    /// header and this server acting on the body can never disagree. Returns null when they match.
    /// </summary>
    private static string? ValidateHeaders(HttpListenerRequest request, string method, string? toolName, JsonNode? parameters)
    {
        var version = request.Headers["MCP-Protocol-Version"];

        if (string.IsNullOrEmpty(version))
        {
            return "Missing MCP-Protocol-Version header.";
        }

        if (parameters?["_meta"]?[MetaPrefix + "protocolVersion"]?.GetValue<string>() is { } bodyVersion
            && !string.Equals(version, bodyVersion, StringComparison.Ordinal))
        {
            return $"MCP-Protocol-Version header '{version}' does not match body value '{bodyVersion}'.";
        }

        var headerMethod = request.Headers["Mcp-Method"];

        if (string.IsNullOrEmpty(headerMethod))
        {
            return "Missing Mcp-Method header.";
        }

        if (!string.Equals(headerMethod, method, StringComparison.Ordinal))
        {
            return $"Mcp-Method header '{headerMethod}' does not match body method '{method}'.";
        }

        if (toolName == null)
        {
            return null;
        }

        var headerName = DecodeHeaderValue(request.Headers["Mcp-Name"]);

        if (string.IsNullOrEmpty(headerName))
        {
            return "Missing Mcp-Name header, which tools/call requires.";
        }

        return string.Equals(headerName, toolName, StringComparison.Ordinal)
            ? null
            : $"Mcp-Name header '{headerName}' does not match body value '{toolName}'.";
    }

    /// <summary>Undoes the sentinel encoding a client uses for a value that is not header safe.</summary>
    private static string? DecodeHeaderValue(string? value)
    {
        if (value == null
            || value.Length < Base64Prefix.Length + Base64Suffix.Length
            || !value.StartsWith(Base64Prefix, StringComparison.Ordinal)
            || !value.EndsWith(Base64Suffix, StringComparison.Ordinal))
        {
            return value;
        }

        var encoded = value[Base64Prefix.Length..^Base64Suffix.Length];

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        }
        catch (FormatException)
        {
            return value;
        }
    }

    private async Task<JsonObject> CallTool(string name, JsonNode? parameters)
    {
        var arguments = parameters?["arguments"] as JsonObject ?? [];

        await CallLock.WaitAsync(Cancellation.Token).ConfigureAwait(false);

        try
        {
            var result = await Tools.Call(name, arguments, Cancellation.Token).ConfigureAwait(false);
            return result.ToJson();
        }
#pragma warning disable CA1031 // Any tool failure is reported to the caller, never thrown at the transport
        catch (Exception e)
#pragma warning restore CA1031
        {
            Log.Error(nameof(McpServer), $"Tool '{name}' failed: {e}");
            return McpToolResult.Error($"{e.GetType().Name}: {e.Message}").ToJson();
        }
        finally
        {
            CallLock.Release();
        }
    }

    private static JsonObject Discover() => new()
    {
        ["supportedVersions"] = new JsonArray(ProtocolVersion),
        ["capabilities"] = new JsonObject
        {
            ["tools"] = new JsonObject(),
        },
        ["instructions"] = """
            Drives the Source 2 Viewer window: open files, move the camera, toggle layers and render modes, inspect entities, pick, read the log and render stats, and screenshot what it draws.
            Successful calls answer with compact JSON; failures answer with plain text and isError. Optional fields are left out when they would be false, null or empty.
            Positions are [x, y, z] world units and angles are [pitch, yaw, roll] degrees with pitch positive downwards, rounded to two decimals.
            Calls that change the view draw a frame before answering, so the window shows the result even in the background. For repeatable screenshots, pause and then step exact amounts of time.
            Tools that take 'tab' use the active tab when it is omitted. Pass the id from open_file or list_tabs, because a failed load or the user can change which tab is active. A tool that acts on a tab that is still loading waits for the load to finish.
            A 3D tab is a model, map, material, particle system or other scene; texture, image and graph tabs render too but have no scene. get_info on a tab lists which of the tools that act on a tab work on it.
            """,
        ["ttlMs"] = ListCacheTtlMs,
        ["cacheScope"] = "public",
    };

    private static JsonObject ServerInfo() => new()
    {
        ["name"] = "source2viewer",
        ["version"] = Program.ProductVersion,
    };

    private static bool IsLoopbackOrigin(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.IsLoopback;
    }

    private static async Task<string> ReadBody(HttpListenerRequest request)
    {
        using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    private static Task WriteRpcResult(HttpListenerResponse response, JsonNode? id, JsonObject result)
    {
        // Every result carries these, so they are stamped on here rather than by each producer.
        result["resultType"] = "complete";
        result["_meta"] = new JsonObject
        {
            [MetaPrefix + "serverInfo"] = ServerInfo(),
        };

        return WriteJson(response, 200, new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["result"] = result,
        });
    }

    private static Task WriteUnsupportedVersion(HttpListenerResponse response, JsonNode? id, string requested)
    {
        return WriteJson(response, 400, new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject
            {
                ["code"] = -32022,
                ["message"] = "Unsupported protocol version",
                ["data"] = new JsonObject
                {
                    ["supported"] = new JsonArray(ProtocolVersion),
                    ["requested"] = requested,
                },
            },
        });
    }

    private static Task WriteRpcError(HttpListenerResponse response, int statusCode, JsonNode? id, int code, string message)
    {
        return WriteJson(response, statusCode, new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message,
            },
        });
    }

    private static async Task WriteJson(HttpListenerResponse response, int statusCode, JsonNode node)
    {
        // Straight to UTF-8 rather than through ToJsonString, which would build a second full copy
        // of the payload as a string. Screenshot replies are megabytes of base64.
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = McpToolResult.SerializerOptions.Encoder }))
        {
            node.WriteTo(writer);
        }

        var bytes = buffer.WrittenMemory;

        response.StatusCode = statusCode;
        response.ContentType = "application/json";
        response.ContentLength64 = bytes.Length;

        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);

        response.Close();
    }

    private static async Task WriteText(HttpListenerResponse response, int statusCode, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);

        response.StatusCode = statusCode;
        response.ContentType = "text/plain; charset=utf-8";
        response.ContentLength64 = bytes.Length;

        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);

        response.Close();
    }
}
#endif
