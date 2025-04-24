using ValveResourceFormat.Serialization.KeyValues;

#nullable disable

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
            kv.AddProperty("name", Name);

            var events = new KVObject(null, isArray: true);
            foreach (var choreoEvent in Events)
            {
                events.AddItem(choreoEvent.ToKeyValues());
            }

            kv.AddProperty("events", events);
            kv.AddProperty("active", IsActive);

            return kv;
        }
    }
}
