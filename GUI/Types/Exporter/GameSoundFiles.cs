using Microsoft.Extensions.Logging;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Audio;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Source1;

namespace GUI.Types.Exporter
{
    /// <summary>
    /// Loads the sounds that the entities and the models of an export play, so that the files of the game that hold them
    /// and the sound and soundscape scripts that name them are opened and copied like the rest of the export.
    /// </summary>
    sealed class GameSoundFiles(IFileLoader fileLoader, ILogger logger)
    {
        private const string SoundscapeKey = "soundscape";

        private readonly SoundBankLoader bankLoader = new(fileLoader, logger);
        private readonly HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> loadedFiles = new(StringComparer.OrdinalIgnoreCase);
        private SoundEventBank? bank;
        private SoundscapeBank? soundscapes;

        /// <summary>Loads the sounds the keyvalues of an entity name: sound events, waves and soundscapes.</summary>
        public void AddEntity(EntityLump.Entity entity)
        {
            foreach (var property in entity)
            {
                if (property.Value.ValueType != KVValueType.String)
                {
                    continue;
                }

                var value = (string)property.Value;

                if (value.Length == 0)
                {
                    continue;
                }

                if (property.Key.Equals(SoundscapeKey, StringComparison.OrdinalIgnoreCase))
                {
                    AddSoundscape(value);
                }

                AddSoundEvent(value);
            }
        }

        /// <summary>Loads the sounds the events of the animations of a model play, and the ones its keyvalues name, such as the sounds of a door.</summary>
        public void AddModel(Model model)
        {
            foreach (var animation in model.GetAllAnimations(fileLoader, includeAnimationGraphClips: false))
            {
                foreach (var name in AnimationPlayer.GetEventSoundNames(animation))
                {
                    AddSoundEvent(name);
                }
            }

            AddKeyValues(model.KeyValues);
        }

        private void AddKeyValues(KVObject keyValues)
        {
            foreach (var property in keyValues)
            {
                if (property.Value.IsCollection)
                {
                    AddKeyValues(property.Value);
                }
                else if (property.Value.ValueType == KVValueType.String)
                {
                    AddSoundEvent((string)property.Value);
                }
            }
        }

        private SoundEventBank GetBank()
        {
            if (bank == null)
            {
                bank = new SoundEventBank();
                bankLoader.LoadSoundEvents(bank);
            }

            return bank;
        }

        private SoundscapeBank GetSoundscapes()
        {
            if (soundscapes == null)
            {
                var events = GetBank();
                soundscapes = new SoundscapeBank(events);
                bankLoader.LoadSoundscapes(events, soundscapes);
            }

            return soundscapes;
        }

        private void AddSoundscape(string name)
        {
            if (GetSoundscapes().GetSoundEvents(name) is not { } soundscapeEvents)
            {
                return;
            }

            foreach (var soundscapeEvent in soundscapeEvents)
            {
                AddSoundEvent(soundscapeEvent.Name);
            }
        }

        private void AddSoundEvent(string name)
        {
            if (!names.Add(name))
            {
                return;
            }

            var events = GetBank();
            var definition = events.GetSoundEvent(name);

            if (definition == null && Source1Paths.IsWaveName(name))
            {
                definition = events.GetSoundEvent(Source1Paths.NormalizeWaveName(name));
            }

            if (definition == null)
            {
                return;
            }

            foreach (var file in GetSoundFiles(definition.Data))
            {
                LoadSound(file);
            }
        }

        // A scripted soundscape operator holds its wave, or the list of waves it picks from
        private static IEnumerable<string> GetSoundFiles(KVObject data)
        {
            if (data.ContainsKey("operator"))
            {
                var scriptedOperator = data.GetSubCollection("operator");
                var wave = scriptedOperator.GetStringProperty("wave");

                if (!string.IsNullOrEmpty(wave))
                {
                    yield return wave;
                }

                foreach (var randomWave in SoundscapeOperatorParsing.GetRandomWaveFiles(scriptedOperator))
                {
                    yield return randomWave;
                }
            }

            foreach (var file in SoundEvent.GetStringOrArrayProperty(data, "vsnd_files"))
            {
                yield return file;
            }
        }

        private void LoadSound(string file)
        {
            if (loadedFiles.Add(file))
            {
                fileLoader.LoadFileCompiled(file)?.Dispose();
            }
        }
    }
}
