using ValveResourceFormat.Serialization.KeyValues;

#nullable disable

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
            kv.AddProperty("name", Name);

            var channels = new KVObject(null, isArray: true);
            foreach (var channel in Channels)
            {
                channels.AddItem(channel.ToKeyValues());
            }

            kv.AddProperty("channels", channels);
            kv.AddProperty("active", IsActive);

            return kv;
        }
    }
}
