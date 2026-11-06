using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using ValveKeyValue;
using ValvePak;
using ValveResourceFormat.IO;

namespace ValveResourceFormat.Source1.FileSystem;

/// <summary>
/// Case insensitive virtual file system of a Source 1 game, built from the search paths of its <c>gameinfo.txt</c>.
/// Sources are searched in order, so a source added with <see cref="AddArchive(ZipArchive, bool)"/> wins over the game.
/// </summary>
internal sealed partial class Source1FileSystem : IDisposable
{
    private const string GameInfoFileName = "gameinfo.txt";
    private const int MaxDlcFolders = 99;

    [GeneratedRegex(@"_\d{3}\.vpk$", RegexOptions.IgnoreCase)]
    private static partial Regex ChunkFileName();

    [GeneratedRegex(@"\|appid_(\d{1,10})\|", RegexOptions.IgnoreCase)]
    private static partial Regex AppIdToken();

    private static readonly string[] RootFolderNames = ["materials", "models", "maps", "scripts", "resource"];

    private abstract class Mount : IDisposable
    {
        public abstract bool Exists(string path);
        public abstract Stream? Open(string path);
        public abstract IEnumerable<string> EnumerateFiles(string folder);
        public virtual void Dispose() { }
    }

    private sealed class DirectoryMount(string directory) : Mount
    {
        private static readonly EnumerationOptions IgnoreCase = new() { MatchCasing = MatchCasing.CaseInsensitive };

        public override bool Exists(string path) => FindFile(path) != null;

        public override Stream? Open(string path)
        {
            var fullPath = FindFile(path);

            if (fullPath == null)
            {
                return null;
            }

            return new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }

        public override IEnumerable<string> EnumerateFiles(string folder)
        {
            var folderPath = Path.Join(directory, folder);

            if (!Directory.Exists(folderPath))
            {
                return [];
            }

            return Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(directory, file).Replace('\\', '/'));
        }

        private string? FindFile(string path)
        {
            var fullPath = Path.Join(directory, path);

            if (File.Exists(fullPath))
            {
                return fullPath;
            }

            // The file systems of Windows ignore case on their own
            if (OperatingSystem.IsWindows())
            {
                return null;
            }

            var current = directory;
            var segments = path.Split('/');

            for (var i = 0; i < segments.Length; i++)
            {
                var isFile = i == segments.Length - 1;
                var next = FindEntry(current, segments[i], isFile);

                if (next == null)
                {
                    return null;
                }

                current = next;
            }

            return current;
        }

        private static string? FindEntry(string parent, string name, bool isFile)
        {
            if (name.Length == 0 || name.AsSpan().IndexOfAny('*', '?') >= 0)
            {
                return null;
            }

            try
            {
                var entries = isFile
                    ? Directory.EnumerateFiles(parent, name, IgnoreCase)
                    : Directory.EnumerateDirectories(parent, name, IgnoreCase);

                return entries.FirstOrDefault();
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Finds the entries of a package whose data is on disk. The directory of a package can list files
    /// of chunk files that are not installed, which is checked once for each chunk file when it is first needed.
    /// </summary>
    internal sealed class AvailablePackage(Package package, List<string>? diagnostics)
    {
        private readonly ConcurrentDictionary<ushort, bool> chunkAvailability = [];

        public Package Package => package;

        public PackageEntry? FindAvailableEntry(string path)
        {
            var entry = package.FindEntry(path);

            if (entry == null || entry.Length == 0 || entry.ArchiveIndex == Package.DirectoryArchiveIndex)
            {
                return entry;
            }

            return IsChunkAvailable(entry.ArchiveIndex) ? entry : null;
        }

        private bool IsChunkAvailable(ushort archiveIndex)
        {
            if (chunkAvailability.TryGetValue(archiveIndex, out var known))
            {
                return known;
            }

            var chunkPath = package.GetChunkFilePath(archiveIndex);
            var available = File.Exists(chunkPath);

            if (chunkAvailability.TryAdd(archiveIndex, available) && !available && diagnostics != null)
            {
                lock (diagnostics)
                {
                    diagnostics.Add($"Missing {chunkPath}, the files it holds are not available");
                }
            }

            return available;
        }
    }

    private sealed class PackageMount(Package package, List<string> diagnostics) : Mount
    {
        private readonly AvailablePackage availablePackage = new(package, diagnostics);

        public override bool Exists(string path) => availablePackage.FindAvailableEntry(path) != null;

        public override Stream? Open(string path)
        {
            var entry = availablePackage.FindAvailableEntry(path);

            if (entry == null)
            {
                return null;
            }

            return package.GetMemoryMappedStreamIfPossible(entry);
        }

        public override IEnumerable<string> EnumerateFiles(string folder)
        {
            if (package.Entries == null)
            {
                yield break;
            }

            foreach (var entries in package.Entries.Values)
            {
                foreach (var entry in entries)
                {
                    var path = entry.GetFullPath();

                    if (path.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                    {
                        yield return path;
                    }
                }
            }
        }

        public override void Dispose()
        {
            package.Dispose();
            base.Dispose();
        }
    }

    private sealed class ArchiveMount(ZipArchive archive, bool ownsArchive) : Mount
    {
        private readonly Dictionary<string, ZipArchiveEntry> entries = BuildEntries(archive);

        private static Dictionary<string, ZipArchiveEntry> BuildEntries(ZipArchive archive)
        {
            var result = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in archive.Entries)
            {
                result[entry.FullName.Replace('\\', '/')] = entry;
            }

            return result;
        }

        public override bool Exists(string path) => entries.ContainsKey(path);

        public override IEnumerable<string> EnumerateFiles(string folder) => entries.Keys.Where(path => path.StartsWith(folder, StringComparison.OrdinalIgnoreCase));

        public override Stream? Open(string path)
        {
            if (!entries.TryGetValue(path, out var entry))
            {
                return null;
            }

            var memory = new MemoryStream((int)entry.Length);

            lock (archive)
            {
                using var stream = entry.Open();
                stream.CopyTo(memory);
            }

            memory.Position = 0;
            return memory;
        }

        public override void Dispose()
        {
            if (ownsArchive)
            {
                archive.Dispose();
            }

            base.Dispose();
        }
    }

    private readonly List<Mount> mounts = [];
    private readonly HashSet<string> mountedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> diagnostics = [];

    /// <summary>Gets the folder of the mod, the one that holds <c>gameinfo.txt</c>. For a file system made of a loose folder, that folder.</summary>
    public string ModDirectory { get; }

    /// <summary>Gets the folder above the mod folder, the base of relative search paths.</summary>
    public string RootDirectory { get; }

    /// <summary>Gets the path of the <c>gameinfo.txt</c> this file system was built from, if any.</summary>
    public string? GameInfoPath { get; }

    /// <summary>Gets the name of the game from <c>gameinfo.txt</c>.</summary>
    public string? GameName { get; private set; }

    /// <summary>Gets the Steam app id that <c>gameinfo.txt</c> names, or 0 when it names none.</summary>
    public int SteamAppId { get; private set; }

    /// <summary>
    /// Gets a copy of the problems met so far, such as a package that failed to open or an archive chunk that is missing.
    /// Missing chunks are added when files are first looked up in them.
    /// </summary>
    public IReadOnlyList<string> Diagnostics
    {
        get
        {
            lock (diagnostics)
            {
                return [.. diagnostics];
            }
        }
    }

    /// <summary>
    /// Gets or sets a callback that receives each file of the game that is opened from a folder or package, not from an archive added with
    /// <see cref="AddArchive(ZipArchive, bool)"/>. It may be called from several threads and must leave the stream at its start.
    /// </summary>
    public Action<string, Stream>? FileOpened { get; set; }

    private Source1FileSystem(string modDirectory, string rootDirectory, string? gameInfoPath)
    {
        ModDirectory = modDirectory;
        RootDirectory = rootDirectory;
        GameInfoPath = gameInfoPath;
    }

    /// <summary>
    /// Finds the <c>gameinfo.txt</c> in the folder of a path, or in the closest folder above it.
    /// </summary>
    public static string? FindGameInfo(string path)
    {
        var directory = Directory.Exists(path) ? path : Path.GetDirectoryName(path);

        while (!string.IsNullOrEmpty(directory))
        {
            var candidate = Path.Join(directory, GameInfoFileName);

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = Path.GetDirectoryName(directory);
        }

        return null;
    }

    /// <summary>
    /// Creates the file system of the game a file belongs to. Without a <c>gameinfo.txt</c> above the file,
    /// the folder above the nearest <c>materials</c>, <c>models</c> or <c>maps</c> folder is used as a loose root.
    /// </summary>
    /// <param name="path">A file or folder on disk, such as a <c>.vpk</c> or <c>.bsp</c> of the game.</param>
    /// <param name="rootFolder">The name of the only folder that marks the loose root, such as <c>materials</c> for a material, whose own folders can be named like the others. Any known folder when <see langword="null"/>.</param>
    public static Source1FileSystem? FromPath(string path, string? rootFolder = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var gameInfoPath = FindGameInfo(path);

        if (gameInfoPath != null)
        {
            return FromGameInfo(gameInfoPath);
        }

        var directory = Directory.Exists(path) ? path : Path.GetDirectoryName(path);

        while (!string.IsNullOrEmpty(directory))
        {
            var name = Path.GetFileName(directory);

            var isRootFolder = rootFolder == null
                ? RootFolderNames.Contains(name, StringComparer.OrdinalIgnoreCase)
                : name.Equals(rootFolder, StringComparison.OrdinalIgnoreCase);

            if (isRootFolder)
            {
                var parent = Path.GetDirectoryName(directory);

                if (!string.IsNullOrEmpty(parent))
                {
                    return FromDirectory(parent);
                }
            }

            directory = Path.GetDirectoryName(directory);
        }

        return null;
    }

    /// <summary>Creates a file system that reads loose files from a single folder.</summary>
    public static Source1FileSystem FromDirectory(string directory)
    {
        var fileSystem = new Source1FileSystem(directory, Path.GetDirectoryName(directory) ?? directory, null);
        fileSystem.AddDirectory(directory);
        return fileSystem;
    }

    /// <summary>Creates a file system from the search paths of a <c>gameinfo.txt</c>.</summary>
    public static Source1FileSystem FromGameInfo(string gameInfoPath)
    {
        var modDirectory = Path.GetDirectoryName(Path.GetFullPath(gameInfoPath))!;
        var rootDirectory = Path.GetDirectoryName(modDirectory) ?? modDirectory;
        var fileSystem = new Source1FileSystem(modDirectory, rootDirectory, gameInfoPath);

        try
        {
            using var stream = File.OpenRead(gameInfoPath);
            KVObject document = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream);

            fileSystem.GameName = GetString(document, "game");

            var fileSystemBlock = GetChild(document, "FileSystem");

            if (fileSystemBlock != null)
            {
                if (int.TryParse(GetString(fileSystemBlock, "SteamAppId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var steamAppId))
                {
                    fileSystem.SteamAppId = steamAppId;
                }

                var searchPaths = GetChild(fileSystemBlock, "SearchPaths");

                if (searchPaths != null)
                {
                    foreach (var (key, value) in searchPaths)
                    {
                        fileSystem.AddSearchPath(key, value.ToString() ?? string.Empty);
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or KeyValueException or UnauthorizedAccessException)
        {
            fileSystem.diagnostics.Add($"Failed to read {gameInfoPath}: {e.Message}");
        }

        fileSystem.AddPlatformDirectory();

        return fileSystem;
    }

    // The platform folder next to the mod folders is searched last, after every path of the gameinfo.txt
    private void AddPlatformDirectory()
    {
        var directory = Path.Join(RootDirectory, "platform");

        AddDirectory(directory);
        AddPackageByName(Path.Join(directory, "platform_misc.vpk"));
    }

    private static KVObject? GetChild(KVObject parent, string name)
    {
        foreach (var (childName, child) in parent)
        {
            if (childName.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }

        return null;
    }

    private static string? GetString(KVObject parent, string name) => GetChild(parent, name)?.ToString();

    private void AddSearchPath(string key, string value)
    {
        var tokens = key.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // A path that is only for the mod is for writing and is not searched. The high definition content is mounted as game content.
        var mountsFiles = tokens.Any(static token => token.Equals("game", StringComparison.OrdinalIgnoreCase)
            || token.Equals("game_hd", StringComparison.OrdinalIgnoreCase));

        if (!mountsFiles)
        {
            return;
        }

        var path = value.Trim().Replace('\\', '/');
        path = path.Replace("|gameinfo_path|", ModDirectory.Replace('\\', '/') + "/", StringComparison.OrdinalIgnoreCase);
        path = path.Replace("|all_source_engine_paths|", RootDirectory.Replace('\\', '/') + "/", StringComparison.OrdinalIgnoreCase);

        var appIdToken = AppIdToken().Match(path);

        if (appIdToken.Success)
        {
            var appId = int.Parse(appIdToken.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture);
            var game = GameFolderLocator.FindSteamGameByAppId(appId);

            if (game == null)
            {
                diagnostics.Add($"Steam app {appId} of search path {value} is not installed");
                return;
            }

            var installDirectory = Path.TrimEndingDirectorySeparator(game.Value.GamePath).Replace('\\', '/');
            path = path.Replace(appIdToken.Value, installDirectory, StringComparison.OrdinalIgnoreCase);
        }

        if (!Path.IsPathRooted(path))
        {
            path = Path.Join(RootDirectory, path);
        }

        path = path.Replace('\\', '/');

        if (path.EndsWith("/*", StringComparison.Ordinal))
        {
            AddDirectoryOfDirectories(path[..^2]);
        }
        else if (path.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase))
        {
            AddPackageByName(path);
        }
        else
        {
            var directory = Path.GetFullPath(path);

            AddGameDirectory(directory);
        }
    }

    // A game folder also mounts the update folder next to it and its numbered DLC folders, newest first. The
    // numbers stop at the first folder that is missing or disabled.
    private void AddGameDirectory(string directory)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(directory);
        var parent = Path.GetDirectoryName(trimmed);

        if (!string.IsNullOrEmpty(parent))
        {
            AddDirectory(Path.Join(parent, "update"));

            var name = Path.GetFileName(trimmed);

            for (var number = CountDlcFolders(parent, name); number >= 1; number--)
            {
                AddDirectory(GetDlcFolder(parent, name, number));
            }
        }

        AddDirectory(directory);
    }

    private static int CountDlcFolders(string parent, string name)
    {
        var count = 0;

        while (count < MaxDlcFolders)
        {
            var folder = GetDlcFolder(parent, name, count + 1);

            if (!Directory.Exists(folder) || File.Exists(Path.Join(folder, "dlc_disabled.txt")))
            {
                break;
            }

            count++;
        }

        return count;
    }

    private static string GetDlcFolder(string parent, string name, int number)
        => Path.Join(parent, string.Create(CultureInfo.InvariantCulture, $"{name}_dlc{number}"));

    private void AddDirectoryOfDirectories(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        // Folders and packages mount in one alphabetical order
        var locations = new List<string>(Directory.EnumerateDirectories(directory));

        foreach (var file in Directory.EnumerateFiles(directory, "*.vpk"))
        {
            if (!ChunkFileName().IsMatch(file))
            {
                locations.Add(file);
            }
        }

        locations.Sort(StringComparer.OrdinalIgnoreCase);

        foreach (var location in locations)
        {
            if (Path.GetFileName(location).StartsWith('.'))
            {
                continue;
            }

            if (Directory.Exists(location))
            {
                AddDirectory(location);
            }
            else
            {
                AddPackage(location);
            }
        }
    }

    private void AddDirectory(string directory)
    {
        if (!Directory.Exists(directory) || !mountedPaths.Add(Path.GetFullPath(directory)))
        {
            return;
        }

        mounts.Add(new DirectoryMount(directory));
        AddPackagesInDirectory(directory);
    }

    private void AddPackagesInDirectory(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "pak*_dir.vpk").Order(StringComparer.OrdinalIgnoreCase))
        {
            if (!ChunkFileName().IsMatch(file))
            {
                AddPackage(file);
            }
        }
    }

    private void AddPackageByName(string path)
    {
        var extensionless = path[..^".vpk".Length];
        var directoryFile = extensionless.EndsWith("_dir", StringComparison.OrdinalIgnoreCase)
            ? path
            : string.Concat(extensionless, "_dir.vpk");

        if (File.Exists(directoryFile))
        {
            AddPackage(directoryFile);
        }
        else if (File.Exists(path))
        {
            AddPackage(path);
        }
    }

    private void AddPackage(string path)
    {
        var fullPath = Path.GetFullPath(path);

        if (!mountedPaths.Add(fullPath))
        {
            return;
        }

        try
        {
#pragma warning disable CA2000 // Owned by the mount, which the file system disposes
            var package = Package.Open(fullPath);
#pragma warning restore CA2000
            mounts.Add(new PackageMount(package, diagnostics));
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            diagnostics.Add($"Failed to open {fullPath}: {e.Message}");
        }
    }

    /// <summary>
    /// Mounts an archive, such as the pakfile of a map, ahead of every other source.
    /// </summary>
    /// <param name="archive">The archive to read files from.</param>
    /// <param name="ownsArchive">Whether disposing this file system disposes the archive.</param>
    public void AddArchive(ZipArchive archive, bool ownsArchive = false)
    {
        ArgumentNullException.ThrowIfNull(archive);

#pragma warning disable CA2000 // Owned by the file system
        var mount = new ArchiveMount(archive, ownsArchive);
#pragma warning restore CA2000

        lock (mounts)
        {
            mounts.Insert(0, mount);
        }
    }

    /// <summary>
    /// Converts a path on disk inside of the game to the path files are opened by, such as <c>materials/brick/wall.vmt</c>.
    /// </summary>
    /// <returns>The path relative to the game, or <see langword="null"/> when the file is outside of it.</returns>
    public string? GetGamePath(string fullPath)
    {
        var full = Path.GetFullPath(fullPath);

        var relative = Path.GetRelativePath(ModDirectory, full).Replace('\\', '/');

        if (!IsOutside(relative))
        {
            return relative.ToLowerInvariant();
        }

        // A file of another mod folder of the game, such as hl2/materials/...
        relative = Path.GetRelativePath(RootDirectory, full).Replace('\\', '/');

        if (IsOutside(relative))
        {
            return null;
        }

        var separator = relative.IndexOf('/', StringComparison.Ordinal);

        return separator < 0 ? null : relative[(separator + 1)..].ToLowerInvariant();
    }

    private static bool IsOutside(string relativePath)
        => relativePath == ".." || relativePath.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relativePath);

    private Mount[] GetMounts()
    {
        lock (mounts)
        {
            return [.. mounts];
        }
    }

    /// <summary>Gets whether a file exists in any source.</summary>
    /// <param name="path">Path relative to the game, such as <c>materials/concrete/floor.vmt</c>.</param>
    public bool Exists(string path)
    {
        var normalized = Source1Paths.Normalize(path);

        if (!Source1Paths.IsContained(normalized))
        {
            return false;
        }

        foreach (var mount in GetMounts())
        {
            if (mount.Exists(normalized))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Opens a file from the first source that has it.</summary>
    /// <param name="path">Path relative to the game, such as <c>materials/concrete/floor.vmt</c>.</param>
    /// <returns>A readable stream, or <see langword="null"/> when no source has the file.</returns>
    public Stream? OpenFile(string path)
    {
        var normalized = Source1Paths.Normalize(path);

        if (!Source1Paths.IsContained(normalized))
        {
            return null;
        }

        foreach (var mount in GetMounts())
        {
            var stream = mount.Open(normalized);

            if (stream != null)
            {
                if (mount is not ArchiveMount)
                {
                    ReportOpened(normalized, stream);
                }

                return stream;
            }
        }

        return null;
    }

    /// <summary>
    /// Tells <see cref="FileOpened"/> that a file of the game was opened, for readers that open a file of the game without this file system.
    /// </summary>
    /// <param name="path">Path relative to the game.</param>
    /// <param name="stream">The opened stream, at its start.</param>
    public void ReportOpened(string path, Stream stream) => FileOpened?.Invoke(path, stream);

    /// <summary>Lists the wave and MP3 files of the sound folder of every source.</summary>
    /// <returns>The normalized names of the files under the sound folder, such as <c>weapons/357/357_reload1.wav</c>.</returns>
    public IEnumerable<string> EnumerateWaves()
    {
        const string SoundFolder = "sound/";

        var names = new HashSet<string>();

        foreach (var mount in GetMounts())
        {
            foreach (var file in mount.EnumerateFiles(SoundFolder))
            {
                var name = Source1Paths.Normalize(file)[SoundFolder.Length..];

                if (Source1Paths.IsWaveName(name))
                {
                    names.Add(name);
                }
            }
        }

        return names;
    }

    public void Dispose()
    {
        foreach (var mount in GetMounts())
        {
            mount.Dispose();
        }

        lock (mounts)
        {
            mounts.Clear();
        }
    }
}
