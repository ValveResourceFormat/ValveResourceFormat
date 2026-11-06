using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Source1.Audio;

/// <summary>
/// Serves the soundscape scripts of a Source 1 game to a Source 2 game: the manifest, and the scripts it lists, with the
/// waves of their <c>playlooping</c> and <c>playrandom</c> operators named by the sounds <see cref="Source1Paths.ToSoundPath"/> serves.
/// </summary>
/// <param name="openFile">Opens a file of the game, or <see langword="null"/> when the game has no scripts.</param>
/// <param name="logger">Where scripts that do not parse are reported.</param>
internal sealed class Source1SoundscapeScripts(Func<string, Stream?>? openFile, ILogger logger)
{
    /// <summary>The path of the manifest that lists the scripts.</summary>
    public const string ManifestPath = "scripts/soundscapes_manifest.txt";

    private const string ManifestKey = "soundscapes_manifest";
    private const string FileKey = "file";
    private const string ScriptKind = "soundscape script";

    private readonly object filesLock = new();
    private Dictionary<string, byte[]>? files;

    /// <summary>Gets whether a path is the manifest or one of the scripts it lists.</summary>
    /// <param name="path">The normalized path of the file.</param>
    public bool Exists(string path) => GetFiles().ContainsKey(path);

    /// <summary>Opens the manifest or one of the scripts it lists.</summary>
    /// <param name="path">The normalized path of the file.</param>
    /// <returns>The stream, or <see langword="null"/> when the path is not served.</returns>
    public Stream? Open(string path)
    {
        if (GetFiles().TryGetValue(path, out var content))
        {
            return new MemoryStream(content, writable: false);
        }

        return null;
    }

    private Dictionary<string, byte[]> GetFiles()
    {
        lock (filesLock)
        {
            return files ??= ConvertFiles();
        }
    }

    private Dictionary<string, byte[]> ConvertFiles()
    {
        var converted = new Dictionary<string, byte[]>();

        if (openFile == null)
        {
            return converted;
        }

        var manifest = Source1SoundFiles.ReadScript(openFile, ManifestPath, ScriptKind, logger);

        // The manifest of some games names its root block with a typo, so the first block stands for it
        var fileList = manifest?.FirstOrDefault().Value;

        if (fileList == null || !fileList.IsCollection)
        {
            return converted;
        }

        var manifestFiles = KVObject.ListCollection();

        foreach (var entry in fileList)
        {
            if (!entry.Key.Equals(FileKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var path = Source1Paths.Normalize((string)entry.Value);
            var script = Source1SoundFiles.ReadScript(openFile, path, ScriptKind, logger);

            if (script == null)
            {
                continue;
            }

            manifestFiles.Add(FileKey, path);
            converted[path] = ConvertScript(script);
        }

        using var manifestStream = new MemoryStream();
        Serialize(manifestStream, manifestFiles, ManifestKey);
        converted[ManifestPath] = manifestStream.ToArray();

        return converted;
    }

    private static byte[] ConvertScript(KVObject script)
    {
        using var stream = new MemoryStream();

        foreach (var soundscape in script)
        {
            Serialize(stream, ConvertSoundscape(soundscape.Value), soundscape.Key);
        }

        return stream.ToArray();
    }

    private static KVObject ConvertSoundscape(KVObject soundscape)
    {
        if (!soundscape.IsCollection)
        {
            return soundscape;
        }

        var converted = KVObject.ListCollection();

        foreach (var operation in soundscape)
        {
            var isSound = operation.Key.Equals("playlooping", StringComparison.OrdinalIgnoreCase)
                || operation.Key.Equals("playrandom", StringComparison.OrdinalIgnoreCase);

            if (isSound && operation.Value.IsCollection)
            {
                converted.Add(operation.Key, ConvertSound(operation.Value));
            }
            else
            {
                converted.Add(operation.Key, operation.Value);
            }
        }

        return converted;
    }

    private static KVObject ConvertSound(KVObject sound)
    {
        var converted = KVObject.ListCollection();

        foreach (var property in sound)
        {
            if (property.Key.Equals("wave", StringComparison.OrdinalIgnoreCase))
            {
                converted.Add(property.Key, Source1Paths.ToSoundPath((string)property.Value));
            }
            else if (property.Key.Equals("rndwave", StringComparison.OrdinalIgnoreCase) && property.Value.IsCollection)
            {
                converted.Add(property.Key, ConvertSound(property.Value));
            }
            else
            {
                converted.Add(property.Key, property.Value);
            }
        }

        return converted;
    }

    private static void Serialize(Stream stream, KVObject value, string name)
    {
        KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Serialize(stream, new KVDocument(null, name, value));
    }
}
