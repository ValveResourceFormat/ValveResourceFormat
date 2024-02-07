using ValveResourceFormat.ResourceTypes.Choreo.Flags;

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
    }
}
