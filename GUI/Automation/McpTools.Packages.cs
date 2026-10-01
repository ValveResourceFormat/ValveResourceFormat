#if DEBUG
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Types.Exporter;
using ValvePak;

namespace GUI.Automation;

/// <summary>Tools that read what is inside a package.</summary>
internal sealed partial class McpTools
{
    private void RegisterPackageTools()
    {
        Add("list_package", "List or search the files inside a package, sorted by path, with each file's size, type and the vpk: link that opens it with open_file. Reads the package of a package tab, or a .vpk file on disk given as 'package', without opening a tab. Packages hold up to hundreds of thousands of files, so narrow it with 'folder', 'pattern' and 'type', and page through the rest with 'offset'. Returns 'next_offset' when more matches remain.",
            Schema(new JsonObject
            {
                ["tab"] = Prop("integer", "A tab showing a package, or a file inside one, from open_file or list_tabs. Defaults to the active tab when 'package' is not given either."),
                ["package"] = Prop("string", "Absolute path of a .vpk file on disk to read instead of a tab, such as game/citadel/pak01_dir.vpk."),
                ["folder"] = Prop("string", "Only files in this folder, such as maps or models/heroes."),
                ["recursive"] = Prop("boolean", "Include the files in subfolders of 'folder'. Defaults to true. When false, the subfolders are listed in 'folders' instead."),
                ["pattern"] = Prop("string", "Case insensitive wildcard where * matches any run of characters and ? one, such as *hideout*.vmap_c. Matched against the file name, or against the whole path when it contains a /."),
                ["type"] = Prop("string", "Only files with this extension, such as vmdl_c."),
                ["offset"] = Prop("integer", "Matches to skip, from an earlier 'next_offset'."),
                ["limit"] = Prop("integer", "Most files to return. Defaults to 100, at most 1000."),
            }),
            ListPackage);
    }

    private sealed record PackageQuery(string Folder, bool Recursive, Regex? Pattern, bool PatternHasFolder, string? Type, int Offset, int Limit);

    private async Task<McpToolResult> ListPackage(JsonObject args, CancellationToken cancellationToken)
    {
        var packagePath = GetString(args, "package");
        var id = GetInt(args, "tab");

        if (packagePath != null && id != null)
        {
            return McpToolResult.Error("Pass either 'tab' or 'package', not both.");
        }

        var folder = (GetString(args, "folder") ?? string.Empty).Replace('\\', '/').Trim('/');
        var pattern = GetString(args, "pattern");
        var type = GetString(args, "type")?.TrimStart('.');

        var query = new PackageQuery(
            folder,
            GetBool(args, "recursive") ?? true,
            string.IsNullOrEmpty(pattern) ? null : WildcardToRegex(pattern),
            pattern?.Contains('/', StringComparison.Ordinal) == true,
            string.IsNullOrEmpty(type) ? null : type,
            Math.Max(0, GetInt(args, "offset") ?? 0),
            Math.Clamp(GetInt(args, "limit") ?? 100, 1, 1000));

        if (packagePath != null)
        {
            return await Task.Run(() => ListPackageOnDisk(packagePath, query), cancellationToken).ConfigureAwait(false);
        }

        // The tab's package is disposed with the tab, so it is read on the UI thread, where the tab cannot close meanwhile.
        return await OnUi(() =>
        {
            var page = id == null ? Program.MainForm.Tabs.SelectedTab : PageFor(id.Value);

            if (page == null)
            {
                return id == null ? McpToolResult.Error("No tab is open.") : NoSuchTab(id.Value);
            }

            if (page.Tag is not ExportData { VrfGuiContext: { CurrentPackage: { } package } context })
            {
                var how = id == null ? " No 'tab' was given, so the active tab was used." : string.Empty;
                return McpToolResult.Error($"Tab {IdFor(page)} '{page.Text}' is not a package or a file inside one.{how} Pass 'package' to read a .vpk on disk.");
            }

            return ListEntries(package, PackageLink(context), query);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static McpToolResult ListPackageOnDisk(string path, PackageQuery query)
    {
        if (!Path.IsPathFullyQualified(path))
        {
            return McpToolResult.Error($"'package' must be an absolute path, got '{path}'.");
        }

        // The same fallback the vpk: links use, so pak01.vpk finds pak01_dir.vpk.
        if (!File.Exists(path) && path.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase))
        {
            var dirPath = string.Concat(path.AsSpan(0, path.Length - 4), "_dir.vpk");

            if (File.Exists(dirPath))
            {
                path = dirPath;
            }
        }

        if (!File.Exists(path))
        {
            return McpToolResult.Error($"'{path}' does not exist.");
        }

        using var package = new Package();

        try
        {
            package.Read(path);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
        {
            return McpToolResult.Error($"Could not read '{path}' as a package: {e.Message}");
        }

        var link = "vpk:" + MainForm.EscapeVpkLinkPath(Path.GetFullPath(path).Replace('\\', '/'));

        return ListEntries(package, link, query);
    }

    private static McpToolResult ListEntries(Package package, string? link, PackageQuery query)
    {
        var prefix = query.Folder.Length == 0 ? string.Empty : query.Folder + "/";
        var matches = new List<(string Path, PackageEntry Entry)>();
        var folders = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var (extension, entries) in package.Entries ?? [])
        {
            if (query.Type != null && !string.Equals(extension, query.Type, StringComparison.OrdinalIgnoreCase))
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

                var rest = path.AsSpan(prefix.Length);
                var slash = rest.IndexOf('/');

                if (!query.Recursive && slash >= 0)
                {
                    var subfolder = rest[..slash].ToString();
                    folders[subfolder] = folders.GetValueOrDefault(subfolder) + 1;
                    continue;
                }

                if (query.Pattern != null && !query.Pattern.IsMatch(query.PatternHasFolder ? path : entry.GetFileName()))
                {
                    continue;
                }

                matches.Add((path, entry));
            }
        }

        matches.Sort(static (a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));

        var files = new JsonArray();

        foreach (var (path, entry) in matches.Skip(query.Offset).Take(query.Limit))
        {
            var file = new JsonObject
            {
                ["path"] = path,
                ["size"] = entry.TotalLength,
                ["type"] = entry.TypeName,
            };

            if (link != null)
            {
                file["open_path"] = $"{link}:{MainForm.EscapeVpkLinkPath(path)}";
            }

            files.Add(file);
        }

        var result = new JsonObject();

        if (link != null)
        {
            result["package"] = link;
        }

        result["total"] = matches.Count;
        result["files"] = files;

        if (query.Offset + files.Count < matches.Count)
        {
            result["next_offset"] = query.Offset + files.Count;
        }

        if (folders.Count > 0)
        {
            var subfolders = new JsonObject();

            foreach (var (name, count) in folders)
            {
                subfolders[name] = count;
            }

            result["folders"] = subfolders;
        }

        return McpToolResult.Json(result);
    }

    private static Regex WildcardToRegex(string pattern)
    {
        var expression = Regex.Escape(pattern).Replace(@"\*", ".*", StringComparison.Ordinal).Replace(@"\?", ".", StringComparison.Ordinal);

        return new Regex($"^{expression}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
    }
}
#endif
