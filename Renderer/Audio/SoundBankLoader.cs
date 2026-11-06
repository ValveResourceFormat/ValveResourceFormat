using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.Logging;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// Loads the sound events and scripted soundscapes of a game into banks.
/// </summary>
/// <param name="fileLoader">The file loader of the game.</param>
/// <param name="logger">Where files that fail to load are reported.</param>
public sealed class SoundBankLoader(IFileLoader fileLoader, ILogger logger)
{
    /// <summary>
    /// Conventional path for an addon or workshop map's extra sound events. Addon files are not listed in
    /// the base game's "soundevents/soundevents_manifest.vrman", so this is always attempted directly in
    /// <see cref="LoadSoundEvents"/>. Most content has no such file.
    /// </summary>
    private const string AddonSoundEventsFile = "soundevents/soundevents_addon.vsndevts";

    private const string SoundEventsManifestFile = "soundevents/soundevents_manifest.vrman";
    private const string SoundscapesManifestFile = "scripts/soundscapes_manifest.txt";

    // Shared across all script files: creating a serializer spins up ValveKeyValue's reflection machinery
    private static readonly KVSerializer KeyValues1Serializer = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);

    /// <summary>
    /// Loads sound event definitions from all soundevent files listed in the game's soundevents manifest,
    /// plus <see cref="AddonSoundEventsFile"/> when present. An optional filter matches against the file
    /// name (e.g. "game_sounds").
    /// </summary>
    /// <param name="bank">Receives the definitions.</param>
    /// <param name="filter">Text the file name must contain, or empty for every file.</param>
    /// <param name="cancellationToken">Stops loading between files.</param>
    public void LoadSoundEvents(SoundEventBank bank, string filter = "", CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        foreach (var soundEventsFile in GetSoundEventFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (filter.Length == 0 || soundEventsFile.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                LoadSoundEventsFile(bank, soundEventsFile, optional: false);
            }
        }

        if (filter.Length == 0 || AddonSoundEventsFile.Contains(filter, StringComparison.OrdinalIgnoreCase))
        {
            LoadSoundEventsFile(bank, AddonSoundEventsFile, optional: true);
        }

        stopwatch.Stop();
        logger.LogInformation("Loaded {Count} sound events in {ElapsedMs} ms", bank.Count, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>
    /// Loads sound event definitions from a single soundevent (vsndevts) file. When <paramref name="optional"/>
    /// is set, a missing file is skipped quietly instead of logging a warning (see <see cref="AddonSoundEventsFile"/>).
    /// </summary>
    /// <param name="bank">Receives the definitions.</param>
    /// <param name="fileName">The path of the file, without the compiled suffix.</param>
    /// <param name="optional">Whether the file may be missing.</param>
    public void LoadSoundEventsFile(SoundEventBank bank, string fileName, bool optional)
    {
        if (optional && !CanLoad(fileName + GameFileLoader.CompiledFileSuffix))
        {
            return;
        }

        using var soundEventsFile = fileLoader.LoadFileCompiled(fileName);
        if (soundEventsFile?.DataBlock == null)
        {
            if (!optional)
            {
                logger.LogWarning("Could not load sound events file {FileName}", fileName);
            }

            return;
        }

        if (soundEventsFile.DataBlock is not BinaryKV3)
        {
            logger.LogWarning(
                "Sound events file {FileName} has unexpected data block type {BlockType}, skipping",
                fileName,
                soundEventsFile.DataBlock.GetType().Name);
            return;
        }

        bank.AddSoundEvents(soundEventsFile.DataBlock.AsKeyValueCollection());
    }

    /// <summary>
    /// Loads scripted soundscape definitions (see <see cref="SoundscapeBank"/>) from the game's
    /// "scripts/soundscapes_manifest.txt", when present. Most modern titles only use the newer
    /// single-sound-event soundscapes (see <see cref="SoundEventPlayer.AddSoundscape"/>), which need no separate loading
    /// step, so a missing manifest is not an error.
    /// </summary>
    /// <param name="bank">Receives the sound events a soundscape file lists.</param>
    /// <param name="soundscapes">Receives the scripted soundscapes.</param>
    public void LoadSoundscapes(SoundEventBank bank, SoundscapeBank soundscapes)
    {
        var manifest = CanLoad(SoundscapesManifestFile) ? ReadKeyValues1(SoundscapesManifestFile) : null;

        if (manifest == null || !manifest.TryGetValue("soundscapes_manifest", out var fileList))
        {
            return;
        }

        foreach (var entry in fileList)
        {
            if (!entry.Key.Equals("file", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fileName = (string)entry.Value;

            if (string.IsNullOrEmpty(fileName))
            {
                continue;
            }

            if (fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                var script = ReadKeyValues1(fileName);

                if (script != null)
                {
                    soundscapes.AddSoundscapes(script);
                }
            }
            else
            {
                // Additional sound events used only by the soundscape script (e.g. an ambience .vsndevts)
                // that may not otherwise be reachable from soundevents_manifest.vrman
                LoadSoundEventsFile(bank, fileName, optional: false);
            }
        }
    }

    /// <summary>
    /// Reads and parses a loose KeyValues1 ("VDF") text file, or null when it does not exist or fails to
    /// parse. Soundscape scripts author several sibling top-level blocks with no single enclosing key.
    /// Hand-authored script files do show up with encoding quirks or syntax errors, so a
    /// failure here is logged and skipped rather than allowed to take down the whole load.
    /// </summary>
    private KVObject? ReadKeyValues1(string file)
    {
        using var stream = fileLoader.GetFileStream(file);

        if (stream == null)
        {
            return null;
        }

        try
        {
            return KeyValues1Serializer.DeserializeWithoutRoot(stream);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse KeyValues1 file {File}", file);
            return null;
        }
    }

    // Files that games may not ship are probed for, so that a game without them does not log a failure
    private bool CanLoad(string file) => fileLoader is not GameFileLoader gameFileLoader || gameFileLoader.Exists(file);

    private IEnumerable<string> GetSoundEventFiles()
    {
        var visitedManifests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!CanLoad(SoundEventsManifestFile + GameFileLoader.CompiledFileSuffix))
        {
            return [];
        }

        return CollectSoundEventFiles(SoundEventsManifestFile, visitedManifests);
    }

    /// <summary>
    /// Manifests can reference other manifests (e.g. a top-level .vrman listing per-game .vrman
    /// files, which in turn list the actual .vsndevts files), so entries are recursed into rather
    /// than assumed to all be sound event files. Guards against cycles with <paramref name="visitedManifests"/>.
    /// </summary>
    private IEnumerable<string> CollectSoundEventFiles(string manifestFileName, HashSet<string> visitedManifests)
    {
        if (!visitedManifests.Add(manifestFileName))
        {
            yield break;
        }

        using var manifestResource = fileLoader.LoadFileCompiled(manifestFileName);

        if (manifestResource?.DataBlock is not ResourceManifest manifest)
        {
            yield break;
        }

        foreach (var resourceGroup in manifest.Resources)
        {
            foreach (var fileName in resourceGroup)
            {
                if (fileName.EndsWith(".vrman", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var nestedFile in CollectSoundEventFiles(fileName, visitedManifests))
                    {
                        yield return nestedFile;
                    }
                }
                else
                {
                    yield return fileName;
                }
            }
        }
    }
}
