using ValveResourceFormat.ResourceTypes.Choreo.Flags;

namespace ValveResourceFormat.ResourceTypes.Choreo.Data
{
    public class ChoreoFlexTrack
    {
        public string Name { get; private set; }
        public ChoreoTrackFlags TrackFlags { get; private set; }
        public float MinRange { get; private set; }
        public float MaxRange { get; private set; } = 1f;
        public ChoreoFlexSample[] Samples { get; private set; }
        public ChoreoFlexSample[] ComboSamples { get; private set; }

        public ChoreoFlexTrack(string name, ChoreoTrackFlags trackFlags, float minRange, float maxRange, ChoreoFlexSample[] samples, ChoreoFlexSample[] comboSamples)
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
