using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.Choreo.Curves
{
    [Flags]
    public enum BezierFlags
    {
        None,
        Unified,
        Unweighted,
    }
    public struct BezierData
    {
        public BezierFlags Flags { get; set; }
        public float InDegrees { get; set; }
        public float InWeight { get; set; }
        public float OutDegrees { get; set; }
        public float OutWeight { get; set; }

        public readonly KVValue ToKeyValue()
        {
            var kv = new KVObject(null);

            var unified = Flags.HasFlag(BezierFlags.Unified);
            kv.AddProperty("unified", new KVValue(KVType.BOOLEAN, unified));
            var unweighted = Flags.HasFlag(BezierFlags.Unweighted);
            kv.AddProperty("unweighted", new KVValue(KVType.BOOLEAN, unweighted));

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
