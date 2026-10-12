using System.IO;

namespace Tests.IO
{
    /// <summary>
    /// A temporary folder that is deleted with everything in it when disposed.
    /// </summary>
    internal sealed class TempFolder : IDisposable
    {
        public string FullName { get; } = Directory.CreateTempSubdirectory("VrfContentExportTest").FullName;

        /// <summary>
        /// Creates a folder below this one.
        /// </summary>
        public string CreateFolder(params string[] parts)
        {
            var path = Path.Join([FullName, .. parts]);
            Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>
        /// Creates a <c>game/&lt;mod&gt;</c> folder with a gameinfo.gi.
        /// </summary>
        public string CreateMod(string mod)
        {
            var modPath = CreateFolder("game", mod);
            File.WriteAllText(Path.Join(modPath, "gameinfo.gi"), "\"GameInfo\" {}");
            return modPath;
        }

        public void Dispose() => Directory.Delete(FullName, recursive: true);
    }
}
