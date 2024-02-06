using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.Choreo.Data
{
    public class ChoreoSample
    {
        public struct BezierData
        {
            public float InDegrees { get; set; }
            public float InWeight { get; set; }
            public float OutDegrees { get; set; }
            public float OutWeight { get; set; }
        }
        public struct CurveType
        {
            public byte InType { get; set; }
            public byte OutType { get; set; }
        }
        public float Time { get; private set; }
        public float Value { get; private set; }
        public BezierData? Bezier { get; private set; }
        public CurveType? Curve { get; private set; }
        public ChoreoSample(float time, float value)
        {
            Time = time;
            Value = value;
        }

        public void SetBezierData(float inDeg, float inWeight, float outDeg, float outWeight)
        {
            Bezier = new BezierData
            {
                InDegrees = inDeg,
                InWeight = inWeight,
                OutDegrees = outDeg,
                OutWeight = outWeight
            };
        }

        public void SetCurveType(byte inType, byte outType)
        {
            Curve = new CurveType
            {
                InType = inType,
                OutType = outType
            };
        }

        public KVObject ToKeyValues()
        {
            var kv = new KVObject(null);

            kv.AddProperty("time", new KVValue(KVType.FLOAT, Time));
            kv.AddProperty("value", new KVValue(KVType.FLOAT, Value));

            if (Curve != null)
            {
                var curveIn = GetCurveTypeName(Curve.Value.InType);
                var curveOut = GetCurveTypeName(Curve.Value.OutType);
                var curveType = $"curve_{curveIn}_to_curve_{curveOut}";
                kv.AddProperty("curvetype", new KVValue(KVType.STRING, curveType));
            }

            if (Bezier != null)
            {
                var bezierKV = GetBezierKV();
                kv.AddProperty("bezier", new KVValue(KVType.OBJECT, bezierKV));
            }

            return kv;
        }

        private KVObject GetBezierKV()
        {
            var kv = new KVObject(null);

            kv.AddProperty("unified", new KVValue(KVType.BOOLEAN, true)); //TODO: Where does this come from?
            kv.AddProperty("unweighted", new KVValue(KVType.BOOLEAN, true)); //TODO: Where does this come from?

            var inKV = new KVObject(null);
            inKV.AddProperty("deg", new KVValue(KVType.FLOAT, Bezier.Value.InDegrees));
            inKV.AddProperty("weight", new KVValue(KVType.FLOAT, Bezier.Value.InWeight));
            kv.AddProperty("in", new KVValue(KVType.OBJECT, inKV));

            var outKV = new KVObject(null);
            outKV.AddProperty("deg", new KVValue(KVType.FLOAT, Bezier.Value.OutDegrees));
            outKV.AddProperty("weight", new KVValue(KVType.FLOAT, Bezier.Value.OutWeight));
            kv.AddProperty("out", new KVValue(KVType.OBJECT, outKV));

            return kv;
        }

        private static string GetCurveTypeName(byte index)
        {
            switch (index)
            {
                case 0x00:
                    return "default"; //todo: is 0x00 default? verify
                case 0x0A:
                    return "simple_cubic";
                case 0x05:
                    return "bspline";
                case 0x01:
                    return "catmullrom_normalize_x";
                case 0x02:
                    return "easein";
                case 0x03:
                    return "easeout";
                case 0x06:
                    return "linear_interp";
                case 0x07:
                    return "kochanek";
                case 0x08:
                    return "kochanek_early";
                case 0x09:
                    return "kochanek_late";
                case 0x10:
                    return "bezier";
                default:
                    throw new NotImplementedException();
            }
        }
    }
}
