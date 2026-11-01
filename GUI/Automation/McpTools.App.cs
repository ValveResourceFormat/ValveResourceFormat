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

    /// <summary>What failed while each tab opened, kept by <see cref="KeepLoadErrors"/>.</summary>
    private readonly ConditionalWeakTable<TabPage, List<string>> TabLoadErrors = [];

    [Tool("Return the viewer status: version, uptime in seconds, vsync, requested msaa samples, and whether the simulation is paused or stepping. It also gives the last error that would have shown a dialog, and the open tabs with id, path, viewer and load errors, and for a map, its world simulation state from the simulation tool. Vsync is the swap interval (0 is off) and the only frame cap. It answers while another call runs. If the UI thread is busy, 'ui_busy' replaces the tabs.", Concurrent = true)]
    private async Task<object> GetStatus(
        [Description("Add memory use in MB after a full compacting GC, so a leak shows apart from garbage. This gives private bytes, working set, managed heap and GC counts. The pools release a buffer only after up to a minute without use.")] bool memory = false,
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

    [Tool("Close the viewer. Do this before you rebuild, because a running viewer locks the build output.")]
    private static object Quit()
    {
        // Answer first, so the caller gets a reply rather than a dropped connection
        _ = Task.Delay(250).ContinueWith(static _ => Program.MainForm.BeginInvoke(Program.MainForm.Close), TaskScheduler.Default);

        return new { Closing = true };
    }

    [Tool("Read the viewer log, oldest first, such as shader compile errors, load failures and renderer warnings. Each line is 'time level [component] message', with level D, I, W or E. Repeats of a line collapse into the last one with an (xN) count. Pass the returned 'cursor' as 'since' to read only newer lines. 'dropped' counts the lines after 'since' that are too old to keep.", Concurrent = true)]
    private static object GetLog(
        [Description("Cursor from an earlier call.")] long since = 0,
        [Description("Lowest level to include.")] Log.Category level = Log.Category.INFO,
        [Description("Case insensitive regular expression that '[component] message' must match.")] string? include = null,
        [Description("Case insensitive regular expression for lines to omit.")] string? exclude = null,
        [Description("Maximum number of lines to return. The tool keeps the newest.")][Range(1, int.MaxValue)] int limit = 200)
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

        var counts = new Dictionary<(Log.Category, string, string), int>();

        foreach (var entry in matching)
        {
            var key = (entry.Category, entry.Component, entry.Message);
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

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

    [Tool("Close a tab. A package editor with unsaved changes is only closed with discard.", Redraws = true)]
    private object CloseTab(TabPage tab, [Description("Close a package editor without saving its changes.")] bool discard = false)
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

    [Tool("Open a file as the command line does, and wait for its tab to load. If the viewer fails, the tab stays open. Pass its id instead of opening the file again.", Redraws = true)]
    private Task<object> OpenFile([Description("Absolute path, or vpk:package_dir.vpk:inner/file for a file in a package. list_package gives these links.")] string path, CancellationToken cancellationToken = default)
        => OpenReturnedTab(() => Program.MainForm.OpenCommandLineArgFile(path), $"'{path}'", cancellationToken);

    [Tool("Reopen a tab from its file, as Ctrl+R does, and wait for it to load. It opens as a new tab with a new id. Returns the new tab and the replaced id.", Redraws = true)]
    private async Task<object> ReloadTab(TabPage tab, CancellationToken cancellationToken = default)
    {
        var replaced = await OnUi(() => TabIds.IdOf(tab), cancellationToken).ConfigureAwait(false);

        var reloaded = await OpenReturnedTab(() =>
        {
            Program.MainForm.Tabs.SelectTab(tab);
            return Program.MainForm.CloseAndReOpenActiveTab() ?? throw new ToolException($"Tab {replaced} has no file to reopen.");
        }, $"tab {replaced}", cancellationToken).ConfigureAwait(false);

        return new { Tab = reloaded, Replaced = replaced };
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
    /// reported first, and keeps the errors its viewers logged after <paramref name="logCursor"/>.
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
                throw new ToolException($"Tab {id} opened {what}, but its viewer failed: {errors}");
            }

            return DescribeTab(page);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Keeps what failed while a tab opened. Its viewers show these failures in their own error tabs,
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

    /// <summary>What failed while the tab opened, or null when nothing did.</summary>
    private string? LoadErrors(TabPage page)
    {
        if (!TabLoadErrors.TryGetValue(page, out var errors))
        {
            return null;
        }

        return string.Join(" | ", errors);
    }

    private object DescribeTab(TabPage page)
    {
        var data = page.Tag as ExportData;
        var viewer = GLBaseControl.FindHostedIn(page);

        return new
        {
            Tab = TabIds.IdOf(page),
            Title = page.Text,
            Path = data?.VrfGuiContext.DisplayPath,
            Viewer = viewer?.GetType().Name ?? data?.DisposableContents?.GetType().Name,
            Active = Flag(page == Program.MainForm.Tabs.SelectedTab),
            Loading = Flag(data is { Loaded.IsCompleted: false }),
            Simulation = (viewer as GLWorldViewer)?.Simulation,
            Errors = LoadErrors(page),
        };
    }

    [Tool("List the files in a package, sorted by path. Each file has its size in bytes and the vpk: link that open_file takes. Reads a package tab, or a .vpk on disk.")]
    private async Task<object> ListPackage(
        [Description("Tab that shows a package or a file in one. Defaults to the active tab when there is no 'package'.")] int? tab = null,
        [Description("Absolute path of a .vpk on disk to read instead of a tab, such as game/citadel/pak01_dir.vpk.")] string? package = null,
        [Description("Only files under this folder, such as maps or models/heroes.")] string folder = "",
        [Description("Include the files in subfolders of 'folder'. If false, 'folders' counts the subfolders instead.")] bool recursive = true,
        [Description("Case insensitive wildcard with * and ?, such as *.vmdl_c or *hideout*.vmap_c. It matches the file name, or the whole path if it contains a /.")] string? pattern = null,
        [Description("Files to skip, from an earlier 'next_offset'.")][Range(0, int.MaxValue)] int offset = 0,
        [Description("Maximum number of files to return.")][Range(1, int.MaxValue)] int limit = 100,
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

        return new
        {
            Package = link,
            Total = matches.Count,
            Files = matches.Skip(offset).Take(limit).Select(match => new { match.Path, Size = match.Entry.TotalLength, OpenPath = $"{link}:{VpkLink.EscapePath(match.Path)}" }).ToList(),
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
