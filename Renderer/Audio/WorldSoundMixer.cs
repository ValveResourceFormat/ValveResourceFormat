using ValveResourceFormat.Renderer.Audio.SoundEventProviders;
using GUI.Types.Renderer;
using GUI.Utils;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio
{
    class WorldSoundMixer : ISampleProvider, IDisposable
    {
        public WorldSoundPlayer WorldSoundPlayer { get; init; }

        WaveFormat waveFormat;
        public WaveFormat WaveFormat => waveFormat;
        public SampleProviderMixer MixingSampleProvider { get; init; }

        HashSet<WorldSoundEvent> soundEvents = new();

        public bool Playing { get; private set; }

        public WorldSoundMixer(WorldSoundPlayer worldSoundPlayer)
        {
            WorldSoundPlayer = worldSoundPlayer;
            waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);

            MixingSampleProvider = new SampleProviderMixer(waveFormat);
        }

        public void Update(Camera camera)
        {
            Vector3 forwardVector = camera.GetForwardVector();
            Vector3 rightEarDirection = Vector3.Cross(Vector3.UnitZ, forwardVector);

            foreach (WorldSoundEvent soundEvent in soundEvents)
            {
                soundEvent.Update(camera.Location, rightEarDirection);
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            var read = MixingSampleProvider.Read(buffer, offset, count);
            Playing = read > 0;
            return read;
        }

        public WorldSoundEvent PlaySoundEvent(KVObject soundEventData, Vector3 position = default)
        {
            var soundEvent = WorldSoundEvent.Build(soundEventData);
            soundEvent.Position = position;
            soundEvent.Init(this, waveFormat.SampleRate);
            PlaySoundEvent(soundEvent);

            return soundEvent;
        }

        public void PlaySoundEvent(WorldSoundEvent soundEvent)
        {
            Playing = true;

            soundEvent.OnSoundStart += SoundEvent_OnSoundStart;
            soundEvent.OnSoundOver += SoundEvent_OnSoundOver;
            soundEvent.OnStart += SoundEvent_OnStart;
            soundEvent.OnStop += SoundEvent_OnStop;
        }

        private void SoundEvent_OnStart(WorldSoundEvent soundEvent)
        {
            soundEvents.Add(soundEvent);
        }

        private void SoundEvent_OnStop(WorldSoundEvent soundEvent)
        {
            soundEvents.Remove(soundEvent);
        }

        private void SoundEvent_OnSoundOver(WorldSoundEvent soundEvent)
        {
            MixingSampleProvider.RemoveProvider(soundEvent.SampleProvider);
        }

        private void SoundEvent_OnSoundStart(WorldSoundEvent soundEvent)
        {
            MixingSampleProvider.AddProvider(soundEvent.SampleProvider);
        }

        public void Dispose()
        {
            foreach (var item in soundEvents)
            {
                item.Dispose();
            }
            soundEvents.Clear();
        }
    }
}
