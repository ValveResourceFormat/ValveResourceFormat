using ValveResourceFormat.ResourceTypes.Choreo.Flags;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.Choreo.Data
{
    public class ChoreoFlexTrack
    {
        public string Name { get; private set; }
        public ChoreoTrackFlags TrackFlags { get; private set; }
        public float MinRange { get; private set; }
        public float MaxRange { get; private set; } = 1f;
        public ChoreoRamp Samples { get; private set; }
        public ChoreoRamp ComboSamples { get; private set; }

        public ChoreoFlexTrack(string name, ChoreoTrackFlags trackFlags, float minRange, float maxRange, ChoreoRamp samples, ChoreoRamp comboSamples)
        {
            Name = name;
            TrackFlags = trackFlags;
            MinRange = minRange;
            MaxRange = maxRange;
            Samples = samples;
            ComboSamples = comboSamples;
        }

        public KVObject ToKeyValues()
        {
            var kv = new KVObject(null);

            var isCombo = TrackFlags.HasFlag(ChoreoTrackFlags.Combo);

            kv.AddProperty("name", new KVValue(KVType.STRING, Name));
            if (isCombo)
            {
                kv.AddProperty("combo", new KVValue(KVType.BOOLEAN, true));
            }
            kv.AddProperty("min", new KVValue(KVType.FLOAT, MinRange));
            kv.AddProperty("max", new KVValue(KVType.FLOAT, MaxRange));

            if (Samples?.Samples.Length > 0)
            {
                kv.AddProperty("samples", new KVValue(KVType.OBJECT, Samples.ToKeyValues()));
            }
            if (isCombo && ComboSamples?.Samples.Length > 0)
            {
                kv.AddProperty("stereo", new KVValue(KVType.OBJECT, ComboSamples.ToKeyValues()));
            }

            return kv;
        }
    }
}
