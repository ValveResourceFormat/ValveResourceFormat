using System.Globalization;
using System.IO;
using Microsoft.Extensions.Logging;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Source1.Physics;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Source1.Audio;

/// <summary>
/// Serves the sounds of a Source 1 or GoldSrc game as the sound event files of a Source 2 game: a manifest, the events of
/// the sound scripts of the game, and an event for each wave file of the game, named by the path of the wave under the
/// sound folder, which is what the animation events of converted models play.
/// The events are of the <c>src1_3d</c> and <c>src1_2d</c> types, and name their sound files by <see cref="Source1Paths.ToSoundPath"/>.
/// </summary>
/// <param name="openFile">Opens a file of the game, or <see langword="null"/> when the game has no sound scripts.</param>
/// <param name="enumerateWaves">Lists the normalized names of the wave and MP3 files of the game under its sound folder, such as <c>weapons/357/357_reload1.wav</c>, or <see langword="null"/> when the game has no files to list.</param>
/// <param name="logger">Where scripts that do not parse are reported.</param>
/// <param name="extraEvents">Events the game defines in code, which are added to the events of its sound scripts, or <see langword="null"/> for none.</param>
internal sealed class Source1SoundFiles(Func<string, Stream?>? openFile, Func<IEnumerable<string>>? enumerateWaves, ILogger logger, IReadOnlyList<Source1SoundFiles.WaveEvent>? extraEvents = null)
{
    /// <summary>A sound event that plays one of a list of waves.</summary>
    /// <param name="Name">The name of the event.</param>
    /// <param name="Volume">The volume of the event.</param>
    /// <param name="Waves">The normalized names of the waves, such as <c>player/pl_step1.wav</c>.</param>
    public sealed record WaveEvent(string Name, float Volume, IReadOnlyList<string> Waves);

    private const string ManifestPath = "soundevents/soundevents_manifest.vrman";
    private const string ScriptsPath = "soundevents/game_sounds.vsndevts";
    private const string WavesPath = "soundevents/game_sound_waves.vsndevts";

    private const string ScriptManifestPath = "scripts/game_sounds_manifest.txt";
    private const string ScriptManifestKey = "game_sounds_manifest";
    private const string ScriptKind = "sound script";

    private const string SpatializedType = "src1_3d";
    private const string FlatType = "src1_2d";

    private const string GenderToken = "$gender";

    private const float DefaultSoundLevel = 75f;
    private const float PitchNormal = 100f;

    private static readonly Dictionary<string, int> Channels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CHAN_WEAPON"] = 1,
        ["CHAN_VOICE"] = 2,
        ["CHAN_ITEM"] = 3,
        ["CHAN_BODY"] = 4,
    };

    private static readonly Dictionary<string, float> NamedSoundLevels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NONE"] = 0f,
        ["IDLE"] = 60f,
        ["STATIC"] = 66f,
        ["NORM"] = 75f,
        ["TALKING"] = 80f,
        ["SINGING"] = 80f,
        ["GUNFIRE"] = 140f,
        ["WEAPON"] = 150f,
    };

    private readonly object scriptsLock = new();
    private Resource? scripts;
    private bool scriptsLoaded;

    /// <summary>Gets whether a file is one of the sound event files.</summary>
    /// <param name="path">The path of the file, without the compiled suffix.</param>
    public bool Exists(string path)
    {
        if (path == ManifestPath)
        {
            return GetScripts() != null || enumerateWaves != null;
        }

        if (path == ScriptsPath)
        {
            return GetScripts() != null;
        }

        return path == WavesPath && enumerateWaves != null;
    }

    /// <summary>Loads one of the sound event files.</summary>
    /// <param name="path">The path of the file, without the compiled suffix.</param>
    /// <returns>The resource, or <see langword="null"/> when the path is not a sound event file or it is empty.</returns>
    public Resource? Load(string path)
    {
        if (path == ManifestPath)
        {
            return CreateManifest();
        }

        if (path == ScriptsPath)
        {
            return GetScripts();
        }

        if (path == WavesPath && enumerateWaves != null)
        {
            return CreateWaveEvents(enumerateWaves());
        }

        return null;
    }

    private Resource? CreateManifest()
    {
        var files = new List<string>();

        if (GetScripts() != null)
        {
            files.Add(ScriptsPath);
        }

        if (enumerateWaves != null)
        {
            files.Add(WavesPath);
        }

        if (files.Count == 0)
        {
            return null;
        }

        var resource = new Resource(ResourceType.ResourceManifest) { FileName = ManifestPath };
        resource.AddBlock(new ResourceManifest(files) { Resource = resource });

        return resource;
    }

    private Resource? GetScripts()
    {
        lock (scriptsLock)
        {
            if (!scriptsLoaded)
            {
                scripts = ConvertScripts();
                scriptsLoaded = true;
            }

            return scripts;
        }
    }

    private static Resource CreateWaveEvents(IEnumerable<string> waves)
    {
        var root = KVObject.Collection();

        foreach (var wave in waves)
        {
            root.Add(wave, CreateWaveEvent(wave));
        }

        return CreateEventsResource(root, WavesPath);
    }

    private static KVObject CreateWaveEvent(string wave) => CreateSpatializedEvent(1f, Source1Paths.ToSoundPath(wave));

    private static KVObject CreateSpatializedEvent(float volume, KVObject files)
    {
        return new KVObject
        {
            { "type", SpatializedType },
            { "volume", volume },
            { "pitch", 1f },
            { "soundlevel", DefaultSoundLevel },
            { "vsnd_files", files },
        };
    }

    private static Resource CreateEventsResource(KVObject root, string path)
    {
        var resource = new Resource(ResourceType.SoundEventScript) { FileName = path };
        resource.AddBlock(new BinaryKV3(root, KV3IDLookup.Get("generic"), BlockType.DATA) { Resource = resource });

        return resource;
    }

    private Resource? ConvertScripts()
    {
        var root = KVObject.Collection();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (openFile != null)
        {
            AddManifestScripts(root, names);
            AddStepAliases(root, names, openFile);
        }

        foreach (var waveEvent in extraEvents ?? [])
        {
            if (names.Add(waveEvent.Name))
            {
                root.Add(waveEvent.Name, CreateRandomWaveEvent(waveEvent));
            }
        }

        if (names.Count == 0)
        {
            return null;
        }

        return CreateEventsResource(root, ScriptsPath);
    }

    // The footstep script names the step and land events of the player, which play the step sounds of the game
    private static void AddStepAliases(KVObject root, HashSet<string> names, Func<string, Stream?> openFile)
    {
        var surfaces = Source1SurfaceProperties.Load(openFile);

        foreach (var (name, source) in surfaces?.CreateStepAliases() ?? [])
        {
            if (names.Contains(name) || !root.TryGetValue(source, out var sourceEvent))
            {
                continue;
            }

            names.Add(name);
            root.Add(name, KVObjectDeepClone.Clone(sourceEvent));
        }
    }

    private static KVObject CreateRandomWaveEvent(WaveEvent waveEvent)
    {
        var files = new List<string>(waveEvent.Waves.Count);

        foreach (var wave in waveEvent.Waves)
        {
            files.Add(Source1Paths.ToSoundPath(wave));
        }

        return CreateSpatializedEvent(waveEvent.Volume, CreateFileList(files));
    }

    // The first definition of a name wins, as the game reads the scripts in the order its manifest lists them
    private void AddManifestScripts(KVObject root, HashSet<string> names)
    {
        var manifest = ReadScript(openFile!, ScriptManifestPath, ScriptKind, logger);

        if (manifest == null || !manifest.TryGetValue(ScriptManifestKey, out var fileList))
        {
            return;
        }

        foreach (var entry in fileList)
        {
            var isScript = entry.Key.Equals("precache_file", StringComparison.OrdinalIgnoreCase)
                || entry.Key.Equals("preload_file", StringComparison.OrdinalIgnoreCase);

            if (!isScript)
            {
                continue;
            }

            var script = ReadScript(openFile!, (string)entry.Value, ScriptKind, logger);

            if (script == null)
            {
                continue;
            }

            foreach (var sound in script)
            {
                if (!sound.Value.IsCollection || names.Contains(sound.Key))
                {
                    continue;
                }

                var soundEvent = ConvertSound(sound.Value);

                if (soundEvent != null)
                {
                    names.Add(sound.Key);
                    root.Add(sound.Key, soundEvent);
                }
            }
        }
    }

    /// <summary>Reads a script of the game as KeyValues.</summary>
    /// <param name="openFile">Opens a file of the game.</param>
    /// <param name="path">The path of the script.</param>
    /// <param name="kind">What the script is, for the warning about a script that does not parse.</param>
    /// <param name="logger">Receives the warning.</param>
    /// <returns>The script, or <see langword="null"/> when the game has no such file or it does not parse.</returns>
    internal static KVObject? ReadScript(Func<string, Stream?> openFile, string path, string kind, ILogger logger)
    {
        using var stream = openFile(Source1Paths.Normalize(path));

        if (stream == null)
        {
            return null;
        }

        try
        {
            return KVSerializer.Create(KVSerializationFormat.KeyValues1Text).DeserializeWithoutRoot(stream);
        }
        catch (KeyValueException e)
        {
            logger.LogWarning("Failed to parse {Kind} {Script}: {Message}", kind, path, e.Message);
            return null;
        }
    }

    // A sound that names no wave has nothing to play. The game picks the male or female wave of a gender token from the
    // model of the actor that plays the sound, and plays none of them for an actor of unknown gender.
    private static KVObject? ConvertSound(KVObject sound)
    {
        var files = new List<string>();

        foreach (var property in sound)
        {
            if (property.Key.Equals("wave", StringComparison.OrdinalIgnoreCase))
            {
                AddWave(files, (string)property.Value);
            }
            else if (property.Key.Equals("rndwave", StringComparison.OrdinalIgnoreCase) && property.Value.IsCollection)
            {
                foreach (var wave in property.Value)
                {
                    AddWave(files, (string)wave.Value);
                }
            }
        }

        if (files.Count == 0)
        {
            return null;
        }

        var (volumeMin, volumeMax) = ParseRange(sound, "volume", 1f);
        var (pitchMin, pitchMax) = ParseRange(sound, "pitch", PitchNormal);
        var soundLevel = GetSoundLevel(sound);

        var soundEvent = new KVObject
        {
            { "type", soundLevel > 0f ? SpatializedType : FlatType },
            { "volume", (volumeMin + volumeMax) / 2f },
        };

        if (volumeMin != volumeMax)
        {
            var spread = (volumeMax - volumeMin) / 2f;
            soundEvent.Add("volume_rand_min", -spread);
            soundEvent.Add("volume_rand_max", spread);
        }

        if (pitchMin != pitchMax)
        {
            var spread = (pitchMax - pitchMin) / 2f / PitchNormal;
            soundEvent.Add("pitch_rand_min", -spread);
            soundEvent.Add("pitch_rand_max", spread);
        }

        soundEvent.Add("pitch", (pitchMin + pitchMax) / 2f / PitchNormal);

        if (soundLevel > 0f)
        {
            soundEvent.Add("soundlevel", soundLevel);
        }

        var channel = GetChannel(sound);

        if (channel > 0)
        {
            soundEvent.Add("event_type", (float)channel);
        }

        soundEvent.Add("vsnd_files", CreateFileList(files));

        return soundEvent;
    }

    private static void AddWave(List<string> files, string wave)
    {
        if (wave.Contains(GenderToken, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        files.Add(Source1Paths.ToSoundPath(wave));
    }

    private static KVObject CreateFileList(List<string> files)
    {
        if (files.Count == 1)
        {
            return files[0];
        }

        var list = KVObject.Array(files.Count);

        foreach (var file in files)
        {
            list.Add(file);
        }

        return list;
    }

    // A range is "min, max", or a single value
    private static (float Min, float Max) ParseRange(KVObject sound, string key, float defaultValue)
    {
        var text = sound.GetStringProperty(key);

        if (string.IsNullOrEmpty(text))
        {
            return (defaultValue, defaultValue);
        }

        var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length >= 2
            && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var min)
            && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var max))
        {
            return (min, max);
        }

        if (parts.Length == 1 && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var single))
        {
            return (single, single);
        }

        return (defaultValue, defaultValue);
    }

    private static float GetSoundLevel(KVObject sound)
    {
        var text = sound.GetStringProperty("soundlevel");

        if (string.IsNullOrEmpty(text))
        {
            return DefaultSoundLevel;
        }

        // A range picks a level per play, the first end stands for it
        var first = text.Split(',', StringSplitOptions.TrimEntries)[0];

        return ParseSoundLevel(first) ?? DefaultSoundLevel;
    }

    // A level is a number of decibels, with or without the SNDLVL_ prefix and the db suffix, or a named level
    private static float? ParseSoundLevel(string token)
    {
        var suffix = token.StartsWith("SNDLVL_", StringComparison.OrdinalIgnoreCase)
            ? token["SNDLVL_".Length..]
            : token;

        if (suffix.EndsWith("db", StringComparison.OrdinalIgnoreCase)
            && float.TryParse(suffix[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var decibels))
        {
            return decibels;
        }

        if (NamedSoundLevels.TryGetValue(suffix, out var named))
        {
            return named;
        }

        if (float.TryParse(suffix, NumberStyles.Float, CultureInfo.InvariantCulture, out var bare))
        {
            return bare;
        }

        return null;
    }

    private static int GetChannel(KVObject sound)
    {
        var text = sound.GetStringProperty("channel");

        if (text != null && Channels.TryGetValue(text, out var channel))
        {
            return channel;
        }

        return 0;
    }
}
