using ValveResourceFormat.Serialization.KeyValues;

#nullable disable

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
            LeftEdge = leftEdge;
            RightEdge = rightEdge;
        }

        //TODO: This doesn't print edges, though they're not part of the ramp object in .vcds either.
        //Maybe return an array of kvobjects instead? That'd be different from the other choreo classes though.
        public KVObject ToKeyValues()
        {
            var kv = new KVObject(null, true, Samples.Length);

            foreach (var sample in Samples)
            {
                kv.AddItem(sample.ToKeyValues());
            }

            return kv;
        }
    }
}
