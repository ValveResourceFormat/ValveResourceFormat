using System.Text.RegularExpressions;

namespace GUI.Utils
{
    static partial class Regexes
    {
        [GeneratedRegex("_[0-9]{3}\\.vpk$", RegexOptions.CultureInvariant)]
        public static partial Regex VpkNumberArchive();

        [GeneratedRegex("setpos(?:_exact)? (?<x>-?[0-9]+\\.[0-9+]+) (?<y>-?[0-9]+\\.[0-9+]+) (?<z>-?[0-9]+\\.[0-9+]+)", RegexOptions.CultureInvariant)]
        public static partial Regex SetPos();

        [GeneratedRegex("setang(?:_exact)? (?<pitch>-?[0-9]+\\.[0-9+]+) (?<yaw>-?[0-9]+\\.[0-9+]+)", RegexOptions.CultureInvariant)]
        public static partial Regex SetAng();
    }
}
