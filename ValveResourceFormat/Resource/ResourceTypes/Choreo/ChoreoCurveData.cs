using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.Choreo
{
    public class ChoreoCurveData
    {
        public ChoreoSample[] Samples { get; private set; }
        public ChoreoEdge LeftEdge { get; private set; }
        public ChoreoEdge RightEdge { get; private set; }
        public ChoreoCurveData(ChoreoSample[] samples, ChoreoEdge leftEdge, ChoreoEdge rightEdge)
        {
            Samples = samples;
        }

        //TODO: This doesn't print edges, though they're not part of the ramp object in .vcds either.
        //Maybe return an array of kvobjects instead? That'd be different from the other choreo classes though.
        //cs2 resourcecompiler has strings for left_edge_active, left_edge_curvetype, left_edge_value, that kinda implies that they're in the ramp object in later versions?
        public KVObject ToKeyValues()
        {
            var kv = new KVObject(null, true, Samples.Length);

            foreach (var sample in Samples)
            {
                kv.AddProperty(null, new KVValue(KVType.OBJECT, sample.ToKeyValues()));
            }

            return kv;
        }
    }
}
