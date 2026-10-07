using System.IO;
using System.Linq;
using ValvePak;
using ValveResourceFormat.IO;

namespace GUI.Utils
{
    /// <summary>
    /// Explains why a referenced file was not found in the loaded packages.
    /// </summary>
    class MissingReferenceReasons
    {
        private const string BakeCacheFolder = "_bakeresourcecache/";
        private const int MaxCandidates = 3;

        private readonly Package[] packages;
        private readonly bool onlyCore;
        private HashSet<string>? folders;
        private readonly Dictionary<string, Dictionary<string, List<string>>> filesByExtension = new(StringComparer.OrdinalIgnoreCase);

        public MissingReferenceReasons(VrfGuiContext guiContext)
        {
            var found = new Dictionary<string, Package>(StringComparer.OrdinalIgnoreCase);

            for (var context = guiContext; context != null; context = context.ParentGuiContext)
            {
                if (context.CurrentPackage?.FileName is { } currentName)
                {
                    found.TryAdd(currentName, context.CurrentPackage);
                }

                foreach (var package in context.MountedPackages)
                {
                    if (package.FileName != null)
                    {
                        found.TryAdd(package.FileName, package);
                    }
                }
            }

            packages = [.. found.Values];
            onlyCore = packages.Length > 0 && packages.All(static package => IsInCoreFolder(package.FileName!));
        }

        /// <summary>
        /// Returns the reason, as lines for a tooltip.
        /// </summary>
        public string Explain(string name)
        {
            if (name.StartsWith(BakeCacheFolder, StringComparison.OrdinalIgnoreCase))
            {
                return "A map bake intermediate, the map ships it merged into files of its own";
            }

            var reason = Path.GetExtension(name.AsSpan()).Equals(".vmap", StringComparison.OrdinalIgnoreCase)
                ? "No map package with this name was found next to the game files"
                : FolderExists(Path.GetDirectoryName(name)?.Replace('\\', '/'))
                    ? "Its folder exists, but this file is not shipped"
                    : "Its folder does not exist in the loaded files, it is an old or development path";

            if (onlyCore)
            {
                reason = string.Concat(reason, "\nOnly the core package is loaded, open the file from the game's own packages to search those too");
            }

            var candidates = SameFileName(name);

            if (candidates.Count > 0)
            {
                reason = string.Concat(reason, "\nA file with the same name exists at:\n", string.Join('\n', candidates));
            }

            return reason;
        }

        private static bool IsInCoreFolder(string packageFileName)
        {
            var folder = Path.GetFileName(Path.GetDirectoryName(packageFileName));

            return string.Equals(folder, "core", StringComparison.OrdinalIgnoreCase);
        }

        private bool FolderExists(string? folder)
        {
            if (string.IsNullOrEmpty(folder))
            {
                return false;
            }

            if (folders == null)
            {
                folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var package in packages)
                {
                    if (package.Entries == null)
                    {
                        continue;
                    }

                    foreach (var entries in package.Entries.Values)
                    {
                        foreach (var entry in entries)
                        {
                            for (var directory = entry.DirectoryName; !string.IsNullOrWhiteSpace(directory) && folders.Add(directory);)
                            {
                                var parent = directory.LastIndexOf(Package.DirectorySeparatorChar);
                                directory = parent < 0 ? null : directory[..parent];
                            }
                        }
                    }
                }
            }

            return folders.Contains(folder);
        }

        private List<string> SameFileName(string name)
        {
            var extension = Path.GetExtension(name);

            if (extension.Length < 2)
            {
                return [];
            }

            var type = string.Concat(extension.AsSpan(1), GameFileLoader.CompiledFileSuffix);

            if (!filesByExtension.TryGetValue(type, out var files))
            {
                files = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

                foreach (var package in packages)
                {
                    if (package.Entries?.TryGetValue(type, out var entries) != true)
                    {
                        continue;
                    }

                    foreach (var entry in entries!)
                    {
                        if (!files.TryGetValue(entry.FileName, out var paths))
                        {
                            paths = [];
                            files.Add(entry.FileName, paths);
                        }

                        paths.Add(entry.GetFullPath()[..^GameFileLoader.CompiledFileSuffix.Length]);
                    }
                }

                filesByExtension.Add(type, files);
            }

            return files.TryGetValue(Path.GetFileNameWithoutExtension(name), out var candidates)
                ? [.. candidates.Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxCandidates)]
                : [];
        }
    }
}
