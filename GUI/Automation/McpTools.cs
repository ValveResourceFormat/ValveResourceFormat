#if DEBUG
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Types.Exporter;
using GUI.Types.GLViewers;
using GUI.Utils;

namespace GUI.Automation;

/// <summary>
/// The tool table exposed over <see cref="McpServer"/>. Every handler marshals to the UI thread
/// before touching tabs, viewers or controls.
/// </summary>
internal sealed partial class McpTools
{
    private sealed record Tool(
        string Name,
        string Description,
        JsonObject InputSchema,
        Func<JsonObject, CancellationToken, Task<McpToolResult>> Handler);

    /// <summary>
    /// How long a call may wait for the UI thread. A modal dialog blocks it for as long as it is
    /// up, so without this the whole server would stop answering behind one message box.
    /// </summary>
    private static readonly TimeSpan UiTimeout = TimeSpan.FromSeconds(20);

    private readonly Dictionary<string, Tool> Table = [];
    private readonly Dictionary<int, TabPage> TabIds = [];
    private readonly long StartedAt = Stopwatch.GetTimestamp();

    private int nextTabId;

    public McpTools()
    {
        RegisterCoreTools();
        RegisterViewerTools();
    }

    public JsonObject List()
    {
        var tools = new JsonArray();

        foreach (var tool in Table.Values)
        {
            tools.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["inputSchema"] = tool.InputSchema.DeepClone(),
            });
        }

        return new JsonObject
        {
            ["tools"] = tools,
        };
    }

    /// <summary>Whether a tool by this name is registered.</summary>
    public bool Has(string name) => Table.ContainsKey(name);

    public async Task<McpToolResult> Call(string name, JsonObject arguments, CancellationToken cancellationToken)
    {
        if (!Table.TryGetValue(name, out var tool))
        {
            return McpToolResult.Error($"Unknown tool: {name}");
        }

        try
        {
            return await tool.Handler(arguments, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return McpToolResult.Error(
                "Timed out waiting for the UI thread. A modal dialog may be open in the viewer.");
        }
        catch (OperationCanceledException)
        {
            return McpToolResult.Error("The server is shutting down.");
        }
    }

    private void Add(string name, string description, JsonObject schema, Func<JsonObject, CancellationToken, Task<McpToolResult>> handler)
    {
        Table.Add(name, new Tool(name, description, schema, handler));
    }

    private void RegisterCoreTools()
    {
        Add("get_status", "Server and application status: process id, version, uptime and the active tab.",
            Schema(),
            (_, ct) => GetStatus(ct));

        Add("quit", "Close the viewer. Required before rebuilding, because a running instance holds the build output open.",
            Schema(),
            (_, _) => Quit());

        Add("get_log", "Lines from the viewer's console, oldest first. Shader compile errors, load failures and renderer warnings all go here.",
            Schema(new JsonObject
            {
                ["limit"] = Prop("integer", "Most recent lines to return. Defaults to 200."),
            }),
            GetLog);

        Add("list_tabs", "List open tabs with their id, title, viewer kind and which one is active.",
            Schema(),
            (_, ct) => ListTabs(ct));

        Add("select_tab", "Make a tab active. Only the active tab renders.",
            Schema(new JsonObject
            {
                ["tab"] = Prop("integer", "Tab id from list_tabs."),
            }, "tab"),
            SelectTab);

        Add("close_tab", "Close a tab.",
            Schema(new JsonObject
            {
                ["tab"] = Prop("integer", "Tab id from list_tabs."),
            }, "tab"),
            CloseTab);

        Add("open_file", "Open a file and wait until its tab has finished loading. Accepts the same paths as the command line, including vpk:outer_dir.vpk:inner/file.",
            Schema(new JsonObject
            {
                ["path"] = Prop("string", "File path, or vpk:package.vpk:inner/file for a file inside a package."),
                ["timeout_seconds"] = Prop("integer", "How long to wait for loading. Defaults to 180."),
            }, "path"),
            OpenFile);
    }

    private async Task<McpToolResult> GetStatus(CancellationToken cancellationToken)
    {
        using var process = Process.GetCurrentProcess();

        var active = await OnUi(() =>
        {
            var page = Program.MainForm.Tabs.SelectedTab;

            return page == null ? null : new JsonObject
            {
                ["tab"] = IdFor(page),
                ["title"] = page.Text,
                ["viewer"] = DescribeViewer(page),
            };
        }, cancellationToken).ConfigureAwait(false);

        return McpToolResult.Json(new JsonObject
        {
            ["pid"] = process.Id,
            ["version"] = Program.ProductVersion,
            ["uptime_seconds"] = Math.Round(Stopwatch.GetElapsedTime(StartedAt).TotalSeconds, 1),
            ["active_tab"] = active,
        });
    }

    private static Task<McpToolResult> Quit()
    {
        // Answer first, then close, so the caller gets a reply rather than a dropped connection.
        _ = Task.Run(async () =>
        {
            await Task.Delay(250).ConfigureAwait(false);

            var form = Program.MainForm;

            if (!form.IsDisposed)
            {
                form.BeginInvoke(form.Close);
            }
        });

        return Task.FromResult(McpToolResult.Text("Closing."));
    }

    private static async Task<McpToolResult> GetLog(JsonObject args, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(GetInt(args, "limit") ?? 200, 1, 5000);
        var lines = await OnUi(() => Log.GetLines(limit), cancellationToken).ConfigureAwait(false);

        return lines.Count == 0
            ? McpToolResult.Text("The console is empty.")
            : McpToolResult.Text(string.Join(Environment.NewLine, lines));
    }

    private async Task<McpToolResult> ListTabs(CancellationToken cancellationToken)
    {
        var tabs = await OnUi(() =>
        {
            PruneTabIds();

            var list = new JsonArray();
            var selected = Program.MainForm.Tabs.SelectedTab;

            foreach (TabPage page in Program.MainForm.Tabs.TabPages)
            {
                list.Add(new JsonObject
                {
                    ["tab"] = IdFor(page),
                    ["title"] = page.Text,
                    ["file"] = page.ToolTipText,
                    ["viewer"] = DescribeViewer(page),
                    ["active"] = page == selected,
                });
            }

            return list;
        }, cancellationToken).ConfigureAwait(false);

        return McpToolResult.Json(new JsonObject
        {
            ["tabs"] = tabs,
        });
    }

    private Task<McpToolResult> SelectTab(JsonObject args, CancellationToken cancellationToken)
        => WithTab(args, page =>
        {
            Program.MainForm.Tabs.SelectTab(page);

            return McpToolResult.Ok();
        }, cancellationToken);

    private Task<McpToolResult> CloseTab(JsonObject args, CancellationToken cancellationToken)
        => WithTab(args, page =>
        {
            Program.MainForm.Tabs.CloseTab(page);

            return McpToolResult.Ok();
        }, cancellationToken);

    /// <summary>Runs <paramref name="action"/> on the UI thread with the tab the arguments name.</summary>
    private async Task<McpToolResult> WithTab(JsonObject args, Func<TabPage, McpToolResult> action, CancellationToken cancellationToken)
    {
        var id = GetInt(args, "tab");

        if (id == null)
        {
            return MissingArgument("tab", args);
        }

        return await OnUi(() =>
        {
            var page = PageFor(id.Value);

            return page == null ? McpToolResult.Error($"No tab with id {id}.") : action(page);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<McpToolResult> OpenFile(JsonObject args, CancellationToken cancellationToken)
    {
        var path = GetString(args, "path");

        if (string.IsNullOrEmpty(path))
        {
            return MissingArgument("path", args);
        }

        var timeout = TimeSpan.FromSeconds(GetInt(args, "timeout_seconds") ?? 180);
        var completion = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);

        TabPage? tab = null;

        void OnTabLoaded(TabPage page, Exception? error)
        {
            if (page == tab)
            {
                completion.TrySetResult(error);
            }
        }

        // Subscribing on the UI thread means no completion can be dispatched before 'tab' is set,
        // because the notification is posted to this same thread.
        var opened = await OnUi(() =>
        {
            var form = Program.MainForm;

            // Looking for a tab that was not there before, rather than for the selection changing:
            // a path that does not exist selects the console tab instead of opening anything, and
            // waiting for that to finish loading would sit here until the timeout.
            var before = new HashSet<TabPage>();

            foreach (TabPage page in form.Tabs.TabPages)
            {
                before.Add(page);
            }

            form.TabLoadCompleted += OnTabLoaded;
            form.OpenCommandLineArgFiles([path]);

            foreach (TabPage page in form.Tabs.TabPages)
            {
                if (!before.Contains(page))
                {
                    tab = page;
                    return true;
                }
            }

            return false;
        }, cancellationToken).ConfigureAwait(false);

        try
        {
            if (!opened)
            {
                return McpToolResult.Error($"Nothing was opened for '{path}'. Call get_log for the reason, usually that the file or the path inside the package does not exist.");
            }

            Exception? failure;

            try
            {
                failure = await completion.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return McpToolResult.Error($"Timed out after {timeout.TotalSeconds:F0}s loading '{path}'.");
            }

            if (failure != null)
            {
                return McpToolResult.Error($"Failed to load '{path}': {failure.Message}");
            }
        }
        finally
        {
            await OnUi(() =>
            {
                Program.MainForm.TabLoadCompleted -= OnTabLoaded;
                return true;
            }, CancellationToken.None).ConfigureAwait(false);
        }

        return await OnUi(() => McpToolResult.Json(new JsonObject
        {
            ["tab"] = IdFor(tab!),
            ["title"] = tab!.Text,
            ["file"] = tab.ToolTipText,
            ["viewer"] = DescribeViewer(tab),
        }), cancellationToken).ConfigureAwait(false);
    }

    private static string DescribeViewer(TabPage page)
    {
        if (GLBaseControl.FindHostedIn(page) is { } glViewer)
        {
            return glViewer.GetType().Name;
        }

        if (page.Tag is ExportData { DisposableContents: { } contents })
        {
            return contents.GetType().Name;
        }

        return "none";
    }

    private int IdFor(TabPage page)
    {
        foreach (var (id, known) in TabIds)
        {
            if (known == page)
            {
                return id;
            }
        }

        var newId = ++nextTabId;
        TabIds[newId] = page;
        return newId;
    }

    private TabPage? PageFor(int id)
    {
        PruneTabIds();

        return TabIds.GetValueOrDefault(id);
    }

    // Closing a tab in the GUI does not tell us, so stale entries would keep disposed pages, and
    // everything their Tag still holds, alive for the life of the process.
    private void PruneTabIds()
    {
        foreach (var (id, page) in TabIds)
        {
            if (page.IsDisposed || page.Parent == null)
            {
                TabIds.Remove(id);
            }
        }
    }

    private static Task<T> OnUi<T>(Func<T> callback, CancellationToken cancellationToken)
        => OnUi(callback, UiTimeout, cancellationToken);

    private static async Task<T> OnUi<T>(Func<T> callback, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var form = Program.MainForm;

        if (form == null || form.IsDisposed)
        {
            throw new InvalidOperationException("The main window is not available.");
        }

        return await form.InvokeAsync(callback, cancellationToken).WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
    }

    private static JsonObject Schema(JsonObject? properties = null, params string[] required)
    {
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties ?? [],
        };

        if (required.Length > 0)
        {
            var names = new JsonArray();

            foreach (var name in required)
            {
                names.Add(name);
            }

            schema["required"] = names;
        }

        return schema;
    }

    private static JsonObject Prop(string type, string description) => new()
    {
        ["type"] = type,
        ["description"] = description,
    };

    /// <summary>Names the argument that was wanted and what actually arrived, so a caller that
    /// guessed the wrong key can see its own spelling in the reply.</summary>
    private static McpToolResult MissingArgument(string name, JsonObject args)
    {
        if (args.Count == 0)
        {
            return McpToolResult.Error($"Missing '{name}'. No arguments were given.");
        }

        var received = new List<string>(args.Count);

        foreach (var (key, _) in args)
        {
            received.Add($"'{key}'");
        }

        return McpToolResult.Error($"Missing '{name}'. Received {string.Join(", ", received)}.");
    }

    private static string? GetString(JsonObject args, string name)
        => args.TryGetPropertyValue(name, out var node) && node != null ? node.GetValue<string>() : null;

    private static int? GetInt(JsonObject args, string name)
        => args.TryGetPropertyValue(name, out var node) && node != null ? node.GetValue<int>() : null;

    private static float? GetFloat(JsonObject args, string name)
        => args.TryGetPropertyValue(name, out var node) && node != null ? node.GetValue<float>() : null;

    private static bool? GetBool(JsonObject args, string name)
        => args.TryGetPropertyValue(name, out var node) && node != null ? node.GetValue<bool>() : null;
}
#endif
