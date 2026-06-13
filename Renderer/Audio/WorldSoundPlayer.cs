using GUI.Utils;
using GUI.Controls;
using NAudio.Wave;
using NLayer.NAudioSupport;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Utils;
using ValveResourceFormat;
using NAudio.Wave.SampleProviders;
using GUI.Types.Renderer;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Serialization;
using System.Linq;

namespace ValveResourceFormat.Renderer.Audio
{
    sealed class WorldSoundPlayer : IDisposable
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

        WaveOutEvent output;
        WorldSoundMixer worldSoundProvider;
        private VrfGuiContext context;

        private WorldSoundEvent SoundscapeEvent = null;

        public WorldSoundPlayer(VrfGuiContext context)
        {
            this.context = context;
            Cache = new SoundCache(context);
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

        public WorldSoundEvent PlaySoundEvent(string soundEventName, Vector3 position, bool soundscape)
        {
            var soundEventData = SoundEventBank.GetSoundEvent(soundEventName);
            if (soundEventData == null)
            {
                Log.Warn("Sounds", $"Couldn't find soundevent: {soundEventName}");
                return null;
            }
            WorldSoundEvent soundEvent = worldSoundProvider.PlaySoundEvent(soundEventData, position);
            soundEvent.Start();
            soundEvent.SoundScape = soundscape;
            return soundEvent;
        }

        public void AddSoundEventsFile(string fileName)
        {
            var soundEventsFile = context.LoadFileCompiled(fileName);
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
