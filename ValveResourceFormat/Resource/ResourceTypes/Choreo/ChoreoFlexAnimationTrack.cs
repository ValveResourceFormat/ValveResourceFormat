using ValveResourceFormat.ResourceTypes.Choreo.Enums;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.Choreo
{
    public class ChoreoFlexAnimationTrack
    {
        public string Name { get; private set; }
        public ChoreoTrackFlags TrackFlags { get; private set; }
        public float MinRange { get; private set; }
        public float MaxRange { get; private set; } = 1f;
        public ChoreoCurveData Ramp { get; private set; }
        public ChoreoCurveData ComboRamp { get; private set; }

        public ChoreoFlexAnimationTrack(string name, ChoreoTrackFlags trackFlags, float minRange, float maxRange, ChoreoCurveData samples, ChoreoCurveData comboSamples)
        {
            Name = name;
            TrackFlags = trackFlags;
            MinRange = minRange;
            MaxRange = maxRange;
            Ramp = samples;
            ComboRamp = comboSamples;
        }

        public KVObject ToKeyValues()
        {
            var kv = new KVObject(null);

            var isDisabled = !TrackFlags.HasFlag(ChoreoTrackFlags.Enabled);
            var isCombo = TrackFlags.HasFlag(ChoreoTrackFlags.Combo);

            kv.AddProperty("name", new KVValue(KVType.STRING, Name));
            if (isDisabled)
            {
                kv.AddProperty("disabled", new KVValue(KVType.BOOLEAN, true));
            }
            if (isCombo)
            {
                kv.AddProperty("combo", new KVValue(KVType.BOOLEAN, true));
            }
            kv.AddProperty("min", new KVValue(KVType.FLOAT, MinRange));
            kv.AddProperty("max", new KVValue(KVType.FLOAT, MaxRange));

            //Edges are the same for both curves
            if (Ramp?.LeftEdge != null)
            {
                kv.AddProperty("left_edge", new KVValue(KVType.OBJECT, Ramp.LeftEdge.ToKeyValues()));
            }
            if (Ramp?.RightEdge != null)
            {
                kv.AddProperty("right_edge", new KVValue(KVType.OBJECT, Ramp.RightEdge.ToKeyValues()));
            }

            if (Ramp?.Samples.Length > 0)
            {
                kv.AddProperty("samples", new KVValue(KVType.OBJECT, Ramp.ToKeyValues()));
            }
            if (isCombo && ComboRamp?.Samples.Length > 0)
            {
                kv.AddProperty("stereo", new KVValue(KVType.OBJECT, ComboRamp.ToKeyValues()));
            }

            return kv;
        }
    }
}
