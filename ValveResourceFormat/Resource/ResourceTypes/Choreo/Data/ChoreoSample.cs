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
    }
}
