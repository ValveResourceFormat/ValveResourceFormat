using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.Choreo
{
    public class ChoreoEventFlex
    {
        public ChoreoFlexAnimationTrack[] Tracks { get; private set; }
        public ChoreoEventFlex(ChoreoFlexAnimationTrack[] tracks)
        {
            Tracks = tracks;
        }

        public KVObject ToKeyValues()
        {
            var kv = new KVObject(null, true, Tracks.Length);

            foreach (var track in Tracks)
            {
                kv.AddProperty(null, track.ToKeyValues());
            }

            return kv;
        }
    }
}
