using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.Choreo.Curves
{
    public struct BezierData
    {
        public float InDegrees { get; set; }
        public float InWeight { get; set; }
        public float OutDegrees { get; set; }
        public float OutWeight { get; set; }

        public KVValue ToKeyValue()
        {
            var kv = new KVObject(null);

            kv.AddProperty("unified", new KVValue(KVType.BOOLEAN, true)); //TODO: Where does this come from?
            kv.AddProperty("unweighted", new KVValue(KVType.BOOLEAN, true)); //TODO: Where does this come from?

            var inKV = new KVObject(null);
            inKV.AddProperty("deg", new KVValue(KVType.FLOAT, InDegrees));
            inKV.AddProperty("weight", new KVValue(KVType.FLOAT, InWeight));
            kv.AddProperty("in", new KVValue(KVType.OBJECT, inKV));

            var outKV = new KVObject(null);
            outKV.AddProperty("deg", new KVValue(KVType.FLOAT, OutDegrees));
            outKV.AddProperty("weight", new KVValue(KVType.FLOAT, OutWeight));
            kv.AddProperty("out", new KVValue(KVType.OBJECT, outKV));

            return new KVValue(KVType.OBJECT, kv);
        }
    }
}
