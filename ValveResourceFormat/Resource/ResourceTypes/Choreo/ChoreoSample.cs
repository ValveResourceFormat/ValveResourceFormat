using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.ResourceTypes.Choreo.Curves;

namespace ValveResourceFormat.ResourceTypes.Choreo
{
    public class ChoreoSample
    {
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
                var curveIn = Curve.Value.InTypeName;
                var curveOut = Curve.Value.OutTypeName;
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
    }
}
