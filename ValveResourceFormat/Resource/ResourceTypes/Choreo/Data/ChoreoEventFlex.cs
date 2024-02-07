using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.Choreo.Data
{
    public class ChoreoEventFlex
    {
        public ChoreoFlexTrack[] Tracks { get; private set; }
        public ChoreoEventFlex(ChoreoFlexTrack[] tracks)
        {
            Tracks = tracks;
        }

        public KVObject ToKeyValues()
        {
            var kv = new KVObject(null, true, Tracks.Length);

            foreach (var track in Tracks)
            {
                kv.AddProperty(null, new KVValue(KVType.OBJECT, track.ToKeyValues()));
            }

            return kv;
        }
    }
}
