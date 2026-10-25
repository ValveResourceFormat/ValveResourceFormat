using System.Linq;
using ValvePak;
using ValveResourceFormat.IO;

namespace CLI
{
    public partial class Decompiler
    {
        private PackageSearchQuery? SearchQuery;

        /// <summary>
        /// Sets <see cref="SearchQuery"/> from --vpk_search and --vpk_search_options, or returns why they are invalid.
        /// </summary>
        private string? SetSearchQuery(string text, string? optionList)
        {
            if (text.Length == 0)
            {
                return "--vpk_search is empty.";
            }

            var options = (optionList ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var hex = options.Remove("hex");
            var matchCase = options.Remove("case");
            var wholeWord = options.Remove("word");
            var useRegex = options.Remove("regex");

            if (options.Count > 0)
            {
                return $"Unknown search option \"{options.First()}\", use any of: case, word, regex, hex.";
            }

            if (hex && (matchCase || wholeWord || useRegex))
            {
                return "The hex search option can not be combined with the other search options.";
            }

            var query = new PackageSearchQuery(text, hex ? PackageSearchType.FileContentsHex : PackageSearchType.FileContents, matchCase, wholeWord, useRegex);

            if (PackageSearch.GetError(query) is { } error)
            {
                return $"Invalid --vpk_search: {error}";
            }

            SearchQuery = query;
            return null;
        }

        private void SearchPackage(Package package, IEnumerable<PackageEntry> entries, string packagePath, PackageSearchQuery query)
        {
            var (matches, notSearched) = PackageSearch.Find(package, [.. FilteredEntries(entries).Select(x => x.Entry)], query);

            // When searching multiple packages, prefix each match with the package it came from
            var packagePrefix = IsInputFolder ? $"{GetDisplayPath(packagePath)}:" : string.Empty;

            foreach (var path in matches.Select(entry => entry.GetFullPath()).Order(StringComparer.Ordinal))
            {
                Console.WriteLine($"{packagePrefix}{path}");
            }

            if (notSearched > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Error.WriteLine($"Warning: {notSearched:N0} files in \"{GetDisplayPath(packagePath)}\" were not searched.");
                Console.ResetColor();
            }
        }
    }
}
