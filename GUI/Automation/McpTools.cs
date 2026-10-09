#if DEBUG
using System.Collections;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Types.Exporter;
using GUI.Types.GLViewers;
using GUI.Utils;
using ValveResourceFormat.Renderer;

namespace GUI.Automation;

/// <summary>
/// Marks a method of <see cref="McpTools"/> as a tool named after it in snake_case. Its parameters
/// are the tool's arguments, each described by a <see cref="DescriptionAttribute"/>, bounded by any
/// <see cref="RangeAttribute"/>, and optional when they have a default. A <see cref="TabPage"/> or
/// viewer parameter makes it act on a tab, the active one unless a 'tab' argument names another. For
/// a viewer, it waits for the tab to load and fails when the tab has no viewer of that type, and a tool
/// that redraws also selects the tab.
/// A method returning a task, or a concurrent one, runs on the thread pool. Any other runs between two
/// frames with the GL context when it acts on a 3D scene, which the render thread changes as it draws,
/// and on the UI thread otherwise. The result is serialized as the tool's JSON reply.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class ToolAttribute(string description) : Attribute
{
    public string Description => description;

    /// <summary>The tool changes what the window shows, so a frame is drawn after it.</summary>
    public bool Redraws { get; init; }

    /// <summary>The tool answers while another call is running, instead of waiting for it.</summary>
    public bool Concurrent { get; init; }
}

/// <summary>A failure worded for the caller.</summary>
internal sealed class ToolException(string message) : Exception(message);

/// <summary>A reply with an image after its JSON.</summary>
internal sealed record ImageReply(object Result, byte[] Jpeg);

/// <summary>Ids handed out for objects, each kept for as long as the object lives.</summary>
internal sealed class IdRegistry<T>
    where T : class
{
    private readonly ConditionalWeakTable<T, StrongBox<int>> ids = [];
    private readonly Dictionary<int, WeakReference<T>> byId = [];
    private readonly Lock sync = new();
    private int lastId;

    public int IdOf(T item)
    {
        using var _ = sync.EnterScope();

        if (!ids.TryGetValue(item, out var id))
        {
            id = new StrongBox<int>(++lastId);
            ids.Add(item, id);
            byId[id.Value] = new WeakReference<T>(item);
        }

        return id.Value;
    }

    public T? Find(int id)
    {
        using var _ = sync.EnterScope();
        return byId.TryGetValue(id, out var item) && item.TryGetTarget(out var target) ? target : null;
    }
}

/// <summary>The tools exposed over <see cref="McpServer"/>, declared by <see cref="ToolAttribute"/>.</summary>
internal sealed partial class McpTools
{
    /// <param name="Target">The tab or viewer type the tool acts on, or null when it acts on no tab.</param>
    private sealed record Tool(string Name, ToolAttribute Info, MethodInfo Method, JsonObject Schema, Type? Target);

    private static readonly TimeSpan UiTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Snake case names, nulls and empty collections left out, positions rounded to two decimals and
    /// other floats to three, and no escaping of text that only a model reads.
    /// </summary>
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        RespectNullableAnnotations = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower), new Vector3Converter(), new FloatConverter() },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { SkipEmpty } },
    };

    private static readonly JsonSchemaExporterOptions SchemaOptions = new()
    {
        TreatNullObliviousAsNonNullable = true,
        TransformSchemaNode = static (context, schema) =>
        {
            // The custom converters leave these unconstrained
            var type = Nullable.GetUnderlyingType(context.TypeInfo.Type) ?? context.TypeInfo.Type;

            if (type == typeof(Vector3))
            {
                return new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "number" }, ["minItems"] = 3, ["maxItems"] = 3 };
            }

            if (type == typeof(float))
            {
                return new JsonObject { ["type"] = "number" };
            }

            if (type == typeof(Dictionary<int, Vector3>))
            {
                return new JsonObject
                {
                    ["type"] = "object",
                    ["propertyNames"] = new JsonObject { ["pattern"] = "^[0-9]+$" },
                    ["additionalProperties"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "number" }, ["minItems"] = 3, ["maxItems"] = 3 },
                };
            }

            // Optional arguments are left out rather than passed as null
            if (schema is JsonObject obj)
            {
                if (obj["type"] is JsonArray types)
                {
                    obj["type"] = types.First(static type => (string?)type != "null")!.DeepClone();
                }

                (obj["enum"] as JsonArray)?.Remove(null);
            }

            return schema;
        },
    };

    private readonly Dictionary<string, Tool> tools = [];
    private readonly IdRegistry<TabPage> tabIds = new();

    public McpTools()
    {
        foreach (var method in typeof(McpTools).GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic))
        {
            if (method.GetCustomAttribute<ToolAttribute>() is { } info)
            {
                var name = JsonNamingPolicy.SnakeCaseLower.ConvertName(method.Name);
                var target = method.GetParameters().FirstOrDefault(static parameter => IsTabTarget(parameter.ParameterType))?.ParameterType;
                tools.Add(name, new Tool(name, info, method, CreateSchema(method), target));
            }
        }
    }

    public bool Has(string name) => tools.ContainsKey(name);

    public bool IsConcurrent(string name) => tools.TryGetValue(name, out var tool) && tool.Info.Concurrent;

    public JsonObject List() => new()
    {
        ["tools"] = new JsonArray([.. tools.Values.Select(static tool => new JsonObject
        {
            ["name"] = tool.Name,
            ["description"] = tool.Info.Description,
            ["inputSchema"] = tool.Schema.DeepClone(),
        })]),
    };

    public async Task<JsonObject> Call(string name, JsonObject arguments, CancellationToken cancellationToken)
    {
        var tool = tools[name];

        try
        {
            var result = await Invoke(tool, arguments, cancellationToken).ConfigureAwait(false);

            if (tool.Info.Redraws)
            {
                await Redraw(cancellationToken).ConfigureAwait(false);
            }

            return result is ImageReply image
                ? Reply(JsonSerializer.Serialize(image.Result, Json), image: image.Jpeg)
                : Reply(JsonSerializer.Serialize(result ?? new { }, Json));
        }
        catch (Exception e) when (e is ToolException or ArgumentException or TimeoutException or IOException or UnauthorizedAccessException)
        {
            Log.Info(nameof(McpTools), $"{name} failed: {e.Message}");
            return Reply(e.Message, isError: true);
        }
#pragma warning disable CA1031 // Any other failure is a bug, but it is reported to the caller rather than thrown at the transport
        catch (Exception e) when (e is not OperationCanceledException)
#pragma warning restore CA1031
        {
            Log.Error(nameof(McpTools), $"{name} failed unexpectedly: {e}");
            return Reply($"Unexpected {e.GetType().Name}: {e.Message}", isError: true);
        }
    }

    /// <summary>A tool result: one text item, with an image after it when there is one.</summary>
    public static JsonObject Reply(string text, bool isError = false, byte[]? image = null)
    {
        var content = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text });

        if (image != null)
        {
            content.Add(new JsonObject { ["type"] = "image", ["data"] = JsonValue.Create(image), ["mimeType"] = "image/jpeg" });
        }

        var result = new JsonObject { ["content"] = content };

        if (isError)
        {
            result["isError"] = true;
        }

        return result;
    }

    private static JsonObject CreateSchema(MethodInfo method)
    {
        var properties = new JsonObject();
        var required = new JsonArray();

        foreach (var parameter in method.GetParameters())
        {
            if (parameter.ParameterType == typeof(CancellationToken))
            {
                continue;
            }

            if (IsTabTarget(parameter.ParameterType))
            {
                properties["tab"] = new JsonObject
                {
                    ["type"] = "integer",
                    ["description"] = "Tab id from open_file or get_status. Defaults to the active tab.",
                };
                continue;
            }

            var name = JsonNamingPolicy.SnakeCaseLower.ConvertName(parameter.Name!);
            var schema = JsonSchemaExporter.GetJsonSchemaAsNode(Json, parameter.ParameterType, SchemaOptions) as JsonObject ?? [];

            schema["description"] = parameter.GetCustomAttribute<DescriptionAttribute>()?.Description
                ?? throw new InvalidOperationException($"{method.Name}({parameter.Name}) has no description.");

            if (parameter.GetCustomAttribute<RangeAttribute>() is { } range)
            {
                // Integer bounds would truncate the value before comparing it
                if (range.OperandType == typeof(int) && (Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType) == typeof(float))
                {
                    throw new InvalidOperationException($"{method.Name}({parameter.Name}) needs double bounds.");
                }

                schema[range.MinimumIsExclusive ? "exclusiveMinimum" : "minimum"] = JsonValue.Create(Convert.ToDouble(range.Minimum, CultureInfo.InvariantCulture));

                if (HasMaximum(range))
                {
                    schema["maximum"] = JsonValue.Create(Convert.ToDouble(range.Maximum, CultureInfo.InvariantCulture));
                }
            }

            properties[name] = schema;

            if (!parameter.HasDefaultValue)
            {
                required.Add(name);
            }
        }

        var result = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
        };

        if (required.Count > 0)
        {
            result["required"] = required;
        }

        return result;
    }

    private static bool IsTabTarget(Type type) => type == typeof(TabPage) || type.IsAssignableTo(typeof(GLBaseControl));

    private static bool HasMaximum(RangeAttribute range) => range.Maximum is not (int.MaxValue or double.MaxValue);

    private async Task<object?> Invoke(Tool tool, JsonObject arguments, CancellationToken cancellationToken)
    {
        var properties = (JsonObject)tool.Schema["properties"]!;

        foreach (var (key, _) in arguments)
        {
            if (!properties.ContainsKey(key))
            {
                throw new ArgumentException($"Unknown argument '{key}'. {tool.Name} takes {(properties.Count == 0 ? "none" : string.Join(", ", properties.Select(static p => p.Key)))}.");
            }
        }

        var parameters = tool.Method.GetParameters();
        var values = new object?[parameters.Length];
        var targetIndex = -1;

        for (var i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].ParameterType == typeof(CancellationToken))
            {
                values[i] = cancellationToken;
            }
            else if (IsTabTarget(parameters[i].ParameterType))
            {
                targetIndex = i;
            }
            else
            {
                var name = JsonNamingPolicy.SnakeCaseLower.ConvertName(parameters[i].Name!);

                values[i] = arguments[name] is { } node
                    ? Deserialize(name, node, parameters[i].ParameterType, (JsonObject)properties[name]!)
                    : parameters[i].HasDefaultValue ? parameters[i].DefaultValue : throw new ArgumentException($"Missing '{name}'.");

                if (parameters[i].GetCustomAttribute<RangeAttribute>() is { } range && !range.IsValid(values[i]))
                {
                    var bounds = range.MinimumIsExclusive ? $"more than {range.Minimum}" : $"at least {range.Minimum}";
                    throw new ArgumentException($"'{name}' must be {bounds}{(HasMaximum(range) ? $" and at most {range.Maximum}" : null)}, got {arguments[name]?.ToJsonString()}.");
                }
            }
        }

        // A concurrent tool answers while the UI thread may be held by the call it runs alongside
        var runsOnUi = !tool.Info.Concurrent && !tool.Method.ReturnType.IsAssignableTo(typeof(Task));
        var betweenFrames = runsOnUi && tool.Target?.IsAssignableTo(typeof(GLSceneViewer)) == true;

        if (tool.Target == null)
        {
            return runsOnUi
                ? await OnUi(() => Run(tool, values), cancellationToken).ConfigureAwait(false)
                : await Await(Run(tool, values)).ConfigureAwait(false);
        }

        var tabId = arguments["tab"] is { } tab ? (int?)Deserialize("tab", tab, typeof(int), (JsonObject)properties["tab"]!) : null;

        while (true)
        {
            var (loading, result) = await OnUi<(Task? Loading, object? Result)>(() =>
            {
                var page = ResolveTab(tabId);

                // A viewer only exists once its tab has loaded
                if (tool.Target != typeof(TabPage) && page.Tag is ExportData { Loaded: { IsCompleted: false } loaded })
                {
                    return (loaded, null);
                }

                if (tool.Target == typeof(TabPage))
                {
                    values[targetIndex] = page;
                }
                else
                {
                    var viewer = ViewerOf(page, tool.Target, tabId == null);
                    values[targetIndex] = viewer;

                    // A tool that only reads leaves the tab the user is looking at alone
                    if (tool.Info.Redraws)
                    {
                        ShowViewer(viewer);
                    }
                }

                return (null, runsOnUi && !betweenFrames ? Run(tool, values) : null);
            }, cancellationToken).ConfigureAwait(false);

            if (loading != null)
            {
                try
                {
                    await loading.WaitAsync(LoadTimeout, cancellationToken).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    throw new ToolException($"The tab is still loading after {LoadTimeout.TotalSeconds:F0}s.");
                }

                continue;
            }

            return betweenFrames ? await WithGl((GLBaseControl)values[targetIndex]!, () => Run(tool, values), cancellationToken).ConfigureAwait(false)
                : runsOnUi ? result
                : await Await(Run(tool, values)).ConfigureAwait(false);
        }
    }

    private object? Run(Tool tool, object?[] values) => tool.Method.Invoke(this, BindingFlags.DoNotWrapExceptions, null, values, null);

    private static async Task<object?> Await(object? returned)
    {
        switch (returned)
        {
            case Task<object> withResult:
                return await withResult.ConfigureAwait(false);
            case Task task:
                await task.ConfigureAwait(false);
                return null;
            default:
                return returned;
        }
    }

    private static object? Deserialize(string name, JsonNode node, Type type, JsonObject schema)
    {
        try
        {
            // The enum converter also takes numbers and comma separated lists, which the schema does not offer
            if (schema["enum"] is JsonArray names && (node.GetValueKind() != JsonValueKind.String
                || !names.Any(value => string.Equals((string?)value, (string?)node, StringComparison.OrdinalIgnoreCase))))
            {
                throw new JsonException();
            }

            return node.Deserialize(type, Json);
        }
        catch (JsonException)
        {
            var expected = schema["enum"] is JsonArray values ? $"one of {string.Join(", ", values)}" : (string?)schema["type"] switch
            {
                "array" when schema["minItems"] is not null => "[x, y, z]",
                "array" => "an array",
                "object" when schema["additionalProperties"] is not null => "an object of indices to [x, y, z]",
                "object" => "an object",
                "integer" => "an integer",
                "boolean" => "true or false",
                var other => $"a {other}",
            };
            throw new ArgumentException($"'{name}' must be {expected}, got {node.ToJsonString()}.");
        }
    }

    /// <summary>The tab with this id, or the active one. Call on the UI thread.</summary>
    private TabPage ResolveTab(int? id)
    {
        if (id == null)
        {
            return Program.MainForm.Tabs.SelectedTab ?? throw new ToolException("No tab is open.");
        }

        return tabIds.Find(id.Value) is { Parent: not null } page
            ? page
            : throw new ToolException($"No tab with id {id}. get_status lists the open tabs.");
    }

    /// <summary>Selects the tab and puts its viewer on the render loop, because only the active tab renders.</summary>
    private GLBaseControl ViewerOf(TabPage page, Type type, bool activeTab)
    {
        var viewer = GLBaseControl.FindHostedIn(page);

        if (!type.IsInstanceOfType(viewer))
        {
            var needs = type == typeof(GLWorldViewer) ? "has no map"
                : type == typeof(GLParticleViewer) ? "is not a particle system"
                : type == typeof(GLSceneViewer) ? "has no 3D scene"
                : "has no rendered view";
            var failure = ViewerError(page) is { } error ? $" Its viewer failed: {error}" : string.Empty;
            var implicitTab = activeTab ? " No 'tab' was given, so the active tab was used." : string.Empty;

            throw new ToolException($"Tab {tabIds.IdOf(page)} '{page.Text}' {needs}.{failure}{implicitTab}");
        }

        return viewer!;
    }

    /// <summary>
    /// Selects the tabs a viewer sits in, the file's own tabs included, and puts it on the render loop,
    /// because only the active tab renders. Call on the UI thread.
    /// </summary>
    private static void ShowViewer(GLBaseControl viewer)
    {
        for (Control? control = viewer.GLControl; control != null; control = control.Parent)
        {
            if (control is TabPage tabPage && tabPage.Parent is TabControl tabControl)
            {
                tabControl.SelectedTab = tabPage;
            }
        }

        viewer.AttachToRenderLoop();
    }

    private static string? ViewerError(TabPage page)
        => page.Tag is ExportData { DisposableContents: Types.Viewers.Resource { ViewerException: { } e } } ? DescribeException(e) : null;

    private static string DescribeException(Exception e)
        => $"{e.GetType().Name}: {e.Message}" + (e.InnerException is { } inner ? " ---> " + DescribeException(inner) : string.Empty);

    /// <summary>A case insensitive regular expression, or null for no pattern.</summary>
    private static Regex? Pattern(string? pattern)
        => pattern == null ? null : new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Runs <paramref name="callback"/> on the UI thread. A callback that timed out, because a load or
    /// a modal dialog holds the thread, is dropped rather than run later.
    /// </summary>
    private static async Task<T> OnUi<T>(Func<T> callback, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        using var abandon = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            return await Program.MainForm.InvokeAsync(callback, abandon.Token).WaitAsync(timeout ?? UiTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            await abandon.CancelAsync().ConfigureAwait(false);
            throw new ToolException($"The UI thread did not respond within {(timeout ?? UiTimeout).TotalSeconds:F0}s. A load or a modal dialog may be holding it.");
        }
    }

    private static Task<bool> OnUi(Action callback, CancellationToken cancellationToken) => OnUi(() =>
    {
        callback();
        return true;
    }, cancellationToken);

    /// <summary>Runs <paramref name="work"/> with the viewer's GL context, between two frames.</summary>
    private static Task<T> WithGl<T>(GLBaseControl viewer, Func<T> work, CancellationToken cancellationToken)
    {
        // Off the UI thread, because a frame can wait on the UI thread while it holds the context
        return Task.Run(() =>
        {
            using var gl = viewer.MakeCurrent();
            return work();
        }, cancellationToken);
    }

    private static Task<bool> WithGl(GLBaseControl viewer, Action work, CancellationToken cancellationToken) => WithGl(viewer, () =>
    {
        work();
        return true;
    }, cancellationToken);

    /// <summary>Every node of every scene a viewer draws, the 3D sky and other spawn groups included.</summary>
    private static IEnumerable<SceneNode> AllNodes(GLSceneViewer viewer) => viewer.Renderer.Scenes.SelectMany(static scene => scene.AllNodes);

    /// <summary>Draws frames of an active viewer, even while the window is in the background.</summary>
    private static async Task RenderFrames(GLBaseControl viewer, int count, CancellationToken cancellationToken)
    {
        await OnUi(() => ShowViewer(viewer), cancellationToken).ConfigureAwait(false);

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var frames = RenderLoopThread.DrawFrames(viewer, count, FrameTimeout, stop.Token);
        var crash = UnhandledExceptions.NextAsync();

        if (await Task.WhenAny(frames, crash).ConfigureAwait(false) == crash)
        {
            await stop.CancelAsync().ConfigureAwait(false);
            throw new ToolException($"Unhandled exception while rendering: {await crash.ConfigureAwait(false)}");
        }

        if (!await frames.ConfigureAwait(false))
        {
            throw new ToolException($"The tab drew no frame within {FrameTimeout.TotalSeconds:F0}s. Only the active tab draws, so another tab may have been selected.");
        }
    }

    /// <summary>
    /// Draws a frame of the active tab, so the window shows what a tool changed even in the background.
    /// The change is made either way, so a busy UI thread only means no frame for now.
    /// </summary>
    private static async Task Redraw(CancellationToken cancellationToken)
    {
        GLBaseControl? viewer;

        try
        {
            viewer = await OnUi(() =>
            {
                var viewer = Program.MainForm.Tabs.SelectedTab is { } page ? GLBaseControl.FindHostedIn(page) : null;
                viewer?.AttachToRenderLoop();
                return viewer;
            }, cancellationToken, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (ToolException)
        {
            return;
        }

        if (viewer != null)
        {
            await RenderLoopThread.DrawFrames(viewer, 1, FrameTimeout, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>For an optional flag, which is left out of a reply rather than written as false.</summary>
    private static bool? Flag(bool value) => value ? true : null;

    private static void SkipEmpty(JsonTypeInfo info)
    {
        foreach (var property in info.Properties)
        {
            if (property.PropertyType.IsAssignableTo(typeof(IEnumerable)))
            {
                property.ShouldSerialize = static (_, value) => value is not (null or "" or ICollection { Count: 0 } or JsonArray { Count: 0 } or JsonObject { Count: 0 });
            }
        }
    }

    /// <summary>Writes a float rounded, or null when it is not finite, which JSON has no number for.</summary>
    private static void WriteRounded(Utf8JsonWriter writer, float value, int digits)
    {
        if (float.IsFinite(value))
        {
            // Adding zero turns a rounded -0 into 0
            writer.WriteNumberValue(Math.Round(value, digits) + 0d);
        }
        else
        {
            writer.WriteNullValue();
        }
    }

    private sealed class Vector3Converter : JsonConverter<Vector3>
    {
        public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => JsonSerializer.Deserialize<float[]>(ref reader, options) is [var x, var y, var z] ? new Vector3(x, y, z) : throw new JsonException();

        public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            WriteRounded(writer, value.X, 2);
            WriteRounded(writer, value.Y, 2);
            WriteRounded(writer, value.Z, 2);
            writer.WriteEndArray();
        }
    }

    private sealed class FloatConverter : JsonConverter<float>
    {
        public override float Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.GetSingle() is var value && float.IsFinite(value) ? value : throw new JsonException();

        public override void Write(Utf8JsonWriter writer, float value, JsonSerializerOptions options) => WriteRounded(writer, value, 3);
    }
}
#endif
