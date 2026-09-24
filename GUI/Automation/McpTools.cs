#if DEBUG
using System.Diagnostics;
using System.Runtime;
using System.Text.Json.Nodes;
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
/// The tool table exposed over <see cref="McpServer"/>. Every handler marshals to the UI thread
/// before touching tabs, viewers or controls.
/// </summary>
internal sealed partial class McpTools
{
    /// <param name="AppliesTo">For a tool that acts on one tab's viewer, which viewers it works on.</param>
    private sealed record Tool(
        string Name,
        string Description,
        JsonObject InputSchema,
        Func<JsonObject, CancellationToken, Task<McpToolResult>> Handler,
        Func<GLBaseControl, bool>? AppliesTo);

    /// <summary>
    /// How long a call may wait for the UI thread. A modal dialog blocks it for as long as it is
    /// up, so without this the whole server would stop answering behind one message box.
    /// </summary>
    private static readonly TimeSpan UiTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// How long a call waits for a tab to finish loading, both for the tab it acts on and for the
    /// UI thread while a load holds it.
    /// </summary>
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(180);

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Tools that change what the active viewer shows without drawing a frame themselves. The loop
    /// parks while the window is in the background, so without a redraw the window would keep
    /// showing the old frame to anyone watching.
    /// </summary>
    private static readonly HashSet<string> ChangesView =
    [
        "select_tab", "close_tab", "open_file", "clear_selection", "set_camera", "set_layer",
        "set_physics_group", "set_render_mode", "select_entity", "pick", "pause", "resume",
    ];

    private static readonly TimeSpan RedrawTimeout = TimeSpan.FromSeconds(2);

    private readonly Dictionary<string, Tool> Table = [];
    private readonly Dictionary<int, TabPage> TabIds = [];
    private readonly long StartedAt = Stopwatch.GetTimestamp();

    private int nextTabId;

    public McpTools()
    {
        TabLoads.Track(Program.MainForm);

        RegisterCoreTools();
        RegisterViewerTools();
        RegisterEntityTools();
        RegisterSimulationTools();
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
            var result = await tool.Handler(arguments, cancellationToken).ConfigureAwait(false);

            if (ChangesView.Contains(name))
            {
                await Redraw(cancellationToken).ConfigureAwait(false);
            }

            return result;
        }
        catch (ArgumentException e)
        {
            // Thrown by the argument readers, and already worded for the caller.
            return McpToolResult.Error(e.Message);
        }
        catch (RegexMatchTimeoutException)
        {
            return McpToolResult.Error("A regular expression took too long to match. Simplify 'include' or 'exclude'.");
        }
        catch (TimeoutException e)
        {
            // Thrown by OnUi, and already worded for the caller.
            return McpToolResult.Error(e.Message);
        }
        catch (OperationCanceledException)
        {
            return McpToolResult.Error("The server is shutting down.");
        }
    }

    /// <summary>
    /// Draws the active tab, if it has a GL viewer and the window is not minimized, with every
    /// texture it needs streamed in, so the window settles on what the next screenshot will show.
    /// Best effort: the call already succeeded, so a frame that does not come is not an error.
    /// </summary>
    private static async Task Redraw(CancellationToken cancellationToken)
    {
        var viewer = await OnUi(() =>
        {
            var form = Program.MainForm;

            if (form.WindowState == FormWindowState.Minimized
                || form.Tabs.SelectedTab is not { } page
                || GLBaseControl.FindHostedIn(page) is not { } viewer)
            {
                return null;
            }

            viewer.EnsureAttachedToRenderLoop();
            return viewer;
        }, cancellationToken).ConfigureAwait(false);

        if (viewer == null)
        {
            return;
        }

        using (RenderLoopThread.BeginAutomationRendering())
        {
            await Task.Run(() =>
            {
                // The first frame asks for the textures the new view needs. A frame after the loop
                // was parked streams nothing on its own, because it counts as taking no time.
                if (!RenderLoopThread.WaitForFrames(1, RedrawTimeout))
                {
                    return;
                }

                viewer.FinishTextureStreaming(cancellationToken);
                RenderLoopThread.WaitForFrames(1, RedrawTimeout);
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    private void Add(string name, string description, JsonObject schema, Func<JsonObject, CancellationToken, Task<McpToolResult>> handler, Func<GLBaseControl, bool>? appliesTo = null)
    {
        Table.Add(name, new Tool(name, description, schema, handler, appliesTo));
    }

    private static bool AnyViewer(GLBaseControl viewer) => true;

    private static bool ShaderViewer(GLBaseControl viewer) => viewer.OffersShaderReload;

    private static bool SceneViewer(GLBaseControl viewer) => viewer is GLSceneViewer;

    private static bool WorldViewer(GLBaseControl viewer) => viewer is GLWorldViewer;

    private static bool MapViewer(GLBaseControl viewer) => viewer is GLWorldViewer { LoadedWorld: not null };

    /// <summary>The tools that act on a tab's viewer and work on this one, or null when none do.</summary>
    private JsonArray? ToolsFor(GLBaseControl? viewer)
    {
        if (viewer == null)
        {
            return null;
        }

        var names = new JsonArray();

        foreach (var tool in Table.Values)
        {
            if (tool.AppliesTo?.Invoke(viewer) == true)
            {
                names.Add(tool.Name);
            }
        }

        return names.Count > 0 ? names : null;
    }

    private void RegisterCoreTools()
    {
        Add("get_status", "Server and application status: process id, version, uptime, the active tab and whether the simulation is paused.",
            Schema(),
            (_, ct) => GetStatus(ct));

        Add("quit", "Close the viewer. Required before rebuilding, because a running instance holds the build output open.",
            Schema(),
            (_, _) => Quit());

        Add("get_memory", "Memory use of the viewer process in MB: private bytes and working set, the managed heap (live objects, fragmentation and committed), and garbage collection counts per generation. Pass collect=true to first run a full blocking collection that also compacts the large object heap and runs finalizers, which tells a leak (the heap stays up) from memory that is merely not yet collected. Pooled buffers are only released by collections after sitting unused for 30 to 60 seconds, so a heap that stays up right after closing a large file should be measured again a minute later.",
            Schema(new JsonObject
            {
                ["collect"] = Prop("boolean", "Run a full compacting collection before measuring. Defaults to false."),
            }),
            GetMemory);

        Add("get_log", "Read the viewer's console, oldest first: shader compile errors, load failures and renderer warnings. Each line is 'time level [component] message', level being D, I, W or E. Returns a 'cursor'; pass it back as 'since' to read only what was logged after this call.",
            Schema(new JsonObject
            {
                ["since"] = Prop("integer", "Only lines logged after this cursor, from an earlier get_log or clear_log."),
                ["level"] = Prop("string", "Lowest level to include. Defaults to info, which leaves out debug lines such as OpenGL performance notes.", "debug", "info", "warn", "error"),
                ["include"] = Prop("string", "Case insensitive regular expression; only lines matching it are returned. Matched against '[component] message'."),
                ["exclude"] = Prop("string", "Case insensitive regular expression; lines matching it are left out."),
                ["dedupe"] = Prop("boolean", "Collapse repeats of the same line into its first occurrence with an (xN) count. Defaults to true."),
                ["limit"] = Prop("integer", "Most lines to return, keeping the newest. Defaults to 200, at most 5000."),
            }),
            GetLog);

        Add("clear_log", "Empty the viewer's console. Returns the cursor to pass to get_log as 'since'.",
            Schema(),
            ClearLog);

        Add("list_tabs", "List open tabs with their id, title, file and viewer kind, marking the active one.",
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

        Add("open_file", "Open a file and wait until its tab has finished loading. Accepts the same paths as the command line. A file inside a package is vpk:outer_dir.vpk:inner/file, so a map is vpk:game/pak01_dir.vpk:maps/name.vmap_c; a bare .vpk only opens the package browser.",
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

            return page == null ? null : DescribeTab(page);
        }, cancellationToken).ConfigureAwait(false);

        var status = new JsonObject
        {
            ["pid"] = process.Id,
            ["version"] = Program.ProductVersion,
            ["uptime_seconds"] = Math.Round(Stopwatch.GetElapsedTime(StartedAt).TotalSeconds),
        };

        if (active != null)
        {
            status["active_tab"] = active;
        }

        if (AutomationClock.IsPaused)
        {
            status["paused"] = true;
        }

        if (UnhandledExceptions.Latest is { } latest)
        {
            status["unhandled_exceptions"] = UnhandledExceptions.Count;
            status["last_unhandled_exception"] = UnhandledExceptions.Summarize(latest);
        }

        return McpToolResult.Json(status);
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

        return Task.FromResult(McpToolResult.Json(new JsonObject
        {
            ["closing"] = true,
        }));
    }

    private static async Task<McpToolResult> GetMemory(JsonObject args, CancellationToken cancellationToken)
    {
        var collect = GetBool(args, "collect") ?? false;
        var tabs = await OnUi(() => Program.MainForm.Tabs.TabCount, cancellationToken).ConfigureAwait(false);

        if (collect)
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        }

        using var process = Process.GetCurrentProcess();
        var info = GC.GetGCMemoryInfo(GCKind.Any);

        static double Megabytes(long bytes) => Math.Round(bytes / (1024.0 * 1024.0), 1);

        return McpToolResult.Json(new JsonObject
        {
            ["tabs"] = tabs,
            ["private_mb"] = Megabytes(process.PrivateMemorySize64),
            ["working_set_mb"] = Megabytes(process.WorkingSet64),
            ["managed_live_mb"] = Megabytes(GC.GetTotalMemory(forceFullCollection: false)),
            ["managed_heap_mb"] = Megabytes(info.HeapSizeBytes),
            ["managed_fragmented_mb"] = Megabytes(info.FragmentedBytes),
            ["managed_committed_mb"] = Megabytes(info.TotalCommittedBytes),
            ["gc_counts"] = new JsonArray(GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)),
        });
    }

    private static Task<McpToolResult> GetLog(JsonObject args, CancellationToken cancellationToken)
    {
        var since = GetLong(args, "since") ?? 0;
        var limit = Math.Clamp(GetInt(args, "limit") ?? 200, 1, 5000);
        var dedupe = GetBool(args, "dedupe") ?? true;
        var include = GetRegex(args, "include");
        var exclude = GetRegex(args, "exclude");

        var level = GetString(args, "level")?.ToLowerInvariant() switch
        {
            null or "info" => Log.Category.INFO,
            "debug" => Log.Category.DEBUG,
            "warn" or "warning" => Log.Category.WARN,
            "error" => Log.Category.ERROR,
            var other => throw new ArgumentException($"Unknown level '{other}'. Use debug, info, warn or error."),
        };

        var (entries, cursor) = AutomationLog.Read(since, level, include, exclude);

        var lines = new List<(AutomationLog.Entry Entry, int Repeats)>(entries.Count);

        if (dedupe)
        {
            var firstIndex = new Dictionary<(Log.Category, string, string), int>();

            foreach (var entry in entries)
            {
                var key = (entry.Category, entry.Component, entry.Message);

                if (firstIndex.TryGetValue(key, out var index))
                {
                    lines[index] = (lines[index].Entry, lines[index].Repeats + 1);
                    continue;
                }

                firstIndex[key] = lines.Count;
                lines.Add((entry, 1));
            }
        }
        else
        {
            foreach (var entry in entries)
            {
                lines.Add((entry, 1));
            }
        }

        var omitted = Math.Max(0, lines.Count - limit);
        var text = new JsonArray();

        for (var i = omitted; i < lines.Count; i++)
        {
            text.Add(AutomationLog.Format(lines[i].Entry, lines[i].Repeats));
        }

        var result = new JsonObject
        {
            ["cursor"] = cursor,
            ["lines"] = text,
        };

        if (omitted > 0)
        {
            result["omitted"] = omitted;
        }

        return Task.FromResult(McpToolResult.Json(result));
    }

    private static async Task<McpToolResult> ClearLog(JsonObject args, CancellationToken cancellationToken)
    {
        var cursor = await OnUi(() =>
        {
            Log.ClearConsole();
            return AutomationLog.Clear();
        }, cancellationToken).ConfigureAwait(false);

        return McpToolResult.Json(new JsonObject
        {
            ["cursor"] = cursor,
        });
    }

    private async Task<McpToolResult> ListTabs(CancellationToken cancellationToken)
    {
        var tabs = await OnUi(() =>
        {
            PruneTabIds();

            var list = new JsonArray();

            foreach (TabPage page in Program.MainForm.Tabs.TabPages)
            {
                list.Add(DescribeTab(page));
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

            return McpToolResult.Json(DescribeTab(page));
        }, cancellationToken);

    private Task<McpToolResult> CloseTab(JsonObject args, CancellationToken cancellationToken)
        => WithTab(args, page =>
        {
            var id = IdFor(page);

            Program.MainForm.Tabs.CloseTab(page);

            if (page.Parent != null)
            {
                return McpToolResult.Error($"Tab {id} '{page.Text}' cannot be closed.");
            }

            TabIds.Remove(id);

            return McpToolResult.Json(new JsonObject
            {
                ["closed"] = id,
            });
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

            return page == null ? NoSuchTab(id.Value) : action(page);
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
        var logCursor = AutomationLog.Cursor;
        var crash = UnhandledExceptions.NextAsync();

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
            var previous = form.Tabs.SelectedTab;

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

            // Put back the tab that was active, so that later calls without 'tab' still reach it
            // rather than the console.
            if (previous != null && !previous.IsDisposed && previous.Parent != null)
            {
                form.Tabs.SelectTab(previous);
            }

            return false;
        }, cancellationToken).ConfigureAwait(false);

        try
        {
            if (!opened)
            {
                return McpToolResult.Error($"Nothing was opened for '{path}'.{LoggedErrorsSince(logCursor)}");
            }

            Exception? failure;

            try
            {
                var finished = await Task.WhenAny(completion.Task, crash).WaitAsync(timeout, cancellationToken).ConfigureAwait(false);

                if (finished == crash)
                {
                    return McpToolResult.Error($"Unhandled exception while loading '{path}': {UnhandledExceptions.Summarize(await crash.ConfigureAwait(false))}");
                }

                failure = await completion.Task.ConfigureAwait(false);
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

        return await OnUi(() =>
        {
            var result = DescribeTab(tab!);

            if (!path.StartsWith("vpk:", StringComparison.OrdinalIgnoreCase)
                && path.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase))
            {
                result["hint"] = "This opened the package browser, not a map. Open a file inside it as vpk:package.vpk:inner/file.";

                if (MapsInPackage(tab!) is { } maps)
                {
                    result["maps"] = maps;
                }
            }

            return McpToolResult.Json(result);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Paths that would open each map in a package tab, ready to pass to open_file.</summary>
    private static JsonArray? MapsInPackage(TabPage page)
    {
        if (page.Tag is not ExportData { VrfGuiContext: { CurrentPackage: { } package } context }
            || package.Entries?.GetValueOrDefault("vmap_c") is not { Count: > 0 } maps)
        {
            return null;
        }

        var paths = new JsonArray();

        foreach (var map in maps)
        {
            paths.Add($"vpk:{context.FileName}:{map.GetFullPath()}");
        }

        return paths;
    }

    /// <summary>Warnings and errors logged since <paramref name="cursor"/>, as a sentence to append to an error.</summary>
    private static string LoggedErrorsSince(long cursor)
    {
        var (entries, _) = AutomationLog.Read(cursor, Log.Category.WARN, null, null);

        if (entries.Count == 0)
        {
            return string.Empty;
        }

        var messages = new List<string>(entries.Count);

        foreach (var entry in entries)
        {
            messages.Add(entry.Message);
        }

        return " The log says: " + string.Join(" ", messages);
    }

    private JsonObject DescribeTab(TabPage page)
    {
        var tab = new JsonObject
        {
            ["tab"] = IdFor(page),
            ["title"] = page.Text,
        };

        // The tooltip is the full path, followed by the packages it came from.
        if (!string.IsNullOrEmpty(page.ToolTipText) && page.ToolTipText != page.Text)
        {
            tab["file"] = page.ToolTipText;
        }

        if (DescribeViewer(page) is { } viewer)
        {
            tab["viewer"] = viewer;
        }

        if (page == Program.MainForm.Tabs.SelectedTab)
        {
            tab["active"] = true;
        }

        if (TabLoads.Of(page) != null)
        {
            tab["loading"] = true;
        }

        return tab;
    }

    private static string? DescribeViewer(TabPage page)
    {
        if (GLBaseControl.FindHostedIn(page) is { } glViewer)
        {
            return glViewer.GetType().Name;
        }

        if (page.Tag is ExportData { DisposableContents: { } contents })
        {
            return contents.GetType().Name;
        }

        return null;
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

        PruneTabIds();

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

    private static McpToolResult NoSuchTab(int id) => McpToolResult.Error($"No tab with id {id}. Call list_tabs for the open ones.");

    private static Task<T> OnUi<T>(Func<T> callback, CancellationToken cancellationToken)
        => OnUi(callback, UiTimeout, cancellationToken);

    /// <summary>
    /// Runs <paramref name="callback"/> on the UI thread. A load can hold the UI thread for longer
    /// than <paramref name="timeout"/>, so the wait goes on while any tab is loading, up to
    /// <see cref="LoadTimeout"/>. A callback that timed out is dropped rather than run later.
    /// </summary>
    private static async Task<T> OnUi<T>(Func<T> callback, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var form = Program.MainForm;

        if (form == null || form.IsDisposed)
        {
            throw new InvalidOperationException("The main window is not available.");
        }

        using var abandon = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var call = form.InvokeAsync(callback, abandon.Token);
        var started = Stopwatch.GetTimestamp();

        while (true)
        {
            try
            {
                return await call.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                var loading = TabLoads.AnyTitle();

                if (loading != null && Stopwatch.GetElapsedTime(started) < LoadTimeout)
                {
                    continue;
                }

                await abandon.CancelAsync().ConfigureAwait(false);

                throw new TimeoutException(loading != null
                    ? $"The UI thread is still busy loading '{loading}' after {LoadTimeout.TotalSeconds:F0}s. Try again once it has loaded."
                    : "Timed out waiting for the UI thread. A modal dialog may be open in the viewer.");
            }
        }
    }

    private static JsonArray Round(Vector3 vector) => [Round(vector.X), Round(vector.Y), Round(vector.Z)];

    private static double Round(float value) => Math.Round(value, 2);

    private static JsonObject DescribeCamera(Camera camera) => new()
    {
        ["position"] = Round(camera.Location),
        ["angles"] = Round(camera.GetQAngle()),
        ["fov"] = Round(camera.FieldOfView),
    };

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

    private static JsonObject Prop(string type, string description, params string[] allowed)
    {
        var prop = new JsonObject
        {
            ["type"] = type,
            ["description"] = description,
        };

        if (allowed.Length > 0)
        {
            var values = new JsonArray();

            foreach (var value in allowed)
            {
                values.Add(value);
            }

            prop["enum"] = values;
        }

        return prop;
    }

    private static JsonObject TabProp() => Prop("integer", "Tab id from open_file or list_tabs. Defaults to the active tab.");

    private static JsonObject VectorProp(string description) => new()
    {
        ["type"] = "array",
        ["items"] = new JsonObject { ["type"] = "number" },
        ["minItems"] = 3,
        ["maxItems"] = 3,
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

    private static T? Get<T>(JsonObject args, string name, string expected)
        where T : struct
    {
        if (!args.TryGetPropertyValue(name, out var node) || node == null)
        {
            return null;
        }

        return node is JsonValue value && value.TryGetValue<T>(out var result)
            ? result
            : throw new ArgumentException($"'{name}' must be {expected}, got {node.ToJsonString()}.");
    }

    private static string? GetString(JsonObject args, string name)
    {
        if (!args.TryGetPropertyValue(name, out var node) || node == null)
        {
            return null;
        }

        return node is JsonValue value && value.TryGetValue<string>(out var result)
            ? result
            : throw new ArgumentException($"'{name}' must be a string, got {node.ToJsonString()}.");
    }

    private static int? GetInt(JsonObject args, string name) => Get<int>(args, name, "an integer");

    private static long? GetLong(JsonObject args, string name) => Get<long>(args, name, "an integer");

    private static float? GetFloat(JsonObject args, string name) => Get<float>(args, name, "a number");

    private static bool? GetBool(JsonObject args, string name) => Get<bool>(args, name, "true or false");

    private static Vector3? GetVector(JsonObject args, string name)
    {
        if (!args.TryGetPropertyValue(name, out var node) || node == null)
        {
            return null;
        }

        if (node is JsonArray { Count: 3 } array
            && array[0] is JsonValue x && x.TryGetValue<float>(out var vx)
            && array[1] is JsonValue y && y.TryGetValue<float>(out var vy)
            && array[2] is JsonValue z && z.TryGetValue<float>(out var vz))
        {
            return new Vector3(vx, vy, vz);
        }

        throw new ArgumentException($"'{name}' must be an array of three numbers, got {node.ToJsonString()}.");
    }

    private static Regex? GetRegex(JsonObject args, string name)
    {
        var pattern = GetString(args, name);

        if (string.IsNullOrEmpty(pattern))
        {
            return null;
        }

        try
        {
            return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
        }
        catch (ArgumentException e)
        {
            throw new ArgumentException($"'{name}' is not a valid regular expression: {e.Message}", e);
        }
    }
}
#endif
