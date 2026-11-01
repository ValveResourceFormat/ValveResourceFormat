#if DEBUG
using System.Collections;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
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
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace GUI.Automation;

/// <summary>
/// Marks a method of <see cref="McpTools"/> as a tool named after it in snake_case. Its parameters
/// are the arguments: each needs a <see cref="DescriptionAttribute"/>, a <see cref="RangeAttribute"/>
/// bounds it, and a default makes it optional. A <see cref="TabPage"/> or viewer parameter makes the
/// tool act on a tab, the active one unless 'tab' names another. For a viewer it waits for the load and
/// fails when the tab has no viewer of that type. A redrawing tool also selects the tab.
/// A method returning a task, or a concurrent one, runs on the thread pool. Any other method that acts
/// on a 3D scene runs between frames with the GL context, because the render thread changes the scene
/// as it draws. The rest run on the UI thread. The return value is serialized as the JSON reply.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class ToolAttribute(string description) : Attribute
{
    public string Description => description;

    /// <summary>
    /// The tool changes what the window shows, so a frame is drawn after it unless it returns an
    /// <see cref="UnchangedReply"/>.
    /// </summary>
    public bool Redraws { get; init; }

    /// <summary>The tool answers while another call runs, instead of waiting for it.</summary>
    public bool Concurrent { get; init; }
}

/// <summary>A failure worded for the caller.</summary>
#pragma warning disable CA1032 // Implement standard exception constructors
internal sealed class ToolException(string message) : Exception(message);
#pragma warning restore CA1032 // Implement standard exception constructors

/// <summary>A reply with an image after its JSON.</summary>
internal sealed record ImageReply(object Result, byte[] Jpeg);

/// <summary>A redrawing tool's reply when the call changed nothing, so no frame is drawn.</summary>
internal sealed record UnchangedReply(object Result);

/// <summary>Ids for objects, each kept as long as the object lives.</summary>
internal sealed class IdRegistry<T>
    where T : class
{
    private static int LastId;
    private static readonly ConditionalWeakTable<T, StrongBox<int>>.CreateValueCallback NextId = static _ => new StrongBox<int>(Interlocked.Increment(ref LastId));

    private readonly ConditionalWeakTable<T, StrongBox<int>> Ids = [];

    public int IdOf(T item) => Ids.GetValue(item, NextId).Value;

    public T? Find(int id)
    {
        foreach (var (item, itemId) in Ids)
        {
            if (itemId.Value == id)
            {
                return item;
            }
        }

        return null;
    }
}

/// <summary>The tools exposed over <see cref="McpServer"/>, declared by <see cref="ToolAttribute"/>.</summary>
internal sealed partial class McpTools : IDisposable
{
    /// <param name="Name">The argument name, in snake case.</param>
    private sealed record ToolParameter(string Name, Type Type, bool HasDefaultValue, object? DefaultValue, RangeAttribute? Range);

    /// <param name="Target">The tab or viewer type the tool acts on, or null when it acts on no tab.</param>
    /// <param name="TargetIndex">The parameter that takes the tab or viewer, or -1.</param>
    private sealed record Tool(string Name, ToolAttribute Info, MethodInfo Method, JsonObject Schema, ToolParameter[] Parameters, Type? Target, int TargetIndex);

    private static readonly TimeSpan UiTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Snake case names, no nulls or empty collections, and no escaping of text that only a model reads.
    /// Positions round to two decimals and other floats to three.
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

                if (obj.TryGetPropertyValue("default", out var defaultValue) && defaultValue == null)
                {
                    obj.Remove("default");
                }
            }

            return schema;
        },
    };

    private readonly Dictionary<string, Tool> Tools = [];
    private readonly IdRegistry<TabPage> TabIds = new();

    // The viewer state the tools drive is global, so calls run one at a time unless marked concurrent
    private readonly SemaphoreSlim CallLock = new(1, 1);

    public McpTools()
    {
        foreach (var method in typeof(McpTools).GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic))
        {
            if (method.GetCustomAttribute<ToolAttribute>() is { } info)
            {
                var name = JsonNamingPolicy.SnakeCaseLower.ConvertName(method.Name);
                Tools.Add(name, CreateTool(name, info, method));
            }
        }
    }

    private static Tool CreateTool(string name, ToolAttribute info, MethodInfo method)
    {
        var parameters = method.GetParameters();
        var described = new ToolParameter[parameters.Length];
        var targetIndex = -1;

        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            var parameterName = JsonNamingPolicy.SnakeCaseLower.ConvertName(parameter.Name!);
            var range = parameter.GetCustomAttribute<RangeAttribute>();

            described[i] = new ToolParameter(parameterName, parameter.ParameterType, parameter.HasDefaultValue, parameter.DefaultValue, range);

            if (IsTabTarget(parameter.ParameterType))
            {
                targetIndex = i;
            }
        }

        var target = targetIndex >= 0 ? parameters[targetIndex].ParameterType : null;

        return new Tool(name, info, method, CreateSchema(method), described, target, targetIndex);
    }

    public bool Has(string name) => Tools.ContainsKey(name);

    public IEnumerable<string> Names => Tools.Keys;

    public void Dispose() => CallLock.Dispose();

    public JsonObject List() => new()
    {
        ["tools"] = new JsonArray([.. Tools.Values.Select(static tool => new JsonObject
        {
            ["name"] = tool.Name,
            ["description"] = tool.Info.Description,
            ["inputSchema"] = tool.Schema.DeepClone(),
        })]),
    };

    public async Task<JsonObject> Call(string name, JsonObject arguments, CancellationToken cancellationToken)
    {
        var tool = Tools[name];
        var locked = false;

        try
        {
            if (!tool.Info.Concurrent)
            {
                await CallLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                locked = true;
            }

            var result = await Invoke(tool, arguments, cancellationToken).ConfigureAwait(false);

            if (result is ImageReply image)
            {
                return Reply(JsonSerializer.Serialize(image.Result, Json), image: image.Jpeg);
            }

            return Reply(JsonSerializer.Serialize(result ?? new { }, Json));
        }
        catch (OperationCanceledException)
        {
            return Reply("The call was cancelled.", isError: true);
        }
        catch (Exception e) when (e is ToolException or ArgumentException or TimeoutException or IOException or UnauthorizedAccessException)
        {
            Log.Info(nameof(McpTools), $"{name} failed: {e.Message}");
            return Reply(e.Message, isError: true);
        }
#pragma warning disable CA1031 // Any other failure is a bug, reported to the caller rather than thrown at the transport
        catch (Exception e)
#pragma warning restore CA1031
        {
            Log.Error(nameof(McpTools), $"{name} failed unexpectedly: {e}");
            return Reply($"Unexpected {e.GetType().Name}: {e.Message}", isError: true);
        }
        finally
        {
            if (locked)
            {
                CallLock.Release();
            }
        }
    }

    /// <summary>A tool result: one text item, with an image after it when there is one.</summary>
    private static JsonObject Reply(string text, bool isError = false, byte[]? image = null)
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
                var valueType = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;

                // Integer bounds would truncate the value before comparing it
                if (range.OperandType == typeof(int) && valueType == typeof(float))
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

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required,
        };
    }

    private static bool IsTabTarget(Type type) => type == typeof(TabPage) || type.IsAssignableTo(typeof(GLBaseControl));

    private static bool HasMaximum(RangeAttribute range) => range.Maximum is not (int.MaxValue or double.MaxValue);

    private async Task<object?> Invoke(Tool tool, JsonObject givenArguments, CancellationToken cancellationToken)
    {
        var properties = (JsonObject)tool.Schema["properties"]!;
        var arguments = new JsonObject();

        foreach (var (key, value) in givenArguments)
        {
            // Callers often guess the camelCase spelling of an argument
            var name = JsonNamingPolicy.SnakeCaseLower.ConvertName(key);

            if (!properties.ContainsKey(name))
            {
                var takes = properties.Count == 0 ? "none" : string.Join(", ", properties.Select(static p => p.Key));
                throw new ArgumentException($"Unknown argument '{key}'. {tool.Name} takes {takes}.");
            }

            if (arguments.ContainsKey(name))
            {
                throw new ArgumentException($"'{key}' and '{name}' are the same argument. Pass only one.");
            }

            arguments[name] = value?.DeepClone();
        }

        var values = new object?[tool.Parameters.Length];

        for (var i = 0; i < tool.Parameters.Length; i++)
        {
            var parameter = tool.Parameters[i];

            if (parameter.Type == typeof(CancellationToken))
            {
                values[i] = cancellationToken;
            }
            else if (i != tool.TargetIndex)
            {
                var node = arguments[parameter.Name];

                if (node != null)
                {
                    values[i] = Deserialize(parameter.Name, node, parameter.Type, (JsonObject)properties[parameter.Name]!);
                }
                else if (parameter.HasDefaultValue)
                {
                    values[i] = parameter.DefaultValue;
                }
                else
                {
                    throw new ArgumentException($"Missing '{parameter.Name}'.");
                }

                var range = parameter.Range;

                if (range != null && !range.IsValid(values[i]))
                {
                    var lower = range.MinimumIsExclusive ? "more than" : "at least";
                    var upper = HasMaximum(range) ? string.Create(CultureInfo.InvariantCulture, $" and at most {range.Maximum}") : string.Empty;
                    throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"'{parameter.Name}' must be {lower} {range.Minimum}{upper}, got {node?.ToJsonString()}."));
                }
            }
        }

        GLBaseControl? viewer = null;

        if (tool.Target != null)
        {
            int? tabId = null;
            var tab = arguments["tab"];

            if (tab != null)
            {
                tabId = (int)Deserialize("tab", tab, typeof(int), (JsonObject)properties["tab"]!)!;
            }

            var target = await ResolveTarget(tool, tabId, cancellationToken).ConfigureAwait(false);
            values[tool.TargetIndex] = target;
            viewer = target as GLBaseControl;
        }

        var result = await Dispatch(tool, values, viewer, cancellationToken).ConfigureAwait(false);

        if (result is UnchangedReply unchanged)
        {
            return unchanged.Result;
        }

        if (tool.Info.Redraws)
        {
            await Redraw(viewer, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>
    /// The tab or viewer a tool acts on. For a viewer, waits for its tab to load and selects the tab
    /// when the tool redraws.
    /// </summary>
    private async Task<object> ResolveTarget(Tool tool, int? tabId, CancellationToken cancellationToken)
    {
        object? target = null;

        while (target == null)
        {
            var loading = await OnUi<Task?>(() =>
            {
                var page = ResolveTab(tabId);

                if (tool.Target == typeof(TabPage))
                {
                    target = page;
                    return null;
                }

                // A viewer exists only after its tab loads
                if (page.Tag is ExportData { Loaded: { IsCompleted: false } loaded })
                {
                    return loaded;
                }

                var viewer = ViewerOf(page, tool.Target!, tabId == null);

                // A read-only tool leaves the tab the user is looking at alone
                if (tool.Info.Redraws)
                {
                    ShowViewer(viewer);
                }

                target = viewer;
                return null;
            }, cancellationToken).ConfigureAwait(false);

            try
            {
                if (loading != null)
                {
                    await loading.WaitAsync(LoadTimeout, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (TimeoutException)
            {
                throw new ToolException(string.Create(CultureInfo.InvariantCulture, $"The tab is still loading after {LoadTimeout.TotalSeconds:F0}s."));
            }
        }

        return target;
    }

    /// <summary>Runs a tool on the thread it needs.</summary>
    private async Task<object?> Dispatch(Tool tool, object?[] values, GLBaseControl? viewer, CancellationToken cancellationToken)
    {
        // A concurrent tool answers while the UI thread may be held by the call it runs alongside
        if (tool.Info.Concurrent || tool.Method.ReturnType.IsAssignableTo(typeof(Task)))
        {
            return await Await(Run(tool, values)).ConfigureAwait(false);
        }

        if (viewer != null && tool.Target!.IsAssignableTo(typeof(GLSceneViewer)))
        {
            return await WithGl(viewer, () => Run(tool, values), cancellationToken).ConfigureAwait(false);
        }

        return await OnUi(() => Run(tool, values), cancellationToken).ConfigureAwait(false);
    }

    private object? Run(Tool tool, object?[] values) => tool.Method.Invoke(this, BindingFlags.DoNotWrapExceptions, null, values, null);

    /// <summary>Awaits a tool's result when it returned a task.</summary>
    private static async Task<object?> Await(object? returned)
    {
        if (returned is Task<object> task)
        {
            return await task.ConfigureAwait(false);
        }

        return returned;
    }

    private static object? Deserialize(string name, JsonNode node, Type type, JsonObject schema)
    {
        try
        {
            // The enum converter also takes numbers and comma separated lists, which the schema does not offer
            if (schema["enum"] is JsonArray names)
            {
                var isName = node.GetValueKind() == JsonValueKind.String
                    && names.Any(value => string.Equals((string?)value, (string?)node, StringComparison.OrdinalIgnoreCase));

                if (!isName)
                {
                    throw new JsonException();
                }
            }

            return node.Deserialize(type, Json);
        }
        catch (JsonException)
        {
            throw new ArgumentException($"'{name}' must be {Expected(schema)}, got {node.ToJsonString()}.");
        }
    }

    /// <summary>What a value of this schema looks like, for an error message.</summary>
    private static string Expected(JsonObject schema)
    {
        if (schema["enum"] is JsonArray values)
        {
            return $"one of {string.Join(", ", values)}";
        }

        return (string?)schema["type"] switch
        {
            "array" when schema["minItems"] is not null => "[x, y, z]",
            "array" => "an array",
            "object" when schema["propertyNames"] is not null => "an object of indices to [x, y, z]",
            "object" => "an object",
            "integer" => "an integer",
            "boolean" => "true or false",
            var other => $"a {other}",
        };
    }

    /// <summary>The tab with this id, or the active one. Call on the UI thread.</summary>
    private TabPage ResolveTab(int? id)
    {
        if (id == null)
        {
            return Program.MainForm.Tabs.SelectedTab ?? throw new ToolException("No tab is open.");
        }

        return TabIds.Find(id.Value) is { Parent: not null } page
            ? page
            : throw new ToolException($"No tab with id {id}. get_status lists the open tabs.");
    }

    /// <summary>The viewer of a loaded tab, failing when it is not of the given type. Call on the UI thread.</summary>
    private GLBaseControl ViewerOf(TabPage page, Type type, bool activeTab)
    {
        var viewer = GLBaseControl.FindHostedIn(page);

        if (!type.IsInstanceOfType(viewer))
        {
            var needs = type.Name switch
            {
                nameof(GLWorldViewer) => "has no map",
                nameof(GLParticleViewer) => "is not a particle system",
                nameof(GLSceneViewer) => "has no 3D scene",
                _ => "has no rendered view",
            };
            var errors = LoadErrors(page);
            var failure = errors != null ? $" Opening it failed: {errors}" : string.Empty;
            var implicitTab = activeTab ? " No 'tab' was given, so the tool used the active tab." : string.Empty;

            throw new ToolException($"Tab {TabIds.IdOf(page)} '{page.Text}' {needs}.{failure}{implicitTab}");
        }

        return viewer!;
    }

    /// <summary>
    /// Selects the tabs a viewer sits in, the file's own tabs included, and attaches it to the render
    /// loop, since only the active tab renders. Call on the UI thread.
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

    [return: NotNullIfNotNull(nameof(e))]
    private static string? DescribeException(Exception? e)
    {
        if (e == null)
        {
            return null;
        }

        var description = $"{e.GetType().Name}: {e.Message}";

        if (e.InnerException == null)
        {
            return description;
        }

        return $"{description} ---> {DescribeException(e.InnerException)}";
    }

    /// <summary>A case insensitive regular expression, or null for no pattern.</summary>
    /// <param name="argument">The argument the pattern came from, for the error.</param>
    private static Regex? Pattern(string? pattern, string argument)
    {
        if (pattern == null)
        {
            return null;
        }

        try
        {
            return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }
        catch (RegexParseException e)
        {
            throw new ArgumentException($"'{argument}' is a regular expression, not a wildcard, and a plain word matches anywhere. {e.Message}");
        }
    }

    /// <summary>Whether text matches a pattern from <see cref="Pattern"/>. A null pattern matches all text.</summary>
    private static bool Matches(Regex? pattern, string? text) => pattern == null || pattern.IsMatch(text ?? string.Empty);

    /// <summary>
    /// Runs <paramref name="callback"/> on the UI thread. A callback that times out, because a load or
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
            throw new ToolException(string.Create(CultureInfo.InvariantCulture, $"The UI thread did not respond within {(timeout ?? UiTimeout).TotalSeconds:F0}s. A load or a modal dialog may hold it."));
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

    /// <summary>
    /// Runs <paramref name="work"/> with the viewer's GL context to undo a tool's change, also after the
    /// tool failed or was cancelled. A viewer whose tab closed meanwhile has nothing to undo, so its
    /// missing context does not hide the tool's own error.
    /// </summary>
    private static async Task RestoreWithGl(GLBaseControl viewer, Action work)
    {
        try
        {
            await WithGl(viewer, work, CancellationToken.None).ConfigureAwait(false);
        }
        catch (InvalidOperationException) when (viewer.GraphicsContext == null)
        {
            // The tab closed, and its context with it
        }
    }

    /// <summary>Every node of every scene a viewer draws, the 3D sky and other spawn groups included.</summary>
    private static IEnumerable<SceneNode> AllNodes(GLSceneViewer viewer) => viewer.Renderer.Scenes.SelectMany(static scene => scene.AllNodes);

    /// <summary>Draws frames of an active viewer, even while the window is in the background.</summary>
    /// <param name="readsPixels">Whether the window's pixels are read back afterwards, which a minimized window has none of.</param>
    private static async Task RenderFrames(GLBaseControl viewer, int count, CancellationToken cancellationToken, bool readsPixels = false)
    {
        await ShowForFrames(viewer, readsPixels, cancellationToken).ConfigureAwait(false);
        await DrawShownFrames(viewer, count, ReportedErrors.NextAsync(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Selects a viewer's tab and attaches it to the render loop, so its frames can be drawn.</summary>
    /// <param name="readsPixels">Whether the window's pixels are read back afterwards, which a minimized window has none of.</param>
    private static async Task ShowForFrames(GLBaseControl viewer, bool readsPixels, CancellationToken cancellationToken)
    {
        await OnUi(() =>
        {
            // Frames still draw while minimized
            if (readsPixels)
            {
                RestoreMinimized();
            }

            ShowViewer(viewer);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Restores a minimized viewer window without activating it, so the app in front keeps the focus.
    /// The window lays out its controls at the restored size before this returns, and the next frame
    /// resizes the render area to match. Call on the UI thread.
    /// </summary>
    private static void RestoreMinimized()
    {
        var form = Program.MainForm;

        if (form.WindowState == FormWindowState.Minimized)
        {
            PInvoke.ShowWindow((HWND)form.Handle, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
        }
    }

    /// <summary>
    /// Draws frames of a viewer that <see cref="ShowViewer"/> attached to the render loop, failing with
    /// <paramref name="crash"/> when an error is reported first.
    /// </summary>
    private static async Task DrawShownFrames(GLBaseControl viewer, int count, Task<string> crash, CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var frames = RenderLoopThread.DrawFrames(viewer, count, FrameTimeout, stop.Token);

        // An error reported while drawing ends the wait for the frames
        if (await Task.WhenAny(crash, frames).ConfigureAwait(false) == crash)
        {
            await stop.CancelAsync().ConfigureAwait(false);
            throw new ToolException($"Error while rendering: {await crash.ConfigureAwait(false)}");
        }

        if (!await frames.ConfigureAwait(false))
        {
            throw new ToolException(string.Create(CultureInfo.InvariantCulture, $"The tab drew no frame within {FrameTimeout.TotalSeconds:F0}s. Only the active tab draws, so another tab may have been selected."));
        }
    }

    /// <summary>
    /// Draws a frame of the tab a tool changed, so the window shows the change even in the background.
    /// The change is made either way, so a busy UI thread only skips the frame.
    /// </summary>
    /// <param name="shown">The viewer the tool acted on, shown by <see cref="ResolveTarget"/>, or null.</param>
    private static async Task Redraw(GLBaseControl? shown, CancellationToken cancellationToken)
    {
        var viewer = shown;

        // A tool without a viewer, or one that opened another tab, changed the active tab
        if (viewer == null || !RenderLoopThread.IsCurrentGLControl(viewer))
        {
            try
            {
                viewer = await OnUi(ShowActiveViewer, cancellationToken, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch (ToolException)
            {
                return;
            }
        }

        if (viewer != null)
        {
            await RenderLoopThread.DrawFrames(viewer, 1, FrameTimeout, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Attaches the active tab's viewer to the render loop and returns it, or null for a tab without one.</summary>
    private static GLBaseControl? ShowActiveViewer()
    {
        var page = Program.MainForm.Tabs.SelectedTab;

        if (page == null)
        {
            return null;
        }

        var viewer = GLBaseControl.FindHostedIn(page);
        viewer?.AttachToRenderLoop();
        return viewer;
    }

    /// <summary>An optional flag, left out of a reply rather than written as false.</summary>
    private static bool? Flag(bool value) => value ? true : null;

    /// <summary>An optional count, left out of a reply rather than written as zero.</summary>
    private static int? NonZero(int value) => value > 0 ? value : null;

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

    /// <summary>Writes a float rounded, or null when it is not finite, since JSON has no number for that.</summary>
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
        {
            var value = reader.GetSingle();

            if (!float.IsFinite(value))
            {
                throw new JsonException();
            }

            return value;
        }

        public override void Write(Utf8JsonWriter writer, float value, JsonSerializerOptions options) => WriteRounded(writer, value, 3);
    }
}
#endif
