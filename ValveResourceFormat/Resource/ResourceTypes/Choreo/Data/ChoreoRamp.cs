namespace ValveResourceFormat.ResourceTypes.Choreo.Data
{
    public class ChoreoRamp
    {
        public ChoreoSample[] Samples { get; private set; }
        public ChoreoRamp(ChoreoSample[] samples)
        {
            Samples = samples;
        }
    }
}
