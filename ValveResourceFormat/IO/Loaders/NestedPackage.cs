using ValvePak;

namespace ValveResourceFormat.IO
{
    /// <summary>
    /// A package read in place from an entry of another package.
    /// </summary>
    public sealed class NestedPackage : Package
    {
        /// <summary>
        /// Gets the package that this package was read from, which must stay open while this package is used.
        /// </summary>
        public Package Parent { get; }

        /// <summary>
        /// Gets the entry of <see cref="Parent"/> that this package was read from.
        /// </summary>
        public PackageEntry ParentEntry { get; }

        private NestedPackage(Package parent, PackageEntry parentEntry)
        {
            Parent = parent;
            ParentEntry = parentEntry;
        }

        /// <summary>
        /// Opens the package stored in an entry of another package, reading it through <see cref="Package.OpenEntryStream"/>.
        /// </summary>
        /// <param name="parent">The package that holds the entry, which must stay open while the returned package is used.</param>
        /// <param name="entry">The entry of <paramref name="parent"/> that holds the package.</param>
        /// <returns>The nested package, which is disposed separately.</returns>
        public static NestedPackage Open(Package parent, PackageEntry entry)
        {
            ArgumentNullException.ThrowIfNull(parent);
            ArgumentNullException.ThrowIfNull(entry);

            var package = new NestedPackage(parent, entry);

            try
            {
                package.OptimizeEntriesForBinarySearch();
                package.SetFileName(entry.GetFullPath());
                package.Read(parent.OpenEntryStream(entry));
            }
            catch
            {
                package.Dispose();
                throw;
            }

            return package;
        }
    }
}
