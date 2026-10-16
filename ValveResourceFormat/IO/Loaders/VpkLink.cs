namespace ValveResourceFormat.IO
{
    /// <summary>
    /// Links to a file inside of a VPK, as copied by Source 2 Viewer, such as <c>vpk:steam:730/game/csgo/pak01_dir.vpk:models/chicken/chicken.vmdl_c</c>.
    /// Packages inside of packages are separated the same way, such as <c>vpk:outer_dir.vpk:maps/inner.vpk:models/file.vmdl_c</c>.
    /// </summary>
    public static class VpkLink
    {
        /// <summary>
        /// Scheme of the links.
        /// </summary>
        public const string Prefix = "vpk:";

        /// <summary>
        /// Whether the text is a link.
        /// </summary>
        public static bool IsVpkLink(string text) => text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Splits a link into the paths of the packages it goes through, and the path inside of the innermost one.
        /// </summary>
        /// <returns>
        /// The package paths with the outermost first, which is empty when the link does not point inside of a package,
        /// and the path inside of the innermost package, or the whole path when there is no package.
        /// </returns>
        public static (List<string> PackagePaths, string InnerPath) Parse(string link)
        {
            var path = Uri.UnescapeDataString(link.AsSpan(Prefix.Length));
            var packagePaths = new List<string>();
            int separator;

            while ((separator = path.IndexOf(".vpk:", StringComparison.OrdinalIgnoreCase)) != -1)
            {
                packagePaths.Add(path[..(separator + 4)]);
                path = path[(separator + 5)..];
            }

            return (packagePaths, path);
        }

        /// <summary>
        /// Escapes a package or inner path for a link, so that <see cref="Parse"/> reads it back unchanged.
        /// </summary>
        public static string EscapePath(string path) => path.Replace("%", "%25", StringComparison.Ordinal);
    }
}
