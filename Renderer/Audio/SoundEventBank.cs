using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio
{
    class SoundEventBank
    {
        private Dictionary<string, KVObject> soundEvents = [];
        public SoundEventBank() { }
        public void AddSoundEvent(string name, KVObject soundEventData)
        {
            soundEvents.Add(name, soundEventData);
        }
        public void AddSoundEvents(KVObject soundEventsFile)
        {
            foreach (var soundEvent in soundEventsFile)
            {
                AddSoundEvent(soundEvent.Key, soundEventsFile.GetProperty<KVObject>(soundEvent.Key));
            }
        }
        public KVObject GetSoundEvent(string name)
        {
            if (soundEvents.TryGetValue(name, out var soundEvent))
            {
                return soundEvent;
            }
            return null;
        }
    }
}
