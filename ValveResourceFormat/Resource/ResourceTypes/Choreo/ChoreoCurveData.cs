using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.Choreo
{
    public class ChoreoCurveData
    {
        public ChoreoSample[] Samples { get; private set; }
        public ChoreoCurveData(ChoreoSample[] samples)
        {
            Samples = samples;
        }

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