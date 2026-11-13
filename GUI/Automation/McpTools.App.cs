#if DEBUG
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.Exporter;
using GUI.Types.GLViewers;
using GUI.Types.PackageViewer;
using GUI.Utils;
using ValvePak;
using ValveResourceFormat.IO;

namespace GUI.Automation;

/// <summary>Tools for the application, its tabs, its log and the packages it reads.</summary>
internal sealed partial class McpTools
{
    private static readonly long StartedAt = Stopwatch.GetTimestamp();

    /// <summary>Load error messages of each tab, kept by <see cref="KeepLoadErrors"/>.</summary>
    private readonly ConditionalWeakTable<TabPage, List<string>> TabLoadErrors = [];

    [Tool("Return the viewer status and the open tabs. 'vsync' is the swap interval (0 is off) and the only frame cap. 'last_error' would have shown a dialog. A map tab has 'simulation' unless it is stopped. Answers while another call runs. 'ui_busy' replaces the tabs when the UI thread is busy.", Concurrent = true)]
    private async Task<object> GetStatus(
        [Description("Add memory use in MB after a full compacting GC. The pools release a buffer only after up to a minute without use.")] bool memory = false,
        CancellationToken cancellationToken = default)
    {
        List<object>? tabs = null;
        string? busy = null;

        try
        {
            tabs = await OnUi(() => Program.MainForm.Tabs.TabPages.Cast<TabPage>().Select(DescribeTab).ToList(), cancellationToken, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (ToolException e)
        {
            busy = e.Message;
        }

        return new
        {
            Pid = Environment.ProcessId,
            Version = Program.ProductVersion,
            UptimeSeconds = (int)Stopwatch.GetElapsedTime(StartedAt).TotalSeconds,
            Vsync = Settings.Config.Vsync,
            Msaa = Settings.Config.AntiAliasingSamples,
            Paused = Flag(AutomationClock.IsPaused),
            Stepping = Flag(AutomationClock.IsStepping),
            Errors = NonZero(ReportedErrors.Count),
            LastError = ReportedErrors.Latest,
            UiBusy = busy,
            Tabs = tabs,
            Memory = memory ? MemoryUse() : null,
        };
    }

    private static object MemoryUse()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        using var process = Process.GetCurrentProcess();
        var info = GC.GetGCMemoryInfo();

        static double Megabytes(long bytes) => Math.Round(bytes / (1024.0 * 1024.0), 1);

        return new
        {
            PrivateMb = Megabytes(process.PrivateMemorySize64),
            WorkingSetMb = Megabytes(process.WorkingSet64),
            ManagedLiveMb = Megabytes(GC.GetTotalMemory(forceFullCollection: false)),
            ManagedHeapMb = Megabytes(info.HeapSizeBytes),
            ManagedFragmentedMb = Megabytes(info.FragmentedBytes),
            GcCounts = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) },
        };
    }

    [Tool("Close the viewer. Do this before a rebuild, because a running viewer locks the build output.")]
    private static object Quit()
    {
        // Answer first, so the caller gets a reply rather than a dropped connection
        _ = Task.Delay(250).ContinueWith(static _ => Program.MainForm.BeginInvoke(Program.MainForm.Close), TaskScheduler.Default);

        return new { Closing = true };
    }

    [Tool("Read the viewer log, oldest first. Each line is 'time L [component] message', where L is the first letter of the level. Repeats collapse into the last one with an (xN) count. Pass 'cursor' as 'since' to read only newer lines. 'dropped' counts the lines after 'since' that are too old to keep.", Concurrent = true)]
    private static object GetLog(
        [Description("Cursor from an earlier call.")] long since = 0,
        [Description("Lowest level to include.")] Log.Category level = Log.Category.WARN,
        [Description("Case insensitive regular expression that '[component] message' must match.")] string? include = null,
        [Description("Case insensitive regular expression for lines to omit.")] string? exclude = null,
        [Description("Maximum lines. Keeps the newest.")][Range(1, int.MaxValue)] int limit = 200)
    {
        var includePattern = Pattern(include, nameof(include));
        var excludePattern = Pattern(exclude, nameof(exclude));
        var (entries, cursor, dropped) = AutomationLog.Since(since);
        var matching = new List<AutomationLog.Entry>();

        foreach (var entry in entries)
        {
            if (entry.Category < level)
            {
                continue;
            }

            if (includePattern != null || excludePattern != null)
            {
                var text = $"[{entry.Component}] {entry.Message}";

                if (!Matches(includePattern, text))
                {
                    continue;
                }

                if (excludePattern != null && excludePattern.IsMatch(text))
                {
                    continue;
                }
            }

            matching.Add(entry);
        }

        var counts = matching.CountBy(entry => (entry.Category, entry.Component, entry.Message)).ToDictionary();

        // Each line stands at its last repeat, so one that keeps coming back stays among the newest
        var lines = new List<string>();
        var written = new HashSet<(Log.Category, string, string)>();

        for (var i = matching.Count - 1; i >= 0; i--)
        {
            var entry = matching[i];
            var key = (entry.Category, entry.Component, entry.Message);

            if (!written.Add(key))
            {
                continue;
            }

            var count = counts[key];
            lines.Add(count > 1 ? $"{entry} (x{count})" : entry.ToString());
        }

        lines.Reverse();

        return new
        {
            Cursor = cursor,
            Lines = lines.Count > limit ? lines[^limit..] : lines,
            Omitted = lines.Count > limit ? lines.Count - limit : (int?)null,
            Dropped = dropped > 0 ? dropped : (long?)null,
        };
    }

    [Tool("Close a tab.", Redraws = true)]
    private object CloseTab(TabPage tab, [Description("Close a package editor with unsaved changes without saving them.")] bool discard = false)
    {
        var id = TabIds.IdOf(tab);

        if (PackageViewer.FromTab(tab) is { HasUnsavedChanges: true } editor)
        {
            if (!discard)
            {
                throw new ToolException($"Tab {id} has unsaved changes, pass discard to close it without saving.");
            }

            editor.DiscardChanges();
        }

        Program.MainForm.Tabs.CloseTab(tab);

        return tab.Parent == null ? new { Closed = id } : throw new ToolException($"Tab {id} cannot be closed.");
    }

    [Tool("List the inner tabs of a tab, such as the DATA and block tabs of a resource. 'name' selects one, which creates its content. Errors from that show in get_log.")]
    private async Task<object> SelectSubTab(
        TabPage tab,
        [Description("Exact title of the inner tab to select.")] string? name = null,
        CancellationToken cancellationToken = default)
    {
        var selected = await OnUi(() =>
        {
            var subTabs = tab.Controls.OfType<TabControl>().FirstOrDefault()
                ?? throw new ToolException($"Tab {TabIds.IdOf(tab)} has no inner tabs.");

            var pages = subTabs.TabPages.Cast<TabPage>().ToList();

            if (name != null)
            {
                var page = pages.Find(page => page.Text == name)
                    ?? throw new ToolException($"No inner tab '{name}', the tabs are {string.Join(", ", pages.Select(page => page.Text))}.");

                subTabs.SelectedTab = page;
            }

            return (Names: pages.Select(page => page.Text).ToList(), Selected: subTabs.SelectedTab?.Text);
        }, cancellationToken).ConfigureAwait(false);

        // Content is created when the page becomes visible, which takes a message loop pass
        await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);

        return new { Tabs = selected.Names, Selected = selected.Selected };
    }

    [Tool("Open a file and wait for its tab to load. If the viewer fails, the tab stays open: pass its id instead of opening the file again.", Redraws = true)]
    private Task<object> OpenFile([Description("Absolute path, or vpk:<package path>:<inner path> for a file in a package, as list_package gives.")] string path, CancellationToken cancellationToken = default)
        => OpenReturnedTab(() => Program.MainForm.OpenCommandLineArgFile(path), $"'{path}'", cancellationToken);

    [Tool("Reopen a tab from its file and wait for it to load. The tab gets a new id, and 'replaced' is the old one.", Redraws = true)]
    private async Task<object> ReloadTab(TabPage tab, CancellationToken cancellationToken = default)
    {
        var replaced = await OnUi(() => TabIds.IdOf(tab), cancellationToken).ConfigureAwait(false);

        var reloaded = await OpenReturnedTab(() =>
        {
            Program.MainForm.Tabs.SelectTab(tab);
            return Program.MainForm.CloseAndReOpenActiveTab() ?? throw new ToolException($"Tab {replaced} has no file to reopen.");
        }, $"tab {replaced}", cancellationToken).ConfigureAwait(false);

        return Merge(reloaded, new { Replaced = replaced });
    }

    /// <summary>Runs <paramref name="open"/> on the UI thread and waits for the tab it returns to load.</summary>
    private async Task<object> OpenReturnedTab(Func<TabPage> open, string what, CancellationToken cancellationToken)
    {
        var logCursor = AutomationLog.Cursor;
        var crash = ReportedErrors.NextAsync();
        var page = await OnUi(open, cancellationToken).ConfigureAwait(false);

        return await WaitForTab(page, what, crash, logCursor, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs <paramref name="open"/>, which opens a tab without returning it, finds the tab and waits for it to load.</summary>
    private async Task<object> OpenTab(Func<CancellationToken, Task> open, string what, CancellationToken cancellationToken)
    {
        var logCursor = AutomationLog.Cursor;
        var crash = ReportedErrors.NextAsync();
        var before = await OnUi(() => Program.MainForm.Tabs.TabPages.Cast<TabPage>().ToHashSet(), cancellationToken).ConfigureAwait(false);

        await open(cancellationToken).ConfigureAwait(false);

        var page = await OnUi(() =>
        {
            var page = Program.MainForm.Tabs.TabPages.Cast<TabPage>().FirstOrDefault(page => !before.Contains(page));

            if (page == null)
            {
                var logged = string.Join(' ', AutomationLog.Since(logCursor).Entries.Where(static entry => entry.Category >= Log.Category.WARN).Select(static entry => entry.Message));
                throw new ToolException($"The viewer opened nothing for {what}. {logged}");
            }

            return page;
        }, cancellationToken).ConfigureAwait(false);

        return await WaitForTab(page, what, crash, logCursor, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits for a tab that was just opened to load. It fails with <paramref name="crash"/> when an error is
    /// reported first. It keeps the errors that the tab's viewers logged after <paramref name="logCursor"/>.
    /// </summary>
    private async Task<object> WaitForTab(TabPage page, string what, Task<string> crash, long logCursor, CancellationToken cancellationToken)
    {
        var (id, loaded, displayPath) = await OnUi(() =>
        {
            var data = page.Tag as ExportData;
            var loaded = data?.Loaded ?? Task.FromResult<Exception?>(null);

            return (TabIds.IdOf(page), loaded, data?.VrfGuiContext.DisplayPath);
        }, cancellationToken).ConfigureAwait(false);

        try
        {
            if (await Task.WhenAny(loaded, crash).WaitAsync(LoadTimeout, cancellationToken).ConfigureAwait(false) == crash)
            {
                throw new ToolException($"Error while loading {what} in tab {id}: {await crash.ConfigureAwait(false)}");
            }
        }
        catch (TimeoutException)
        {
            throw new ToolException(string.Create(CultureInfo.InvariantCulture, $"Tab {id} is still loading {what} after {LoadTimeout.TotalSeconds:F0}s. Tools that act on it wait for the load."));
        }

        if (await loaded.ConfigureAwait(false) is { } error)
        {
            throw new ToolException($"Tab {id} failed to load {what}: {DescribeException(error)}");
        }

        if (displayPath != null)
        {
            KeepLoadErrors(page, displayPath, logCursor);
        }

        return await OnUi(() =>
        {
            var errors = LoadErrors(page);

            // A tab that renders still works when only its decompiled view failed
            if (errors != null && GLBaseControl.FindHostedIn(page) == null)
            {
                throw new ToolException($"Tab {id} opened {what}, but its viewer failed: {string.Join(" | ", errors)}");
            }

            return DescribeTab(page);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Keeps the load errors of a tab. The tab's viewers show them in error tabs
    /// and log them as <c>Failed to open "path": exception</c>.
    /// </summary>
    private void KeepLoadErrors(TabPage page, string displayPath, long logCursor)
    {
        var prefix = $"Failed to open \"{displayPath}\": ";
        var errors = new List<string>();

        foreach (var entry in AutomationLog.Since(logCursor).Entries)
        {
            if (entry.Component != nameof(CodeTextBox) || !entry.Message.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            // The exception's type and message, without its stack trace
            var exception = entry.Message.AsSpan(prefix.Length);
            var lineEnd = exception.IndexOfAny('\r', '\n');

            if (lineEnd >= 0)
            {
                exception = exception[..lineEnd];
            }

            errors.Add(exception.ToString());
        }

        if (errors.Count > 0)
        {
            TabLoadErrors.AddOrUpdate(page, errors);
        }
    }

    /// <summary>Load errors kept for the tab, or null when there are none.</summary>
    private List<string>? LoadErrors(TabPage page)
    {
        if (!TabLoadErrors.TryGetValue(page, out var errors))
        {
            return null;
        }

        return errors;
    }

    private object DescribeTab(TabPage page)
    {
        var data = page.Tag as ExportData;
        var viewer = GLBaseControl.FindHostedIn(page);
        var path = data?.VrfGuiContext.DisplayPath;

        // The path of a file ends with the title of its tab
        var title = path != null && path.EndsWith(page.Text, StringComparison.Ordinal) ? null : page.Text;

        GLWorldViewer.SimulationState? simulation = null;

        if (viewer is GLWorldViewer world && world.Simulation != GLWorldViewer.SimulationState.Stopped)
        {
            simulation = world.Simulation;
        }

        return new
        {
            Tab = TabIds.IdOf(page),
            Title = title,
            Path = path,
            Viewer = viewer?.GetType().Name ?? data?.DisposableContents?.GetType().Name,
            Active = Flag(page == Program.MainForm.Tabs.SelectedTab),
            Loading = Flag(data is { Loaded.IsCompleted: false }),
            Simulation = simulation,
            Errors = LoadErrors(page),
        };
    }

    [Tool("List the files of a package tab or of a .vpk on disk, sorted by path, with sizes in bytes. open_file takes '<package>:<path>', or 'open_path' for a file that has one.")]
    private async Task<object> ListPackage(
        [Description("Tab of a package or of a file in one. Defaults to the active tab.")] int? tab = null,
        [Description("Absolute path of a .vpk on disk to read instead of a tab.")] string? package = null,
        [Description("Only files under this folder, such as models/heroes.")] string folder = "",
        [Description("Include files in subfolders. If false, 'folders' gives the file count of each subfolder instead.")] bool recursive = true,
        [Description("Case insensitive wildcard with * and ?, such as *.vmdl_c. Matches the file name, or the whole path if it contains a /.")] string? pattern = null,
        [Description("Files to skip, from 'next_offset'.")][Range(0, int.MaxValue)] int offset = 0,
        [Description("Maximum files.")][Range(1, int.MaxValue)] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        object List(Package entries, string link) => ListEntries(entries, link, folder, recursive, pattern, offset, limit);

        if (package != null)
        {
            return await Task.Run(() =>
            {
                using var onDisk = Package.Open(package);
                return List(onDisk, VpkLink.Create([Path.GetFullPath(package)]));
            }, cancellationToken).ConfigureAwait(false);
        }

        // On the UI thread, where the tab cannot close and dispose its package meanwhile
        return await OnUi(() =>
        {
            var page = ResolveTab(tab);
            var data = page.Tag as ExportData;
            var context = data?.PackageEntry != null ? data.VrfGuiContext.ParentGuiContext : data?.VrfGuiContext;

            if (context?.CurrentPackage == null)
            {
                throw new ToolException($"Tab {TabIds.IdOf(page)} '{page.Text}' is not a package or a file in one. Pass 'package' to read a .vpk on disk.");
            }

            return List(context.CurrentPackage, context.GetVpkLink());
        }, cancellationToken).ConfigureAwait(false);
    }

    private static object ListEntries(Package package, string link, string folder, bool recursive, string? pattern, int offset, int limit)
    {
        var trimmed = folder.Replace('\\', '/').Trim('/');
        var matchesPath = pattern != null && pattern.Contains('/', StringComparison.Ordinal);
        var matches = new List<(string Path, PackageEntry Entry)>();
        var folders = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var (_, entries) in package.Entries ?? [])
        {
            foreach (var entry in entries)
            {
                var below = DirectoryBelow(entry, trimmed);

                if (below == null)
                {
                    continue;
                }

                if (!recursive && below.Length > 0)
                {
                    var slash = below.IndexOf('/', StringComparison.Ordinal);
                    var subfolder = slash < 0 ? below : below[..slash];
                    folders[subfolder] = folders.GetValueOrDefault(subfolder) + 1;
                    continue;
                }

                var path = entry.GetFullPath();

                if (pattern != null)
                {
                    var name = matchesPath ? path : entry.GetFileName();

                    if (!FileSystemName.MatchesSimpleExpression(pattern, name))
                    {
                        continue;
                    }
                }

                matches.Add((path, entry));
            }
        }

        matches.Sort(static (a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));

        // Null unless escaping changes the path, because otherwise it is the package link followed by the path
        string? OpenPath(string path)
        {
            var escaped = VpkLink.EscapePath(path);
            return escaped == path ? null : $"{link}:{escaped}";
        }

        return new
        {
            Package = link,
            Total = matches.Count,
            Files = matches.Skip(offset).Take(limit).Select(match => new { match.Path, Size = match.Entry.TotalLength, OpenPath = OpenPath(match.Path) }).ToList(),
            NextOffset = offset + limit < matches.Count ? offset + limit : (int?)null,
            Folders = folders,
        };
    }

    /// <summary>
    /// The directory of an entry relative to <paramref name="folder"/>, empty when it is directly in it,
    /// or null when it is outside it.
    /// </summary>
    private static string? DirectoryBelow(PackageEntry entry, string folder)
    {
        // Files at the root have a blank directory
        var directory = string.IsNullOrWhiteSpace(entry.DirectoryName) ? string.Empty : entry.DirectoryName;

        if (folder.Length == 0)
        {
            return directory;
        }

        if (directory.Equals(folder, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var inSubfolder = directory.Length > folder.Length
            && directory[folder.Length] == '/'
            && directory.StartsWith(folder, StringComparison.OrdinalIgnoreCase);

        if (inSubfolder)
        {
            return directory[(folder.Length + 1)..];
        }

        return null;
    }
}
#endif
