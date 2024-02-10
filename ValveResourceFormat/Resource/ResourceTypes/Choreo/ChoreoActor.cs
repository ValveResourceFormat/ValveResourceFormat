using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.Choreo
{
    public class ChoreoActor
    {
        public string Name { get; private set; }
        public ChoreoChannel[] Channels { get; private set; }
        public bool IsActive { get; private set; }
        public ChoreoActor(string name, ChoreoChannel[] channels, bool isActive)
        {
            Name = name;
            Channels = channels;
            IsActive = isActive;
        }

        public KVObject ToKeyValues()
        {
            var kv = new KVObject(null);
            kv.AddProperty("name", new KVValue(KVType.STRING, Name));

            var channels = new KVObject(null, isArray: true);
            foreach (var channel in Channels)
            {
                channels.AddProperty(null, new KVValue(KVType.OBJECT, channel.ToKeyValues()));
            }
            kv.AddProperty("channels", new KVValue(KVType.ARRAY, channels));

            kv.AddProperty("active", new KVValue(KVType.BOOLEAN, IsActive));

            return kv;
        }
    }
}
