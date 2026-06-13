using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using ValveKeyValue;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Renderer;

using Vector3 = System.Numerics.Vector3;

namespace ValveResourceFormat.Renderer.Audio
{
    public sealed class WorldSoundPlayer : IDisposable
    {
        public struct Soundscape
        {
            public Vector3 Position;
            public float Radius;
            public string Name;
        }
        public SoundCache Cache { get; init; }
        public SoundEventBank SoundEventBank { get; init; }
        public WaveFormat WaveFormat => worldSoundProvider.WaveFormat;

        public List<Soundscape> Soundscapes { get; init; } = new();

        private readonly IFileLoader fileLoader;
        private readonly WaveOutEvent output;
        private readonly WorldSoundMixer worldSoundProvider;

        private WorldSoundEvent SoundscapeEvent = null;

        public WorldSoundPlayer(IFileLoader fileLoader)
        {
            this.fileLoader = fileLoader;
            Cache = new SoundCache(fileLoader);
            SoundEventBank = new SoundEventBank();

            output = new WaveOutEvent();

            worldSoundProvider = new WorldSoundMixer(this);
            output.DesiredLatency = 120;
            output.Init(worldSoundProvider);
        }

        public void Dispose()
        {
            output?.Dispose();
            worldSoundProvider?.Dispose();
        }

        public void Update(Camera camera)
        {
            worldSoundProvider.Update(camera);
            if (output.PlaybackState == PlaybackState.Stopped && worldSoundProvider.Playing)
            {
                output.Play();
            }
            UpdateSoundscape(camera.Location);
        }

        public WorldSoundEvent? PlaySoundEvent(string soundEventName, Vector3 position, bool soundscape)
        {
            var soundEventData = SoundEventBank.GetSoundEvent(soundEventName);
            if (soundEventData == null)
            {
                Debug.WriteLine($"Couldn't find soundevent: {soundEventName}");
                return null;
            }
            WorldSoundEvent soundEvent = worldSoundProvider.PlaySoundEvent(soundEventData, position);
            soundEvent.Start();
            soundEvent.SoundScape = soundscape;
            return soundEvent;
        }

        public void AddSoundEventsFile(string fileName)
        {
            var soundEventsFile = fileLoader.LoadFileCompiled(fileName);
            if (soundEventsFile == null)
            {
                return;
            }
            SoundEventBank.AddSoundEvents(soundEventsFile.DataBlock.AsKeyValueCollection());
        }

        public void AddSoundscape(Vector3 position, float range, string soundname)
        {
            Soundscapes.Add(new Soundscape
            {
                Name = soundname,
                Position = position,
                Radius = range,
            });
        }

        public void UpdateSoundscape(Vector3 cameraPosition)
        {
            var soundscapes = Soundscapes.Select(x => (Soundscape: x, Distance: (x.Position - cameraPosition).Length()))
                                         .Where(x => x.Distance < x.Soundscape.Radius)
                                         .OrderBy(x => x.Distance)
                                         .ToArray();
            if (soundscapes.Length == 0)
            {
                return;
            }

            var targetSoundscape = soundscapes[0];

            if (SoundscapeEvent != null && SoundscapeEvent.SoundName == targetSoundscape.Soundscape.Name)
            {
                return;
            }

            if (SoundscapeEvent != null)
            {
                SoundscapeEvent.Stop();
                SoundscapeEvent = null;
            }

            SoundscapeEvent = PlaySoundEvent(targetSoundscape.Soundscape.Name, targetSoundscape.Soundscape.Position, true);
        }
    }
}
