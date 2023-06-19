using System.Text.RegularExpressions;

namespace GUI.Utils
{
    static partial class Regexes
    {
        public static Regex VpkNumberArchive { get; } = VpkNumberArchiveGenerator();

        [GeneratedRegex("_[0-9]{3}\\.vpk$", RegexOptions.CultureInvariant)]
        private static partial Regex VpkNumberArchiveGenerator();
    }
}
