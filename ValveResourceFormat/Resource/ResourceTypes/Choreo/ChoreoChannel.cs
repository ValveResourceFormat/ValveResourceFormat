using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.Choreo
{
    public class ChoreoChannel
    {
        public string Name { get; private set; }
        public ChoreoEvent[] Events { get; private set; }
        public bool IsActive { get; private set; }
        public ChoreoChannel(string name, ChoreoEvent[] events, bool isActive)
        {
            Name = name;
            Events = events;
            IsActive = isActive;
        }

        public KVObject ToKeyValues()
        {
            var kv = new KVObject(null);
            kv.AddProperty("name", new KVValue(KVType.STRING, Name));

            var events = new KVObject(null, isArray: true);
            foreach (var choreoEvent in Events)
            {
                events.AddProperty(null, new KVValue(KVType.OBJECT, choreoEvent.ToKeyValues()));
            }
            kv.AddProperty("events", new KVValue(KVType.ARRAY, events));

            kv.AddProperty("active", new KVValue(KVType.BOOLEAN, IsActive));

            return kv;
        }
    }
}
