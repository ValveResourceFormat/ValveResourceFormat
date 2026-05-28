using GUI.Types.Audio.SoundEventProviders;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Audio
{
    /*internal class WorldSoundEventTest : WorldSoundEvent
    {
        private WaveStream soundStream;
        public WorldSoundEventTest(KVObject soundEvent) : base(soundEvent)
        {
        }

        protected override void DoInit(WorldSoundMixer mixer, int sampleRate)
        {
            var soundName = (SoundEventData.GetArray<string>("vsnd_files")?.First() ?? SoundEventData.GetStringProperty("vsnd_files_track_01"));
            soundStream = mixer.WorldSoundPlayer.Cache.GetSoundStream(soundName);
            if (soundStream == null)
            {
                return;
            }
            var resampleProvider = new WdlResamplingSampleProvider(soundStream.ToSampleProvider(), sampleRate);
            var stereoSampleProvider = resampleProvider.ToStereo();

            var provider = new SampleProvider3D(stereoSampleProvider);
            provider.Position = Position;
            provider.Range = 10000;

            SampleProviders.Add(provider);
        }

        public override void Dispose()
        {
            soundStream.Dispose();
        }
    }*/
}
