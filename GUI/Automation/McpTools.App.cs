#if DEBUG
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Types.Exporter;
using GUI.Types.GLViewers;
using GUI.Utils;
using ValvePak;
using ValveResourceFormat.IO;

namespace GUI.Automation;

/// <summary>Tools for the application, its tabs, its log and the packages it reads.</summary>
internal sealed partial class McpTools
{
    private static readonly long StartedAt = Stopwatch.GetTimestamp();

    /// <summary>The paths open_file opened tabs from, which open the same file again.</summary>
    private readonly ConditionalWeakTable<TabPage, string> openPaths = [];

    [Tool("Status of the viewer: version, uptime, whether the simulation is paused or stepping, the last unhandled exception, and the open tabs with their id, path, viewer and any error the viewer showed instead. Answers while another call is running; when the UI thread is busy it says so in 'ui_busy' instead of listing tabs.", Concurrent = true)]
    private async Task<object> GetStatus(
        [Description("Add the memory use in MB: private bytes, working set, the managed heap and garbage collection counts.")] bool memory = false,
        [Description("Run a full compacting garbage collection before measuring memory, which tells a leak from garbage not yet collected. Pooled buffers are only released after sitting unused for up to a minute.")] bool collect = false,
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
            Paused = Flag(AutomationClock.IsPaused),
            Stepping = Flag(AutomationClock.IsStepping),
            UnhandledExceptions = UnhandledExceptions.Count > 0 ? UnhandledExceptions.Count : (int?)null,
            LastUnhandledException = UnhandledExceptions.Latest,
            UiBusy = busy,
            Tabs = tabs,
            Memory = memory ? MemoryUse(collect) : null,
        };
    }

    private static object MemoryUse(bool collect)
    {
        if (collect)
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        }

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

    [Tool("Close the viewer. Needed before rebuilding, because a running instance holds the build output open.")]
    private static object Quit()
    {
        // Answer first, so the caller gets a reply rather than a dropped connection
        _ = Task.Delay(250).ContinueWith(static _ => Program.MainForm.BeginInvoke(Program.MainForm.Close), TaskScheduler.Default);

        return new { Closing = true };
    }

    [Tool("Read the viewer's log, oldest first: shader compile errors, load failures and renderer warnings, each as 'time level [component] message' with level D, I, W or E. Pass the returned cursor back as 'since' to read only newer lines.", Concurrent = true)]
    private static object GetLog(
        [Description("Only lines logged after this cursor from an earlier call.")] long since = 0,
        [Description("Lowest level to include.")] Log.Category level = Log.Category.INFO,
        [Description("Case insensitive regular expression that lines must match, against '[component] message'.")] string? include = null,
        [Description("Case insensitive regular expression for lines to leave out.")] string? exclude = null,
        [Description("Collapse repeats of a line into its first occurrence with an (xN) count.")] bool dedupe = true,
        [Description("Most lines to return, keeping the newest.")][Range(1, int.MaxValue)] int limit = 200)
    {
        var (includePattern, excludePattern) = (Pattern(include), Pattern(exclude));
        var (entries, cursor) = AutomationLog.Since(since);

        var matching = entries.Where(entry => entry.Category >= level
            && (includePattern == null && excludePattern == null || $"[{entry.Component}] {entry.Message}" is var text
                && includePattern?.IsMatch(text) != false && excludePattern?.IsMatch(text) != true));

        var lines = dedupe
            ? matching.GroupBy(static entry => (entry.Category, entry.Component, entry.Message)).Select(static group => group.Count() > 1 ? $"{group.First()} (x{group.Count()})" : group.First().ToString()).ToList()
            : matching.Select(static entry => entry.ToString()).ToList();

        return new
        {
            Cursor = cursor,
            Lines = lines.Count > limit ? lines[^limit..] : lines,
            Omitted = lines.Count > limit ? lines.Count - limit : (int?)null,
        };
    }

    [Tool("Close a tab.", Redraws = true)]
    private object CloseTab(TabPage tab)
    {
        var id = tabIds.IdOf(tab);
        Program.MainForm.Tabs.CloseTab(tab);

        return tab.Parent == null ? new { Closed = id } : throw new ToolException($"Tab {id} cannot be closed.");
    }

    [Tool("Open a file and wait until its tab has loaded. Accepts what the command line does: a path, or vpk:package_dir.vpk:inner/file for a file inside a package, which list_package finds. When the viewer fails the tab stays open, so pass its id rather than opening the file again.", Redraws = true)]
    private Task<object> OpenFile([Description("Absolute path, or vpk:package_dir.vpk:inner/file.")] string path, CancellationToken cancellationToken = default)
        => OpenTab(ct => OnUi(() => Program.MainForm.OpenCommandLineArgFiles([path]), ct), $"'{path}'", path, cancellationToken);

    [Tool("Reopen a tab from its file, as Ctrl+R does, and wait until it has loaded. It opens as a new tab with a new id, returned with the id it replaced.", Redraws = true)]
    private async Task<object> ReloadTab(TabPage tab, CancellationToken cancellationToken = default)
    {
        var replaced = await OnUi(() => tabIds.IdOf(tab), cancellationToken).ConfigureAwait(false);

        var reloaded = await OpenTab(ct => OnUi(() =>
        {
            Program.MainForm.Tabs.SelectTab(tab);
            Program.MainForm.CloseAndReOpenActiveTab();
        }, ct), $"tab {replaced}", openPaths.TryGetValue(tab, out var path) ? path : null, cancellationToken).ConfigureAwait(false);

        return new { Tab = reloaded, Replaced = replaced };
    }

    /// <summary>Runs <paramref name="open"/>, finds the tab it added and waits for it to load.</summary>
    private async Task<object> OpenTab(Func<CancellationToken, Task> open, string what, string? path, CancellationToken cancellationToken)
    {
        var logCursor = AutomationLog.Cursor;
        var crash = UnhandledExceptions.NextAsync();
        var (before, previous) = await OnUi(() => (Program.MainForm.Tabs.TabPages.Cast<TabPage>().ToHashSet(), Program.MainForm.Tabs.SelectedTab), cancellationToken).ConfigureAwait(false);

        await open(cancellationToken).ConfigureAwait(false);

        var (page, id, loaded) = await OnUi(() =>
        {
            var page = Program.MainForm.Tabs.TabPages.Cast<TabPage>().FirstOrDefault(page => !before.Contains(page));

            if (page == null)
            {
                // A path that does not exist selects the console instead
                if (previous is { Parent: not null })
                {
                    Program.MainForm.Tabs.SelectTab(previous);
                }

                var logged = string.Join(' ', AutomationLog.Since(logCursor).Entries.Where(static entry => entry.Category >= Log.Category.WARN).Select(static entry => entry.Message));
                throw new ToolException($"Nothing was opened for {what}. {logged}");
            }

            if (path != null)
            {
                openPaths.AddOrUpdate(page, path);
            }

            return (page, tabIds.IdOf(page), (page.Tag as ExportData)?.Loaded ?? Task.FromResult<Exception?>(null));
        }, cancellationToken).ConfigureAwait(false);

        try
        {
            if (await Task.WhenAny(loaded, crash).WaitAsync(LoadTimeout, cancellationToken).ConfigureAwait(false) == crash)
            {
                throw new ToolException($"Unhandled exception while loading {what} in tab {id}: {await crash.ConfigureAwait(false)}");
            }
        }
        catch (TimeoutException)
        {
            throw new ToolException($"Tab {id} is still loading {what} after {LoadTimeout.TotalSeconds:F0}s. Tools that act on it wait for the load.");
        }

        if (await loaded.ConfigureAwait(false) is { } error)
        {
            throw new ToolException($"Tab {id} failed to load {what}: {DescribeException(error)}");
        }

        return await OnUi(() => ViewerError(page) is { } viewerError
            ? throw new ToolException($"Opened {what} as tab {id}, but its viewer failed: {viewerError}")
            : DescribeTab(page), cancellationToken).ConfigureAwait(false);
    }

    private object DescribeTab(TabPage page)
    {
        var data = page.Tag as ExportData;

        return new
        {
            Tab = tabIds.IdOf(page),
            Title = page.Text,
            Path = openPaths.TryGetValue(page, out var path) ? path : page.ToolTipText is { Length: > 0 } tooltip && tooltip != page.Text ? tooltip : null,
            Viewer = ((object?)GLBaseControl.FindHostedIn(page) ?? data?.DisposableContents)?.GetType().Name,
            Active = Flag(page == Program.MainForm.Tabs.SelectedTab),
            Loading = Flag(data is { Loaded.IsCompleted: false }),
            ViewerError = ViewerError(page),
            DecompileError = (data?.DisposableContents as Types.Viewers.Resource)?.DecompileException is { } decompileError ? DescribeException(decompileError) : null,
        };
    }

    [Tool("List or search the files inside a package, sorted by path, with each file's size and the vpk: link that opens it with open_file. Reads a package tab, or a .vpk on disk given as 'package'. Narrow it with 'folder', 'pattern' and 'type', and page with 'offset'.")]
    private async Task<object> ListPackage(
        [Description("A tab showing a package or a file inside one. Defaults to the active tab when 'package' is not given either.")] int? tab = null,
        [Description("Absolute path of a .vpk on disk to read instead of a tab, such as game/citadel/pak01_dir.vpk.")] string? package = null,
        [Description("Only files under this folder, such as maps or models/heroes.")] string folder = "",
        [Description("Include the files in subfolders of 'folder'. When false the subfolders are counted in 'folders' instead.")] bool recursive = true,
        [Description("Case insensitive wildcard with * and ?, such as *hideout*.vmap_c, matched against the file name, or against the whole path when it has a /.")] string? pattern = null,
        [Description("Only files with this extension, such as vmdl_c.")] string? type = null,
        [Description("Files to skip, from an earlier 'next_offset'.")][Range(0, int.MaxValue)] int offset = 0,
        [Description("Most files to return.")][Range(1, int.MaxValue)] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        object List(Package entries, string link) => ListEntries(entries, link, folder, recursive, pattern, type?.TrimStart('.'), offset, limit);

        if (package != null)
        {
            return await Task.Run(() =>
            {
                using var onDisk = new Package();
                onDisk.Read(package);
                return List(onDisk, VpkLink.Prefix + VpkLink.EscapePath(Path.GetFullPath(package).Replace('\\', '/')));
            }, cancellationToken).ConfigureAwait(false);
        }

        // On the UI thread, where the tab cannot close and dispose its package meanwhile
        return await OnUi(() =>
        {
            var page = ResolveTab(tab);
            var data = page.Tag as ExportData;
            var context = data?.PackageEntry != null ? data.VrfGuiContext.ParentGuiContext : data?.VrfGuiContext;

            return context?.CurrentPackage is { } entries
                ? List(entries, MainForm.GetVpkLinkPackagePath(context))
                : throw new ToolException($"Tab {tabIds.IdOf(page)} '{page.Text}' is not a package or a file inside one. Pass 'package' to read a .vpk on disk.");
        }, cancellationToken).ConfigureAwait(false);
    }

    private static object ListEntries(Package package, string link, string folder, bool recursive, string? pattern, string? type, int offset, int limit)
    {
        var prefix = folder.Replace('\\', '/').Trim('/') is { Length: > 0 } trimmed ? trimmed + "/" : string.Empty;
        var matches = new List<(string Path, PackageEntry Entry)>();
        var folders = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var (extension, entries) in package.Entries ?? [])
        {
            if (type != null && !extension.Equals(type, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var entry in entries)
            {
                var path = entry.GetFullPath();

                if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!recursive && path.IndexOf('/', prefix.Length) is var slash and >= 0)
                {
                    var subfolder = path[prefix.Length..slash];
                    folders[subfolder] = folders.GetValueOrDefault(subfolder) + 1;
                }
                else if (pattern == null || FileSystemName.MatchesSimpleExpression(pattern, pattern.Contains('/', StringComparison.Ordinal) ? path : entry.GetFileName()))
                {
                    matches.Add((path, entry));
                }
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
}
#endif
