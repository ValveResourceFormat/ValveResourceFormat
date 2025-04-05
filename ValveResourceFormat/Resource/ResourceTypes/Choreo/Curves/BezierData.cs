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
            kv.AddProperty("unified", unified);
            var unweighted = Flags.HasFlag(BezierFlags.Unweighted);
            kv.AddProperty("unweighted", unweighted);

            var inKV = new KVObject(null);
            inKV.AddProperty("deg", InDegrees);
            inKV.AddProperty("weight", InWeight);
            kv.AddProperty("in", inKV);

            var outKV = new KVObject(null);
            outKV.AddProperty("deg", OutDegrees);
            outKV.AddProperty("weight", OutWeight);
            kv.AddProperty("out", outKV);

            return new KVValue(KVType.OBJECT, kv);
        }
    }
}
