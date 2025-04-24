using ValveResourceFormat.ResourceTypes.Choreo.Curves;
using ValveResourceFormat.Serialization.KeyValues;

#nullable disable

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

        public void SetBezierData(BezierFlags flags, float inDeg, float inWeight, float outDeg, float outWeight)
        {
            Bezier = new BezierData
            {
                Flags = flags,
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

            kv.AddProperty("time", Time);
            kv.AddProperty("value", Value);

            if (Curve != null)
            {
                kv.AddProperty("curvetype", Curve.Value.ToKeyValue());
            }

            if (Bezier != null)
            {
                kv.AddProperty("bezier", Bezier.Value.ToKeyValue());
            }

            return kv;
        }
    }
}
