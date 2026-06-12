using System;
using System.Collections.Generic;
using GUI.Forms;

namespace GUI.Types.PackageViewer
{
    internal readonly record struct SearchRequest(string Text, SearchType Type, string? FilterKey, string? FilterValue);

    /// <summary>
    /// A single back/forward history location: either a folder node or a search.
    /// </summary>
    internal abstract record NavigationEntry;

    internal sealed record FolderNavigationEntry(VirtualPackageNode Node) : NavigationEntry;

    internal sealed record SearchNavigationEntry(SearchRequest Search) : NavigationEntry;

    /// <summary>
    /// Browser-style navigation history: a flat list of visited locations with a cursor pointing at
    /// the current one. Going back/forward moves the cursor; navigating somewhere new drops the
    /// forward tail and appends.
    /// </summary>
    internal sealed class NavigationHistory
    {
        private readonly List<NavigationEntry> entries = [];
        private int index = -1;

        public NavigationEntry? Current => index >= 0 ? entries[index] : null;
        public bool CanGoBack => index > 0;
        public bool CanGoForward => index >= 0 && index < entries.Count - 1;

        public void Record(NavigationEntry entry)
        {
            if (index >= 0 && entries[index] == entry)
            {
                return;
            }

            if (index < entries.Count - 1)
            {
                entries.RemoveRange(index + 1, entries.Count - index - 1);
            }

            entries.Add(entry);
            index = entries.Count - 1;
        }

        public NavigationEntry? Back() => CanGoBack ? entries[--index] : null;

        public NavigationEntry? Forward() => CanGoForward ? entries[++index] : null;

        /// <summary>
        /// Removes every entry matching <paramref name="predicate"/>, keeping the cursor on the
        /// nearest surviving earlier entry when the current one is removed.
        /// </summary>
        public void RemoveWhere(Func<NavigationEntry, bool> predicate)
        {
            for (var i = entries.Count - 1; i >= 0; i--)
            {
                if (predicate(entries[i]))
                {
                    entries.RemoveAt(i);

                    if (i <= index)
                    {
                        index--;
                    }
                }
            }
        }
    }
}
