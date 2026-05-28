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
    internal class WorldSoundEventCSGOMega : WorldSoundEvent
    {
        private bool wasInitialized = false;
        private bool waitingForRetrigger;
        DateTime retriggerTime = DateTime.MinValue;
        private WaveStream soundStream;
        public WorldSoundEventCSGOMega(KVObject soundEvent) : base(soundEvent)
        {
        }

        public override void Dispose()
        {
            soundStream?.Dispose();
        }

        protected override void DoStart()
        {
            bool hasPosition = SoundEventData.ContainsKey("position");
            if (hasPosition)
            {
                Position = new Vector3(SoundEventData.GetFloatArray("position"));
            }

            if (!wasInitialized && CheckRetrigger())
            {
                wasInitialized = true;
                return;
            }
            wasInitialized = true;

            var soundNames = GetArrayProperty<string>("vsnd_files_track_01");
            if (soundNames.Length > 0)
            {
                //TODO: don't use shared random?
                var soundIndex = Random.Shared.Next(soundNames.Length);
                var soundName = soundNames[soundIndex];

                soundStream = Mixer.WorldSoundPlayer.Cache.GetSoundStream(soundName);
                if (soundStream != null)
                {
                    var curveProperty = SoundEventData.GetProperty<KVObject>("distance_volume_mapping_curve");
                    var distanceMappingCurve = curveProperty.Properties.ToArray()[1];
                    var distanceMappingArray = ((KVObject)distanceMappingCurve.Value.Value).Properties.First().Value.Value;

                    var range = Convert.ToSingle(distanceMappingArray);
                    var resampleProvider = new WdlResamplingSampleProvider(soundStream.ToSampleProvider(), SampleRate);
                    var stereoSampleProvider = resampleProvider.ToStereo();

                    SampleProvider sampleProvider;
                    if (!hasPosition)//SoundEventData.GetProperty<bool>("position_relative_to_player"))
                    {
                        var provider = new SampleProvider2D(stereoSampleProvider);
                        provider.Volume = SoundEventData.GetFloatProperty("volume");
                        sampleProvider = provider;
                    }
                    else
                    {
                        var provider = new SampleProvider3D(stereoSampleProvider);
                        provider.Position = Position;
                        provider.Range = 1000;
                        provider.Volume = SoundEventData.GetFloatProperty("volume");
                        sampleProvider = provider;
                    }

                    SampleProviders.Add(sampleProvider);
                }
            }

            var soundEvents = GetArrayProperty<string>("soundevent_01");
            foreach (var soundEvent in soundEvents)
            {
                var soundEventData = Mixer.WorldSoundPlayer.SoundEventBank.GetSoundEvent(soundEvent);
                var childSoundEvent = WorldSoundEvent.Build(soundEventData);
                StartAsChild(childSoundEvent);
            }
        }

        protected override void OnFinished()
        {
            base.OnFinished();
            CheckRetrigger();
        }

        private bool CheckRetrigger()
        {
            if (!SoundEventData.GetProperty<bool>("enable_retrigger"))
            {
                return false;
            }

            var retriggerMin = SoundEventData.GetFloatProperty("retrigger_interval_min");
            var retriggerMax = SoundEventData.GetFloatProperty("retrigger_interval_max");
            //TODO: don't use shared random?
            float retriggerAt = float.Lerp(retriggerMin, retriggerMax, Random.Shared.NextSingle());
            retriggerTime = DateTime.Now.AddSeconds(retriggerAt);
            waitingForRetrigger = true;
            return true;
        }

        public override bool Update(Vector3 cameraPosition, Vector3 rightEarDirection)
        {
            if (waitingForRetrigger && DateTime.Now >= retriggerTime)
            {
                waitingForRetrigger = false;
                Start();
            }
            return base.Update(cameraPosition, rightEarDirection);
        }
    }
}
