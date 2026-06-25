using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ValveKeyValue;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio
{
    public class SoundEventBank
    {
        // Sound event names are hashed case-insensitively by the engine (see StringToken), match that here.
        private Dictionary<string, KVObject> soundEvents = new(StringComparer.OrdinalIgnoreCase);
        public SoundEventBank() { }
        public void AddSoundEvent(string name, KVObject soundEventData)
        {
            soundEvents.Add(name, soundEventData);
        }
        public void AddSoundEvents(KVObject soundEventsFile)
        {
            foreach (var soundEvent in soundEventsFile)
            {
                AddSoundEvent(soundEvent.Key, soundEventsFile.GetSubCollection(soundEvent.Key));
            }
        }
        public KVObject? GetSoundEvent(string name)
        {
            if (soundEvents.TryGetValue(name, out var soundEvent))
            {
                return soundEvent;
            }
            return null;
        }
    }
}
