namespace ValveResourceFormat.ResourceTypes.Choreo.Data
{
    public class ChoreoEventFlex
    {
        public ChoreoFlexTrack[] Tracks { get; private set; }
        public ChoreoEventFlex(ChoreoFlexTrack[] tracks)
        {
            Tracks = tracks;
        }
    }
}
