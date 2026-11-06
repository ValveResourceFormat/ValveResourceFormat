using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ValveResourceFormat.Source1;

namespace ValveResourceFormat.GoldSrc;

/// <summary>
/// The folders of a GoldSrc game that files are looked up in, from the mod folder down to the base game.
/// </summary>
internal sealed class GoldSrcFileSystem
{
    private const string LibListFileName = "liblist.gam";
    private const string BaseGameFolder = "valve";
    private const string DecalWadFileName = "decals.wad";

    private readonly ILogger logger;
    private readonly List<string> searchDirectories;
    private readonly Dictionary<string, WadFile?> wads = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The folder that holds the game folders, next to the engine executable.</summary>
    public string RootDirectory { get; }

    /// <summary>The folder of the mod a file belongs to, such as <c>cstrike</c>.</summary>
    public string ModDirectory { get; }

    /// <summary>The folders of the game by name, from the mod down to the base game it runs on, without repeats.</summary>
    public IReadOnlyList<string> GameDirectories { get; }

    /// <summary>
    /// Gets or sets a callback that receives the path on disk of each file found in the game folders.
    /// It may be called from several threads.
    /// </summary>
    public Action<string>? FileFound { get; set; }

    private GoldSrcFileSystem(string rootDirectory, string modDirectory, ILogger logger)
    {
        RootDirectory = rootDirectory;
        ModDirectory = modDirectory;
        this.logger = logger;

        var fallback = ReadFallbackFolder(rootDirectory, modDirectory);
        GameDirectories = BuildGameDirectories(modDirectory, fallback);
        searchDirectories = BuildSearchDirectories(rootDirectory, modDirectory, fallback);
    }

    /// <summary>
    /// Finds the game that a path on disk belongs to. The game is the folder that is next to the base game folder
    /// and contains the path. Returns null when the path is not inside such a layout.
    /// </summary>
    /// <param name="path">A path on disk inside the game.</param>
    /// <param name="logger">Receives warnings about files that cannot be read, or null to drop them.</param>
    public static GoldSrcFileSystem? FromPath(string path, ILogger? logger = null)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));

        while (!string.IsNullOrEmpty(directory))
        {
            var parent = Path.GetDirectoryName(directory);

            if (parent != null && Directory.Exists(Path.Combine(parent, BaseGameFolder)))
            {
                return new GoldSrcFileSystem(parent, Path.GetFileName(directory), logger ?? NullLogger.Instance);
            }

            directory = parent;
        }

        return null;
    }

    /// <summary>
    /// Finds a file by its game relative path in the search folders.
    /// </summary>
    public string? FindFile(string gamePath)
    {
        if (Path.IsPathRooted(gamePath) || HasParentSegment(gamePath))
        {
            return null;
        }

        foreach (var directory in searchDirectories)
        {
            var candidate = Path.Combine(directory, gamePath);

            if (File.Exists(candidate))
            {
                FileFound?.Invoke(candidate);
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Lists the wave and MP3 files of the sound folder of the search folders.
    /// </summary>
    /// <returns>The normalized names of the files under the sound folder, such as <c>weapons/357/357_reload1.wav</c>.</returns>
    public IEnumerable<string> EnumerateWaves()
    {
        var names = new HashSet<string>();

        foreach (var directory in searchDirectories)
        {
            var soundDirectory = Path.Combine(directory, "sound");

            if (!Directory.Exists(soundDirectory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(soundDirectory, "*", SearchOption.AllDirectories))
            {
                var name = Source1Paths.Normalize(Path.GetRelativePath(soundDirectory, file));

                if (Source1Paths.IsWaveName(name))
                {
                    names.Add(name);
                }
            }
        }

        return names;
    }

    /// <summary>
    /// Reads a file that belongs to a model: the one next to it, or else the one in the same place of another
    /// folder of the game.
    /// </summary>
    /// <param name="modelPath">The path of the model on disk.</param>
    /// <param name="fileName">The name of the file to read, such as <c>modelT.mdl</c>.</param>
    /// <param name="fileSystem">The game the model belongs to, or null to look next to the model only.</param>
    /// <returns>The contents of the file, or null when there is none.</returns>
    public static byte[]? ReadCompanion(string modelPath, string fileName, GoldSrcFileSystem? fileSystem)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(modelPath));

        if (directory == null)
        {
            return null;
        }

        var path = Path.Combine(directory, fileName);

        if (File.Exists(path))
        {
            fileSystem?.FileFound?.Invoke(path);
            return File.ReadAllBytes(path);
        }

        if (fileSystem == null)
        {
            return null;
        }

        var modRoot = Path.Combine(fileSystem.RootDirectory, fileSystem.ModDirectory);
        var relativeDirectory = Path.GetRelativePath(modRoot, directory);
        var found = fileSystem.FindFile(Path.Combine(relativeDirectory, fileName));
        return found == null ? null : File.ReadAllBytes(found);
    }

    /// <summary>
    /// Opens a WAD by file name, caching it.
    /// </summary>
    public WadFile? FindWad(string fileName)
    {
        lock (wads)
        {
            if (wads.TryGetValue(fileName, out var cached))
            {
                return cached;
            }

            var path = FindFile(fileName);
            var wad = path == null ? null : OpenWad(path);
            wads[fileName] = wad;

            return wad;
        }
    }

    private WadFile? OpenWad(string path)
    {
        try
        {
            return WadFile.Open(path);
        }
        catch (Exception e) when (e is InvalidDataException or UnexpectedMagicException or NotSupportedException)
        {
            logger.LogWarning("Failed to read WAD {File}: {Message}", path, e.Message);
            return null;
        }
    }

    /// <summary>
    /// Finds a texture of the WADs a map lists, in the order they are listed.
    /// </summary>
    public PalettedImage? FindTexture(string textureName, IEnumerable<string> wadNames)
    {
        foreach (var wadName in wadNames)
        {
            var image = FindImage(wadName, textureName);

            if (image != null)
            {
                return image;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds a decal texture in the decal WAD of the game folders, the mod folder first.
    /// </summary>
    public PalettedImage? FindDecal(string textureName)
    {
        foreach (var directory in searchDirectories)
        {
            var path = Path.Combine(directory, DecalWadFileName);

            if (!File.Exists(path))
            {
                continue;
            }

            FileFound?.Invoke(path);

            WadFile? wad;

            lock (wads)
            {
                if (!wads.TryGetValue(path, out wad))
                {
                    wad = OpenWad(path);
                    wads[path] = wad;
                }
            }

            var image = wad?.FindImage(textureName);

            if (image != null)
            {
                return image;
            }
        }

        return null;
    }

    private PalettedImage? FindImage(string wadName, string textureName)
    {
        var wad = FindWad(wadName);

        if (wad == null)
        {
            return null;
        }

        try
        {
            return wad.FindImage(textureName);
        }
        catch (Exception e) when (e is InvalidDataException or UnexpectedMagicException or NotSupportedException)
        {
            logger.LogWarning("Failed to read texture {Texture} from WAD {File}: {Message}", textureName, wadName, e.Message);
            return null;
        }
    }

    private static bool HasParentSegment(string gamePath)
    {
        foreach (var segment in gamePath.Split('/', '\\'))
        {
            if (segment == "..")
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> BuildSearchDirectories(string rootDirectory, string modDirectory, string fallback)
    {
        // Each game folder is preceded by its _addon and _hd variants, and the mod's _downloads folder follows
        // the mod. The fallback folder from liblist.gam gets the same variants, and the base game is always last.
        var names = new List<string>();
        AddGameFolder(names, modDirectory);
        names.Add(modDirectory + "_downloads");

        if (fallback.Length > 0)
        {
            AddGameFolder(names, fallback);
        }

        AddGameFolder(names, BaseGameFolder);

        var paths = names.Select(name => Path.Combine(rootDirectory, name));
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists).ToList();
    }

    private static List<string> BuildGameDirectories(string modDirectory, string fallback)
    {
        var names = new List<string> { modDirectory };

        if (fallback.Length > 0)
        {
            names.Add(fallback);
        }

        names.Add(BaseGameFolder);

        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string ReadFallbackFolder(string rootDirectory, string modDirectory)
    {
        var liblistPath = Path.Combine(rootDirectory, modDirectory, LibListFileName);

        if (!File.Exists(liblistPath))
        {
            return string.Empty;
        }

        return ReadLibListValue(File.ReadAllLines(liblistPath), "fallback_dir");
    }

    private static void AddGameFolder(List<string> names, string folder)
    {
        names.Add(folder + "_addon");
        names.Add(folder + "_hd");
        names.Add(folder);
    }

    /// <summary>
    /// Reads a value from the lines of a <c>liblist.gam</c>, which are a key followed by a quoted value.
    /// </summary>
    private static string ReadLibListValue(IEnumerable<string> lines, string key)
    {
        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (!trimmed.StartsWith(key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var rest = trimmed[key.Length..];

            if (rest.Length == 0 || !char.IsWhiteSpace(rest[0]))
            {
                continue;
            }

            return rest.Trim().Trim('"');
        }

        return string.Empty;
    }
}
